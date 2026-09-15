#pragma once

#include <cstdint>
#include <filesystem>
#include <functional>
#include <string>
#include <vector>

namespace ambient::guidance {

// A row of the added-document list. Plaintext in the store except the name
struct DocumentInfo {
    std::int64_t id = 0;
    std::string name;
    std::string mime;
    std::string state;  // indexing, ready, failed, stale, removed
    std::string error;  // a reason code
    std::string added_at;
    std::string indexed_at;
    std::int64_t bytes = 0;
    int pages = 0;
    int pages_without_text = 0;
    std::int64_t chunks = 0;
};

struct IngestProgress {
    std::int64_t id = 0;
    std::string phase;  // reading, preparing, paused
    int done = 0;
    int total = 0;
};

struct Skipped {
    std::string path;
    std::string reason;  // duplicate, unsupported, noSpace, unreadable, patientData
};

struct Accepted {
    std::vector<DocumentInfo> documents;
    std::vector<Skipped> skipped;
};

// The clinician's added documents: accepted at once, read and embedded on a
// thread of their own, paused while a consultation runs. The listeners hear
// progress and every change of state, including removal
class IDocumentIngest {
   public:
    virtual ~IDocumentIngest() = default;
    virtual Accepted Add(const std::vector<std::filesystem::path>& paths) = 0;
    virtual void Cancel() = 0;
    virtual std::vector<DocumentInfo> List() = 0;
    virtual void Remove(std::int64_t id) = 0;
    virtual std::size_t RemoveAll() = 0;
    virtual void SetListener(std::function<void(const IngestProgress&)> progress,
                             std::function<void(const DocumentInfo&)> document) = 0;
};

}  // namespace ambient::guidance
