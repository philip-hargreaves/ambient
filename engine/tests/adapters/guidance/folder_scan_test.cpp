#include "adapters/guidance/folder_scan.hpp"

#include <gtest/gtest.h>

#include <filesystem>
#include <fstream>
#include <set>
#include <string>

namespace clinicavt::guidance {
namespace {

struct TempFolder {
    std::filesystem::path path;
    TempFolder() {
        path = std::filesystem::temp_directory_path() / "clinicavt-folder-scan-test";
        std::filesystem::remove_all(path);
        std::filesystem::create_directories(path);
    }
    ~TempFolder() {
        std::error_code ignored;
        std::filesystem::remove_all(path, ignored);
    }
    void Put(const std::string& relative, const std::string& text = "x") {
        std::filesystem::create_directories((path / relative).parent_path());
        std::ofstream(path / relative, std::ios::binary) << text;
    }
};

TEST(FolderScan, MimeByExtensionCaseInsensitive) {
    EXPECT_EQ(Mime("a.PDF"), "application/pdf");
    EXPECT_EQ(Mime("a.md"), "text/markdown");
    EXPECT_EQ(Mime("a.txt"), "text/plain");
    EXPECT_EQ(Mime("a.docx"), "");
}

TEST(FolderScan, ListsSupportedFilesToTheDepthAndCountsTheRest) {
    TempFolder folder;
    folder.Put("one.pdf", "pdf");
    folder.Put("Instructions.txt");
    folder.Put("~lock.pdf");
    folder.Put("notes.docx");
    folder.Put("a/two.txt");
    folder.Put("a/b/three.md");
    folder.Put("a/b/c/four.md");
    folder.Put("a/b/c/d/five.md");  // one deeper than the limit

    const auto listing = ListFolder(
        folder.path, 3, [](const std::string& mime) { return !mime.empty(); }, "Instructions.txt");

    std::set<std::string> paths;
    for (const auto& file : listing.files) paths.insert(file.path);
    EXPECT_EQ(paths, (std::set<std::string>{"one.pdf", "a\\two.txt", "a\\b\\three.md",
                                            "a\\b\\c\\four.md"}));
    EXPECT_EQ(listing.unsupported, 1);
    for (const auto& file : listing.files) {
        EXPECT_GT(file.size, 0);
        EXPECT_NE(file.modified, 0);
    }
}

TEST(FolderScan, AMissingFolderListsNothing) {
    const auto listing = ListFolder(
        "C:/no/such/folder/for/clinicavt", 3, [](const std::string&) { return true; }, "");
    EXPECT_TRUE(listing.files.empty());
    EXPECT_EQ(listing.unsupported, 0);
}

}  // namespace
}  // namespace clinicavt::guidance
