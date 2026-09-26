#pragma once

#include <memory>
#include <mutex>
#include <thread>

#include "core/session/session_events.hpp"
#include "ports/audio_source.hpp"
#include "ports/diariser.hpp"
#include "ports/streaming_vad.hpp"

namespace clinicavt::session {

// Voice enrolment: the microphone until Finish (or a cap in seconds), the
// speech embedded, the anchor replaced. Progress and the outcome arrive on
// the events. The caller keeps this exclusive with recording
class Enrolment {
   public:
    Enrolment(const SourceFactory& factory, audio::IStreamingVad& vad, diar::IDiariser& diariser,
              ISessionEvents& events);
    ~Enrolment();
    Enrolment(const Enrolment&) = delete;
    Enrolment& operator=(const Enrolment&) = delete;

    // False while one runs
    bool Start(double seconds, const MicSelection& mic, double min_speech_s);
    void Cancel();
    // The clinician reached the end of the passage: stop listening and make
    // the print from what was heard
    void Finish();
    bool Running() const;
    // Waits for a running enrolment to end
    void Join();

   private:
    void Run(double seconds, const MicSelection& mic, double min_speech_s);

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

}  // namespace clinicavt::session
