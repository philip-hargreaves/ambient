#pragma once

#include <filesystem>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include "adapters/guidance/corpus_store.hpp"
#include "adapters/guidance/embedder.hpp"
#include "core/guidance/guidance_rank.hpp"
#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

struct RetrieverOptions {
    double floor = kDefaultFloor;
    double upload_floor = kDefaultFloor;
    double upload_note_floor = kNoteFloor;
    bool include_research = false;  // dev only
};

// Every ready added document as one matrix and its rows. Built by the ingest
// and swapped in whole, so a search never touches the store
struct UploadSnapshot {
    struct Row {
        std::int64_t document = 0;
        std::int64_t ord = 0;  // the chunk's ordinal within its document
        int page = 0;
        int pages = 0;  // the document's page count, 0 for text
        std::string name;
        std::string number;
        std::string section;
        std::string text;
        std::string added_at;
    };
    int dim = 0;
    std::vector<float> matrix;  // rows.size() by dim
    std::vector<Row> rows;
    std::vector<Corpus> documents;  // one per document, for the record's searched list
};

using EmbedderLoader = std::function<std::unique_ptr<IEmbedder>()>;

// The shipped retriever: one embedder, every corpus under corpora_root that
// passes the load guards, exact scan, rank vote across the sub-queries, cosine
// floor, population guard. A result's score is its best cosine and the order
// is the vote. Prepare and Search run one at a time. Corpora and Status may be
// read from any thread. A load failure is kept and rethrown without a retry,
// since the model store does not change while the engine runs
class Retriever : public IGuidanceRetriever {
   public:
    Retriever(EmbedderLoader load_embedder, std::filesystem::path corpora_root,
              RetrieverOptions options = {});

    void Prepare() override;
    Results Search(const std::string& text, int limit, SearchMode mode) override;
    std::vector<Corpus> Corpora() override;
    Readiness Status() override;

    // For the ingest: one embed at a time, interleaved with searches
    Embedding Embed(const std::string& text);
    EmbedderIdentity Identity();
    void PublishUploads(std::shared_ptr<const UploadSnapshot> uploads);

    // Dev only: reload the corpora with or without the ones marked research
    void SetResearch(bool include) override;

   private:
    struct Loaded {
        Corpus corpus;
        std::unique_ptr<CorpusStore> store;  // null when unavailable
    };
    void Load();
    void LoadCorpora();

    EmbedderLoader load_embedder_;
    std::filesystem::path corpora_root_;
    RetrieverOptions options_;

    std::mutex search_mutex_;
    std::unique_ptr<IEmbedder> embedder_;
    std::vector<Loaded> loaded_;
    std::shared_ptr<const UploadSnapshot> uploads_;
    std::string load_error_;

    std::mutex corpora_mutex_;
    std::vector<Corpus> corpora_;
    Readiness readiness_;
};

}  // namespace ambient::guidance
