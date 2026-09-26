#pragma once

#include <gtest/gtest.h>
#include <process.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <map>
#include <nlohmann/json.hpp>
#include <set>
#include <stdexcept>
#include <string>
#include <system_error>
#include <vector>

#include "adapters/guidance/embedder.hpp"
#include "tools/corpus/corpus_builder.hpp"

// The invented fixture corpus and notes, and a corpus directory built from
// them with any embedder
namespace clinicavt::guidance::fixture {

// One directory per test and process, so parallel runs never share a path
struct TempDir {
    std::filesystem::path path;
    explicit TempDir(const char* name)
        : path(std::filesystem::temp_directory_path() /
               ("clinicavt-guidance-" + std::string(name) + "-" +
                ::testing::UnitTest::GetInstance()->current_test_info()->name() + "-" +
                std::to_string(_getpid()))) {
        std::error_code ignored;
        std::filesystem::remove_all(path, ignored);
        std::filesystem::create_directories(path);
    }
    // A file a test made read-only would otherwise survive the sweep
    ~TempDir() {
        std::error_code ignored;
        std::filesystem::recursive_directory_iterator it(path, ignored);
        for (; it != std::filesystem::recursive_directory_iterator(); it.increment(ignored)) {
            std::filesystem::permissions(it->path(), std::filesystem::perms::owner_write,
                                         std::filesystem::perm_options::add, ignored);
        }
        std::filesystem::remove_all(path, ignored);
    }
};

inline std::ifstream Open(const std::filesystem::path& path) {
    std::ifstream in(path);
    if (!in.is_open()) throw std::runtime_error("missing guidance fixture " + path.string());
    return in;
}

// Every fixture recommendation, or those of the given guideline codes
inline std::vector<Chunk> Chunks(const std::filesystem::path& fixture_dir,
                                 const std::set<std::string>& codes = {}) {
    auto in = Open(fixture_dir / "corpus.jsonl");
    std::vector<Chunk> chunks;
    for (std::string line; std::getline(in, line);) {
        if (line.empty()) continue;
        const auto row = nlohmann::json::parse(line);
        Chunk c;
        c.id = row.at("id");
        c.code = row.at("code");
        if (!codes.empty() && !codes.count(c.code)) continue;
        c.title = row.at("title");
        c.number = c.id.substr(c.code.size() + 1);
        std::replace(c.number.begin(), c.number.end(), '_', '.');
        c.section = row.at("section");
        c.text = row.at("text");
        c.url = "https://example.test/" + c.id;
        chunks.push_back(std::move(c));
    }
    return chunks;
}

struct Note {
    std::string id;
    std::string text;
    std::vector<std::string> expected;  // chunk ids a search should surface
    std::vector<std::string> must_not;  // guideline codes it must not
};

inline std::vector<Note> Notes(const std::filesystem::path& fixture_dir) {
    auto in = Open(fixture_dir / "notes.jsonl");
    std::vector<Note> notes;
    for (std::string line; std::getline(in, line);) {
        if (line.empty()) continue;
        const auto row = nlohmann::json::parse(line);
        notes.push_back({row.at("id"), row.at("text"), row.at("expected"), row.at("must_not")});
    }
    return notes;
}

// The fixture corpus as one markdown file per guideline under docs, each
// recommendation numbered so the text chunker keeps them apart
inline void WriteMarkdown(const std::filesystem::path& fixture_dir,
                          const std::filesystem::path& docs) {
    std::map<std::string, std::string> per_code;
    for (const auto& chunk : Chunks(fixture_dir)) {
        per_code[chunk.code] += chunk.number + " " + chunk.text + "\n\n";
    }
    std::filesystem::create_directories(docs);
    for (const auto& [code, text] : per_code) {
        std::ofstream(docs / (code + ".md"), std::ios::binary) << text;
    }
}

// Embeds the chunks and writes corpus.db and manifest.json into dir
inline void Build(const std::filesystem::path& dir, const std::string& id, IEmbedder& embedder,
                  const std::vector<Chunk>& chunks, bool research = false) {
    const auto& identity = embedder.Identity();
    std::vector<float> vectors;
    for (const auto& chunk : chunks) {
        const auto embedding = embedder.Embed(chunk.text);
        vectors.insert(vectors.end(), embedding.vector.begin(), embedding.vector.end());
    }
    CorpusSpec spec;
    spec.id = id;
    spec.name = "Fixture guidance corpus";
    spec.licence = "invented";
    spec.attribution = "none";
    spec.source = "text";
    spec.research = research;
    spec.embedder = identity;
    spec.built_at = "2026-09-11T00:00:00Z";
    spec.builder = "tests";
    BuildCorpus(dir, spec, chunks, vectors);
}

}  // namespace clinicavt::guidance::fixture
