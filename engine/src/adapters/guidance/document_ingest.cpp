#include "adapters/guidance/document_ingest.hpp"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstdio>
#include <fstream>
#include <nlohmann/json.hpp>
#include <system_error>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
// clang-format off
#include <windows.h>
#include <objbase.h>
#include <shellapi.h>
#include <shobjidl_core.h>
// clang-format on

#include "core/document_units.hpp"
#include "core/patient_screen.hpp"
#include "ports/store_error.hpp"

namespace ambient::guidance {
namespace {

constexpr std::uintmax_t kSpareBytes = 200ull << 20;
constexpr auto kPoll = std::chrono::milliseconds(200);
constexpr auto kProgressEvery = std::chrono::milliseconds(250);
constexpr std::size_t kDrawnPages = 8;
constexpr int kRenderDpi = 144;
constexpr int kMaxDepth = 3;

constexpr const char* kPdf = "application/pdf";

constexpr const char* kReadMeText =
    "Guidelines for Ambient\n\n"
    "PDF, text and Markdown files in this folder are read by Ambient and searched after each\n"
    "consultation note, beside the installed guidance. Passages it finds are shown with the\n"
    "page they came from.\n\n"
    "Add a file to start searching it. Replace a file to update it. Delete a file, or move it\n"
    "out, to stop searching it. Ambient notices within a few seconds and a new document takes\n"
    "about ten seconds to read. Ambient never changes your files. If you delete this folder,\n"
    "Ambient makes it again, empty.\n\n"
    "Do not add patient-identifiable documents. Text from these files is shown beside other\n"
    "consultations and kept with their notes. If this folder is inside OneDrive, its files\n"
    "sync like the rest of your Documents.\n\n"
    "Only add documents you are entitled to use.\n";

std::string LowerExtension(const std::filesystem::path& path) {
    auto ext = path.extension().string();
    for (auto& c : ext) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return ext;
}

std::string Mime(const std::filesystem::path& path) {
    const auto ext = LowerExtension(path);
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

std::vector<std::uint8_t> ReadAll(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open()) throw store::StoreError(store::StoreCode::kIo, "cannot read the file");
    return {std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
}

std::int64_t Ticks(std::filesystem::file_time_type time) {
    return time.time_since_epoch().count();
}

// The open does not share writing, so a file another program is still writing
// refuses it
bool Unlocked(const std::filesystem::path& path) {
    const HANDLE handle =
        CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr,
                    OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return false;
    CloseHandle(handle);
    return true;
}

bool HiddenOrSystem(const std::filesystem::path& path) {
    const auto attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
           (attributes & (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM)) != 0;
}

// Copies in progress, Office locks and downloads in the making
bool Transient(const std::filesystem::path& path) {
    const auto name = path.filename().string();
    const auto ext = LowerExtension(path);
    return name.starts_with("~") || name.starts_with(".") || ext == ".tmp" ||
           ext == ".crdownload" || ext == ".partial";
}

void RequireInFolder(const std::filesystem::path& path) {
    std::error_code ec;
    if (!std::filesystem::exists(path, ec)) {
        throw store::StoreError(store::StoreCode::kNotFound,
                                "the document is no longer in the guidelines folder");
    }
}

std::filesystem::path IndexPath(const std::filesystem::path& root) {
    std::error_code ignored;
    std::filesystem::create_directories(root, ignored);
    return root / kIndexFile;
}

DocumentInfo Removed(DocumentInfo info) {
    info.state = "removed";
    return info;
}

}  // namespace

void RecycleFile(const std::filesystem::path& path) {
    struct Apartment {
        HRESULT hr;
        Apartment() : hr(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) {}
        ~Apartment() {
            if (SUCCEEDED(hr)) CoUninitialize();
        }
    } com;
    IFileOperation* op = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOperation, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&op)))) {
        throw store::StoreError(store::StoreCode::kOther, "the Recycle Bin is not available");
    }
    IShellItem* item = nullptr;
    const auto fail = [&](const char* what) {
        if (item != nullptr) item->Release();
        op->Release();
        throw store::StoreError(store::StoreCode::kBusy, what);
    };
    if (FAILED(op->SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT |
                                     FOF_NOERRORUI | FOFX_RECYCLEONDELETE))) {
        fail("the Recycle Bin is not available");
    }
    if (FAILED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(&item)))) {
        fail("the file was not found");
    }
    if (FAILED(op->DeleteItem(item, nullptr))) fail("the file could not be removed");
    const HRESULT hr = op->PerformOperations();
    BOOL aborted = FALSE;
    op->GetAnyOperationsAborted(&aborted);
    item->Release();
    op->Release();
    if (FAILED(hr) || aborted) {
        throw store::StoreError(store::StoreCode::kBusy,
                                "the file could not be removed, it may be open in another program");
    }
}

