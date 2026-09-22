#include <gtest/gtest.h>

#include <chrono>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <vector>

#include "adapters/models/model_store.hpp"
#include "adapters/models/ov_runtime.hpp"
#include "adapters/transcription/whisper_transcriber.hpp"
#include "ports/audio_source.hpp"

namespace ambient::asr {
namespace {

// Not in the repo. The test skips without it
constexpr const char* kWav =
    "C:/dev/intelliscribe/bench/transcription/mixed/day1_consultation01_mixed.wav";

std::vector<float> First30Seconds(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open()) {
        throw std::runtime_error(std::string("missing dev wav: ") + kWav);
    }
    in.seekg(44);  // PCM16 mono 16 kHz, header skipped
    std::vector<std::int16_t> pcm(30 * audio::kSampleRate);
    in.read(reinterpret_cast<char*>(pcm.data()), static_cast<std::streamsize>(pcm.size() * 2));
    std::vector<float> frames(pcm.size());
    for (std::size_t i = 0; i < pcm.size(); ++i) frames[i] = pcm[i] / 32768.0f;
    return frames;
}

TEST(WhisperReal, TranscribesRealSpeechWithTimingsInsideTheClip) {
    if (!std::filesystem::exists(kWav)) {
        GTEST_SKIP() << "research corpus not mounted";
    }
    const auto frames = First30Seconds(kWav);
    const models::ModelStore store(std::filesystem::path(AMBIENT_MODELS_DIR));
    models::OvRuntime runtime;

    WhisperTranscriber transcriber(store, runtime);

    const auto start = std::chrono::steady_clock::now();
    const auto chunks = transcriber.DecodeClipChunks(frames, 0);
    const auto took = std::chrono::duration<double>(std::chrono::steady_clock::now() - start);

    ASSERT_FALSE(chunks.empty());
    for (const auto& chunk : chunks) {
        EXPECT_FALSE(chunk.text.empty());
        EXPECT_LE(chunk.first_frame + chunk.frame_count, frames.size() + audio::kSampleRate);
    }
    std::printf("chunks: %zu, %.1fx realtime, first: \"%s\"\n", chunks.size(), 30.0 / took.count(),
                chunks[0].text.c_str());
}

}  // namespace
}  // namespace ambient::asr
