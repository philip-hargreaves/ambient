#pragma once

#include <cstdint>
#include <nlohmann/json.hpp>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

using nlohmann::json;

// Bumped when a field changes meaning, so an old record reads as old
inline constexpr int kRecordVersion = 1;

// One shape for a search on the wire and at rest: guidance/ready wraps it with
// the session id, and a stored copy reads back whole, with the corpora it ran
// over and the floor it was held to
json ToJson(const Corpus& corpus);
json ToJson(const Results& results);

Corpus CorpusFromJson(const json& j);
Results FromJson(const json& j);

// A search kept with its session, tied to the note revision it ran on
struct Record {
    Results results;
    std::int64_t note_revision = 0;
};

json ToJson(const Record& record);
Record RecordFromJson(const json& j);

}  // namespace ambient::guidance
