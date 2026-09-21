#pragma once

#include <functional>
#include <string>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

struct SearchRequest {
    std::string session;   // empty for a typed query
    bool as_note = false;  // typed text searched the way a saved note is
    std::string note;
    int limit = 0;
    std::function<void(const Results&)> on_ready;
    std::function<void(const std::string& detail)> on_failed;
};

// One search at a time off the RPC thread. Note searches queue one per session
// in arrival order and are served before a typed query, which waits alone. A
// request arriving while one waits for the same session (any session, for a
// typed query) replaces it, and the replaced request fails with "superseded".
// Requests still waiting at shutdown are dropped without a callback
class IGuidanceLane {
   public:
    virtual ~IGuidanceLane() = default;
    virtual void Prepare() = 0;
    virtual void Run(SearchRequest request) = 0;
};

}  // namespace ambient::guidance
