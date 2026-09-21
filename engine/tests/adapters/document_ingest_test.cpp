#include "adapters/guidance/document_ingest.hpp"

#include <gtest/gtest.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <fstream>
#include <functional>
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

constexpr auto kScan = std::chrono::milliseconds(100);

Retriever MakeRetriever(const std::filesystem::path& dir) {
    std::filesystem::create_directories(dir / "corpora");
    return Retriever{[] { return std::make_unique<WordEmbedder>(); }, dir / "corpora",
                     RetrieverOptions{.floor = 0.2, .upload_floor = 0.2, .upload_note_floor = 0.2}};
}

void Delete(const std::filesystem::path& path) {
    std::filesystem::remove(path);
}

struct Harness {
    fixture::TempDir dir{"ingest"};
    Retriever retriever = MakeRetriever(dir.path);
    std::filesystem::path folder = dir.path / "guidelines";
    std::atomic<bool> busy{false};
    std::vector<std::filesystem::path> binned;  // what Remove sent to the bin
    DocumentIngest ingest;
    std::mutex mutex;
    std::vector<DocumentInfo> documents;
    std::vector<IngestProgress> progress;

    explicit Harness(std::filesystem::path host = {})
        : ingest(
              retriever, folder, dir.path / "index", [this] { return busy.load(); },
              std::move(host), HostLimits{},
              [this](const std::filesystem::path& path) {
                  binned.push_back(path);
                  Delete(path);
              },
              kScan) {
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

    // A file in the folder, or under a subfolder of it
    std::filesystem::path Write(const char* name, const std::string& text) {
        const auto path = folder / name;
        std::filesystem::create_directories(path.parent_path());
        std::ofstream(path, std::ios::binary) << text;
        return path;
    }

    std::filesystem::path WriteOutside(const char* name, const std::string& text) {
        const auto path = dir.path / name;
        std::ofstream(path, std::ios::binary) << text;
        return path;
    }

    std::filesystem::path WritePdf(const char* name, const std::vector<std::string>& lines) {
        const auto pdf = fixture::TinyPdf(lines);
        return Write(name, std::string(pdf.begin(), pdf.end()));
    }

    bool WaitUntil(const std::function<bool()>& condition, int seconds = 10) {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
        while (std::chrono::steady_clock::now() < deadline) {
            if (condition()) return true;
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        return false;
    }

    // The document the file holds, once the scan has taken it
    std::int64_t IdOf(const char* path, int seconds = 10) {
        std::int64_t id = 0;
        WaitUntil(
            [&] {
                for (const auto& d : ingest.List().documents) {
                    if (d.path == path) {
                        id = d.id;
                        return true;
                    }
                }
                return false;
            },
            seconds);
        return id;
    }

    bool WaitForState(std::int64_t id, const char* state, int seconds = 10) {
        return WaitUntil(
            [&] {
                std::lock_guard<std::mutex> lock(mutex);
                return std::ranges::any_of(documents, [&](const DocumentInfo& d) {
                    return d.id == id && d.state == state;
                });
            },
            seconds);
    }
};

// Attaches a listener and waits for any document to turn ready
bool WaitReady(DocumentIngest& ingest) {
    std::mutex mutex;
    bool ready = false;
    ingest.SetListener([](const IngestProgress&) {},
                       [&](const DocumentInfo& d) {
                           std::lock_guard<std::mutex> lock(mutex);
                           ready = ready || d.state == "ready";
                       });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (std::chrono::steady_clock::now() < deadline) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        std::lock_guard<std::mutex> lock(mutex);
        if (ready) return true;
    }
    return false;
}

const char* const kGuideline =
    "Gout guideline\n\n1.1 Offer allopurinol after a first attack when urate stays high.\n\n"
    "1.2 Offer colchicine or an NSAID for an acute flare.\n\n"
    "1.3 Check urate six weeks after any dose change.\n";

const char* const kPathway =
    "PMR pathway\n\n1.1 Start prednisolone 15 mg daily and review the response at a week.\n\n"
    "1.2 Taper by 2.5 mg every two to four weeks once symptoms settle.\n";

TEST(DocumentIngest, AFileInTheFolderIsIndexedAndSearchedAndItsDeletionRemovesIt) {
    Harness h;
    const auto file = h.Write("Gout local guideline.md", kGuideline);
    const auto id = h.IdOf("Gout local guideline.md");
    ASSERT_NE(id, 0);
    ASSERT_TRUE(h.WaitForState(id, "ready"));

    const auto listing = h.ingest.List();
    EXPECT_EQ(listing.folder, h.folder);
    EXPECT_TRUE(listing.found);
    EXPECT_EQ(listing.unsupported, 0);
    ASSERT_EQ(listing.documents.size(), 1u);
    const auto& row = listing.documents[0];
    EXPECT_EQ(row.name, "Gout local guideline");
    EXPECT_EQ(row.sha256.size(), 64u);
    EXPECT_EQ(row.id, DocumentIndex::IdOf(row.sha256));
    EXPECT_EQ(row.bytes, static_cast<std::int64_t>(std::filesystem::file_size(file)));
    EXPECT_GE(row.chunks, 1);
    EXPECT_TRUE(std::filesystem::exists(h.folder / kReadMe)) << "the folder explains itself";
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
    EXPECT_NE(results.shown[0].text.find("colchicine"), std::string::npos);
    ASSERT_EQ(results.searched.size(), 1u);
    EXPECT_EQ(results.searched[0].id, "upload:" + std::to_string(id));
    EXPECT_EQ(results.searched[0].sha256, row.sha256);

    Delete(file);
    ASSERT_TRUE(h.WaitForState(id, "removed"));
    EXPECT_TRUE(h.ingest.List().documents.empty());
    EXPECT_TRUE(h.retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery)
                    .shown.empty());
}

