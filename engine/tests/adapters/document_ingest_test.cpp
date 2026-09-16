#include "adapters/guidance/document_ingest.hpp"

#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <fstream>
#include <mutex>
#include <thread>

#include "guidance_fixture.hpp"
#include "tiny_pdf.hpp"

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
    DocumentIngest ingest;
    std::mutex mutex;
    std::vector<DocumentInfo> documents;
    std::vector<IngestProgress> progress;

    explicit Harness(std::filesystem::path host = {})
        : ingest(retriever, dir.path / "uploads", [this] { return busy.load(); }, std::move(host)) {
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

    std::filesystem::path WritePdf(const char* name, const std::vector<std::string>& lines) {
        const auto pdf = fixture::TinyPdf(lines);
        return Write(name, std::string(pdf.begin(), pdf.end()));
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

TEST(DocumentIngest, ReadsAPdfThroughTheHostWithItsPages) {
    Harness h(AMBIENT_INGEST_HOST);
    const std::vector<std::string> lines = {
        "Offer allopurinol after a first attack when urate stays high.",
        "Check urate six weeks after any dose change."};
    const auto path = h.WritePdf("gout.pdf", lines);
    const auto id = h.ingest.Add({path}).documents[0].id;
    ASSERT_TRUE(h.WaitForState(id, "ready"));
    const auto row = h.ingest.List()[0];
    EXPECT_EQ(row.pages, 1);
    EXPECT_EQ(row.pages_without_text, 0);
    EXPECT_GE(row.chunks, 1);

    const auto results =
        h.retriever.Search("allopurinol after a first attack", 3, SearchMode::kQuery);
    ASSERT_FALSE(results.shown.empty());
    EXPECT_EQ(results.shown[0].document, id);
    EXPECT_EQ(results.shown[0].page, 0);
    EXPECT_EQ(results.shown[0].pages, 1);
    EXPECT_TRUE(results.shown[0].citation.starts_with("gout, page 1 (added "))
        << results.shown[0].citation;
    EXPECT_NE(results.shown[0].text.find("allopurinol"), std::string::npos);

    std::string reason;
    auto store = UploadStore::Open(h.dir.path / "uploads", h.retriever.Identity(), reason);
    ASSERT_TRUE(store) << reason;
    const auto chunks = store->ReadChunks(id);
    ASSERT_FALSE(chunks.empty());
    EXPECT_EQ(chunks[0].page, 0);
    EXPECT_NE(chunks[0].boxes.find("\"page\":0"), std::string::npos);

    // The page view: a bitmap under scratch with the chunk's boxes, and a copy to open
    const auto drawn = h.ingest.Render(id, 0, 0);
    EXPECT_EQ(drawn.width, 1190);
    EXPECT_EQ(drawn.height, 1684);
    EXPECT_EQ(drawn.pages, 1);
    EXPECT_EQ(drawn.boxes, chunks[0].boxes);
    EXPECT_TRUE(std::filesystem::exists(drawn.path));
    EXPECT_EQ(drawn.path.parent_path().filename(), "scratch");
    const auto copy = h.ingest.OpenCopy(id);
    EXPECT_EQ(copy, h.ingest.OpenCopy(id));
    EXPECT_EQ(std::filesystem::file_size(copy), fixture::TinyPdf(lines).size());
    EXPECT_THROW(h.ingest.Render(id, 3, 0), store::StoreError);
}

TEST(DocumentIngest, HostRefusalsBecomeTheRowsError) {
    Harness h(AMBIENT_FAKE_INGEST_HOST);
    const auto accepted =
        h.ingest.Add({h.Write("locked.pdf", "FAKE exit 3"), h.Write("broken.pdf", "FAKE crash"),
                      h.Write("fine.pdf", "%PDF canned")});
    ASSERT_EQ(accepted.documents.size(), 3u);
    const auto locked = accepted.documents[0].id;
    const auto broken = accepted.documents[1].id;
    const auto fine = accepted.documents[2].id;
    ASSERT_TRUE(h.WaitForState(locked, "failed"));
    ASSERT_TRUE(h.WaitForState(broken, "failed"));
    ASSERT_TRUE(h.WaitForState(fine, "ready"));
    for (const auto& row : h.ingest.List()) {
        if (row.id == locked) EXPECT_EQ(row.error, "password");
        if (row.id == broken) EXPECT_EQ(row.error, "crashed");
        if (row.id == fine) {
            EXPECT_EQ(row.pages, 2);
            EXPECT_EQ(row.pages_without_text, 1);
            EXPECT_GE(row.chunks, 1);
        }
    }
    const auto results = h.retriever.Search("Offer allopurinol", 3, SearchMode::kQuery);
    ASSERT_FALSE(results.shown.empty());
    EXPECT_EQ(results.shown[0].number, "1.1");
    EXPECT_TRUE(results.shown[0].citation.starts_with("fine, page 1, 1.1 (added "))
        << results.shown[0].citation;
}

TEST(DocumentIngest, AReadyDocumentIsSearchedAgainAfterARestart) {
    fixture::TempDir dir{"ingest-restart"};
    std::filesystem::create_directories(dir.path / "corpora");
    const auto path = dir.path / "guideline.md";
    std::ofstream(path, std::ios::binary) << kGuideline;
    {
        Retriever retriever{[] { return std::make_unique<WordEmbedder>(); }, dir.path / "corpora",
                            RetrieverOptions{.floor = 0.2, .upload_floor = 0.2}};
        retriever.Prepare();
        DocumentIngest ingest(retriever, dir.path / "uploads", [] { return false; });
        std::mutex mutex;
        bool ready = false;
        ingest.SetListener([](const IngestProgress&) {},
                           [&](const DocumentInfo& d) {
                               std::lock_guard<std::mutex> lock(mutex);
                               ready = ready || d.state == "ready";
                           });
        const auto id = ingest.Add({path}).documents[0].id;
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
        for (;;) {
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
            std::lock_guard<std::mutex> lock(mutex);
            if (ready || std::chrono::steady_clock::now() > deadline) break;
        }
        ASSERT_GT(id, 0);
    }

    Retriever retriever{[] { return std::make_unique<WordEmbedder>(); }, dir.path / "corpora",
                        RetrieverOptions{.floor = 0.2, .upload_floor = 0.2}};
    retriever.Prepare();
    DocumentIngest ingest(retriever, dir.path / "uploads", [] { return false; });
    EXPECT_TRUE(retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery)
                    .shown.empty());
    ASSERT_EQ(ingest.List().size(), 1u);
    const auto results =
        retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery);
    ASSERT_FALSE(results.shown.empty());
    EXPECT_EQ(results.shown[0].source, "upload");
}

TEST(DocumentIngest, WithoutAHostAPdfIsSkipped) {
    Harness h;
    const auto accepted = h.ingest.Add({h.Write("scan.pdf", "%PDF")});
    EXPECT_TRUE(accepted.documents.empty());
    ASSERT_EQ(accepted.skipped.size(), 1u);
    EXPECT_EQ(accepted.skipped[0].reason, "unsupported");
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
