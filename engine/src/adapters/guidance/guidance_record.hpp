#pragma once

#include <nlohmann/json.hpp>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

using nlohmann::json;

// One shape for a search on the wire and at rest: guidance/ready wraps it with
// the session id, and a stored copy reads back whole, with the corpora it ran
// over and the floor it was held to
json ToJson(const Corpus& corpus);
json ToJson(const Results& results);

Corpus CorpusFromJson(const json& j);
Results FromJson(const json& j);

}  // namespace ambient::guidance