TEST(DocumentIngest, AResultsChunkIdNamesTheChunkWithinItsDocument) {
    Harness h;
    h.Write("gout.md", kGuideline);
    h.Write("pmr.md", kPathway);
    const auto gout = h.IdOf("gout.md");
    const auto pmr = h.IdOf("pmr.md");
    ASSERT_TRUE(h.WaitForState(gout, "ready"));
    ASSERT_TRUE(h.WaitForState(pmr, "ready"));

    const auto results =
        h.retriever.Search("Taper prednisolone once symptoms settle.", 3, SearchMode::kQuery);
    ASSERT_FALSE(results.shown.empty());
    const auto& hit = results.shown[0];
    ASSERT_EQ(hit.document, pmr);
    const auto ord = std::stoll(hit.chunk_id.substr(hit.chunk_id.rfind('-') + 1));
    DocumentIndex index(h.dir.path / "index" / kIndexFile);
    EXPECT_EQ(index.ReadChunk(pmr, ord).text, hit.text);
}

TEST(DocumentIngest, ARenameKeepsTheDocumentAndAChangedFileIsANewOne) {
    Harness h;
    const auto file = h.Write("pmr.md", kPathway);
    const auto id = h.IdOf("pmr.md");
    ASSERT_TRUE(h.WaitForState(id, "ready"));
    const auto sha = h.ingest.List().documents[0].sha256;

    std::filesystem::rename(file, h.folder / "PMR pathway 2024.md");
    ASSERT_TRUE(h.WaitUntil([&] {
        const auto rows = h.ingest.List().documents;
        return rows.size() == 1 && rows[0].path == "PMR pathway 2024.md" && rows[0].id == id;
    }));
    const auto renamed = h.ingest.List().documents[0];
    EXPECT_EQ(renamed.name, "PMR pathway 2024");
    EXPECT_EQ(renamed.state, "ready") << "a rename does not re-index";

    h.Write("PMR pathway 2024.md", std::string(kPathway) + "\n1.3 Review bone protection.\n");
    ASSERT_TRUE(h.WaitForState(id, "removed")) << "the old content is gone";
    ASSERT_TRUE(h.WaitUntil([&] {
        const auto rows = h.ingest.List().documents;
        return rows.size() == 1 && rows[0].state == "ready" && rows[0].sha256 != sha;
    })) << "the changed file is a new document";
    EXPECT_NE(h.ingest.List().documents[0].id, id) << "so old citations never point into new text";
}

