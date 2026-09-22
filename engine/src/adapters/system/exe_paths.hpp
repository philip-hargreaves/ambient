#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <filesystem>

namespace ambient::system {

inline std::filesystem::path ExeDir() {
    wchar_t exe_path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, exe_path, MAX_PATH);
    return std::filesystem::path(exe_path).parent_path();
}

// Beside the executable is the production shape (the package directory);
// packaged debug runs put the executable one level below the layout
inline std::filesystem::path DefaultModelsRoot() {
    const auto exe_dir = ExeDir();
    auto root = exe_dir / "models";
    if (!std::filesystem::exists(root)) root = exe_dir.parent_path() / "models";
    return root;
}

}  // namespace ambient::system
