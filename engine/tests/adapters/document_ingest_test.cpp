#include "adapters/guidance/document_ingest.hpp"

#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <fstream>
#include <mutex>
#include <thread>

#include "guidance_fixture.hpp"

namespace ambient::guidance {
namespace {

// Sixty-four buckets of hashed words, unit length: shared words score higher
struct WordEmbedder : IEmbedder {
    EmbedderIdentity identity{"words", "rev-1", 64, 64, ""};
    const EmbedderIdentity& Identity() const override {
        return identity;
    }
    Embedding Embed(const std::string& text) override {
        Embedding out;
        out.vector.assign(64, 0.0F);
        unsigned hash = 0;
        bool in_word = false;
        const auto close = [&] {
            if (in_word) out.vector[hash % 64] += 1.0F;
            hash = 0;
            in_word = false;
        };
        for (const unsigned char c : text) {
            if (std::isalnum(c)) {
                hash = hash * 31 + static_cast<unsigned>(std::tolower(c));
                in_word = true;
            } else {
                close();
            }
        }
        close();
        float norm = 0;
        for (const float v : out.vector) norm += v * v;
        norm = norm > 0 ? std::sqrt(norm) : 1.0F;
        for (auto& v : out.vector) v /= norm;
        return out;
    }
};

struct Harness {
    fixture::TempDir dir{"ingest"};
    Retriever retriever{[] { return std::make_unique<WordEmbedder>(); }, dir.path / "corpora",
                        RetrieverOptions{.floor = 0.2, .upload_floor = 0.2}};
    std::atomic<bool> busy{false};
    DocumentIngest ingest{retriever, dir.path / "uploads", [this] { return busy.load(); }};
    std::mutex mutex;
    std::vector<DocumentInfo> documents;
    std::vector<IngestProgress> progress;

    Harness() {
        std::filesystem::create_directories(dir.path / "corpora");
        retriever.Prepare();
        ingest.SetListener(
            [this](const IngestProgress& p) {
                std::lock_guard<std::mutex> lock(mutex);
                progress.push_back(p);
            },
            [this](const DocumentInfo& d) {
                std::lock_guard<std::mutex> lock(mutex);
                documents.push_back(d);
            });
    }

    std::filesystem::path Write(const char* name, const std::string& text) {
        const auto path = dir.path / name;
        std::ofstream(path, std::ios::binary) << text;
        return path;
    }

    bool WaitForState(std::int64_t id, const char* state, int seconds = 10) {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
        while (std::chrono::steady_clock::now() < deadline) {
            {
                std::lock_guard<std::mutex> lock(mutex);
                for (const auto& d : documents) {
                    if (d.id == id && d.state == state) return true;
                }
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        return false;
    }
};

const char* const kGuideline =
    "Gout guideline\n\n1.1 Offer allopurinol after a first attack when urate stays high.\n\n"
    "1.2 Offer colchicine or an NSAID for an acute flare.\n\n"
    "1.3 Check urate six weeks after any dose change.\n";

TEST(DocumentIngest, AddsATextDocumentAndTheRetrieverSearchesIt) {
    Harness h;
    const auto accepted = h.ingest.Add({h.Write("Gout local guideline.md", kGuideline)});
    ASSERT_EQ(accepted.documents.size(), 1u);
    EXPECT_TRUE(accepted.skipped.empty());
    const auto id = accepted.documents[0].id;
    EXPECT_EQ(accepted.documents[0].state, "indexing");
    EXPECT_EQ(accepted.documents[0].name, "Gout local guideline");
    ASSERT_TRUE(h.WaitForState(id, "ready"));

    const auto rows = h.ingest.List();
    ASSERT_EQ(rows.size(), 1u);
    EXPECT_GE(rows[0].chunks, 1);
    {
        std::lock_guard<std::mutex> lock(h.mutex);
        EXPECT_FALSE(h.progress.empty());
        EXPECT_EQ(h.progress.back().phase, "preparing");
        EXPECT_EQ(h.progress.back().done, h.progress.back().total);
    }

    const auto results =
        h.retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery);
    ASSERT_FALSE(results.shown.empty());
    EXPECT_EQ(results.shown[0].source, "upload");
    EXPECT_EQ(results.shown[0].corpus, "upload:" + std::to_string(id));
    EXPECT_EQ(results.shown[0].title, "Gout local guideline");
    EXPECT_EQ(results.shown[0].document, id);
    EXPECT_EQ(results.shown[0].page, 0);
    EXPECT_NE(results.shown[0].text.find("colchicine"), std::string::npos);
    ASSERT_EQ(results.searched.size(), 1u);
    EXPECT_EQ(results.searched[0].id, "upload:" + std::to_string(id));
    EXPECT_EQ(results.searched[0].source, "upload");

    h.ingest.Remove(id);
    ASSERT_TRUE(h.WaitForState(id, "removed"));
    EXPECT_TRUE(h.ingest.List().empty());
    const auto after =
        h.retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery);
    EXPECT_TRUE(after.shown.empty());
    EXPECT_TRUE(after.searched.empty());
}

TEST(DocumentIngest, SkipsWhatItCannotTakeAndRefusesPatientData) {
    Harness h;
    const auto guideline = h.Write("guideline.txt", kGuideline);
    const auto accepted = h.ingest.Add({guideline, guideline, h.Write("scan.pdf", "%PDF"),
                                        h.Write("letter.txt",
                                                "Dear Dr Jones, this man's NHS number is 943 476 "
                                                "5919 and his DOB: 1961.")});
    ASSERT_EQ(accepted.documents.size(), 2u);
    ASSERT_EQ(accepted.skipped.size(), 2u);
    EXPECT_EQ(accepted.skipped[0].reason, "duplicate");
    EXPECT_EQ(accepted.skipped[1].reason, "unsupported");

    const auto letter = accepted.documents[1].id;
    ASSERT_TRUE(h.WaitForState(letter, "failed"));
    for (const auto& row : h.ingest.List()) {
        if (row.id == letter) EXPECT_EQ(row.error, "patientData");
    }
    ASSERT_TRUE(h.WaitForState(accepted.documents[0].id, "ready"));
}

TEST(DocumentIngest, PausesWhileAConsultationRunsAndCancelRemovesTheDocument) {
    Harness h;
    h.busy = true;
    const auto id = h.ingest.Add({h.Write("guideline.md", kGuideline)}).documents[0].id;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    bool paused = false;
    while (!paused && std::chrono::steady_clock::now() < deadline) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        std::lock_guard<std::mutex> lock(h.mutex);
        for (const auto& p : h.progress) paused = paused || p.phase == "paused";
    }
    EXPECT_TRUE(paused);
    EXPECT_EQ(h.ingest.List()[0].state, "indexing");

    h.ingest.Cancel();
    h.busy = false;
    ASSERT_TRUE(h.WaitForState(id, "removed"));
    EXPECT_TRUE(h.ingest.List().empty());
}

}  // namespace
}  // namespace ambient::guidance
