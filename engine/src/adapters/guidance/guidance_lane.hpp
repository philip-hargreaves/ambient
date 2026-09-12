#pragma once

#include <condition_variable>
#include <mutex>
#include <optional>
#include <thread>

#include "ports/guidance_lane.hpp"
#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {

// One worker over the retriever: loads in the background and calls the
// request's callbacks on the worker
class GuidanceLane : public IGuidanceLane {
   public:
    explicit GuidanceLane(IGuidanceRetriever& retriever);
    ~GuidanceLane() override;

    void Prepare() override;
    void Run(SearchRequest request) override;

   private:
    void Start();  // under mutex_
    void Work();

    IGuidanceRetriever& retriever_;
    std::mutex mutex_;
    std::condition_variable wake_;
    std::thread worker_;
    bool prepare_ = false;
    bool stop_ = false;
    std::optional<SearchRequest> pending_;
};

}  // namespace ambient::guidance
