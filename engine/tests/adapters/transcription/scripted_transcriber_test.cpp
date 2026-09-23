#include "adapters/transcription/scripted_transcriber.hpp"

#include <gtest/gtest.h>

#include <vector>

namespace ambient::asr {
namespace {

TEST(ScriptedTranscriber, OneChunkPerClipWithItsTiming) {
    ScriptedTranscriber transcriber;
    const std::vector<float> clip(160);

    const auto chunks = transcriber.DecodeClipChunks(clip, 480);

    ASSERT_EQ(chunks.size(), 1u);
    EXPECT_EQ(chunks[0].first_frame, 480u);
    EXPECT_EQ(chunks[0].frame_count, 160u);
    EXPECT_TRUE(chunks[0].speaker.empty());
    EXPECT_EQ(chunks[0].text, "scripted turn 0, 160 frames");
}

TEST(ScriptedTranscriber, ClipsAreNumberedInDecodeOrder) {
    ScriptedTranscriber transcriber;
    const std::vector<float> clip(320);

    EXPECT_EQ(transcriber.DecodeClip(clip, 0), "scripted turn 0, 320 frames");
    EXPECT_EQ(transcriber.DecodeClip(clip, 320), "scripted turn 1, 320 frames");
}

TEST(ScriptedTranscriber, TwoInstancesScriptTheSameText) {
    ScriptedTranscriber first;
    ScriptedTranscriber second;
    const std::vector<float> clip(320);

    EXPECT_EQ(first.DecodeClip(clip, 0), second.DecodeClip(clip, 0));
}

}  // namespace
}  // namespace ambient::asr
