#include "adapters/system/process_scan.hpp"

#include <gtest/gtest.h>

#include <chrono>
#include <cwchar>

namespace ambient::system {
namespace {

TEST(ProcessScan, ReturnsAtOnceWhenNoSuchProcessRuns) {
    const auto t0 = std::chrono::steady_clock::now();
    EXPECT_TRUE(WaitUntilGone(L"ambient_no_such_process.exe", std::chrono::seconds(5)));
    EXPECT_LT(std::chrono::steady_clock::now() - t0, std::chrono::seconds(1));
}

// The test runner itself is a process that stays for the whole test
TEST(ProcessScan, GivesUpAtTheBoundOnAProcessThatStays) {
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* slash = wcsrchr(path, L'\\');
    const wchar_t* image = slash != nullptr ? slash + 1 : path;

    const auto t0 = std::chrono::steady_clock::now();
    EXPECT_FALSE(WaitUntilGone(image, std::chrono::milliseconds(300)));
    EXPECT_GE(std::chrono::steady_clock::now() - t0, std::chrono::milliseconds(250));
}

}  // namespace
}  // namespace ambient::system
