#include "adapters/guidance/upload_store.hpp"

#include <gtest/gtest.h>

#include <algorithm>
#include <fstream>
#include <iterator>
#include <vector>

#include "guidance_fixture.hpp"
#include "adapters/guidance/corpus_store.hpp"
#include "adapters/storage/chunk_cipher.hpp"
#include "adapters/storage/db.hpp"
#include "ports/store_error.hpp"

namespace ambient::guidance {
namespace {

using store::ChunkCipher;
using store::Db;
using store::Domain;
using store::StoreError;

const EmbedderIdentity kEmbedder{"fixture-embedder", "rev-a", 4, 16, ""};

std::filesystem::path WriteFile(const std::filesystem::path& dir, const char* name,
                                const std::string& text) {
    const auto path = dir / name;
    std::ofstream(path, std::ios::binary) << text;
    return path;
}

UploadChunk Chunk(const std::string& text, int page = 0) {
    return {page, "Recommendations", text, {1.0F, 0.0F, 0.0F, 0.0F}, "[[0.1,0.2,0.8,0.25]]"};
}

bool FileHolds(const std::filesystem::path& path, const std::vector<std::uint8_t>& needle) {
    std::ifstream in(path, std::ios::binary);
    const std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(in)),
                                          std::istreambuf_iterator<char>());
    return std::search(bytes.begin(), bytes.end(), needle.begin(), needle.end()) != bytes.end();
}

std::vector<std::uint8_t> Needle(const std::string& text) {
    return {text.begin(), text.end()};
}

TEST(ChunkCipher, WrapsAKeyUnderAnotherAndNamesAFileWithoutRevealingIt) {
    const auto store = ChunkCipher::Generate();
    const auto doc = ChunkCipher::Generate();
    const std::string plain = "sealed under the document key";
    const auto sealed =
        doc.Seal(Domain::kUploadText, "upload:7", 3,
                 std::span(reinterpret_cast<const std::uint8_t*>(plain.data()), plain.size()));

    const auto wrapped = store.WrapKey(doc, "upload:7", 42);
    const auto unwrapped = ChunkCipher::FromWrappedKey(store, "upload:7", 42, wrapped);
    const auto opened = unwrapped.Open(Domain::kUploadText, "upload:7", 3, sealed);
    EXPECT_EQ(std::string(opened.begin(), opened.end()), plain);
    EXPECT_THROW(ChunkCipher::FromWrappedKey(ChunkCipher::Generate(), "upload:7", 42, wrapped),
                 StoreError);
    EXPECT_THROW(ChunkCipher::FromWrappedKey(store, "upload:8", 42, wrapped), StoreError);

    fixture::TempDir dir("identity");
    const auto a = WriteFile(dir.path, "a.txt", "the same bytes");
    const auto b = WriteFile(dir.path, "b.txt", "the same bytes");
    const auto c = WriteFile(dir.path, "c.txt", "other bytes");
    EXPECT_EQ(store.Identity(a), store.Identity(b));
    EXPECT_NE(store.Identity(a), store.Identity(c));
    EXPECT_NE(store.Identity(a), ChunkCipher::Generate().Identity(a)) << "keyed, so not a hash";
}

TEST(UploadStore, CreatesRefusesForeignFilesAndAddsFinishesListsAndRemoves) {
    fixture::TempDir dir("store");
    std::string reason;
    auto store = UploadStore::Open(dir.path, kEmbedder, reason);
    ASSERT_NE(store, nullptr) << reason;
    EXPECT_TRUE(store->List().empty());

    const auto file = WriteFile(dir.path, "BSR PMR 2009.pdf", "%PDF the whole document");
    const auto added = store->Add(file, "BSR PMR 2009", "application/pdf");
    EXPECT_TRUE(added.added);
    EXPECT_EQ(store->Get(added.id).state, "indexing");
    EXPECT_FALSE(store->Add(file, "again", "application/pdf").added) << "the same file once";

    store->Finish(added.id,
                  {Chunk("Start prednisolone 15 mg daily.", 1), Chunk("Taper slowly.", 2)}, 5, 0);
    const auto rows = store->List();
    ASSERT_EQ(rows.size(), 1u);
    EXPECT_EQ(rows[0].name, "BSR PMR 2009");
    EXPECT_EQ(rows[0].state, "ready");
    EXPECT_EQ(rows[0].pages, 5);
    EXPECT_EQ(rows[0].chunks, 2);
    EXPECT_FALSE(rows[0].indexed_at.empty());

    const auto chunks = store->ReadChunks(added.id);
    ASSERT_EQ(chunks.size(), 2u);
    EXPECT_EQ(chunks[0].text, "Start prednisolone 15 mg daily.");
    EXPECT_EQ(chunks[1].page, 2);
    EXPECT_EQ(chunks[1].vector, (std::vector<float>{1.0F, 0.0F, 0.0F, 0.0F}));
    const auto copy = store->ReadFile(added.id);
    EXPECT_EQ(std::string(copy.begin(), copy.end()), "%PDF the whole document");

    store->Remove(added.id);
    EXPECT_TRUE(store->List().empty());
    EXPECT_FALSE(std::filesystem::exists(dir.path / "files" / (std::to_string(added.id) + ".bin")));
    EXPECT_THROW(store->Get(added.id), StoreError);

    // A corpus file and a newer store are both refused with a reason
    fixture::TempDir other("foreign");
    {
        Db db(other.path / kUploadFile, Db::Mode::kBuild);
        db.SetApplicationId(kCorpusApplicationId);
        db.SetUserVersion(1);
    }
    EXPECT_EQ(UploadStore::Open(other.path, kEmbedder, reason), nullptr);
    EXPECT_EQ(reason, "not a document store");
    {
        Db db(dir.path / kUploadFile, Db::Mode::kBuild);
        db.SetUserVersion(kUploadFormat + 1);
    }
    EXPECT_EQ(UploadStore::Open(dir.path, kEmbedder, reason), nullptr);
    EXPECT_EQ(reason, "document store is newer than this build");
}

