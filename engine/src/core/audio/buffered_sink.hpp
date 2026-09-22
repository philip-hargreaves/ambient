#pragma once

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <exception>
#include <span>
#include <string>
#include <thread>
#include <vector>

#include "core/audio/audio_ring.hpp"
#include "ports/audio_source.hpp"

namespace ambient::audio {

// Takes the source's thread off the processing path: OnAudio only copies into
// a ring and returns, a consumer thread delivers to the inner sink. A stall
// behind the sink (a model compile, a slow disk) costs latency up to the
// ring's capacity, then frames, counted as lost like any other gap. OnEnd
// waits for the ring to drain and the inner OnEnd to run, so the source's
// contract holds: end is last, and delivered before Run returns.
class BufferedSink : public IAudioSink {
   public:
    BufferedSink(IAudioSink& inner, std::size_t capacity_frames)
        : inner_(inner),
          ring_(capacity_frames),
          scratch_(std::min(ring_.Capacity(), kChunkFrames)) {
        consumer_ = std::thread([this] { Consume(); });
    }

    ~BufferedSink() override {
        if (consumer_.joinable()) {
            OnEnd({SourceEndReason::kStopped, ""});
        }
    }

    BufferedSink(const BufferedSink&) = delete;
    BufferedSink& operator=(const BufferedSink&) = delete;

    // Source thread. Never blocks: what does not fit is lost, and said so.
    // The source's loss sits before its packet, so it is counted before the
    // push and travels with these frames. An overrun sits after what fitted,
    // so it is counted after and travels with the next
    void OnAudio(std::span<const float> frames, std::uint64_t lost_frames) override {
        if (lost_frames > 0) {
            pending_lost_.fetch_add(lost_frames, std::memory_order_relaxed);
        }
        const std::size_t pushed = ring_.TryPush(frames);
        if (pushed < frames.size()) {
            pending_lost_.fetch_add(frames.size() - pushed, std::memory_order_relaxed);
        }
        Wake();
    }

    // Source thread. Returns once the inner sink has seen every frame and the end
    void OnEnd(const SourceEnd& end) override {
        end_ = end;
        ended_.store(true, std::memory_order_release);
        Wake();
        if (consumer_.joinable()) {
            consumer_.join();
        }
    }

   private:
    void Wake() {
        wake_.store(1, std::memory_order_release);
        wake_.notify_one();
    }

    void Consume() {
        try {
            for (;;) {
                wake_.wait(0, std::memory_order_acquire);
                wake_.store(0, std::memory_order_relaxed);
                // Drain fully before looking at the end flag, so an end never
                // overtakes audio pushed ahead of it
                for (;;) {
                    const std::size_t n = ring_.TryPop(scratch_);
                    if (n == 0) {
                        break;
                    }
                    const std::uint64_t lost = pending_lost_.exchange(0, std::memory_order_relaxed);
                    inner_.OnAudio(std::span<const float>(scratch_.data(), n), lost);
                }
                if (ended_.load(std::memory_order_acquire)) {
                    inner_.OnEnd(end_);
                    return;
                }
            }
        } catch (const std::exception& e) {
            inner_.OnEnd(
                {SourceEndReason::kFailed, std::string("audio pipeline threw: ") + e.what()});
        } catch (...) {
            inner_.OnEnd({SourceEndReason::kFailed, "audio pipeline threw"});
        }
    }

    // One second per inner call bounds the work behind a stall
    static constexpr std::size_t kChunkFrames = static_cast<std::size_t>(kSampleRate);

    IAudioSink& inner_;
    AudioRing ring_;
    std::vector<float> scratch_;  // consumer only
    std::atomic<std::uint64_t> pending_lost_{0};
    std::atomic<int> wake_{0};
    std::atomic<bool> ended_{false};
    SourceEnd end_;  // written before ended_, read after
    std::thread consumer_;
};

}  // namespace ambient::audio
