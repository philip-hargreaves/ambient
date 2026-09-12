#pragma once

#include <filesystem>
#include <span>
#include <string>
#include <vector>

#include "adapters/guidance/chunker.hpp"
#include "adapters/guidance/embedder.hpp"

namespace ambient::guidance {

// What the indexer knows about a corpus that the chunks do not carry
struct CorpusSpec {
    std::string id;  // carries the fetch date, "nice-2026-08-25"
    std::string name;
    std::string licence;
    std::string attribution;
    std::string source;  // "nice" or "text"
    EmbedderIdentity embedder;
    std::string built_at;  // ISO 8601 UTC
    std::string builder;
};

// Writes corpus.db and manifest.json into dir. The database is built as a
// temp file with the journal off, vacuumed, switched to a rollback journal and
// renamed into place, so dir holds either the old corpus or the new one.
// vectors is row-major, chunks.size() by spec.dim, unit length. Throws on any
// failure and leaves dir as it was
void BuildCorpus(const std::filesystem::path& dir, const CorpusSpec& spec,
                 const std::vector<Chunk>& chunks, std::span<const float> vectors);

}  // namespace ambient::guidance