TEST(UploadStore, ContentIsCiphertextAtRestAndRemoveLeavesNoKey) {
    fixture::TempDir dir("erase");
    std::string reason;
    auto store = UploadStore::Open(dir.path, kEmbedder, reason);
    ASSERT_NE(store, nullptr) << reason;
    const std::string text = "needle recommendation text that must never rest in plaintext";
    const auto file = WriteFile(dir.path, "doc.txt", text);
    const auto id = store->Add(file, "needle document name", "text/plain").id;
    store->Finish(id, {Chunk(text)}, 1, 0);

    const auto db = dir.path / kUploadFile;
    const auto wal = dir.path / "uploads.db-wal";
    const auto copy = dir.path / "files" / (std::to_string(id) + ".bin");
    for (const auto& plain : {text, std::string("needle document name")}) {
        EXPECT_FALSE(FileHolds(db, Needle(plain)));
        EXPECT_FALSE(FileHolds(wal, Needle(plain)));
        EXPECT_FALSE(FileHolds(copy, Needle(plain)));
    }

    std::vector<std::uint8_t> wrapped;
    {
        Db reader(db);
        auto key = reader.Prepare("SELECT key_wrapped FROM documents WHERE id = ?");
        key.BindInt64(1, id);
        ASSERT_TRUE(key.Step());
        wrapped = key.ColumnBlob(0);
    }
    ASSERT_GT(wrapped.size(), 32u);
    ASSERT_TRUE(FileHolds(db, wrapped) || FileHolds(wal, wrapped));

    store->Remove(id);
    EXPECT_FALSE(FileHolds(db, wrapped));
    EXPECT_FALSE(FileHolds(wal, wrapped));
    EXPECT_FALSE(std::filesystem::exists(copy));
}

TEST(UploadStore, OpeningErasesHalfIndexedDocumentsAndStalesReadyOnesForANewEmbedder) {
    fixture::TempDir dir("reopen");
    std::string reason;
    std::int64_t half = 0, ready = 0;
    {
        auto store = UploadStore::Open(dir.path, kEmbedder, reason);
        ASSERT_NE(store, nullptr) << reason;
        half = store->Add(WriteFile(dir.path, "half.txt", "half"), "half", "text/plain").id;
        ready = store->Add(WriteFile(dir.path, "ready.txt", "ready"), "ready", "text/plain").id;
        store->Finish(ready, {Chunk("done")}, 1, 0);
        std::ofstream(dir.path / "files" / "12345.bin") << "orphan";
    }
    {
        auto store = UploadStore::Open(dir.path, kEmbedder, reason);
        ASSERT_NE(store, nullptr) << reason;
        const auto rows = store->List();
        ASSERT_EQ(rows.size(), 1u);
        EXPECT_EQ(rows[0].id, ready);
        EXPECT_EQ(rows[0].state, "ready");
        EXPECT_FALSE(std::filesystem::exists(dir.path / "files" / (std::to_string(half) + ".bin")));
        EXPECT_FALSE(std::filesystem::exists(dir.path / "files" / "12345.bin"));
    }
    EmbedderIdentity changed = kEmbedder;
    changed.rev = "rev-b";
    auto store = UploadStore::Open(dir.path, changed, reason);
    ASSERT_NE(store, nullptr) << reason;
    EXPECT_EQ(store->List()[0].state, "stale");
    EXPECT_EQ(store->Embedder().rev, "rev-b");
    store->Finish(ready, {Chunk("indexed again")}, 1, 0);
    EXPECT_EQ(store->Get(ready).state, "ready");
    EXPECT_EQ(store->RemoveAll(), 1u);
    EXPECT_TRUE(store->List().empty());
}

}  // namespace
}  // namespace ambient::guidance
