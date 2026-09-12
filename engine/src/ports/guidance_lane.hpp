#pragma once

#include <functional>
#include <string>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

struct SearchRequest {
    std::string note;
    int limit = 0;
    std::function<void(const Results&)> on_ready;
    std::function<void(const std::string& detail)> on_failed;
};

// One search at a time off the RPC thread; a request arriving mid-search
// replaces any still waiting, so the latest note is the one searched
class IGuidanceLane {
   public:
    virtual ~IGuidanceLane() = default;
    virtual void Prepare() = 0;
    virtual void Run(SearchRequest request) = 0;
};

}  // namespace ambient::guidance