DocumentIngest::DocumentIngest(Retriever& retriever, std::filesystem::path folder,
                               std::filesystem::path root, std::function<bool()> busy,
                               std::filesystem::path host_exe, HostLimits host_limits,
                               Discard discard, std::chrono::milliseconds scan_every)
    : retriever_(retriever),
      folder_(std::move(folder)),
      scratch_(root / "scratch"),
      busy_(std::move(busy)),
      host_(host_exe, host_limits),
      has_host_(!host_exe.empty()),
      discard_(std::move(discard)),
      scan_every_(scan_every),
      index_(IndexPath(root)) {
    std::error_code ignored;
    std::filesystem::remove_all(scratch_, ignored);
    std::filesystem::create_directories(scratch_, ignored);
    worker_ = std::thread([this] { Work(); });
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

std::filesystem::path DocumentIngest::Absolute(const std::string& relative) const {
    return folder_ / std::filesystem::path(std::u8string(relative.begin(), relative.end()));
}

bool DocumentIngest::Supported(const std::string& mime) const {
    return !mime.empty() && (mime != kPdf || has_host_);
}

Accepted DocumentIngest::Add(const std::vector<std::filesystem::path>& paths) {
    Accepted out;
    const auto skip = [&out](const std::filesystem::path& path, const char* reason) {
        out.skipped.push_back({Utf8(path), reason});
    };
    std::set<std::string> fresh;
    std::error_code ec;
    std::filesystem::create_directories(folder_, ec);
    for (const auto& path : paths) {
        const auto bytes = std::filesystem::file_size(path, ec);
        if (ec || bytes == 0) {
            skip(path, "unreadable");
            continue;
        }
        if (!Supported(Mime(path))) {
            skip(path, "unsupported");
            continue;
        }
        const auto target = folder_ / path.filename();
        if (!std::filesystem::equivalent(path, target, ec)) {
            const auto space = std::filesystem::space(folder_, ec);
            if (!ec && space.available < bytes + kSpareBytes) {
                skip(path, "noSpace");
                continue;
            }
            std::filesystem::copy_file(path, target,
                                       std::filesystem::copy_options::overwrite_existing, ec);
            if (ec) {
                skip(path, "unreadable");
                continue;
            }
        }
        fresh.insert(Utf8(path.filename()));
    }
    Scan(fresh);
    std::lock_guard<std::mutex> lock(store_mutex_);
    for (auto& document : index_.List()) {
        for (const auto& held : index_.PathsOf(document.id)) {
            if (fresh.contains(held)) {
                out.documents.push_back(document);
                break;
            }
        }
    }
    return out;
}

// Listing never waits on the folder: the worker scans on the poke
Listing DocumentIngest::List() {
    wake_.notify_all();
    std::lock_guard<std::mutex> lock(store_mutex_);
    return {folder_, found_, unsupported_, index_.List()};
}

void DocumentIngest::Remove(std::int64_t id) {
    std::vector<std::string> paths;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        paths = index_.PathsOf(id);
    }
    if (paths.empty()) {
        throw store::StoreError(store::StoreCode::kNotFound, "no document " + std::to_string(id));
    }
    for (const auto& path : paths) discard_(Absolute(path));
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (id == current_) cancel_ = true;
        std::erase_if(queue_, [id](const Queued& item) { return item.id == id; });
    }
    Scan();
}

