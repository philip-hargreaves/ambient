#include "adapters/guidance/guidance_lane.hpp"

#include <gtest/gtest.h>

#include <chrono>
#include <condition_variable>
#include <functional>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace ambient::guidance {
namespace {

using namespace std::chrono_literals;

// Searches record their note and can be held open until released
struct FakeRetriever : IGuidanceRetriever {
    std::mutex mutex;
    std::condition_variable changed;
    bool hold = false;
    bool prepare_throws = false;
    bool search_throws = false;
    int prepares = 0;
    std::vector<std::string> searched;

    void Prepare() override {
        std::lock_guard<std::mutex> lock(mutex);
        ++prepares;
        changed.notify_all();
        if (prepare_throws) throw std::runtime_error("no embedding model staged");
    }
    Results Search(const std::string& note, int limit) override {
        std::unique_lock<std::mutex> lock(mutex);
        if (prepare_throws) throw std::runtime_error("no embedding model staged");
        if (search_throws) throw std::runtime_error("corpus gone");
        searched.push_back(note);
        changed.notify_all();
        changed.wait(lock, [this] { return !hold; });
        Results results;
        results.considered = limit;
        Result one;
        one.chunk_id = note;
        results.shown.push_back(one);
        return results;
    }
    std::vector<Corpus> Corpora() override {
        return {};
    }
    Readiness Status() override {
        std::lock_guard<std::mutex> lock(mutex);
        if (prepare_throws) return {Readiness::Phase::kUnavailable, "no embedding model staged"};
        return {Readiness::Phase::kReady, ""};
    }

    template <typename Pred>
    bool WaitUntil(Pred pred) {
        std::unique_lock<std::mutex> lock(mutex);
        return changed.wait_for(lock, 5s, pred);
    }
    void Release() {
        std::lock_guard<std::mutex> lock(mutex);
        hold = false;
        changed.notify_all();
    }
};

// What the callbacks delivered, waitable
struct Outcome {
    std::mutex mutex;
    std::condition_variable changed;
    std::vector<std::string> ready;   // first chunk id of each result set
    std::vector<std::string> failed;  // details
    std::thread::id last_thread;

    SearchRequest Request(std::string note, int limit = 3, std::string session = "") {
        SearchRequest request;
        request.session = std::move(session);
        request.note = std::move(note);
        request.limit = limit;
        request.on_ready = [this](const Results& results) {
            std::lock_guard<std::mutex> lock(mutex);
            ready.push_back(results.shown.empty() ? "" : results.shown[0].chunk_id);
            last_thread = std::this_thread::get_id();
            changed.notify_all();
        };
        request.on_failed = [this](const std::string& detail) {
            std::lock_guard<std::mutex> lock(mutex);
            failed.push_back(detail);
            changed.notify_all();
        };
        return request;
    }
    template <typename Pred>
    bool WaitUntil(Pred pred) {
        std::unique_lock<std::mutex> lock(mutex);
        return changed.wait_for(lock, 5s, pred);
    }
};

TEST(GuidanceLane, ReadyArrivesOffTheCallingThread) {
    FakeRetriever retriever;
    Outcome outcome;
    GuidanceLane lane(retriever);
    lane.Run(outcome.Request("Chest pain on exertion.", 4));
    ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 1; }));
    EXPECT_EQ(outcome.ready[0], "Chest pain on exertion.");
    EXPECT_NE(outcome.last_thread, std::this_thread::get_id());
    EXPECT_TRUE(outcome.failed.empty());
}

TEST(GuidanceLane, TheLatestRequestReplacesOneStillWaiting) {
    FakeRetriever retriever;
    retriever.hold = true;
    Outcome outcome;
    {
        GuidanceLane lane(retriever);
        lane.Run(outcome.Request("first"));
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.searched.size() == 1; }));
        lane.Run(outcome.Request("second"));
        lane.Run(outcome.Request("third"));
        retriever.Release();
        ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 2; }));
    }
    EXPECT_EQ(retriever.searched, (std::vector<std::string>{"first", "third"}));
    EXPECT_EQ(outcome.ready, (std::vector<std::string>{"first", "third"}));
    EXPECT_EQ(outcome.failed, (std::vector<std::string>{"superseded"}));
}

TEST(GuidanceLane, AFailedSearchReportsTheDetail) {
    FakeRetriever retriever;
    retriever.search_throws = true;
    Outcome outcome;
    GuidanceLane lane(retriever);
    lane.Run(outcome.Request("Chest pain on exertion."));
    ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.failed.size() == 1; }));
    EXPECT_EQ(outcome.failed[0], "corpus gone");
    EXPECT_TRUE(outcome.ready.empty());
}

