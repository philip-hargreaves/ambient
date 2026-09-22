#include "core/diarisation/frontier.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

namespace {

TEST(SegSettledFrontier, TrailsVadByTheRegionMargin) {
    using ambient::diar::kSegFrontierMarginFrames;
    EXPECT_EQ(ambient::diar::SegSettledFrontier(320000, 160000),
              160000u - kSegFrontierMarginFrames);                          // vad behind
    EXPECT_EQ(ambient::diar::SegSettledFrontier(100000, 320000), 100000u);  // seg behind
}

TEST(SegSettledFrontier, NothingSettlesInsideTheFirstMargin) {
    EXPECT_EQ(ambient::diar::SegSettledFrontier(320000, ambient::diar::kSegFrontierMarginFrames),
              0u);
    EXPECT_EQ(ambient::diar::SegSettledFrontier(320000, 0), 0u);
}

}  // namespace

namespace ambient::diar {
namespace {

// 32 ms hops: speech to hop 50, silence after
std::vector<float> SpeechThenSilence(std::size_t hops) {
    std::vector<float> p(hops, 0.05f);
    for (std::size_t i = 0; i <= 50 && i < hops; ++i) p[i] = 0.9f;
    return p;
}

TEST(TurnClosed, SilenceAfterTheEndClosesTheTurn) {
    const auto end = 51u * audio::kVadHopFrames;
    EXPECT_TRUE(TurnClosed(SpeechThenSilence(120), end));
}

TEST(TurnClosed, NotClosedWhileTheAudioIsShorterThanTheSilenceWindow) {
    const auto end = 51u * audio::kVadHopFrames;
    EXPECT_FALSE(TurnClosed(SpeechThenSilence(60), end));
}

TEST(TurnClosed, SpeechInsideTheWindowKeepsItOpen) {
    auto p = SpeechThenSilence(120);
    p[65] = 0.8f;
    const auto end = 51u * audio::kVadHopFrames;
    EXPECT_FALSE(TurnClosed(p, end));
}

}  // namespace
}  // namespace ambient::diar
