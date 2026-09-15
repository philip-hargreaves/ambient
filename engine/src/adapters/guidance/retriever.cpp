#include "adapters/guidance/retriever.hpp"

#include <algorithm>
#include <map>
#include <stdexcept>

#include "core/guidance_query.hpp"
#include "core/guidance_scan.hpp"

namespace ambient::guidance {
namespace {

Corpus Describe(const CorpusInfo& info) {
    Corpus c;
    c.id = info.id;
    c.name = info.name;
    c.licence = info.licence;
    c.attribution = info.attribution;
    c.source = info.source;
    c.embedder = info.embedder_id;
    c.sha256 = info.sha256;
    c.chunks = static_cast<int>(info.chunk_count);
    c.built_at = info.built_at;
    return c;
}

// A hit's place across the corpora, keyed for the vote
struct Located {
    std::size_t corpus = 0;
    std::size_t ord = 0;
    float cosine = 0;
};

std::string Key(const Located& l) {
    return std::to_string(l.corpus) + ":" + std::to_string(l.ord);
}

// "BSR PMR guidelines 2009, page 3, 1.2": the name, then what the document gives
std::string UploadCitation(const UploadSnapshot::Row& row) {
    std::string out = row.name;
    if (row.page > 0) out += ", page " + std::to_string(row.page + 1);
    if (!row.number.empty()) out += ", " + row.number;
    return out;
}

}  // namespace

Retriever::Retriever(EmbedderLoader load_embedder, std::filesystem::path corpora_root,
                     RetrieverOptions options)
    : load_embedder_(std::move(load_embedder)),
      corpora_root_(std::move(corpora_root)),
      options_(options) {}

void Retriever::Prepare() {
    std::lock_guard<std::mutex> lock(search_mutex_);
    Load();
}

void Retriever::Load() {
    if (embedder_) return;
    if (!load_error_.empty()) throw std::runtime_error(load_error_);
    try {
        auto embedder = load_embedder_();
        if (!embedder) throw std::runtime_error("no guidance embedder");
        std::vector<std::filesystem::path> dirs;
        if (std::filesystem::is_directory(corpora_root_)) {
            for (const auto& entry : std::filesystem::directory_iterator(corpora_root_)) {
                if (entry.is_directory() && std::filesystem::exists(entry.path() / kManifestFile)) {
                    dirs.push_back(entry.path());
                }
            }
        }
        std::sort(dirs.begin(), dirs.end());
        std::vector<Loaded> loaded;
        std::vector<Corpus> corpora;
        for (const auto& dir : dirs) {
            Loaded item;
            std::string reason;
            item.store = CorpusStore::Open(dir, embedder->Identity(), reason);
            if (item.store) {
                item.corpus = Describe(item.store->Info());
            } else {
                item.corpus.id = dir.filename().string();
                item.corpus.unavailable = reason;
            }
            corpora.push_back(item.corpus);
            loaded.push_back(std::move(item));
        }
        embedder_ = std::move(embedder);
        loaded_ = std::move(loaded);
        std::lock_guard<std::mutex> lock(corpora_mutex_);
        corpora_ = std::move(corpora);
        readiness_ = {Readiness::Phase::kReady, ""};
    } catch (const std::exception& e) {
        load_error_ = e.what();
        std::lock_guard<std::mutex> lock(corpora_mutex_);
        readiness_ = {Readiness::Phase::kUnavailable, load_error_};
        throw;
    }
}

Embedding Retriever::Embed(const std::string& text) {
    std::lock_guard<std::mutex> lock(search_mutex_);
    Load();
    return embedder_->Embed(text);
}

EmbedderIdentity Retriever::Identity() {
    std::lock_guard<std::mutex> lock(search_mutex_);
    Load();
    return embedder_->Identity();
}

void Retriever::PublishUploads(std::shared_ptr<const UploadSnapshot> uploads) {
    std::lock_guard<std::mutex> lock(search_mutex_);
    uploads_ = std::move(uploads);
}

Results Retriever::Search(const std::string& text, int limit, SearchMode mode) {
    std::lock_guard<std::mutex> lock(search_mutex_);
    Load();
    Results out;
    out.floor = options_.floor;
    out.upload_floor = options_.upload_floor;
    const auto uploads = uploads_;
    std::vector<CorpusStore*> stores;
    if (uploads) {
        for (const auto& document : uploads->documents) out.searched.push_back(document);
    }
    for (auto& item : loaded_) {
        if (item.store) {
            stores.push_back(item.store.get());
            out.searched.push_back(item.corpus);
        }
    }
    const std::string whole(detail::Trim(text));
    std::vector<std::string> queries;
    if (mode == SearchMode::kQuery) {
        if (!whole.empty()) queries.push_back(whole);
    } else {
        queries = SubQueries(text);
    }
    if (queries.empty()) {
        out.abstained = true;
        return out;
    }
    const bool have_uploads = uploads && !uploads->rows.empty();
    if (stores.empty() && !have_uploads) return out;

    // Each sub-query is embedded once and scanned against both groups
    std::vector<std::pair<std::string, Embedding>> embedded;
    for (const auto& query : queries) embedded.emplace_back(query, embedder_->Embed(query));

    struct Source {
        const float* matrix;
        std::size_t size;
        int dim;
    };
    const int k = options_.union_size;
    // One group's sources sort into one list per sub-query before the vote
    const auto vote = [&](const std::vector<Source>& sources, double floor,
                          std::map<std::string, Located>& where) {
        std::vector<SubQueryHits> lists;
        for (const auto& [query, embedding] : embedded) {
            std::vector<Located> located;
            for (std::size_t c = 0; c < sources.size(); ++c) {
                const auto hits = Scan(sources[c].matrix, sources[c].size, sources[c].dim,
                                       embedding.vector.data(), k);
                for (const auto& hit : hits) located.push_back({c, hit.ord, hit.cosine});
            }
            std::stable_sort(
                located.begin(), located.end(),
                [](const Located& a, const Located& b) { return a.cosine > b.cosine; });
            if (located.size() > static_cast<std::size_t>(k))
                located.resize(static_cast<std::size_t>(k));
            SubQueryHits list{query, query == whole, {}};
            for (const auto& l : located) {
                auto key = Key(l);
                list.hits.push_back({key, l.cosine});
                where.emplace(std::move(key), l);
            }
            lists.push_back(std::move(list));
        }
        return ApplyFloor(RankVote(lists, options_.note_weight, k), floor);
    };

    // Added documents lead, as their own group with their own floor
    if (have_uploads) {
        std::map<std::string, Located> where;
        const auto ordered = vote({{uploads->matrix.data(), uploads->rows.size(), uploads->dim}},
                                  options_.upload_floor, where);
        out.considered += ordered.considered;
        int shown = 0;
        for (const auto& candidate : ordered.kept) {
            if (shown >= limit) break;
            const auto& at = where.at(candidate.id);
            const auto& row = uploads->rows[at.ord];
            if (PopulationConflict(text, row.text, row.name)) continue;
            Result result;
            result.corpus = "upload:" + std::to_string(row.document);
            result.chunk_id = result.corpus + "-" + std::to_string(at.ord);
            result.number = row.number;
            result.title = row.name;
            result.section = row.section;
            result.citation = UploadCitation(row);
            result.text = row.text;
            result.last_updated = row.added_at;
            result.source = "upload";
            result.score = candidate.cosine;
            result.trigger = candidate.trigger == whole ? "" : candidate.trigger;
            result.document = row.document;
            result.page = row.page;
            out.shown.push_back(std::move(result));
            ++shown;
        }
    }

    if (!stores.empty()) {
        std::vector<Source> sources;
        for (auto* store : stores)
            sources.push_back({store->Matrix(), store->Size(), store->Dim()});
        std::map<std::string, Located> where;
        const auto ordered = vote(sources, options_.floor, where);
        out.considered += ordered.considered;
        int shown = 0;
        for (const auto& candidate : ordered.kept) {
            if (shown >= limit) break;
            const auto& at = where.at(candidate.id);
            auto* store = stores[at.corpus];
            auto chunk = store->TextAt(at.ord);
            const auto& cite = store->CiteAt(at.ord);
            if (PopulationConflict(text, chunk.text, cite.title)) continue;
            Result result;
            result.corpus = store->Info().id;
            result.chunk_id = cite.chunk_id;
            result.code = cite.code;
            result.number = cite.number;
            result.title = cite.title;
            result.section = cite.section;
            result.citation = Citation(cite.code, cite.number, cite.title);
            result.text = std::move(chunk.text);
            result.url = std::move(chunk.url);
            result.last_updated = std::move(chunk.last_updated);
            result.update_tag = std::move(chunk.update_tag);
            result.source = store->Info().source;
            result.score = candidate.cosine;
            result.trigger = candidate.trigger == whole ? "" : candidate.trigger;
            out.shown.push_back(std::move(result));
            ++shown;
        }
    }
    out.abstained = out.shown.empty();
    return out;
}

std::vector<Corpus> Retriever::Corpora() {
    std::lock_guard<std::mutex> lock(corpora_mutex_);
    return corpora_;
}

Readiness Retriever::Status() {
    std::lock_guard<std::mutex> lock(corpora_mutex_);
    return readiness_;
}

}  // namespace ambient::guidance
