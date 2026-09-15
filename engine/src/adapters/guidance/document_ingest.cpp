#include "adapters/guidance/document_ingest.hpp"

#include <cctype>
#include <chrono>
#include <cstdio>
#include <system_error>

#include "adapters/guidance/chunker.hpp"
#include "core/patient_screen.hpp"
#include "ports/store_error.hpp"

namespace ambient::guidance {
namespace {

constexpr std::uintmax_t kSpareBytes = 200ull << 20;
constexpr auto kPoll = std::chrono::milliseconds(200);
constexpr auto kProgressEvery = std::chrono::milliseconds(250);

// PDF and HTML arrive with their readers
std::string Mime(const std::filesystem::path& path) {
    auto ext = path.extension().string();
    for (auto& c : ext) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    if (ext == ".txt") return "text/plain";
    if (ext == ".md" || ext == ".markdown") return "text/markdown";
    return "";
}

std::string Utf8(const std::filesystem::path& path) {
    const auto u8 = path.u8string();
    return std::string(u8.begin(), u8.end());
}

// The chunker wants a guideline code, so a document's name stands in
std::string Slug(const std::string& name) {
    std::string out;
    for (const unsigned char c : name) {
        out += std::isalnum(c) ? static_cast<char>(std::tolower(c)) : '-';
    }
    return out.empty() ? "document" : out;
}

}  // namespace

DocumentIngest::DocumentIngest(Retriever& retriever, std::filesystem::path root,
                               std::function<bool()> busy)
    : retriever_(retriever), root_(std::move(root)), busy_(std::move(busy)) {}

DocumentIngest::~DocumentIngest() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stop_ = true;
        wake_.notify_all();
    }
    if (worker_.joinable()) worker_.join();
}

UploadStore& DocumentIngest::Store() {
    if (!store_) {
        if (retriever_.Status().phase != Readiness::Phase::kReady) {
            throw store::StoreError(store::StoreCode::kBusy, "guidance model not ready");
        }
        std::string reason;
        store_ = UploadStore::Open(root_, retriever_.Identity(), reason);
        if (!store_) throw store::StoreError(store::StoreCode::kIo, reason);
    }
    return *store_;
}

Accepted DocumentIngest::Add(const std::vector<std::filesystem::path>& paths) {
    Accepted out;
    std::vector<Queued> queued;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        auto& store = Store();
        for (const auto& path : paths) {
            std::error_code ec;
            const auto bytes = std::filesystem::file_size(path, ec);
            if (ec || bytes == 0) {
                out.skipped.push_back({Utf8(path), "unreadable"});
                continue;
            }
            const auto mime = Mime(path);
            if (mime.empty()) {
                out.skipped.push_back({Utf8(path), "unsupported"});
                continue;
            }
            const auto space = std::filesystem::space(root_, ec);
            if (!ec && space.available < bytes * 3 + kSpareBytes) {
                out.skipped.push_back({Utf8(path), "noSpace"});
                continue;
            }
            const auto added = store.Add(path, Utf8(path.stem()), mime);
            if (!added.added) {
                out.skipped.push_back({Utf8(path), "duplicate"});
                continue;
            }
            out.documents.push_back(store.Get(added.id));
            queued.push_back({added.id, path});
        }
    }
    std::lock_guard<std::mutex> lock(mutex_);
    for (auto& item : queued) queue_.push_back(std::move(item));
    if (!worker_.joinable()) worker_ = std::thread([this] { Work(); });
    wake_.notify_all();
    return out;
}

void DocumentIngest::Cancel() {
    std::vector<std::int64_t> waiting;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        for (const auto& item : queue_) waiting.push_back(item.id);
        queue_.clear();
        cancel_ = true;
    }
    std::lock_guard<std::mutex> lock(store_mutex_);
    for (const auto id : waiting) {
        DocumentInfo removed = Store().Get(id);
        Store().Remove(id);
        removed.state = "removed";
        Notify(removed);
    }
}

std::vector<DocumentInfo> DocumentIngest::List() {
    std::lock_guard<std::mutex> lock(store_mutex_);
    return Store().List();
}

void DocumentIngest::Remove(std::int64_t id) {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (id == current_) {
            cancel_ = true;
            return;
        }
        std::erase_if(queue_, [id](const Queued& item) { return item.id == id; });
    }
    std::lock_guard<std::mutex> lock(store_mutex_);
    DocumentInfo removed = Store().Get(id);
    Store().Remove(id);
    Publish();
    removed.state = "removed";
    Notify(removed);
}

std::size_t DocumentIngest::RemoveAll() {
    Cancel();
    std::lock_guard<std::mutex> lock(store_mutex_);
    auto rows = Store().List();
    const auto count = Store().RemoveAll();
    Publish();
    for (auto& row : rows) {
        row.state = "removed";
        Notify(row);
    }
    return count;
}

