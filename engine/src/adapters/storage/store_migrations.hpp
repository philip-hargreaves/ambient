#pragma once

#include <cstdint>
#include <filesystem>

#include "adapters/storage/db.hpp"

namespace clinicavt::store {

// 1 was a catalog beside one file per session. 2 is the single database.
// 3 adds retain. 4 adds the summary and reflection kinds. 5 adds the demo flag.
// 6 adds the document sequence and the guidance kind
inline constexpr std::int64_t kSchemaVersion = 6;
// "AMBC": the header mark of a clinical store
inline constexpr std::int64_t kApplicationId = 0x414D4243;

// Opens or creates the database under `root` and brings it to
// kSchemaVersion. Foreign and newer files are refused. Each step stamps its
// version inside its transaction. The column additions are skipped where an
// older build's half-stamped file already has them
Db OpenDatabase(const std::filesystem::path& root);

}  // namespace clinicavt::store