TEST(GuidanceLane, ALoadFailureIsLoggedAndSurfacesOnTheSearch) {
    FakeRetriever retriever;
    retriever.prepare_throws = true;
    Outcome outcome;
    GuidanceLane lane(retriever);
    lane.Prepare();
    lane.Run(outcome.Request("Chest pain on exertion."));
    ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.failed.size() == 1; }));
    EXPECT_EQ(outcome.failed[0], "no embedding model staged");
    EXPECT_EQ(retriever.prepares, 1);
}

TEST(GuidanceLane, PrepareReportsHowLoadingEnded) {
    std::vector<Readiness> heard;
    auto listen = [&](const Readiness& readiness) { heard.push_back(readiness); };
    {
        FakeRetriever retriever;
        GuidanceLane lane(retriever, listen);
        lane.Prepare();
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.prepares == 1; }));
    }
    {
        FakeRetriever retriever;
        retriever.prepare_throws = true;
        GuidanceLane lane(retriever, listen);
        lane.Prepare();
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.prepares == 1; }));
    }
    ASSERT_EQ(heard.size(), 2u);
    EXPECT_EQ(heard[0].phase, Readiness::Phase::kReady);
    EXPECT_EQ(heard[1].phase, Readiness::Phase::kUnavailable);
    EXPECT_EQ(heard[1].detail, "no embedding model staged");
}

TEST(GuidanceLane, TheNotesSearchAndATypedQueryWaitApart) {
    FakeRetriever retriever;
    retriever.hold = true;
    Outcome outcome;
    {
        GuidanceLane lane(retriever);
        lane.Run(outcome.Request("typed one"));
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.searched.size() == 1; }));
        lane.Run(outcome.Request("typed two"));
        lane.Run(outcome.Request("note", 3, "s1"));
        lane.Run(outcome.Request("typed three"));
        retriever.Release();
        ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 3; }));
    }
    EXPECT_EQ(retriever.searched, (std::vector<std::string>{"typed one", "note", "typed three"}));
    EXPECT_EQ(outcome.failed, (std::vector<std::string>{"superseded"}));
}

TEST(GuidanceLane, NoteSearchesForDifferentSessionsBothRun) {
    FakeRetriever retriever;
    retriever.hold = true;
    Outcome outcome;
    {
        GuidanceLane lane(retriever);
        lane.Run(outcome.Request("typed"));
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.searched.size() == 1; }));
        lane.Run(outcome.Request("note one", 3, "s1"));
        lane.Run(outcome.Request("note two", 3, "s2"));
        retriever.Release();
        ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 3; }));
    }
    EXPECT_EQ(retriever.searched, (std::vector<std::string>{"typed", "note one", "note two"}));
    EXPECT_TRUE(outcome.failed.empty());
}

TEST(GuidanceLane, ANewerNoteSearchReplacesOnlyItsOwnSessions) {
    FakeRetriever retriever;
    retriever.hold = true;
    Outcome outcome;
    {
        GuidanceLane lane(retriever);
        lane.Run(outcome.Request("busy"));
        ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.searched.size() == 1; }));
        lane.Run(outcome.Request("s1 first", 3, "s1"));
        lane.Run(outcome.Request("s2 first", 3, "s2"));
        lane.Run(outcome.Request("s1 second", 3, "s1"));
        retriever.Release();
        ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 3; }));
    }
    EXPECT_EQ(retriever.searched, (std::vector<std::string>{"busy", "s1 second", "s2 first"}));
    EXPECT_EQ(outcome.failed, (std::vector<std::string>{"superseded"}));
}

TEST(GuidanceLane, AFailureCallbackThatThrowsDoesNotStopTheWorker) {
    FakeRetriever retriever;
    retriever.search_throws = true;
    Outcome outcome;
    GuidanceLane lane(retriever);
    SearchRequest throwing = outcome.Request("first");
    throwing.on_failed = [](const std::string&) { throw std::runtime_error("shell gone"); };
    lane.Run(std::move(throwing));
    lane.Run(outcome.Request("second"));
    ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.failed.size() == 1; }));
    EXPECT_EQ(outcome.failed[0], "corpus gone");
}

TEST(GuidanceLane, PrepareRunsOnceOnTheWorker) {
    FakeRetriever retriever;
    retriever.hold = true;
    Outcome outcome;
    GuidanceLane lane(retriever);
    lane.Run(outcome.Request("busy"));
    ASSERT_TRUE(retriever.WaitUntil([&] { return retriever.searched.size() == 1; }));
    lane.Prepare();
    lane.Prepare();
    retriever.Release();
    lane.Run(outcome.Request("after prepare"));
    ASSERT_TRUE(outcome.WaitUntil([&] { return outcome.ready.size() == 2; }));
    EXPECT_EQ(retriever.prepares, 1);
}

}  // namespace
}  // namespace ambient::guidance
