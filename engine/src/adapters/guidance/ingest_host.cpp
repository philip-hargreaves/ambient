#include "adapters/guidance/ingest_host.hpp"

#include <algorithm>
#include <cmath>
#include <nlohmann/json.hpp>
#include <thread>
#include <utility>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

namespace ambient::guidance {
namespace {

using json = nlohmann::json;

constexpr int kCannotOpen = 2;
constexpr int kPassword = 3;
constexpr int kOutputBound = 4;
constexpr int kBadPage = 6;
constexpr std::size_t kMaxPages = 10'000;
constexpr std::size_t kMaxLinesPerPage = 100'000;
constexpr int kMaxPixels = 10'000;
constexpr std::size_t kBmpHeader = 54;

struct Handle {
    HANDLE value = nullptr;
    ~Handle() {
        if (value != nullptr) CloseHandle(value);
    }
    HANDLE Release() {
        return std::exchange(value, nullptr);
    }
};

struct Outcome {
    DWORD exit = 0;
    std::string output;
    bool timed_out = false;
    bool bounded = false;
};

void Pipe(Handle& read, Handle& write) {
    SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE};
    if (!CreatePipe(&read.value, &write.value, &attributes, 0)) {
        throw HostError("crashed", "ingest host pipe failed");
    }
}

Outcome RunHost(const std::filesystem::path& exe, const std::wstring& args,
                std::span<const std::uint8_t> input, const HostLimits& limits) {
    Handle in_read, in_write, out_read, out_write;
    Pipe(in_read, in_write);
    Pipe(out_read, out_write);
    SetHandleInformation(in_write.value, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(out_read.value, HANDLE_FLAG_INHERIT, 0);

    // The host shares the engine's stderr so its counts land in the same log
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = in_read.value;
    startup.hStdOutput = out_write.value;
    startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    SetHandleInformation(startup.hStdError, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
    std::wstring command = L"\"" + exe.wstring() + L"\" " + args;
    // Suspended until it is in the job, so it can never outlive the engine
    PROCESS_INFORMATION info{};
    if (!CreateProcessW(exe.wstring().c_str(), command.data(), nullptr, nullptr, TRUE,
                        CREATE_NO_WINDOW | CREATE_SUSPENDED, nullptr, nullptr, &startup, &info)) {
        throw HostError("crashed", "ingest host failed to start");
    }
    Handle process{info.hProcess};
    Handle thread{info.hThread};
    Handle job{CreateJobObjectW(nullptr, nullptr)};
    if (job.value != nullptr) {
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION job_limits{};
        job_limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                                                      JOB_OBJECT_LIMIT_PROCESS_MEMORY |
                                                      JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        job_limits.BasicLimitInformation.ActiveProcessLimit = 1;
        job_limits.ProcessMemoryLimit = limits.memory_cap;
        SetInformationJobObject(job.value, JobObjectExtendedLimitInformation, &job_limits,
                                sizeof(job_limits));
        AssignProcessToJobObject(job.value, process.value);
    }
    ResumeThread(thread.value);
    CloseHandle(in_read.Release());
    CloseHandle(out_write.Release());

    Outcome outcome;
    std::thread writer([&] {
        std::size_t at = 0;
        while (at < input.size()) {
            DWORD written = 0;
            const auto count =
                static_cast<DWORD>(std::min<std::size_t>(input.size() - at, 1 << 16));
            if (!WriteFile(in_write.value, input.data() + at, count, &written, nullptr)) break;
            at += written;
        }
        CloseHandle(in_write.Release());
    });
    std::thread reader([&] {
        char buffer[1 << 16];
        DWORD count = 0;
        while (ReadFile(out_read.value, buffer, sizeof buffer, &count, nullptr) && count > 0) {
            if (outcome.output.size() + count > limits.output_cap) {
                outcome.bounded = true;
                TerminateJobObject(job.value, 1);
                break;
            }
            outcome.output.append(buffer, count);
        }
    });
    if (WaitForSingleObject(process.value, static_cast<DWORD>(limits.timeout.count())) ==
        WAIT_TIMEOUT) {
        outcome.timed_out = true;
        TerminateJobObject(job.value, 1);
    }
    // The reader ends when the process is gone and its end of the pipe with it
    writer.join();
    reader.join();
    GetExitCodeProcess(process.value, &outcome.exit);
    return outcome;
}

float Fraction(const json& value, float whole) {
    if (!value.is_number() || whole <= 0) return 0;
    return std::clamp(static_cast<float>(value.get<double>() / whole), 0.0F, 1.0F);
}

// The host's JSON as pages, refused when a count or a size is out of range
std::vector<Page> PagesFrom(const std::string& text) {
    json root;
    try {
        root = json::parse(text);
    } catch (const json::exception& e) {
        throw HostError("badOutput", e.what());
    }
    if (!root.is_object() || !root["pages"].is_array() || root["pages"].size() > kMaxPages) {
        throw HostError("badOutput", "pages missing or too many");
    }
    std::vector<Page> pages;
    for (const auto& p : root["pages"]) {
        Page page;
        const auto width = p.value("width", 0.0);
        const auto height = p.value("height", 0.0);
        if (!std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0) {
            throw HostError("badOutput", "page size out of range");
        }
        page.width = static_cast<float>(width);
        page.height = static_cast<float>(height);
        page.rotation = p.value("rotation", 0);
        page.images = p.value("images", 0);
        const auto& lines = p["lines"];
        if (!lines.is_array() || lines.size() > kMaxLinesPerPage) {
            throw HostError("badOutput", "lines missing or too many");
        }
        for (const auto& l : lines) {
            const auto& box = l["box"];
            if (!l["text"].is_string() || !box.is_array() || box.size() != 4) {
                throw HostError("badOutput", "line shape");
            }
            PageLine line;
            line.text = l["text"].get<std::string>();
            line.box = {Fraction(box[0], page.width), Fraction(box[1], page.height),
                        Fraction(box[2], page.width), Fraction(box[3], page.height)};
            if (line.box.right < line.box.left || line.box.bottom < line.box.top) {
                throw HostError("badOutput", "box inverted");
            }
            page.lines.push_back(std::move(line));
        }
        pages.push_back(std::move(page));
    }
    return pages;
}

std::uint32_t Read32(const std::string& s, std::size_t at) {
    std::uint32_t v = 0;
    for (int i = 3; i >= 0; --i) v = (v << 8) | static_cast<unsigned char>(s[at + i]);
    return v;
}

// The host's BMP, refused unless its header and its size agree
Bitmap BitmapFrom(std::string output) {
    if (output.size() < kBmpHeader || output[0] != 'B' || output[1] != 'M') {
        throw HostError("badOutput", "not a bitmap");
    }
    Bitmap bitmap;
    bitmap.width = static_cast<int>(Read32(output, 18));
    bitmap.height = -static_cast<int>(Read32(output, 22));
    if (bitmap.width <= 0 || bitmap.height <= 0 || bitmap.width > kMaxPixels ||
        bitmap.height > kMaxPixels ||
        output.size() != kBmpHeader + static_cast<std::size_t>(bitmap.width) * bitmap.height * 4) {
        throw HostError("badOutput", "bitmap size out of range");
    }
    bitmap.bmp.assign(output.begin(), output.end());
    return bitmap;
}

}  // namespace

IngestHost::IngestHost(std::filesystem::path exe, HostLimits limits)
    : exe_(std::move(exe)), limits_(limits) {}

std::string IngestHost::Run(std::span<const std::uint8_t> document,
                            const std::wstring& args) const {
    const auto outcome = RunHost(exe_, args, document, limits_);
    if (outcome.timed_out) throw HostError("timeout", "ingest host ran out of time");
    if (outcome.bounded) throw HostError("outputBound", "ingest host wrote too much");
    switch (outcome.exit) {
        case 0:
            return outcome.output;
        case kCannotOpen:
            throw HostError("cannotOpen", "not a PDF this reader can open");
        case kPassword:
            throw HostError("password", "the PDF is password protected");
        case kOutputBound:
            throw HostError("outputBound", "ingest host wrote too much");
        case kBadPage:
            throw HostError("badPage", "no such page");
        default:
            throw HostError("crashed", "ingest host exited with " + std::to_string(outcome.exit));
    }
}

std::vector<Page> IngestHost::Extract(std::span<const std::uint8_t> document) const {
    return PagesFrom(Run(document, L"extract"));
}

Bitmap IngestHost::Render(std::span<const std::uint8_t> document, int page, int dpi) const {
    return BitmapFrom(
        Run(document, L"render " + std::to_wstring(page) + L" " + std::to_wstring(dpi)));
}

}  // namespace ambient::guidance
