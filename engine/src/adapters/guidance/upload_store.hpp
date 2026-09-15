#pragma once

#include <cstdint>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

#include "adapters/guidance/embedder.hpp"
#include "adapters/storage/chunk_cipher.hpp"
#include "adapters/storage/db.hpp"

namespace ambient::guidance {

inline constexpr std::uint32_t kUploadApplicationId = 0x414D4255;  // "AMBU"
inline constexpr int kUploadFormat = 1;
inline constexpr const char* kUploadFile = "uploads.db";

// A row of the list. Everything here is plaintext in the file except the name
struct DocumentInfo {
    std::int64_t id = 0;
    std::string name;
    std::string mime;
    std::string state;  // indexing, ready, failed, stale
    std::string error;  // a reason code
    std::string added_at;
    std::string indexed_at;
    std::int64_t bytes = 0;
    int pages = 0;
    int pages_without_text = 0;
    std::int64_t chunks = 0;
};

struct UploadChunk {
    int page = 0;
    std::string section;
    std::string text;
    std::vector<float> vector;  // unit length, the store's dimension
    std::string boxes;          // line boxes as page fractions, JSON
};

// The clinician's added documents: uploads.db and files/ under root, every
// piece of content sealed under a per-document key that the store key wraps.
// Opening creates an empty store, erases documents left half indexed, and
// marks the ready ones stale when the staged embedder has changed. One thread
// at a time
class UploadStore {
   public:
    static std::unique_ptr<UploadStore> Open(const std::filesystem::path& root,
                                             const EmbedderIdentity& embedder, std::string& reason);

    std::vector<DocumentInfo> List();
    DocumentInfo Get(std::int64_t id);

    // Copies and seals the file under a new key and inserts an indexing row.
    // A file already held comes back with its id and added false
    struct Added {
        std::int64_t id = 0;
        bool added = false;
    };
    Added Add(const std::filesystem::path& file, const std::string& name, const std::string& mime);

    // The passages land in one transaction and the row turns ready
    void Finish(std::int64_t id, const std::vector<UploadChunk>& chunks, int pages,
                int pages_without_text);
    void Fail(std::int64_t id, const std::string& error);

    std::vector<UploadChunk> ReadChunks(std::int64_t id);
    std::vector<std::uint8_t> ReadFile(std::int64_t id);

    // Row, passages, file and key go together; what lingers in free pages is
    // ciphertext without a key
    void Remove(std::int64_t id);
    std::size_t RemoveAll();

    const EmbedderIdentity& Embedder() const {
        return embedder_;
    }

   private:
    UploadStore(store::Db db, store::ChunkCipher key, EmbedderIdentity embedder,
                std::filesystem::path files);

    store::ChunkCipher KeyFor(std::int64_t id);
    void RemoveFile(std::int64_t id);
    std::filesystem::path FilePath(std::int64_t id) const;

    store::Db db_;
    store::ChunkCipher key_;
    EmbedderIdentity embedder_;
    std::filesystem::path files_;
};

}  // namespace ambient::guidance
