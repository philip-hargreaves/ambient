#include "adapters/guidance/upload_store.hpp"

#include <algorithm>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <format>
#include <fstream>
#include <nlohmann/json.hpp>
#include <optional>
#include <random>
#include <span>
#include <system_error>

#include "adapters/guidance/loader_core.hpp"
#include "adapters/guidance/schema.hpp"
#include "ports/store_error.hpp"

namespace ambient::guidance {
namespace {

using store::ChunkCipher;
using store::Db;
using store::Domain;
using store::StoreCode;
using store::StoreError;

constexpr std::size_t kFileBlock = 1 << 20;
constexpr const wchar_t* kStoreKeyDescription = L"ambient document store key";

std::string Iso8601Now() {
    const auto now = std::chrono::floor<std::chrono::seconds>(std::chrono::system_clock::now());
    return std::format("{:%FT%T}Z", now);
}

std::uint64_t Random64() {
    std::random_device device;
    return (static_cast<std::uint64_t>(device()) << 32) | device();
}

std::string Aad(std::int64_t id) {
    return "upload:" + std::to_string(id);
}

std::span<const std::uint8_t> Bytes(std::string_view s) {
    return {reinterpret_cast<const std::uint8_t*>(s.data()), s.size()};
}

std::string Text(const std::vector<std::uint8_t>& plain) {
    return {plain.begin(), plain.end()};
}

std::vector<std::uint8_t> ReadAll(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open()) throw StoreError(StoreCode::kIo, "cannot read " + path.string());
    return {std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
}

DocumentInfo Row(Db::Stmt& select, const ChunkCipher& key) {
    DocumentInfo info;
    info.id = select.ColumnInt64(0);
    info.mime = select.ColumnText(1);
    info.bytes = select.ColumnInt64(2);
    info.added_at = select.ColumnText(3);
    info.indexed_at = select.ColumnText(4);
    info.state = select.ColumnText(5);
    info.error = select.ColumnText(6);
    info.pages = static_cast<int>(select.ColumnInt64(7));
    info.pages_without_text = static_cast<int>(select.ColumnInt64(8));
    info.chunks = select.ColumnInt64(9);
    // A name that will not open still lists, as "could not be read"
    try {
        const auto seq = static_cast<std::uint64_t>(select.ColumnInt64(11));
        const auto doc_key =
            ChunkCipher::FromWrappedKey(key, Aad(info.id), seq, select.ColumnBlob(10));
        info.name = Text(doc_key.Open(Domain::kUploadName, Aad(info.id), 0, select.ColumnBlob(12)));
    } catch (const StoreError&) {
        info.state = "failed";
        info.error = "unreadable";
    }
    return info;
}

constexpr const char* kSelectRow =
    "SELECT d.id, d.mime, d.bytes, d.added_at, d.indexed_at, d.state, d.error, d.pages,"
    " d.pages_without_text, (SELECT count(*) FROM chunks c WHERE c.document_id = d.id),"
    " d.key_wrapped, d.key_seq, d.name FROM documents d";

}  // namespace

UploadStore::UploadStore(Db db, ChunkCipher key, EmbedderIdentity embedder,
                         std::filesystem::path files)
    : db_(std::move(db)),
      key_(std::move(key)),
      embedder_(std::move(embedder)),
      files_(std::move(files)) {}

std::unique_ptr<UploadStore> UploadStore::Open(const std::filesystem::path& root,
                                               const EmbedderIdentity& embedder,
                                               std::string& reason) {
    try {
        std::filesystem::create_directories(root / "files");
        Db db(root / kUploadFile, Db::Mode::kSession);
        const auto application_id = db.ApplicationId();
        Guard(application_id == 0 || application_id == kUploadApplicationId,
              "not a document store");
        Guard(db.UserVersion() <= kUploadFormat, "document store is newer than this build");
        std::optional<ChunkCipher> key;
        if (db.UserVersion() == 0) {
            Guard(db.QueryInt64("SELECT count(*) FROM sqlite_master") == 0, "not a document store");
            key.emplace(ChunkCipher::Generate());
            const auto wrapped = key->Wrapped(kStoreKeyDescription);
            Db::Transaction txn(db);
            db.Exec(kUploadSchemaSql);
            auto insert = db.Prepare(
                "INSERT INTO metadata(id, embedder_id, embedder_rev, dim, query_prefix, max_tokens,"
                " created_at, wrapped_key) VALUES(1, ?, ?, ?, ?, ?, ?, ?)");
            insert.BindText(1, embedder.id);
            insert.BindText(2, embedder.rev);
            insert.BindInt64(3, embedder.dim);
            insert.BindText(4, embedder.query_prefix);
            insert.BindInt64(5, embedder.max_tokens);
            insert.BindText(6, Iso8601Now());
            insert.BindBlob(7, wrapped);
            insert.Step();
            db.SetApplicationId(kUploadApplicationId);
            db.SetUserVersion(kUploadFormat);
            txn.Commit();
        } else {
            auto meta = db.Prepare(
                "SELECT embedder_id, embedder_rev, dim, query_prefix, max_tokens, wrapped_key"
                " FROM metadata WHERE id = 1");
            Guard(meta.Step(), "metadata has no row");
            key.emplace(ChunkCipher::FromWrapped(meta.ColumnBlob(5)));
            const bool same = meta.ColumnText(0) == embedder.id &&
                              meta.ColumnText(1) == embedder.rev &&
                              meta.ColumnInt64(2) == embedder.dim &&
                              meta.ColumnText(3) == embedder.query_prefix &&
                              meta.ColumnInt64(4) == embedder.max_tokens;
            meta.Reset();
            if (!same) {
                // Old vectors stay until each document is indexed again
                Db::Transaction txn(db);
                db.Exec("UPDATE documents SET state = 'stale' WHERE state = 'ready'");
                auto update = db.Prepare(
                    "UPDATE metadata SET embedder_id = ?, embedder_rev = ?, dim = ?,"
                    " query_prefix = ?, max_tokens = ? WHERE id = 1");
                update.BindText(1, embedder.id);
                update.BindText(2, embedder.rev);
                update.BindInt64(3, embedder.dim);
                update.BindText(4, embedder.query_prefix);
                update.BindInt64(5, embedder.max_tokens);
                update.Step();
                txn.Commit();
            }
        }
        auto store = std::unique_ptr<UploadStore>(
            new UploadStore(std::move(db), std::move(*key), embedder, root / "files"));

        // A document left half indexed goes, and so does a file without a row
        std::vector<std::int64_t> half;
        auto indexing = store->db_.Prepare("SELECT id FROM documents WHERE state = 'indexing'");
        while (indexing.Step()) half.push_back(indexing.ColumnInt64(0));
        indexing.Reset();
        for (const auto id : half) store->Remove(id);
        std::error_code ignored;
        for (const auto& entry : std::filesystem::directory_iterator(store->files_, ignored)) {
            auto exists = store->db_.Prepare("SELECT 1 FROM documents WHERE id = ?");
            exists.BindInt64(1, std::strtoll(entry.path().stem().string().c_str(), nullptr, 10));
            if (!exists.Step()) std::filesystem::remove(entry.path(), ignored);
        }
        reason.clear();
        return store;
    } catch (const Refused& e) {
        reason = e.what();
    } catch (const std::exception& e) {
        reason = std::string("document store unreadable: ") + e.what();
    }
    return nullptr;
}

std::vector<DocumentInfo> UploadStore::List() {
    std::vector<DocumentInfo> out;
    auto select = db_.Prepare((std::string(kSelectRow) + " ORDER BY d.added_at, d.id").c_str());
    while (select.Step()) out.push_back(Row(select, key_));
    return out;
}

DocumentInfo UploadStore::Get(std::int64_t id) {
    auto select = db_.Prepare((std::string(kSelectRow) + " WHERE d.id = ?").c_str());
    select.BindInt64(1, id);
    if (!select.Step()) throw StoreError(StoreCode::kNotFound, "no document " + std::to_string(id));
    return Row(select, key_);
}

UploadStore::Added UploadStore::Add(const std::filesystem::path& file, const std::string& name,
                                    const std::string& mime) {
    const auto identity = key_.Identity(file);
    {
        auto held = db_.Prepare("SELECT id FROM documents WHERE identity = ?");
        held.BindBlob(1, identity);
        if (held.Step()) return {held.ColumnInt64(0), false};
    }
    const auto bytes = ReadAll(file);
    if (bytes.empty()) throw StoreError(StoreCode::kIo, "empty file");

    std::int64_t id = 0;
    while (id <= 0) id = static_cast<std::int64_t>(Random64() & 0x7FFFFFFFFFFFFFFF);
    const auto key_seq = Random64();
    const auto doc_key = ChunkCipher::Generate();
    {
        Db::Transaction txn(db_);
        auto insert = db_.Prepare(
            "INSERT INTO documents(id, key_wrapped, key_seq, name, mime, bytes, identity,"
            " added_at, state) VALUES(?, ?, ?, ?, ?, ?, ?, ?, 'indexing')");
        insert.BindInt64(1, id);
        insert.BindBlob(2, key_.WrapKey(doc_key, Aad(id), key_seq));
        insert.BindInt64(3, static_cast<std::int64_t>(key_seq));
        insert.BindBlob(4, doc_key.Seal(Domain::kUploadName, Aad(id), 0, Bytes(name)));
        insert.BindText(5, mime);
        insert.BindInt64(6, static_cast<std::int64_t>(bytes.size()));
        insert.BindBlob(7, identity);
        insert.BindText(8, Iso8601Now());
        insert.Step();
        txn.Commit();
    }
    // The copy follows the row in sealed blocks. A copy that fails takes the row with it
    try {
        std::ofstream out(FilePath(id), std::ios::binary);
        if (!out.is_open()) throw StoreError(StoreCode::kIo, "cannot write the document copy");
        for (std::size_t block = 0, at = 0; at < bytes.size(); ++block, at += kFileBlock) {
            const auto count = std::min(kFileBlock, bytes.size() - at);
            const auto sealed = doc_key.Seal(Domain::kUploadFile, Aad(id), block,
                                             std::span(bytes.data() + at, count));
            out.write(reinterpret_cast<const char*>(sealed.data()),
                      static_cast<std::streamsize>(sealed.size()));
        }
        if (!out) throw StoreError(StoreCode::kFull, "the document copy did not write");
    } catch (...) {
        Remove(id);
        throw;
    }
    return {id, true};
}

void UploadStore::Finish(std::int64_t id, const std::vector<UploadChunk>& chunks, int pages,
                         int pages_without_text) {
    const auto doc_key = KeyFor(id);
    const auto dim = static_cast<std::size_t>(embedder_.dim);
    Db::Transaction txn(db_);
    auto clear = db_.Prepare("DELETE FROM chunks WHERE document_id = ?");
    clear.BindInt64(1, id);
    clear.Step();
    auto insert = db_.Prepare(
        "INSERT INTO chunks(document_id, ord, page, section, text, vec, boxes)"
        " VALUES(?, ?, ?, ?, ?, ?, ?)");
    for (std::size_t ord = 0; ord < chunks.size(); ++ord) {
        const auto& chunk = chunks[ord];
        if (chunk.vector.size() != dim)
            throw std::invalid_argument("vector has the wrong dimension");
        const std::span<const std::uint8_t> vec(
            reinterpret_cast<const std::uint8_t*>(chunk.vector.data()), dim * sizeof(float));
        insert.Reset();
        insert.BindInt64(1, id);
        insert.BindInt64(2, static_cast<std::int64_t>(ord));
        insert.BindInt64(3, chunk.page);
        const auto heading =
            nlohmann::json{{"number", chunk.number}, {"section", chunk.section}}.dump();
        insert.BindBlob(4, doc_key.Seal(Domain::kUploadSection, Aad(id), ord, Bytes(heading)));
        insert.BindBlob(5, doc_key.Seal(Domain::kUploadText, Aad(id), ord, Bytes(chunk.text)));
        insert.BindBlob(6, doc_key.Seal(Domain::kUploadVector, Aad(id), ord, vec));
        insert.BindBlob(7, doc_key.Seal(Domain::kUploadBoxes, Aad(id), ord, Bytes(chunk.boxes)));
        insert.Step();
    }
    auto ready = db_.Prepare(
        "UPDATE documents SET state = 'ready', error = NULL, indexed_at = ?, pages = ?,"
        " pages_without_text = ? WHERE id = ?");
    ready.BindText(1, Iso8601Now());
    ready.BindInt64(2, pages);
    ready.BindInt64(3, pages_without_text);
    ready.BindInt64(4, id);
    ready.Step();
    txn.Commit();
}

void UploadStore::Fail(std::int64_t id, const std::string& error) {
    Db::Transaction txn(db_);
    auto clear = db_.Prepare("DELETE FROM chunks WHERE document_id = ?");
    clear.BindInt64(1, id);
    clear.Step();
    auto failed = db_.Prepare("UPDATE documents SET state = 'failed', error = ? WHERE id = ?");
    failed.BindText(1, error);
    failed.BindInt64(2, id);
    failed.Step();
    if (db_.QueryInt64("SELECT changes()") == 0) {
        throw StoreError(StoreCode::kNotFound, "no document " + std::to_string(id));
    }
    txn.Commit();
}

std::vector<UploadChunk> UploadStore::ReadChunks(std::int64_t id) {
    const auto doc_key = KeyFor(id);
    const auto dim = static_cast<std::size_t>(embedder_.dim);
    std::vector<UploadChunk> out;
    auto select = db_.Prepare(
        "SELECT ord, page, section, text, vec, boxes FROM chunks WHERE document_id = ?"
        " ORDER BY ord");
    select.BindInt64(1, id);
    while (select.Step()) {
        const auto ord = static_cast<std::uint64_t>(select.ColumnInt64(0));
        UploadChunk chunk;
        chunk.page = static_cast<int>(select.ColumnInt64(1));
        const auto heading = nlohmann::json::parse(
            Text(doc_key.Open(Domain::kUploadSection, Aad(id), ord, select.ColumnBlob(2))));
        chunk.number = heading.value("number", "");
        chunk.section = heading.value("section", "");
        chunk.text = Text(doc_key.Open(Domain::kUploadText, Aad(id), ord, select.ColumnBlob(3)));
        const auto vec = doc_key.Open(Domain::kUploadVector, Aad(id), ord, select.ColumnBlob(4));
        Guard(vec.size() == dim * sizeof(float), "a stored vector has the wrong length");
        chunk.vector.resize(dim);
        std::memcpy(chunk.vector.data(), vec.data(), vec.size());
        chunk.boxes = Text(doc_key.Open(Domain::kUploadBoxes, Aad(id), ord, select.ColumnBlob(5)));
        out.push_back(std::move(chunk));
    }
    return out;
}

std::vector<std::uint8_t> UploadStore::ReadFile(std::int64_t id) {
    const auto doc_key = KeyFor(id);
    const auto sealed = ReadAll(FilePath(id));
    std::vector<std::uint8_t> plain;
    const std::size_t sealed_block = kFileBlock + 16;
    for (std::size_t block = 0, at = 0; at < sealed.size(); ++block, at += sealed_block) {
        const auto count = std::min(sealed_block, sealed.size() - at);
        const auto part =
            doc_key.Open(Domain::kUploadFile, Aad(id), block, std::span(sealed.data() + at, count));
        plain.insert(plain.end(), part.begin(), part.end());
    }
    return plain;
}

void UploadStore::Remove(std::int64_t id) {
    auto erase = db_.Prepare("DELETE FROM documents WHERE id = ?");
    erase.BindInt64(1, id);
    erase.Step();
    if (db_.QueryInt64("SELECT changes()") == 0) {
        throw StoreError(StoreCode::kNotFound, "no document " + std::to_string(id));
    }
    db_.CheckpointTruncate();
    RemoveFile(id);
}

std::size_t UploadStore::RemoveAll() {
    std::vector<std::int64_t> ids;
    auto select = db_.Prepare("SELECT id FROM documents");
    while (select.Step()) ids.push_back(select.ColumnInt64(0));
    select.Reset();
    db_.Exec("DELETE FROM documents");
    db_.CheckpointTruncate();
    for (const auto id : ids) RemoveFile(id);
    return ids.size();
}

ChunkCipher UploadStore::KeyFor(std::int64_t id) {
    auto select = db_.Prepare("SELECT key_wrapped, key_seq FROM documents WHERE id = ?");
    select.BindInt64(1, id);
    if (!select.Step()) throw StoreError(StoreCode::kNotFound, "no document " + std::to_string(id));
    return ChunkCipher::FromWrappedKey(
        key_, Aad(id), static_cast<std::uint64_t>(select.ColumnInt64(1)), select.ColumnBlob(0));
}

void UploadStore::RemoveFile(std::int64_t id) {
    std::error_code ignored;
    std::filesystem::remove(FilePath(id), ignored);
}

std::filesystem::path UploadStore::FilePath(std::int64_t id) const {
    return files_ / (std::to_string(id) + ".bin");
}

}  // namespace ambient::guidance
