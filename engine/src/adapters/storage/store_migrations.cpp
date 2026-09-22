#include "adapters/storage/store_migrations.hpp"

#include <string>

#include "adapters/storage/schema.hpp"
#include "ports/store_error.hpp"

namespace ambient::store {
namespace {

bool TableExists(Db& db, const char* table) {
    Db::Stmt exists = db.Prepare("SELECT count(*) FROM sqlite_master WHERE name = ?");
    exists.BindText(1, table);
    return exists.Step() && exists.ColumnInt64(0) != 0;
}

bool HasColumn(Db& db, const char* table, const char* column) {
    Db::Stmt info = db.Prepare(("PRAGMA table_info(" + std::string(table) + ")").c_str());
    while (info.Step()) {
        if (info.ColumnText(1) == column) return true;
    }
    return false;
}

// A row per dangling reference, none when the rebuild kept every one
void RequireForeignKeys(Db& db) {
    Db::Stmt check = db.Prepare("PRAGMA foreign_key_check");
    if (check.Step()) throw StoreError(StoreCode::kSchema, "migration left a dangling reference");
}

std::filesystem::path DatabasePath(const std::filesystem::path& root) {
    std::filesystem::create_directories(root);
    return root / "ambient.db";
}

}  // namespace

Db OpenDatabase(const std::filesystem::path& root) {
    Db db(DatabasePath(root));
    const std::int64_t application_id = db.ApplicationId();
    std::int64_t version = db.UserVersion();
    if (application_id != 0 && application_id != kApplicationId) {
        throw StoreError(StoreCode::kSchema, "not an ambient store");
    }
    if (version == 0) {
        if (db.QueryInt64("SELECT count(*) FROM sqlite_master") != 0) {
            throw StoreError(StoreCode::kSchema, "not an ambient store");
        }
        // Incremental vacuum is creation-time. The WAL switch already wrote the
        // header, so the empty file is rebuilt to take it
        db.Exec("PRAGMA auto_vacuum=INCREMENTAL");
        db.Exec("VACUUM");
        Db::Transaction txn(db);
        db.Exec(kSchemaSql);
        db.SetApplicationId(kApplicationId);
        db.SetUserVersion(kSchemaVersion);
        txn.Commit();
    } else if (version > kSchemaVersion) {
        throw StoreError(StoreCode::kSchema, "store schema is newer than this build");
    } else {
        if (application_id == 0 && !TableExists(db, "sessions")) {
            throw StoreError(StoreCode::kSchema, "not an ambient store");
        }
        if (version == 2) {
            Db::Transaction txn(db);
            if (!HasColumn(db, "sessions", "retain")) db.Exec(kMigrate2To3Sql);
            db.SetUserVersion(3);
            txn.Commit();
            version = 3;
        }
        if (version == 3) {
            // note_options references the dropped table: keys off, or the drop cascades. The pragma
            // is a no-op inside a transaction
            db.Exec("PRAGMA foreign_keys=OFF");
            {
                Db::Transaction txn(db);
                db.Exec(kMigrate3To4Sql);
                db.SetUserVersion(4);
                txn.Commit();
            }
            db.Exec("PRAGMA foreign_keys=ON");
            version = 4;
        }
        if (version == 4) {
            Db::Transaction txn(db);
            if (!HasColumn(db, "sessions", "demo")) db.Exec(kMigrate4To5Sql);
            db.SetUserVersion(5);
            txn.Commit();
            version = 5;
        }
        if (version == 5) {
            db.Exec("PRAGMA foreign_keys=OFF");
            {
                Db::Transaction txn(db);
                if (!HasColumn(db, "documents", "seq")) db.Exec(kMigrate5To6Sql);
                RequireForeignKeys(db);
                db.SetUserVersion(6);
                txn.Commit();
            }
            db.Exec("PRAGMA foreign_keys=ON");
            // Every earlier rewrite resealed under one IV. The copies sit in freed pages
            db.Exec("VACUUM");
        }
        // Stores from before the mark take it once
        if (application_id == 0) db.SetApplicationId(kApplicationId);
    }
    return db;
}

}  // namespace ambient::store
