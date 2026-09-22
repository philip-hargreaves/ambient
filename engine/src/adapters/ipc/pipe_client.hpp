#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <algorithm>
#include <cstddef>
#include <optional>
#include <string>
#include <string_view>

#include "adapters/ipc/framing.hpp"

namespace ambient::ipc {

// The client end of a private named pipe: whole frames out, frames in
// through a decoder. Synchronous handle. The caller serialises its readers
class PipeClient {
   public:
    PipeClient() = default;
    ~PipeClient() {
        Close();
    }
    PipeClient(const PipeClient&) = delete;
    PipeClient& operator=(const PipeClient&) = delete;

    // False while nobody serves `path` yet
    bool Open(const std::wstring& path) {
        Close();
        pipe_ = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
                            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
        return IsOpen();
    }

    bool IsOpen() const {
        return pipe_ != INVALID_HANDLE_VALUE;
    }

    void Close() {
        if (IsOpen()) CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
        decoder_ = FrameDecoder{};
    }

    // The process serving the pipe, or 0 when it cannot be told
    DWORD ServerPid() const {
        ULONG pid = 0;
        if (!IsOpen() || !GetNamedPipeServerProcessId(pipe_, &pid)) return 0;
        return pid;
    }

    // False when the pipe is gone
    bool Write(std::string_view frame) {
        DWORD written = 0;
        return IsOpen() &&
               WriteFile(pipe_, frame.data(), static_cast<DWORD>(frame.size()), &written,
                         nullptr) &&
               written == frame.size();
    }

    enum class Poll { kNothing, kRead, kGone };

    // Reads what is waiting, at most `max` bytes, into the decoder. Never blocks
    Poll Read(std::size_t max = 64 * 1024) {
        DWORD available = 0;
        if (!IsOpen() || !PeekNamedPipe(pipe_, nullptr, 0, nullptr, &available, nullptr)) {
            return Poll::kGone;
        }
        if (available == 0) return Poll::kNothing;
        std::string buffer(std::min<std::size_t>(available, max), '\0');
        DWORD read = 0;
        if (!ReadFile(pipe_, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr) ||
            read == 0) {
            return Poll::kGone;
        }
        decoder_.Push({buffer.data(), read});
        return Poll::kRead;
    }

    // The next whole frame already read, if any
    std::optional<std::string> NextFrame() {
        return decoder_.Next();
    }

   private:
    HANDLE pipe_ = INVALID_HANDLE_VALUE;
    FrameDecoder decoder_;
};

}  // namespace ambient::ipc