TEST(DocumentIngest, RemoveBinsTheFileAndRemoveAllEveryFile) {
    Harness h;
    const auto gout = h.Write("gout.md", kGuideline);
    const auto pmr = h.Write("pmr.md", kPathway);
    const auto gout_id = h.IdOf("gout.md");
    const auto pmr_id = h.IdOf("pmr.md");
    ASSERT_TRUE(h.WaitForState(gout_id, "ready"));
    ASSERT_TRUE(h.WaitForState(pmr_id, "ready"));

    h.ingest.Remove(gout_id);
    EXPECT_EQ(h.binned, std::vector<std::filesystem::path>{gout});
    EXPECT_FALSE(std::filesystem::exists(gout));
    ASSERT_TRUE(h.WaitForState(gout_id, "removed"));
    EXPECT_THROW(h.ingest.Remove(gout_id), store::StoreError);

    EXPECT_EQ(h.ingest.RemoveAll(), 1u);
    EXPECT_EQ(h.binned.size(), 2u);
    EXPECT_FALSE(std::filesystem::exists(pmr));
    ASSERT_TRUE(h.WaitForState(pmr_id, "removed"));
    EXPECT_TRUE(h.ingest.List().documents.empty());
    EXPECT_TRUE(std::filesystem::exists(h.folder / kReadMe));
}

TEST(DocumentIngest, ADeletedFolderIsMadeAgainEmptyAndAnUnreachableParentHoldsTheRows) {
    Harness h;
    h.Write("gout.md", kGuideline);
    const auto id = h.IdOf("gout.md");
    ASSERT_TRUE(h.WaitForState(id, "ready"));
    std::filesystem::remove_all(h.folder);
    ASSERT_TRUE(h.WaitForState(id, "removed"));
    ASSERT_TRUE(h.WaitUntil([&] { return std::filesystem::exists(h.folder / kReadMe); }));
    EXPECT_TRUE(h.ingest.List().found);

    // A folder under a parent that went is out of reach, and nothing is acted on
    fixture::TempDir dir{"ingest-unreachable"};
    auto retriever = MakeRetriever(dir.path);
    retriever.Prepare();
    const auto parent = dir.path / "drive";
    std::filesystem::create_directories(parent / "guidelines");
    std::ofstream(parent / "guidelines" / "gout.md", std::ios::binary) << kGuideline;
    DocumentIngest ingest(
        retriever, parent / "guidelines", dir.path / "index", [] { return false; }, {},
        HostLimits{}, Delete, kScan);
    ASSERT_TRUE(WaitReady(ingest));
    std::filesystem::remove_all(parent);
    std::this_thread::sleep_for(kScan * 4);
    const auto listing = ingest.List();
    EXPECT_FALSE(listing.found);
    EXPECT_EQ(listing.documents.size(), 1u) << "nothing removed while the folder is out of reach";
}

TEST(DocumentIngest, PatientDataIsRefusedAndOtherFilesAreCountedOrIgnored) {
    Harness h;
    h.Write("letter.txt",
            "Dear Dr Jones, this man's NHS number is 943 476 5919 and his DOB: 1961.");
    h.Write("notes.docx", "PK not a text file");
    h.Write("scan.pdf", "%PDF");  // no host in this harness
    h.Write("~$lock.txt", "an Office lock");
    h.Write("gout.md", kGuideline);
    const auto letter = h.IdOf("letter.txt");
    ASSERT_TRUE(h.WaitForState(letter, "failed"));
    ASSERT_TRUE(h.WaitForState(h.IdOf("gout.md"), "ready"));
    const auto listing = h.ingest.List();
    EXPECT_EQ(listing.unsupported, 2);
    ASSERT_EQ(listing.documents.size(), 2u);
    for (const auto& row : listing.documents) {
        if (row.id == letter) EXPECT_EQ(row.error, "patientData");
    }
}

TEST(DocumentIngest, AddCopiesIntoTheFolderAndSkipsWhatItCannotTake) {
    Harness h;
    const auto accepted =
        h.ingest.Add({h.WriteOutside("Gout local guideline.md", kGuideline),
                      h.WriteOutside("empty.txt", ""), h.WriteOutside("scan.pdf", "%PDF")});
    ASSERT_EQ(accepted.skipped.size(), 2u);
    EXPECT_EQ(accepted.skipped[0].reason, "unreadable");
    EXPECT_EQ(accepted.skipped[1].reason, "unsupported");
    EXPECT_TRUE(std::filesystem::exists(h.folder / "Gout local guideline.md"));
    // Add copies then scans; a scan under load may take the file on the next pass
    const auto id = h.IdOf("Gout local guideline.md");
    ASSERT_NE(id, 0);
    ASSERT_TRUE(h.WaitForState(id, "ready"));
    EXPECT_EQ(h.ingest.Add({h.dir.path / "Gout local guideline.md"}).documents.size(), 1u)
        << "the same content again is the same document";
    EXPECT_EQ(h.ingest.List().documents.size(), 1u);
}

