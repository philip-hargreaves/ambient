#pragma once

#include <chrono>
#include <cstdint>
#include <exception>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

#include "core/audio/voice_enrolment.hpp"
#include "core/session/session_events.hpp"
#include "ports/audio_source.hpp"
#include "ports/diariser.hpp"
#include "ports/streaming_vad.hpp"

namespace ambient::session {

// Voice enrolment: the microphone until Finish (or a cap in seconds), the
// speech embedded, the anchor replaced. Progress and the outcome arrive on
// the events. The caller keeps this exclusive with recording
class Enrolment {
   public:
    Enrolment(const SourceFactory& factory, audio::IStreamingVad& vad, diar::IDiariser& diariser,
              ISessionEvents& events)
        : factory_(factory), vad_(vad), diariser_(diariser), events_(events) {}

    ~Enrolment() {
        Cancel();
        Join();
    }
    Enrolment(const Enrolment&) = delete;
    Enrolment& operator=(const Enrolment&) = delete;

    // False while one runs
    bool Start(double seconds, const MicSelection& mic, double min_speech_s) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (running_) return false;
            running_ = true;
            cancel_ = false;
            finish_ = false;
        }
        if (thread_.joinable()) thread_.join();
        thread_ =
            std::thread([this, seconds, mic, min_speech_s] { Run(seconds, mic, min_speech_s); });
        return true;
    }

    void Cancel() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!running_) return;
        cancel_ = true;
        if (source_) source_->RequestStop();
    }

    // The clinician reached the end of the passage: stop listening and make
    // the print from what was heard
    void Finish() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!running_) return;
        finish_ = true;
        if (source_) source_->RequestStop();
    }

    bool Running() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return running_;
    }

    // Waits for a running enrolment to end
    void Join() {
        if (thread_.joinable()) thread_.join();
    }

   private:
    void Run(double seconds, const MicSelection& mic, double min_speech_s) {
        std::string why;
        double speech_s = 0.0;
        try {
            {
                std::lock_guard<std::mutex> lock(mutex_);
                source_ = factory_(std::nullopt, mic.id);
            }
            const auto frames = static_cast<std::uint64_t>(seconds * audio::kSampleRate);
            audio::EnrolmentSink sink(
                vad_, frames,
                [this] {
                    std::lock_guard<std::mutex> lock(mutex_);
                    if (source_) source_->RequestStop();
                },
                [this](const audio::EnrolProgress& progress) {
                    events_.OnEnrolProgress(progress);
                });
            try {
                source_->Run(sink);
            } catch (const std::exception& e) {
                sink.OnEnd({audio::SourceEndReason::kFailed, e.what()});
            }
            auto capture = sink.Take();
            speech_s = static_cast<double>(capture.speech.size()) / audio::kSampleRate;
            bool cancelled = false;
            {
                std::lock_guard<std::mutex> lock(mutex_);
                cancelled = cancel_ && !finish_;
            }
            why = audio::EnrolRejection(capture, cancelled, min_speech_s);
            if (why.empty()) {
                const auto voiceprint = diariser_.EmbedVoice(capture.speech);
                if (voiceprint.empty()) {
                    why = "could not build a voiceprint from the recording";
                } else {
                    const auto now = std::chrono::duration_cast<std::chrono::seconds>(
                                         std::chrono::system_clock::now().time_since_epoch())
                                         .count();
                    diariser_.ReplaceAnchor(voiceprint, static_cast<std::uint64_t>(now));
                }
            }
        } catch (const std::exception& e) {
            why = std::string("microphone unavailable: ") + e.what();
        }
        {
            std::lock_guard<std::mutex> lock(mutex_);
            source_.reset();
            running_ = false;
        }
        events_.OnEnrolDone(why.empty(), why, speech_s);
    }

    const SourceFactory& factory_;
    audio::IStreamingVad& vad_;
    diar::IDiariser& diariser_;
    ISessionEvents& events_;
    mutable std::mutex mutex_;
    std::unique_ptr<audio::IAudioSource> source_;  // under mutex_
    std::thread thread_;
    bool running_ = false;  // under mutex_
    bool cancel_ = false;   // under mutex_
    bool finish_ = false;   // under mutex_
};

}  // namespace ambient::session
