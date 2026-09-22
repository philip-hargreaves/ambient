#include <algorithm>
#include <cctype>
#include <cstddef>
#include <cstdio>
#include <memory>
#include <openvino/core/version.hpp>
#include <optional>
#include <set>
#include <stdexcept>

#include "adapters/demo/sample_year.hpp"
#include "adapters/guidance/guidance_record.hpp"
#include "adapters/ipc/handlers.hpp"
#include "adapters/models/ov_runtime.hpp"
#include "adapters/system/power_throttling.hpp"
#include "adapters/translate/translate_lane.hpp"
#include "core/common/version.hpp"
#include "core/note/summary_scrub.hpp"
#include "ports/store_error.hpp"

namespace ambient::ipc {
namespace {

json GuidanceReadyJson(const std::string& session, const ambient::guidance::Record& record) {
    json body = ambient::guidance::ToJson(record);
    body["id"] = NullWhenEmpty(session);
    body["storeError"] = nullptr;
    body["stale"] = nullptr;
    return body;
}

const char* PhaseName(ambient::guidance::Readiness::Phase phase) {
    switch (phase) {
        case ambient::guidance::Readiness::Phase::kLoading:
            return "loading";
        case ambient::guidance::Readiness::Phase::kReady:
            return "ready";
        case ambient::guidance::Readiness::Phase::kUnavailable:
            return "unavailable";
    }
    return "unavailable";
}

}  // namespace

json GuidanceModelJson(const ambient::guidance::Readiness& readiness) {
    return json{{"state", PhaseName(readiness.phase)}, {"detail", NullWhenEmpty(readiness.detail)}};
}

json GuidanceCorporaJson(const ambient::guidance::Readiness& readiness,
                         const std::vector<ambient::guidance::Corpus>& corpora) {
    json result = GuidanceModelJson(readiness);
    json list = json::array();
    for (const auto& c : corpora) list.push_back(ambient::guidance::ToJson(c));
    result["corpora"] = list;
    return result;
}

ambient::guidance::SearchRequest GuidanceSearchRequest(ambient::store::ISessionStore& sessions,
                                                       const std::string& session,
                                                       ambient::store::Document note, int limit,
                                                       Notify notify) {
    ambient::guidance::SearchRequest request;
    request.session = session;
    request.note = std::move(note.text);
    request.limit = limit;
    const auto revision = note.revision;
    request.on_ready = [&sessions, session, revision,
                        notify](const ambient::guidance::Results& results) {
        using ambient::store::DocumentKind;
        const ambient::guidance::Record record{results, revision};
        json body = GuidanceReadyJson(session, record);
        if (!session.empty()) {
            try {
                sessions.SaveDocument(session, DocumentKind::kGuidance,
                                      {.text = ambient::guidance::Dump(record)});
            } catch (const ambient::store::StoreError& e) {
                if (e.Code() == ambient::store::StoreCode::kNotFound) {
                    std::fprintf(stderr, "ambient-engine: guidance for %s dropped, session gone\n",
                                 session.c_str());
                    return;
                }
                body["storeError"] = e.what();
            } catch (const std::exception& e) {
                body["storeError"] = e.what();
            }
            // Stale stays unknown when the note cannot be read back
            try {
                body["stale"] =
                    sessions.ReadDocument(session, DocumentKind::kNote).revision != revision;
            } catch (const ambient::store::StoreError& e) {
                if (e.Code() == ambient::store::StoreCode::kNotFound) return;
            } catch (const std::exception&) {  // NOLINT(bugprone-empty-catch)
            }
        }
        notify("guidance/ready", std::move(body));
    };
    request.on_failed = [session, notify](const std::string& detail) {
        notify("guidance/failed", json{{"id", NullWhenEmpty(session)}, {"detail", detail}});
    };
    return request;
}

std::variant<json, Error> HandleGuidanceSearch(ambient::store::ISessionStore& sessions,
                                               ambient::guidance::IGuidanceLane& lane,
                                               const json& params, const Notify& notify) {
    int limit = kGuidanceLimit;
    if (params.contains("limit")) {
        if (!params["limit"].is_number_integer() || params["limit"].get<int>() < 1 ||
            params["limit"].get<int>() > 20) {
            return InvalidParams("limit must be 1-20");
        }
        limit = params["limit"].get<int>();
    }
    std::string session;
    bool as_note = false;
    ambient::store::Document note;
    if (params.contains("text")) {
        if (!params["text"].is_string()) {
            return InvalidParams("text must be a string");
        }
        note.text = params["text"].get<std::string>();
        // Typed text is embedded whole. Mode "note" splits it into sub-queries
        // the way a stored note is split
        if (params.contains("mode")) {
            if (params["mode"] != "query" && params["mode"] != "note") {
                return InvalidParams("mode must be query or note");
            }
            as_note = params["mode"] == "note";
        }
    } else {
        const auto id = IdFrom(params);
        if (std::holds_alternative<Error>(id)) return std::get<Error>(id);
        session = std::get<std::string>(id);
        try {
            note = sessions.ReadDocument(session, ambient::store::DocumentKind::kNote);
        } catch (const std::exception& e) {
            return SessionError(e.what());
        }
    }
    if (note.text.empty()) return SessionError("no note to search");
    auto request = GuidanceSearchRequest(sessions, session, std::move(note), limit, notify);
    request.as_note = as_note;
    lane.Run(std::move(request));
    return json::object();
}

namespace {

// The searched list mixes corpora with added documents. Only the added ones,
// which carry an "upload:" id, are compared
bool DocumentsChangedSince(const ambient::guidance::Record& record,
                           ambient::guidance::IDocumentIngest& ingest) {
    std::set<std::string> searched;
    for (const auto& c : record.results.searched) {
        if (c.id.starts_with("upload:")) searched.insert(c.id);
    }
    std::set<std::string> ready;
    for (const auto& d : ingest.List().documents) {
        if (d.state == "ready") ready.insert("upload:" + std::to_string(d.id));
    }
    return searched != ready;
}

}  // namespace

std::variant<json, Error> HandleSessionGuidance(ambient::store::ISessionStore& sessions,
                                                const json& params,
                                                ambient::guidance::IDocumentIngest* ingest) {
    const auto id = IdFrom(params);
    if (std::holds_alternative<Error>(id)) return std::get<Error>(id);
    try {
        using ambient::store::DocumentKind;
        const auto& session = std::get<std::string>(id);
        const auto stored = sessions.ReadDocument(session, DocumentKind::kGuidance);
        if (stored.text.empty()) return json{{"guidance", nullptr}};
        const json parsed = json::parse(stored.text, nullptr, false);
        std::optional<ambient::guidance::Record> record;
        if (ambient::guidance::CanRead(parsed)) {
            try {
                record = ambient::guidance::RecordFromJson(parsed);
            } catch (const json::exception&) {  // NOLINT(bugprone-empty-catch)
            }
        }
        if (!record) {
            std::fprintf(stderr, "ambient-engine: guidance record for %s unreadable, dropped\n",
                         session.c_str());
            return json{{"guidance", nullptr}};
        }
        json guidance = ambient::guidance::ToJson(*record);
        guidance["generatedAt"] = NullWhenEmpty(stored.generated_at);
        guidance["stale"] =
            record->note_revision != sessions.ReadDocument(session, DocumentKind::kNote).revision;
        guidance["documentsChanged"] = ingest != nullptr && DocumentsChangedSince(*record, *ingest);
        return json{{"guidance", guidance}};
    } catch (const std::exception& e) {
        return SessionError(e.what());
    }
}

json DocumentJson(const ambient::guidance::DocumentInfo& document) {
    return json{{"id", document.id},
                {"name", document.name},
                {"path", document.path},
                {"sha256", document.sha256},
                {"mime", document.mime},
                {"state", document.state},
                {"error", NullWhenEmpty(document.error)},
                {"addedAt", document.added_at},
                {"indexedAt", NullWhenEmpty(document.indexed_at)},
                {"bytes", document.bytes},
                {"pages", document.pages},
                {"pagesWithoutText", document.pages_without_text},
                {"chunks", document.chunks}};
}

json ProgressJson(const ambient::guidance::IngestProgress& progress) {
    return json{{"id", progress.id},
                {"phase", progress.phase},
                {"done", progress.done},
                {"total", progress.total}};
}

bool ChangesReadySet(const ambient::guidance::DocumentInfo& document) {
    return document.state == "ready" || (document.state == "removed" && document.chunks > 0);
}

namespace {

std::string Utf8(const std::filesystem::path& path) {
    const auto u8 = path.u8string();
    return std::string(u8.begin(), u8.end());
}

Error DocumentsError(const std::exception& e) {
    return Error{kSessionError, "Document store error", json(e.what())};
}

// An unknown document is the caller's mistake, anything else the store's
std::variant<json, Error> DocumentsRefused(const std::exception& e) {
    if (const auto* store = dynamic_cast<const ambient::store::StoreError*>(&e);
        store != nullptr && store->Code() == ambient::store::StoreCode::kNotFound) {
        return InvalidParams("unknown document");
    }
    return DocumentsError(e);
}

json Param(const json& params, const char* key) {
    return params.is_object() ? params.value(key, json()) : json();
}

}  // namespace

std::variant<json, Error> HandleDocumentsAdd(ambient::guidance::IDocumentIngest& ingest,
                                             const json& params) {
    const json paths = Param(params, "paths");
    const Error invalid{kInvalidParams, "Invalid params", json("paths must be a list of strings")};
    if (!paths.is_array() || paths.empty()) return invalid;
    std::vector<std::filesystem::path> files;
    for (const auto& path : paths) {
        if (!path.is_string()) return invalid;
        const auto text = path.get<std::string>();
        files.emplace_back(std::u8string(text.begin(), text.end()));
    }
    try {
        const auto accepted = ingest.Add(files);
        json documents = json::array();
        for (const auto& document : accepted.documents) documents.push_back(DocumentJson(document));
        json skipped = json::array();
        for (const auto& s : accepted.skipped) {
            skipped.push_back(json{{"path", s.path}, {"reason", s.reason}});
        }
        return json{{"documents", documents}, {"skipped", skipped}};
    } catch (const std::exception& e) {
        return DocumentsError(e);
    }
}

std::variant<json, Error> HandleDocumentsList(ambient::guidance::IDocumentIngest& ingest) {
    try {
        const auto listing = ingest.List();
        json documents = json::array();
        for (const auto& document : listing.documents) documents.push_back(DocumentJson(document));
        return json{{"folder", Utf8(listing.folder)},
                    {"found", listing.found},
                    {"unsupported", listing.unsupported},
                    {"documents", documents}};
    } catch (const std::exception& e) {
        return DocumentsError(e);
    }
}

std::variant<json, Error> HandleDocumentsRemove(ambient::guidance::IDocumentIngest& ingest,
                                                const json& params) {
    const json id = Param(params, "id");
    if (!id.is_number_integer()) {
        return InvalidParams("id must be an integer");
    }
    try {
        ingest.Remove(id.get<std::int64_t>());
        return json::object();
    } catch (const std::exception& e) {
        return DocumentsRefused(e);
    }
}

std::variant<json, Error> HandleDocumentsPage(ambient::guidance::IDocumentIngest& ingest,
                                              const json& params) {
    const json id = Param(params, "id");
    const json page = Param(params, "page");
    const std::string chunk_id = params.is_object() ? params.value("chunkId", "") : "";
    // The chunk id is "upload:<document>-<ord>"
    const auto dash = chunk_id.rfind('-');
    const bool numbered =
        dash != std::string::npos && dash + 1 < chunk_id.size() &&
        std::all_of(chunk_id.begin() + static_cast<std::ptrdiff_t>(dash) + 1, chunk_id.end(),
                    [](unsigned char c) { return std::isdigit(c) != 0; });
    if (!id.is_number_integer() || !page.is_number_integer() || page.get<int>() < 0 || !numbered) {
        return InvalidParams("id, page and chunkId are required");
    }
    try {
        const auto ord = std::stoll(chunk_id.substr(dash + 1));
        const auto drawn = ingest.Render(id.get<std::int64_t>(), page.get<int>(), ord);
        return json{{"path", Utf8(drawn.path)},
                    {"width", drawn.width},
                    {"height", drawn.height},
                    {"pages", drawn.pages},
                    {"boxes", json::parse(drawn.boxes)}};
    } catch (const std::exception& e) {
        return DocumentsRefused(e);
    }
}

std::variant<json, Error> HandleDocumentsOpen(ambient::guidance::IDocumentIngest& ingest,
                                              const json& params) {
    const json id = Param(params, "id");
    if (!id.is_number_integer()) {
        return InvalidParams("id must be an integer");
    }
    try {
        return json{{"path", Utf8(ingest.Path(id.get<std::int64_t>()))}};
    } catch (const std::exception& e) {
        return DocumentsRefused(e);
    }
}

void RegisterGuidanceMethods(PipeServer& server, ambient::store::ISessionStore& sessions,
                             ambient::guidance::IGuidanceRetriever& retriever,
                             ambient::guidance::IGuidanceLane& lane,
                             ambient::guidance::IDocumentIngest& ingest) {
    server.RegisterMethod("guidance/search", [&server, &sessions, &lane](const json& params) {
        return HandleGuidanceSearch(sessions, lane, params,
                                    [&server](const std::string& method, json notification) {
                                        server.PushNotification(method, std::move(notification));
                                    });
    });
    server.RegisterMethod("guidance/corpora", [&retriever](const json&) {
        return GuidanceCorporaJson(retriever.Status(), retriever.Corpora());
    });
    // Dev only: the shell's research switch reloads the corpora without a restart
    server.RegisterMethod("guidance/research",
                          [&server, &retriever](const json& params) -> std::variant<json, Error> {
                              const json include = Param(params, "include");
                              if (!include.is_boolean()) {
                                  return InvalidParams("include must be a boolean");
                              }
                              retriever.SetResearch(include.get<bool>());
                              server.PushNotification("guidance/model",
                                                      GuidanceModelJson(retriever.Status()));
                              return json::object();
                          });
    server.RegisterMethod("session/guidance", [&sessions, &ingest](const json& params) {
        return HandleSessionGuidance(sessions, params, &ingest);
    });
    server.RegisterMethod("guidance/documents/add", [&ingest](const json& params) {
        return HandleDocumentsAdd(ingest, params);
    });
    server.RegisterMethod("guidance/documents",
                          [&ingest](const json&) { return HandleDocumentsList(ingest); });
    server.RegisterMethod("guidance/documents/remove", [&ingest](const json& params) {
        return HandleDocumentsRemove(ingest, params);
    });
    server.RegisterMethod("guidance/page", [&ingest](const json& params) {
        return HandleDocumentsPage(ingest, params);
    });
    server.RegisterMethod("guidance/documents/open", [&ingest](const json& params) {
        return HandleDocumentsOpen(ingest, params);
    });
    server.RegisterMethod("guidance/documents/removeAll",
                          [&ingest](const json&) -> std::variant<json, Error> {
                              try {
                                  return json{{"removed", ingest.RemoveAll()}};
                              } catch (const std::exception& e) {
                                  return DocumentsError(e);
                              }
                          });
}

}  // namespace ambient::ipc
