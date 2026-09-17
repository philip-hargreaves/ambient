#pragma once

#include <cstdint>
#include <nlohmann/json.hpp>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

using nlohmann::json;

// Bumped when a field changes meaning, so a reader refuses a newer record
inline constexpr int kRecordVersion = 1;

// One shape on the wire and at rest: guidance/ready adds the session id, a
// stored copy reads back whole
json ToJson(const Corpus& corpus);
json ToJson(const Results& results);

Results FromJson(const json& j);

// A search kept with its session, tied to the note revision it ran on
struct Record {
    Results results;
    std::int64_t note_revision = 0;
};

json ToJson(const Record& record);
Record RecordFromJson(const json& j);

// Text for the store, invalid UTF-8 replaced as on the wire
std::string Dump(const Record& record);

// A stored record this reader can trust: an object no newer than kRecordVersion
bool CanRead(const json& j);

}  // namespace ambient::guidance