std::size_t DocumentIngest::RemoveAll() {
    std::size_t count = 0;
    std::vector<std::string> paths;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        for (const auto& document : index_.List()) {
            ++count;
            for (auto& path : index_.PathsOf(document.id)) paths.push_back(std::move(path));
        }
    }
    for (const auto& path : paths) discard_(Absolute(path));
    {
        std::lock_guard<std::mutex> lock(mutex_);
        cancel_ = true;
        queue_.clear();
    }
    Scan();
    return count;
}

PageRender DocumentIngest::Render(std::int64_t id, int page, std::int64_t chunk) {
    PageRender out;
    std::filesystem::path path;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        const auto info = index_.Get(id);
        if (info.mime != kPdf) throw store::StoreError(store::StoreCode::kOther, "not a PDF");
        out.pages = info.pages;
        out.boxes = index_.ReadChunk(id, chunk).boxes;
        path = Absolute(info.path);
    }
    RequireInFolder(path);
    Bitmap bitmap;
    try {
        bitmap = host_.Render(ReadAll(path), page, kRenderDpi);
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
    return out;
}

std::filesystem::path DocumentIngest::Path(std::int64_t id) {
    std::filesystem::path path;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        path = Absolute(index_.Get(id).path);
    }
    RequireInFolder(path);
    return path;
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

// The folder is the truth: a file that went takes its document, a file that
// is new or changed by content starts one. A file still being written waits
// for the next scan, as does one that has just appeared unless Add put it there
void DocumentIngest::Scan(const std::set<std::string>& fresh) {
    std::vector<Queued> queued;
    std::vector<DocumentInfo> changed;
    {
        std::lock_guard<std::mutex> lock(store_mutex_);
        std::error_code ec;
        if (!std::filesystem::is_directory(folder_, ec)) {
            if (!std::filesystem::is_directory(folder_.parent_path(), ec)) {
                found_ = false;
                return;
            }
            std::filesystem::create_directories(folder_, ec);
        }
        found_ = true;
        if (!std::filesystem::exists(folder_ / kReadMe, ec)) {
            std::ofstream(folder_ / kReadMe, std::ios::binary) << kReadMeText;
        }

        std::map<std::string, Seen> present;
        int unsupported = 0;
        std::filesystem::recursive_directory_iterator it(
            folder_, std::filesystem::directory_options::skip_permission_denied, ec);
        for (const std::filesystem::recursive_directory_iterator end; !ec && it != end;
             it.increment(ec)) {
            const auto& entry = *it;
            const bool symlink = entry.is_symlink(ec);
            if (it.depth() >= kMaxDepth || symlink) it.disable_recursion_pending();
            if (symlink || !entry.is_regular_file(ec)) continue;
            if (HiddenOrSystem(entry.path()) || Transient(entry.path())) continue;
            const auto relative = Utf8(entry.path().lexically_relative(folder_));
            if (relative == kReadMe) continue;
            if (!Supported(Mime(entry.path()))) {
                ++unsupported;
                continue;
            }
            present[relative] = {static_cast<std::int64_t>(entry.file_size(ec)),
                                 Ticks(entry.last_write_time(ec))};
        }
        unsupported_ = unsupported;

        std::map<std::string, IndexedFile> known;
        for (auto& file : index_.Files()) known.emplace(file.path, std::move(file));

        // New and changed files first, so a rename moves the document to its new
        // path before the old path is released and never re-indexes
        const auto now = std::filesystem::file_time_type::clock::now();
        for (const auto& [path, seen] : present) {
            const auto same = [size = seen.size, modified = seen.modified](const auto& other) {
                return other.size == size && other.modified == modified;
            };
            const auto was = known.find(path);
            if (was != known.end() && same(was->second)) {
                pending_.erase(path);
                continue;
            }
            const auto written = std::filesystem::file_time_type(
                std::filesystem::file_time_type::duration(seen.modified));
            const auto held = pending_.find(path);
            const bool still = held != pending_.end() && same(held->second);
            const bool settled = fresh.contains(path) || still || now - written > scan_every_;
            const auto full = Absolute(path);
            if (!settled || !Unlocked(full)) {
                pending_[path] = seen;
                continue;
            }
            pending_.erase(path);
            std::vector<std::uint8_t> bytes;
            try {
                bytes = ReadAll(full);
            } catch (const store::StoreError&) {
                continue;
            }
            if (bytes.empty()) continue;
            DocumentInfo previous;
            if (was != known.end()) {
                try {
                    previous = index_.Get(was->second.document);
                } catch (const store::StoreError&) {  // NOLINT(bugprone-empty-catch)
                }
            }
            const auto held_now =
                index_.Hold({path, 0, seen.size, seen.modified}, Sha256Hex(bytes), Mime(full));
            if (held_now.released != 0 && held_now.released == previous.id) {
                changed.push_back(Removed(previous));
            }
            if (held_now.added) {
                queued.push_back({held_now.document, path});
                changed.push_back(index_.Get(held_now.document));
            }
        }

        // Then files that went: a document no path holds any more is dropped
        for (const auto& [path, file] : known) {
            if (present.contains(path)) continue;
            DocumentInfo info;
            try {
                info = index_.Get(file.document);
            } catch (const store::StoreError&) {
                index_.Release(path);
                continue;
            }
            if (index_.Release(path) != 0) changed.push_back(Removed(info));
        }

        if (std::ranges::any_of(changed, [](const DocumentInfo& info) {
                return info.state == "removed" && info.chunks > 0;
            })) {
            Publish();
        }
    }
    for (const auto& info : changed) Notify(info);
    std::lock_guard<std::mutex> lock(mutex_);
    for (auto& item : queued) queue_.push_back(std::move(item));
    if (!queued.empty()) wake_.notify_all();
}

