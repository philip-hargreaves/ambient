#pragma once

#include <condition_variable>
#include <deque>
#include <filesystem>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>

#include "adapters/guidance/retriever.hpp"
#include "adapters/guidance/upload_store.hpp"
#include "ports/document_ingest.hpp"

namespace ambient::guidance {

// Reads added documents on its own thread, embeds them through the retriever a
// chunk at a time so note searches interleave, and publishes every ready
// document to the retriever as one snapshot. The store opens on first use with
// the retriever's embedder, so an add before the embedder is ready is refused
class DocumentIngest : public IDocumentIngest {
   public:
    DocumentIngest(Retriever& retriever, std::filesystem::path root, std::function<bool()> busy);
    ~DocumentIngest() override;

    Accepted Add(const std::vector<std::filesystem::path>& paths) override;
    void Cancel() override;
    std::vector<DocumentInfo> List() override;
    void Remove(std::int64_t id) override;
    std::size_t RemoveAll() override;
    void SetListener(std::function<void(const IngestProgress&)> progress,
                     std::function<void(const DocumentInfo&)> document) override;

   private:
    struct Queued {
        std::int64_t id;
        std::filesystem::path path;
    };

    UploadStore& Store();  // under store_mutex_
    void Work();
    void Index(const Queued& item);
    void Publish();
    void Notify(const DocumentInfo& info);
    void Progress(std::int64_t id, const char* phase, int done, int total);

    Retriever& retriever_;
    std::filesystem::path root_;
    std::function<bool()> busy_;

    std::mutex store_mutex_;
    std::unique_ptr<UploadStore> store_;

    std::mutex mutex_;
    std::condition_variable wake_;
    std::thread worker_;
    std::deque<Queued> queue_;
    std::int64_t current_ = 0;
    bool cancel_ = false;
    bool stop_ = false;

    std::mutex listener_mutex_;
    std::function<void(const IngestProgress&)> on_progress_;
    std::function<void(const DocumentInfo&)> on_document_;
};

}  // namespace ambient::guidance
