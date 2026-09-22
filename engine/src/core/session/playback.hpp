#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

#include "core/session/session_events.hpp"
#include "ports/session_store.hpp"

namespace ambient::session {

// The clock covers the whole recording in `listen`. Each document waits
// `first_token` then types itself out a little faster than the accuracy tier
struct PlaybackPacing {
    std::chrono::milliseconds listen{5000};
    std::chrono::milliseconds tick{80};
    std::chrono::milliseconds transcript{400};
    std::chrono::milliseconds speakers{1000};
    std::chrono::milliseconds guidance{400};
    std::chrono::milliseconds first_token{1500};
    double words_per_second = 18;
};

// Demo playback of a stored consultation through the session events: a
// demo-flagged copy is recorded, the clock races through the audio, then the
// copy's note and patient sheet stream as if written. The note is searched for
// guidance as a written one is, so the cards are what the documents hold today.
// Nothing is transcribed or generated
class Playback {
   public:
    struct Hooks {
        std::function<void(const store::SessionId&)> finalised;  // the copy is the review target
        std::function<void(const store::SessionId&)> guidance;   // search the copy's note
    };

    Playback(ISessionEvents& events, store::ISessionStore& store, Hooks hooks,
             PlaybackPacing pacing = {});
    ~Playback();
    Playback(const Playback&) = delete;
    Playback& operator=(const Playback&) = delete;

    // Copies `source` and starts the clock. False while one plays or when
    // the source has no finished transcript and note
    bool Start(const store::SessionId& source);
    // Ends listening and returns once finalise has run. The documents
    // follow on the playback thread
    void Stop();
    // Abandons the run. A copy still listening is erased
    void Cancel();
    void SetPaused(bool paused);
    bool Listening() const;
    // Anything in flight, listening through the patient sheet
    bool Active() const;
    store::SessionId Current() const;

   private:
    enum class Phase { kIdle, kListening, kFinalising, kWriting };

    struct Copy {
        store::SessionId id;
        double audio_seconds = 0;
        std::string note;
        std::string patient;
    };

    Copy MakeCopy(const store::SessionId& source);
    void Run(const Copy& copy);
    void Listen(double audio_seconds);
    void Finalise(const store::SessionId& id);
    void Write(const Copy& copy);
    void Stream(const std::string& text, const std::function<void(const std::string&)>& emit);
    void Erase(const store::SessionId& id);
    void Finish();
    void Join();

    ISessionEvents& events_;
    store::ISessionStore& store_;
    Hooks hooks_;
    PlaybackPacing pacing_;

    mutable std::mutex mutex_;
    std::condition_variable cv_;
    std::thread thread_;
    Phase phase_ = Phase::kIdle;
    bool stop_ = false;
    std::atomic<bool> cancel_ = false;
    bool paused_ = false;
    store::SessionId current_;
};

}  // namespace ambient::session
