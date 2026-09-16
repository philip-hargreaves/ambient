#include "adapters/guidance/document_ingest.hpp"

#include <cctype>
#include <chrono>
#include <cstdio>
#include <fstream>
#include <nlohmann/json.hpp>
#include <system_error>

#include "core/document_units.hpp"
#include "core/patient_screen.hpp"
#include "ports/store_error.hpp"

namespace ambient::guidance {
namespace {

constexpr std::uintmax_t kSpareBytes = 200ull << 20;
constexpr auto kPoll = std::chrono::milliseconds(200);
constexpr auto kProgressEvery = std::chrono::milliseconds(250);
constexpr auto kHoldSource = std::chrono::seconds(60);
constexpr std::size_t kHoldBytes = 64u << 20;
constexpr std::size_t kDrawnPages = 8;
constexpr int kRenderDpi = 144;

constexpr const char* kPdf = "application/pdf";

std::string Mime(const std::filesystem::path& path) {
    auto ext = path.extension().string();
    for (auto& c : ext) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    if (ext == ".txt") return "text/plain";
    if (ext == ".md" || ext == ".markdown") return "text/markdown";
    if (ext == ".pdf") return kPdf;
    return "";
}

// The unit's line boxes as fractions of the page, each with its page
std::string BoxesJson(const Unit& unit) {
    nlohmann::json boxes = nlohmann::json::array();
    for (const auto& [page, box] : unit.boxes) {
        boxes.push_back({{"page", page},
                         {"left", box.left},
                         {"top", box.top},
                         {"right", box.right},
                         {"bottom", box.bottom}});
    }
    return boxes.dump();
}

std::string Utf8(const std::filesystem::path& path) {
    const auto u8 = path.u8string();
    return std::string(u8.begin(), u8.end());
}

}  // namespace

DocumentIngest::DocumentIngest(Retriever& retriever, std::filesystem::path root,
                               std::function<bool()> busy, std::filesystem::path host_exe,
                               HostLimits host_limits)
    : retriever_(retriever),
      root_(std::move(root)),
      busy_(std::move(busy)),
      host_(host_exe, host_limits),
      has_host_(!host_exe.empty()),
      scratch_(root_ / "scratch") {
    // Whatever a previous run left decrypted goes before anything else
    std::error_code ignored;
    std::filesystem::remove_all(scratch_, ignored);
    std::filesystem::create_directories(scratch_, ignored);
}

DocumentIngest::~DocumentIngest() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stop_ = true;
        wake_.notify_all();
    }
    if (worker_.joinable()) worker_.join();
    std::error_code ignored;
    std::filesystem::remove_all(scratch_, ignored);
}

