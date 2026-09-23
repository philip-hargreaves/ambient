#include "adapters/models/residency.hpp"

#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <future>
#include <stdexcept>
#include <thread>

namespace ambient::models {
namespace {

using namespace std::chrono_literals;

constexpr auto kLong = std::chrono::hours(1);

template <typename Condition>
bool Eventually(Condition condition) {
    const auto until = std::chrono::steady_clock::now() + 2s;
    while (std::chrono::steady_clock::now() < until) {
        if (condition()) return true;
        std::this_thread::sleep_for(5ms);
    }
    return condition();
}

struct Counts {
    std::atomic<int> loads{0};
    std::atomic<int> unloads{0};
    std::atomic<int> failures_left{0};

    std::function<void()> Load() {
        return [this] {
            if (failures_left > 0) {
                --failures_left;
                throw std::runtime_error("no model");
            }
            ++loads;
        };
    }

    std::function<void()> Unload() {
        return [this] { ++unloads; };
    }
};

TEST(Residency, WantLoadsInTheBackgroundOnce) {
    Counts counts;
    Residency residency(counts.Load(), counts.Unload(), kLong);
    residency.Want();
    residency.Want();
    ASSERT_TRUE(Eventually([&] { return residency.Loaded(); }));
    residency.Want();
    std::this_thread::sleep_for(20ms);
    EXPECT_EQ(counts.loads, 1);
}

TEST(Residency, UseLoadsFirstWhenNothingIsLoaded) {
    Counts counts;
    Residency residency(counts.Load(), counts.Unload(), kLong);
    EXPECT_EQ(residency.Use([] { return 7; }), 7);
    EXPECT_EQ(counts.loads, 1);
    EXPECT_EQ(residency.Use([] { return 8; }), 8);
    EXPECT_EQ(counts.loads, 1);
}

TEST(Residency, ReleaseWaitsForWorkInProgress) {
    Counts counts;
    Residency residency(counts.Load(), counts.Unload(), kLong);
    std::promise<void> started;
    std::promise<void> finish;
    auto work = std::async(std::launch::async, [&] {
        residency.Use([&] {
            started.set_value();
            finish.get_future().wait();
        });
    });
    started.get_future().wait();
    residency.Release();
    std::this_thread::sleep_for(30ms);
    EXPECT_EQ(counts.unloads, 0);
    EXPECT_TRUE(residency.Loaded());
    finish.set_value();
    work.get();
    EXPECT_TRUE(Eventually([&] { return counts.unloads == 1; }));
    EXPECT_FALSE(residency.Loaded());
}

TEST(Residency, AnIdleSpellUnloadsAndTheNextUseReloads) {
    Counts counts;
    Residency residency(counts.Load(), counts.Unload(), 40ms);
    residency.Use([] {});
    ASSERT_TRUE(Eventually([&] { return counts.unloads == 1; }));
    residency.Use([] {});
    EXPECT_EQ(counts.loads, 2);
}

TEST(Residency, WantCancelsAPendingRelease) {
    Counts counts;
    Residency residency(counts.Load(), counts.Unload(), kLong);
    std::promise<void> started;
    std::promise<void> finish;
    auto work = std::async(std::launch::async, [&] {
        residency.Use([&] {
            started.set_value();
            finish.get_future().wait();
        });
    });
    started.get_future().wait();
    residency.Release();
    residency.Want();
    finish.set_value();
    work.get();
    std::this_thread::sleep_for(30ms);
    EXPECT_EQ(counts.unloads, 0);
    EXPECT_TRUE(residency.Loaded());
}

TEST(Residency, ReleaseDuringABackgroundLoadUnloadsWhenItEnds) {
    std::promise<void> gate;
    auto opened = gate.get_future().share();
    std::atomic<int> unloads{0};
    Residency residency([opened] { opened.wait(); }, [&unloads] { ++unloads; }, kLong);
    residency.Want();
    std::this_thread::sleep_for(20ms);
    residency.Release();
    gate.set_value();
    EXPECT_TRUE(Eventually([&] { return unloads == 1; }));
    EXPECT_FALSE(residency.Loaded());
}

TEST(Residency, AFailedBackgroundLoadIsRetriedByUseWhichReportsItsOwnFailure) {
    Counts counts;
    counts.failures_left = 2;
    Residency residency(counts.Load(), counts.Unload(), kLong);
    residency.Want();
    ASSERT_TRUE(Eventually([&] { return counts.failures_left == 1; }));
    EXPECT_FALSE(residency.Loaded());
    EXPECT_THROW(residency.Use([] {}), std::runtime_error);
    EXPECT_EQ(residency.Use([] { return 3; }), 3);
    EXPECT_EQ(counts.loads, 1);
}

}  // namespace
}  // namespace ambient::models