void DocumentIngest::SetListener(std::function<void(const IngestProgress&)> progress,
                                 std::function<void(const DocumentInfo&)> document) {
    std::lock_guard<std::mutex> lock(listener_mutex_);
    on_progress_ = std::move(progress);
    on_document_ = std::move(document);
}

void DocumentIngest::Work() {
    for (;;) {
        Queued item;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            wake_.wait(lock, [this] { return stop_ || !queue_.empty(); });
            if (stop_) return;
            item = std::move(queue_.front());
            queue_.pop_front();
            current_ = item.id;
            cancel_ = false;
        }
        Index(item);
        std::lock_guard<std::mutex> lock(mutex_);
        current_ = 0;
    }
}

// The sealed copy is the source from here on, so the original may move
void DocumentIngest::Index(const Queued& item) {
    const auto id = item.id;
    const auto cancelled = [this] {
        std::lock_guard<std::mutex> lock(mutex_);
        return cancel_ || stop_;
    };
    const auto remove = [&] {
        std::lock_guard<std::mutex> lock(store_mutex_);
        DocumentInfo removed = Store().Get(id);
        Store().Remove(id);
        removed.state = "removed";
        Notify(removed);
    };
    try {
        std::string name, text;
        {
            std::lock_guard<std::mutex> lock(store_mutex_);
            name = Store().Get(id).name;
            const auto bytes = Store().ReadFile(id);
            text.assign(bytes.begin(), bytes.end());
        }
        Progress(id, "reading", 1, 1);
        if (LooksLikePatientData(text)) {
            std::lock_guard<std::mutex> lock(store_mutex_);
            Store().Fail(id, "patientData");
            Notify(Store().Get(id));
            return;
        }
        const auto chunks = ChunksFromText(Slug(name), name, text);
        std::vector<UploadChunk> ready;
        auto last = std::chrono::steady_clock::now() - kProgressEvery;
        for (std::size_t i = 0; i < chunks.size(); ++i) {
            bool paused = false;
            while (busy_ && busy_() && !cancelled()) {
                if (!paused)
                    Progress(id, "paused", static_cast<int>(i), static_cast<int>(chunks.size()));
                paused = true;
                std::this_thread::sleep_for(kPoll);
            }
            if (cancelled()) {
                remove();
                return;
            }
            const auto embedding = retriever_.Embed(chunks[i].text);
            ready.push_back(
                {0, chunks[i].number, chunks[i].section, chunks[i].text, embedding.vector, "[]"});
            const auto now = std::chrono::steady_clock::now();
            if (paused || now - last >= kProgressEvery || i + 1 == chunks.size()) {
                Progress(id, "preparing", static_cast<int>(i + 1), static_cast<int>(chunks.size()));
                last = now;
            }
        }
        std::lock_guard<std::mutex> lock(store_mutex_);
        Store().Finish(id, ready, 0, 0);
        Publish();
        Notify(Store().Get(id));
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: document %lld failed: %s\n",
                     static_cast<long long>(id), e.what());
        std::lock_guard<std::mutex> lock(store_mutex_);
        try {
            Store().Fail(id, "unreadable");
            Notify(Store().Get(id));
        } catch (const std::exception&) {  // NOLINT(bugprone-empty-catch)
        }
    }
}

// Every ready document as one snapshot, swapped in under the retriever's lock
void DocumentIngest::Publish() {
    auto snapshot = std::make_shared<UploadSnapshot>();
    snapshot->dim = store_->Embedder().dim;
    for (const auto& doc : store_->List()) {
        if (doc.state != "ready") continue;
        Corpus corpus;
        corpus.id = "upload:" + std::to_string(doc.id);
        corpus.name = doc.name;
        corpus.source = "upload";
        corpus.embedder = store_->Embedder().id;
        corpus.chunks = static_cast<int>(doc.chunks);
        corpus.built_at = doc.indexed_at;
        snapshot->documents.push_back(std::move(corpus));
        for (auto& chunk : store_->ReadChunks(doc.id)) {
            snapshot->matrix.insert(snapshot->matrix.end(), chunk.vector.begin(),
                                    chunk.vector.end());
            snapshot->rows.push_back({doc.id, chunk.page, doc.name, std::move(chunk.number),
                                      std::move(chunk.section), std::move(chunk.text),
                                      std::move(chunk.boxes), doc.added_at});
        }
    }
    retriever_.PublishUploads(std::move(snapshot));
}

void DocumentIngest::Notify(const DocumentInfo& info) {
    std::function<void(const DocumentInfo&)> listener;
    {
        std::lock_guard<std::mutex> lock(listener_mutex_);
        listener = on_document_;
    }
    if (listener) listener(info);
}

void DocumentIngest::Progress(std::int64_t id, const char* phase, int done, int total) {
    std::function<void(const IngestProgress&)> listener;
    {
        std::lock_guard<std::mutex> lock(listener_mutex_);
        listener = on_progress_;
    }
    if (listener) listener({id, phase, done, total});
}

}  // namespace ambient::guidance