// Opening publishes what is already ready, so a restart searches the documents
// from the first list or add
UploadStore& DocumentIngest::Store() {
    if (!store_) {
        if (retriever_.Status().phase != Readiness::Phase::kReady) {
            throw store::StoreError(store::StoreCode::kBusy, "guidance model not ready");
        }
        std::string reason;
        store_ = UploadStore::Open(root_, retriever_.Identity(), reason);
        if (!store_) throw store::StoreError(store::StoreCode::kIo, reason);
        Publish();
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
            if (mime.empty() || (mime == kPdf && !has_host_)) {
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

PageRender DocumentIngest::Render(std::int64_t id, int page, std::int64_t chunk) {
    PageRender out;
    std::string boxes;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        const auto info = Store().Get(id);
        if (info.mime != kPdf) throw store::StoreError(store::StoreCode::kOther, "not a PDF");
        out.pages = info.pages;
        boxes = Store().ReadChunk(id, chunk).boxes;
    }
    const auto source = Source(id);
    Bitmap bitmap;
    try {
        bitmap = host_.Render(*source, page, kRenderDpi);
    } catch (const HostError& e) {
        throw store::StoreError(store::StoreCode::kOther, e.Reason());
    }
    out.path = scratch_ / ("page-" + std::to_string(id) + "-" + std::to_string(page) + ".bmp");
    {
        std::ofstream file(out.path, std::ios::binary | std::ios::trunc);
        file.write(reinterpret_cast<const char*>(bitmap.bmp.data()),
                   static_cast<std::streamsize>(bitmap.bmp.size()));
        if (!file) throw store::StoreError(store::StoreCode::kIo, "the page could not be written");
    }
    Keep(out.path);
    out.width = bitmap.width;
    out.height = bitmap.height;
    out.boxes = std::move(boxes);
    return out;
}

std::filesystem::path DocumentIngest::OpenCopy(std::int64_t id) {
    const auto path = scratch_ / (std::to_string(id) + ".pdf");
    if (std::filesystem::exists(path)) return path;
    const auto source = Source(id);
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file.write(reinterpret_cast<const char*>(source->data()),
               static_cast<std::streamsize>(source->size()));
    if (!file) throw store::StoreError(store::StoreCode::kIo, "the copy could not be written");
    return path;
}

// The decrypted document, kept a minute so page turns do not decrypt again
std::shared_ptr<const std::vector<std::uint8_t>> DocumentIngest::Source(std::int64_t id) {
    std::lock_guard<std::mutex> lock(scratch_mutex_);
    const auto now = std::chrono::steady_clock::now();
    std::size_t held = 0;
    for (auto it = sources_.begin(); it != sources_.end();) {
        if (now - it->second.at > kHoldSource) {
            it = sources_.erase(it);
        } else {
            held += it->second.bytes->size();
            ++it;
        }
    }
    if (const auto it = sources_.find(id); it != sources_.end()) {
        it->second.at = now;
        return it->second.bytes;
    }
    std::vector<std::uint8_t> bytes;
    {
        std::lock_guard<std::mutex> store_lock(store_mutex_);
        bytes = Store().ReadFile(id);
    }
    auto shared = std::make_shared<const std::vector<std::uint8_t>>(std::move(bytes));
    if (held + shared->size() <= kHoldBytes) sources_[id] = {shared, now};
    return shared;
}

// The last few pages drawn stay on disk, older ones go
void DocumentIngest::Keep(const std::filesystem::path& path) {
    std::lock_guard<std::mutex> lock(scratch_mutex_);
    std::erase(drawn_, path);
    drawn_.push_back(path);
    std::error_code ignored;
    while (drawn_.size() > kDrawnPages) {
        std::filesystem::remove(drawn_.front(), ignored);
        drawn_.pop_front();
    }
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
        std::string mime;
        std::vector<std::uint8_t> bytes;
        {
            std::lock_guard<std::mutex> lock(store_mutex_);
            mime = Store().Get(id).mime;
            bytes = Store().ReadFile(id);
        }
        Progress(id, "reading", 0, 0);
        std::vector<Page> pages;
        std::vector<Paragraph> paragraphs;
        if (mime == kPdf) {
            pages = Extract(bytes);
            paragraphs = ParagraphsFromPages(pages);
        } else {
            paragraphs = ParagraphsFromText(std::string(bytes.begin(), bytes.end()));
        }
        std::string text;
        for (const auto& para : paragraphs) text += para.text + "\n\n";
        const int page_count = static_cast<int>(pages.size());
        int pages_without_text = 0;
        for (const auto& page : pages) {
            if (page.lines.empty()) ++pages_without_text;
        }
        Progress(id, "reading", 1, 1);
        if (LooksLikePatientData(text)) {
            std::lock_guard<std::mutex> lock(store_mutex_);
            Store().Fail(id, "patientData", page_count, pages_without_text);
            Notify(Store().Get(id));
            return;
        }
        const auto units = UnitsFromParagraphs(paragraphs);
        if (units.empty() && page_count > 0) {
            std::lock_guard<std::mutex> lock(store_mutex_);
            Store().Fail(id, "noText", page_count, pages_without_text);
            Notify(Store().Get(id));
            return;
        }
        std::vector<UploadChunk> ready;
        auto last = std::chrono::steady_clock::now() - kProgressEvery;
        for (std::size_t i = 0; i < units.size(); ++i) {
            bool paused = false;
            while (busy_ && busy_() && !cancelled()) {
                if (!paused)
                    Progress(id, "paused", static_cast<int>(i), static_cast<int>(units.size()));
                paused = true;
                std::this_thread::sleep_for(kPoll);
            }
            if (cancelled()) {
                remove();
                return;
            }
            const auto& unit = units[i];
            const auto embedding = retriever_.Embed(unit.text);
            ready.push_back({unit.page, unit.number, unit.section, unit.text, embedding.vector,
                             BoxesJson(unit)});
            const auto now = std::chrono::steady_clock::now();
            if (paused || now - last >= kProgressEvery || i + 1 == units.size()) {
                Progress(id, "preparing", static_cast<int>(i + 1), static_cast<int>(units.size()));
                last = now;
            }
        }
        std::lock_guard<std::mutex> lock(store_mutex_);
        Store().Finish(id, ready, page_count, pages_without_text);
        Publish();
        Notify(Store().Get(id));
    } catch (const HostError& e) {
        std::fprintf(stderr, "ambient-engine: document %lld %s: %s\n", static_cast<long long>(id),
                     e.Reason().c_str(), e.what());
        std::lock_guard<std::mutex> lock(store_mutex_);
        Store().Fail(id, e.Reason());
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

// A crash gets one more try. Any other refusal stands
std::vector<Page> DocumentIngest::Extract(const std::vector<std::uint8_t>& bytes) {
    try {
        return host_.Extract(bytes);
    } catch (const HostError& e) {
        if (e.Reason() != "crashed") throw;
        return host_.Extract(bytes);
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
            snapshot->rows.push_back({doc.id, chunk.page, doc.pages, doc.name,
                                      std::move(chunk.number), std::move(chunk.section),
                                      std::move(chunk.text), std::move(chunk.boxes), doc.added_at});
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