TEST(DocumentIngest, ReadsAPdfThroughTheHostWithItsPages) {
    Harness h(AMBIENT_INGEST_HOST);
    const std::vector<std::string> lines = {
        "Offer allopurinol after a first attack when urate stays high.",
        "Check urate six weeks after any dose change."};
    const auto path = h.WritePdf("gout.pdf", lines);
    const auto id = h.IdOf("gout.pdf");
    ASSERT_TRUE(h.WaitForState(id, "ready"));
    const auto row = h.ingest.List().documents[0];
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

    // The page view draws a bitmap under scratch with the chunk boxes. Open gets her file
    const auto drawn = h.ingest.Render(id, 0, 0);
    EXPECT_EQ(drawn.width, 1190);
    EXPECT_EQ(drawn.height, 1684);
    EXPECT_EQ(drawn.pages, 1);
    EXPECT_NE(drawn.boxes.find("\"page\":0"), std::string::npos);
    EXPECT_TRUE(std::filesystem::exists(drawn.path));
    EXPECT_EQ(drawn.path.parent_path().filename(), "scratch");
    EXPECT_EQ(h.ingest.Path(id), path);
    EXPECT_THROW(h.ingest.Render(id, 3, 0), store::StoreError);
}

TEST(DocumentIngest, HostRefusalsBecomeTheRowsError) {
    Harness h(AMBIENT_FAKE_INGEST_HOST);
    h.Write("locked.pdf", "FAKE exit 3");
    h.Write("broken.pdf", "FAKE crash");
    h.Write("fine.pdf", "%PDF canned");
    const auto locked = h.IdOf("locked.pdf");
    const auto broken = h.IdOf("broken.pdf");
    const auto fine = h.IdOf("fine.pdf");
    ASSERT_TRUE(h.WaitForState(locked, "failed"));
    ASSERT_TRUE(h.WaitForState(broken, "failed"));
    ASSERT_TRUE(h.WaitForState(fine, "ready"));
    for (const auto& row : h.ingest.List().documents) {
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

TEST(DocumentIngest, AReadyDocumentIsListedAndSearchedAgainAfterARestart) {
    fixture::TempDir dir{"ingest-restart"};
    const auto folder = dir.path / "guidelines";
    std::filesystem::create_directories(folder);
    std::ofstream(folder / "guideline.md", std::ios::binary) << kGuideline;
    {
        auto retriever = MakeRetriever(dir.path);
        retriever.Prepare();
        DocumentIngest ingest(
            retriever, folder, dir.path / "index", [] { return false; }, {}, HostLimits{}, Delete,
            kScan);
        ASSERT_TRUE(WaitReady(ingest));
    }

    auto retriever = MakeRetriever(dir.path);
    retriever.Prepare();
    DocumentIngest ingest(
        retriever, folder, dir.path / "index", [] { return false; }, {}, HostLimits{}, Delete,
        kScan);
    ASSERT_EQ(ingest.List().documents.size(), 1u) << "listed before any scan or embedder";
    EXPECT_EQ(ingest.List().documents[0].state, "ready");
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    Results results;
    while (std::chrono::steady_clock::now() < deadline) {
        results = retriever.Search("Colchicine for an acute flare of gout.", 3, SearchMode::kQuery);
        if (!results.shown.empty()) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    ASSERT_FALSE(results.shown.empty()) << "published once the embedder was adopted";
    EXPECT_EQ(results.shown[0].source, "upload");
}

TEST(DocumentIngest, IndexingPausesWhileAConsultationRunsAndRemoveDuringItCancels) {
    Harness h;
    h.busy = true;
    const auto file = h.Write("guideline.md", kGuideline);
    // The file is found and its row appears, but embedding waits for the consultation
    const auto id = h.IdOf("guideline.md");
    ASSERT_NE(id, 0);
    bool paused = false;
    ASSERT_TRUE(h.WaitUntil([&] {
        std::lock_guard<std::mutex> lock(h.mutex);
        for (const auto& p : h.progress) paused = paused || p.phase == "paused";
        return paused;
    }));
    EXPECT_EQ(h.ingest.List().documents[0].state, "indexing");

    h.ingest.Remove(id);
    EXPECT_FALSE(std::filesystem::exists(file));
    h.busy = false;
    ASSERT_TRUE(h.WaitForState(id, "removed"));
    EXPECT_TRUE(h.ingest.List().documents.empty());
}

}  // namespace
}  // namespace ambient::guidance
