#pragma once

#include <filesystem>
#include <functional>
#include <optional>
#include <variant>

#include "adapters/audio/capture_devices.hpp"
#include "adapters/diarisation/anchor_store.hpp"
#include "adapters/ipc/messages.hpp"
#include "adapters/ipc/pipe_server.hpp"
#include "adapters/models/model_store.hpp"
#include "core/session/playback.hpp"
#include "core/session/session_controller.hpp"
#include "ports/document_ingest.hpp"
#include "ports/guidance_lane.hpp"
#include "ports/note_lane.hpp"

namespace ambient::models {
class OvRuntime;
}  // namespace ambient::models

namespace ambient::translate {
class ITranslator;
class TranslateLane;
}  // namespace ambient::translate

namespace ambient::ipc {

std::variant<json, Error> HandleHello(const json& params);

std::variant<json, Error> HandleEcho(const json& params);

// Every staged model. `active` marks the one each role loads, the note
// role by its configured tier
json HandleModels(const ambient::models::ModelStore& models,
                  const std::string& note_tier = "default");

json NoteModelJson(const ambient::note::NoteModelState& state);

// note/tier: the shell names a tier, the lane resolves and loads it.
// Refused during a consultation. An unknown or unstaged tier is a
// parameter error naming what is staged
std::variant<json, Error> HandleNoteTier(ambient::note::INoteLane* lane, bool session_active,
                                         const json& params);

json HandleAudioInputs(const std::vector<ambient::audio::CaptureDevice>& devices);

// The clinician's voiceprint: where it came from and how many consultations
// refined it. Clearing is refused while a session runs
json HandleAnchorStatus(const ambient::diar::AnchorStore& anchors);
std::variant<json, Error> HandleAnchorClear(ambient::diar::AnchorStore& anchors,
                                            bool session_active);

json HandleSessionList(ambient::store::ISessionStore& sessions);

std::variant<json, Error> HandleSessionNote(ambient::store::ISessionStore& sessions,
                                            const json& params);

std::variant<json, Error> HandleSessionPatient(ambient::store::ISessionStore& sessions,
                                               const json& params);

std::variant<json, Error> HandleSessionTranscript(ambient::store::ISessionStore& sessions,
                                                  const json& params);

// Appraisal reflections on a stored session
std::variant<json, Error> HandleReflectionGet(ambient::store::ISessionStore& sessions,
                                              const json& params);
std::variant<json, Error> HandleReflectionUpdate(ambient::store::ISessionStore& sessions,
                                                 const json& params);
std::variant<json, Error> HandleReflectionDelete(ambient::store::ISessionStore& sessions,
                                                 const json& params);
json HandleReflectionList(ambient::store::ISessionStore& sessions);
std::variant<json, Error> HandleSessionDelete(ambient::store::ISessionStore& sessions,
                                              const json& params);

// Seed data from demo_dir, a no-op while present. Clearing leaves real sessions untouched
std::variant<json, Error> HandleDemoSeed(ambient::store::ISessionStore& sessions,
                                         const std::filesystem::path& demo_dir);
json HandleDemoClear(ambient::store::ISessionStore& sessions);

// Guidance: the panel shows the top three
inline constexpr int kGuidanceLimit = 3;
using Notify = std::function<void(const std::string& method, json params)>;
// guidance/corpora: whether the embedder is loading, ready or unavailable, and
// every corpus directory. guidance/model carries the state alone once loading ends
json GuidanceCorporaJson(const ambient::guidance::Readiness& readiness,
                         const std::vector<ambient::guidance::Corpus>& corpora);
json GuidanceModelJson(const ambient::guidance::Readiness& readiness);
// The lane request behind every search: results go out as guidance/ready, a
// failure as guidance/failed, both naming the session (null for free text).
// With a session the record is stored first and the payload says whether the
// note moved. A session erased meanwhile ends the search quietly, any other
// store error rides on the payload
ambient::guidance::SearchRequest GuidanceSearchRequest(ambient::store::ISessionStore& sessions,
                                                       const std::string& session,
                                                       ambient::store::Document note, int limit,
                                                       Notify notify);
// session/guidance: the stored record, null when the note was never searched
// or the record cannot be read, stale when the note has been written since
// documentsChanged: the added documents the record searched are not the ones ready now
std::variant<json, Error> HandleSessionGuidance(
    ambient::store::ISessionStore& sessions, const json& params,
    ambient::guidance::IDocumentIngest* ingest = nullptr);
// guidance/search: the stored note of session id, or free text, through the
// lane. The reply is immediate. The results arrive as a notification
std::variant<json, Error> HandleGuidanceSearch(ambient::store::ISessionStore& sessions,
                                               ambient::guidance::IGuidanceLane& lane,
                                               const json& params, const Notify& notify);
// Added documents: the row guidance/documents lists and guidance/document
// announces, and the progress notification
json DocumentJson(const ambient::guidance::DocumentInfo& document);
json ProgressJson(const ambient::guidance::IngestProgress& progress);
// The ready set changes when a document finishes or a finished one goes
bool ChangesReadySet(const ambient::guidance::DocumentInfo& document);
// guidance/documents: the folder and its documents. guidance/documents/add
// copies files into the folder and answers with their rows, the rest skipped
// with a reason. guidance/documents/remove sends a document's files to the
// Recycle Bin
std::variant<json, Error> HandleDocumentsAdd(ambient::guidance::IDocumentIngest& ingest,
                                             const json& params);
std::variant<json, Error> HandleDocumentsList(ambient::guidance::IDocumentIngest& ingest);
std::variant<json, Error> HandleDocumentsRemove(ambient::guidance::IDocumentIngest& ingest,
                                                const json& params);
// guidance/page: one page of an added PDF drawn to a bitmap under the scratch
// folder, with the cited chunk's boxes. guidance/documents/open: the file in
// the guidelines folder, for the shell to hand to a viewer
std::variant<json, Error> HandleDocumentsPage(ambient::guidance::IDocumentIngest& ingest,
                                              const json& params);
std::variant<json, Error> HandleDocumentsOpen(ambient::guidance::IDocumentIngest& ingest,
                                              const json& params);
void RegisterGuidanceMethods(PipeServer& server, ambient::store::ISessionStore& sessions,
                             ambient::guidance::IGuidanceRetriever& retriever,
                             ambient::guidance::IGuidanceLane& lane,
                             ambient::guidance::IDocumentIngest& ingest);

// Everything the methods reach. The controller, models and store are always
// present. The rest is wired when its model or feature is staged. first_use:
// model caches were cold at launch, so the one-off compiles are running and
// readiness reports them
struct EngineServices {
    ambient::session::SessionController& controller;
    const ambient::models::ModelStore& models;
    ambient::store::ISessionStore& sessions;
    ambient::metrics::Registry* metrics = nullptr;
    ambient::models::OvRuntime* runtime = nullptr;
    ambient::translate::ITranslator* translator = nullptr;
    ambient::translate::TranslateLane* translate_lane = nullptr;
    bool first_use = false;
    ambient::diar::AnchorStore* anchors = nullptr;
    ambient::note::INoteLane* note_lane = nullptr;
    bool stray_note_host = false;  // one from an earlier engine is wedged in the GPU driver
    std::filesystem::path demo_dir;
    ambient::session::Playback* playback = nullptr;
};

// engine/*, note/tier, anchor/* and audio/inputs
void RegisterEngineMethods(PipeServer& server, const EngineServices& services);
// session/*, note/*, patient/*, reflection/*, demo/* and translate/*
void RegisterSessionMethods(PipeServer& server, const EngineServices& services);

inline void RegisterMethods(PipeServer& server, const EngineServices& services) {
    RegisterEngineMethods(server, services);
    RegisterSessionMethods(server, services);
}

}  // namespace ambient::ipc
