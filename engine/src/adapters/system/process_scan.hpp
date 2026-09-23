#pragma once

#include <chrono>
#include <cstddef>
#include <cwchar>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
// clang-format off
#include <tlhelp32.h>
// clang-format on

namespace ambient::system {

// Live processes with this image name: detects a note host left wedged
// by an earlier engine
inline std::size_t CountProcesses(const wchar_t* image) {
    std::size_t count = 0;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    for (BOOL ok = Process32FirstW(snapshot, &entry); ok; ok = Process32NextW(snapshot, &entry)) {
        if (_wcsicmp(entry.szExeFile, image) == 0) ++count;
    }
    CloseHandle(snapshot);
    return count;
}

// True once no process with this image is left, false at the bound. A host
// whose engine has died takes a moment to leave the process table
inline bool WaitUntilGone(const wchar_t* image, std::chrono::milliseconds bound) {
    const auto deadline = std::chrono::steady_clock::now() + bound;
    while (CountProcesses(image) > 0) {
        if (std::chrono::steady_clock::now() >= deadline) return false;
        Sleep(100);
    }
    return true;
}

}  // namespace ambient::system
