#pragma once

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <future>
#include <mutex>
#include <span>
#include <string>
#include <thread>
#include <vector>

#include "ports/transcriber.hpp"

namespace clinicavt::models {
class ModelStore;
class OvRuntime;
}  // namespace clinicavt::models

namespace clinicavt::metrics {
class Registry;
}  // namespace clinicavt::metrics

namespace clinicavt::asr {

// One decode: Whisper's chunks with absolute frames, the cut-point source
using DecodeFn = std::function<std::vector<Turn>(std::span<const float>, std::uint64_t)>;

using DecodeLoader = std::function<DecodeFn()>;

// Whisper behind the transcriber port: one worker thread loads then decodes
// clips in order, so the load never blocks a caller
class WhisperTranscriber : public ITranscriber {
   public:
    // device_override: the device every decode runs on, else the manifest's
    WhisperTranscriber(const models::ModelStore& store, models::OvRuntime& runtime,
                       std::string device_override = "", metrics::Registry* metrics = nullptr);
    explicit WhisperTranscriber(DecodeFn decode);  // Tests inject the decode
    explicit WhisperTranscriber(DecodeLoader loader,
                                metrics::Registry* metrics = nullptr);  // Tests pace the load
    ~WhisperTranscriber() override;

    // Blocks until the worker has decoded the clip
    std::vector<Turn> DecodeClipChunks(std::span<const float> frames,
                                       std::uint64_t first_frame) override;

    std::vector<std::uint64_t> TakeClipCuts() override;

   private:
    struct Clip {
        std::vector<float> frames;
        std::uint64_t first_frame;
        std::promise<std::vector<Turn>> chunks;
    };

    void WorkerLoop();
    void LoadIfPending();
    void RecordDecode(std::size_t frames, std::chrono::steady_clock::time_point t0);

    DecodeLoader loader_;
    DecodeFn decode_;  // Worker-thread only once the loader has run
    metrics::Registry* metrics_ = nullptr;
    std::mutex mutex_;
    std::condition_variable cv_;
    std::deque<Clip> clips_;
    std::vector<std::uint64_t> clip_cuts_;  // segment edges inside decoded clips
    bool stopping_ = false;
    std::thread worker_;
};

}  // namespace clinicavt::asr