bool DocumentIngest::Cancelled() {
    std::lock_guard<std::mutex> lock(mutex_);
    return cancel_ || stop_;
}

// Idle time scans the folder. A queued document waits for the embedder. The
// index adopts it once, which publishes what an earlier run left ready
void DocumentIngest::Work() {
    for (;;) {
        Queued item;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            wake_.wait_for(lock, scan_every_, [this] { return stop_ || !queue_.empty(); });
            if (stop_) return;
            const bool ready = retriever_.Status().phase == Readiness::Phase::kReady;
            if (ready) {
                lock.unlock();
                std::lock_guard<std::mutex> store_lock(store_mutex_);
                if (!index_.Adopted()) {
                    index_.Adopt(retriever_.Identity());
                    Publish();
                }
                lock.lock();
            }
            if (queue_.empty()) {
                lock.unlock();
                Scan();
                continue;
            }
            if (!ready) {
                lock.unlock();
                std::this_thread::sleep_for(kPoll);
                continue;
            }
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

// The file is read in place, so one that changed or went since the scan is
// left to the next scan
void DocumentIngest::Index(const Queued& item) {
    const auto id = item.id;
    const auto path = Absolute(item.path);
    const auto fail = [&](const std::string& error, int pages = 0, int pages_without_text = 0) {
        std::lock_guard<std::mutex> lock(store_mutex_);
        try {
            index_.Fail(id, error, pages, pages_without_text);
            Notify(index_.Get(id));
        } catch (const store::StoreError&) {  // NOLINT(bugprone-empty-catch)
        }
    };
    try {
        std::error_code ec;
        const auto size = static_cast<std::int64_t>(std::filesystem::file_size(path, ec));
        const auto modified = Ticks(std::filesystem::last_write_time(path, ec));
        if (ec) return;
        {
            std::lock_guard<std::mutex> lock(store_mutex_);
            const auto files = index_.Files();
            if (!std::ranges::any_of(files, [&](const IndexedFile& file) {
                    return file.path == item.path && file.document == id && file.size == size &&
                           file.modified == modified;
                })) {
                return;
            }
        }
        const auto bytes = ReadAll(path);
        const auto mime = Mime(path);
        Progress(id, "reading", 0, 0);
        std::vector<Page> pages;
        std::vector<Unit> units;
        if (mime == kPdf) {
            pages = Extract(bytes);
            units = UnitsFromPages(pages);
        } else {
            units = UnitsFromText(std::string(bytes.begin(), bytes.end()));
        }
        std::string text;
        for (const auto& unit : units) text += unit.text + "\n\n";
        const int page_count = static_cast<int>(pages.size());
        const int pages_without_text = static_cast<int>(
            std::ranges::count_if(pages, [](const Page& page) { return page.lines.empty(); }));
        Progress(id, "reading", 1, 1);
        if (LooksLikePatientData(text)) {
            fail("patientData", page_count, pages_without_text);
            return;
        }
        if (units.empty() && page_count > 0) {
            fail("noText", page_count, pages_without_text);
            return;
        }
        std::vector<IndexChunk> ready;
        auto last = std::chrono::steady_clock::now() - kProgressEvery;
        for (std::size_t i = 0; i < units.size(); ++i) {
            bool paused = false;
            while (busy_ && busy_() && !Cancelled()) {
                if (!paused)
                    Progress(id, "paused", static_cast<int>(i), static_cast<int>(units.size()));
                paused = true;
                std::this_thread::sleep_for(kPoll);
            }
            if (Cancelled()) return;
            const auto& unit = units[i];
            const auto embedding = retriever_.Embed(unit.text);
            ready.push_back({static_cast<std::int64_t>(i), unit.page, unit.number, unit.section,
                             unit.text, embedding.vector, BoxesJson(unit)});
            const auto now = std::chrono::steady_clock::now();
            if (paused || now - last >= kProgressEvery || i + 1 == units.size()) {
                Progress(id, "preparing", static_cast<int>(i + 1), static_cast<int>(units.size()));
                last = now;
            }
        }
        std::lock_guard<std::mutex> lock(store_mutex_);
        if (Cancelled() || index_.PathsOf(id).empty()) return;
        index_.Finish(id, ready, page_count, pages_without_text);
        Publish();
        Notify(index_.Get(id));
    } catch (const HostError& e) {
        std::fprintf(stderr, "ambient-engine: document %lld %s: %s\n", static_cast<long long>(id),
                     e.Reason().c_str(), e.what());
        fail(e.Reason());
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: document %lld failed: %s\n",
                     static_cast<long long>(id), e.what());
        fail("unreadable");
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
    snapshot->dim = index_.Embedder().dim;
    for (const auto& doc : index_.List()) {
        if (doc.state != "ready") continue;
        Corpus corpus;
        corpus.id = "upload:" + std::to_string(doc.id);
        corpus.name = doc.name;
        corpus.source = "upload";
        corpus.sha256 = doc.sha256;
        corpus.embedder = index_.Embedder().id;
        corpus.chunks = static_cast<int>(doc.chunks);
        corpus.built_at = doc.indexed_at;
        snapshot->documents.push_back(std::move(corpus));
        for (auto& chunk : index_.ReadChunks(doc.id)) {
            snapshot->matrix.insert(snapshot->matrix.end(), chunk.vector.begin(),
                                    chunk.vector.end());
            snapshot->rows.push_back({doc.id, chunk.ord, chunk.page, doc.pages, doc.name,
                                      std::move(chunk.number), std::move(chunk.section),
                                      std::move(chunk.text), doc.added_at});
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
