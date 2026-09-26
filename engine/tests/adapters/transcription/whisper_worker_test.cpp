#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <future>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "adapters/transcription/whisper_transcriber.hpp"
#include "core/metrics/metrics.hpp"

namespace clinicavt::asr {
namespace {

Turn Labelled(std::uint64_t first_frame, std::size_t count) {
    Turn turn;
    turn.first_frame = first_frame;
    turn.frame_count = count;
    turn.text = "w" + std::to_string(first_frame);
    return turn;
}

TEST(WhisperWorker, ClipSegmentEdgesInsideTheClipAreTakenAsCuts) {
    // Two segments in a 3 s clip starting at frame 16000: the interior edge at
    // 1.2 s (and the segment end short of the clip end) are cuts. The clip's own
    // edges are excluded
    WhisperTranscriber transcriber([](std::span<const float> f, std::uint64_t first) {
        return std::vector<Turn>{{first, 19200, "", "have you had any clots"},
                                 {first + 19200, static_cast<std::uint64_t>(f.size()) - 19200 - 800,
                                  "", "not that I know of"}};
    });
    const std::vector<float> frames(48000, 0.1f);
    ASSERT_EQ(transcriber.DecodeClip(frames, 16000), "have you had any clots not that I know of");
    const auto cuts = transcriber.TakeClipCuts();
    EXPECT_EQ(cuts,
              (std::vector<std::uint64_t>{16000 + 19200, 16000 + 19200, 16000 + 48000 - 800}));
    EXPECT_TRUE(transcriber.TakeClipCuts().empty()) << "taking drains";
}

TEST(WhisperWorker, DecodesAccumulateIntoTheMetrics) {
    metrics::Registry registry;
    {
        WhisperTranscriber transcriber(
            DecodeLoader([] {
                return DecodeFn(
                    [](std::span<const float>, std::uint64_t) { return std::vector<Turn>{}; });
            }),
            &registry);
        const std::vector<float> clip(16000);
        (void)transcriber.DecodeClip(clip, 0);
        (void)transcriber.DecodeClip(clip, 16000);
    }

    const auto s = registry.Take();
    EXPECT_EQ(s.decoded_audio_seconds, 2.0);
    EXPECT_GE(s.decode_busy_seconds, 0.0);
}

TEST(WhisperWorker, AClipWaitsForTheLoadThenDecodes) {
    std::atomic<int> loads{0};
    WhisperTranscriber transcriber(DecodeLoader([&loads] {
        std::this_thread::sleep_for(std::chrono::milliseconds(30));
        ++loads;
        return DecodeFn([](std::span<const float> f, std::uint64_t first) {
            return std::vector<Turn>{Labelled(first, f.size())};
        });
    }));

    const std::vector<float> clip(10);
    EXPECT_EQ(transcriber.DecodeClip(clip, 40), "w40");
    EXPECT_EQ(loads.load(), 1) << "one load serves every clip";
    EXPECT_EQ(transcriber.DecodeClip(clip, 50), "w50");
    EXPECT_EQ(loads.load(), 1);
}

TEST(WhisperWorker, AThrowingDecodeResolvesTheClipEmpty) {
    WhisperTranscriber transcriber([](std::span<const float>, std::uint64_t) -> std::vector<Turn> {
        throw std::runtime_error("driver");
    });

    const std::vector<float> clip(10);
    EXPECT_EQ(transcriber.DecodeClip(clip, 0), "")
        << "a failed decode loses the text, not the session";
    EXPECT_TRUE(transcriber.TakeClipCuts().empty());
}

TEST(WhisperWorker, DecodeClipReturnsTheJoinedTurnTexts) {
    WhisperTranscriber transcriber([](std::span<const float>, std::uint64_t first) {
        std::vector<Turn> turns{Labelled(first, 100), Labelled(first + 100, 100)};
        turns.push_back(Labelled(first + 200, 100));
        turns.back().text.clear();  // empty texts are skipped
        return turns;
    });

    const std::vector<float> clip(300);
    EXPECT_EQ(transcriber.DecodeClip(clip, 7000), "w7000 w7100");
}

TEST(WhisperWorker, AFailedLoadResolvesClipsEmpty) {
    WhisperTranscriber transcriber(
        DecodeLoader([]() -> DecodeFn { throw std::runtime_error("no GPU"); }));

    const std::vector<float> clip(10);
    EXPECT_EQ(transcriber.DecodeClip(clip, 0), "") << "an aborted re-split keeps the original";
}

}  // namespace
}  // namespace clinicavt::asr

namespace clinicavt::asr {
namespace {

Turn At(std::uint64_t first, std::uint64_t count, const char* text) {
    Turn t;
    t.first_frame = first;
    t.frame_count = count;
    t.text = text;
    return t;
}

TEST(WhisperWorker, ChunksReachTheClipCallerAndTheirEdgesBecomeCuts) {
    WhisperTranscriber transcriber(DecodeFn([](std::span<const float>, std::uint64_t first) {
        return std::vector<Turn>{At(first, 16000, "have you had clots?"),
                                 At(first + 16000, 8000, "No.")};
    }));
    const std::vector<float> clip(24000, 0.0f);
    const auto chunks = transcriber.DecodeClipChunks(clip, 32000);
    ASSERT_EQ(chunks.size(), 2u);
    EXPECT_EQ(chunks[1].text, "No.");
    const auto cuts = transcriber.TakeClipCuts();
    EXPECT_EQ(cuts, (std::vector<std::uint64_t>{48000u, 48000u}))
        << "the interior chunk edge, both sides";
    EXPECT_EQ(transcriber.DecodeClip(clip, 32000), "have you had clots? No.");
}

}  // namespace
}  // namespace clinicavt::asr
