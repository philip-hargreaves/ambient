#pragma once

#include <cstdint>
#include <filesystem>
#include <functional>
#include <string>
#include <vector>

namespace clinicavt::guidance {

// A document in the guidelines folder, one per content
struct DocumentInfo {
    std::int64_t id = 0;
    std::string name;    // the file's stem
    std::string path;    // relative to the folder, the shortest when several hold it
    std::string sha256;  // of the file, hex
    std::string mime;
    std::string state;  // indexing, ready, failed, removed
    std::string error;  // a reason code
    std::string added_at;
    std::string indexed_at;
    std::int64_t bytes = 0;
    int pages = 0;
    int pages_without_text = 0;
    std::int64_t chunks = 0;
};

// The folder and what it holds at the last scan
struct Listing {
    std::filesystem::path folder;
    bool found = true;    // false when the folder and its parent were out of reach
    int unsupported = 0;  // files of other types
    std::vector<DocumentInfo> documents;
};

struct IngestProgress {
    std::int64_t id = 0;
    std::string phase;  // reading, preparing, paused
    int done = 0;
    int total = 0;
};

struct Skipped {
    std::string path;
    std::string reason;  // unsupported, noSpace, unreadable
};

struct Accepted {
    std::vector<DocumentInfo> documents;
    std::vector<Skipped> skipped;
};

// One page drawn for the page view: a BMP under the scratch folder
struct PageRender {
    std::filesystem::path path;
    int width = 0;
    int height = 0;
    int pages = 0;      // the document's page count
    std::string boxes;  // the chunk's line boxes as page fractions, JSON
};

// The clinician's guidelines folder: what is in it is searched. Files are
// picked up, read and embedded on a thread of their own, paused while a
// consultation runs, and dropped when their file goes. The listeners hear
// progress and every change of state, including removal
class IDocumentIngest {
   public:
    virtual ~IDocumentIngest() = default;
    // Copies files into the folder
    virtual Accepted Add(const std::vector<std::filesystem::path>& paths) = 0;
    virtual Listing List() = 0;
    // Every file holding the document goes to the Recycle Bin
    virtual void Remove(std::int64_t id) = 0;
    // Every document goes, and the count of them comes back
    virtual std::size_t RemoveAll() = 0;
    virtual PageRender Render(std::int64_t id, int page, std::int64_t chunk) = 0;
    // The file in the guidelines folder, for the PDF viewer
    virtual std::filesystem::path Path(std::int64_t id) = 0;
    virtual void SetListener(std::function<void(const IngestProgress&)> progress,
                             std::function<void(const DocumentInfo&)> document) = 0;
};

}  // namespace clinicavt::guidance
