#pragma once

#include <chrono>
#include <condition_variable>
#include <deque>
#include <filesystem>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <thread>

#include "adapters/guidance/ingest_host.hpp"
#include "adapters/guidance/retriever.hpp"
#include "adapters/guidance/upload_store.hpp"
#include "ports/document_ingest.hpp"

namespace ambient::guidance {

// Reads added documents on its own thread, embeds them through the retriever a
// chunk at a time so note searches interleave, and publishes every ready
// document to the retriever as one snapshot. The store opens on first use with
// the retriever's embedder, so an add before the embedder is ready is refused.
// PDFs go through the host executable and are skipped without one
class DocumentIngest : public IDocumentIngest {
   public:
    DocumentIngest(Retriever& retriever, std::filesystem::path root, std::function<bool()> busy,
                   std::filesystem::path host_exe = {}, HostLimits host_limits = {});
    ~DocumentIngest() override;

    Accepted Add(const std::vector<std::filesystem::path>& paths) override;
    void Cancel() override;
    std::vector<DocumentInfo> List() override;
    void Remove(std::int64_t id) override;
    std::size_t RemoveAll() override;
    PageRender Render(std::int64_t id, int page, std::int64_t chunk) override;
    std::filesystem::path OpenCopy(std::int64_t id) override;
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
    std::vector<Page> Extract(const std::vector<std::uint8_t>& bytes);
    std::shared_ptr<const std::vector<std::uint8_t>> Source(std::int64_t id);
    void Keep(const std::filesystem::path& path);
    void Publish();
    void Notify(const DocumentInfo& info);
    void Progress(std::int64_t id, const char* phase, int done, int total);

    Retriever& retriever_;
    std::filesystem::path root_;
    std::function<bool()> busy_;
    IngestHost host_;
    bool has_host_ = false;
    std::filesystem::path scratch_;

    // Decrypted sources held a minute for page turns, and the last few pages drawn
    struct Held {
        std::shared_ptr<const std::vector<std::uint8_t>> bytes;
        std::chrono::steady_clock::time_point at;
    };
    std::mutex scratch_mutex_;
    std::map<std::int64_t, Held> sources_;
    std::deque<std::filesystem::path> drawn_;

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
