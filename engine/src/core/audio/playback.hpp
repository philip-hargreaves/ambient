#pragma once

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <format>
#include <functional>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "core/audio/session_controller.hpp"
#include "ports/session_store.hpp"

namespace ambient::audio {

// The clock covers the whole recording in `listen`; each document waits
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
             PlaybackPacing pacing = {})
        : events_(events), store_(store), hooks_(std::move(hooks)), pacing_(pacing) {}

    ~Playback() {
        Cancel();
    }
    Playback(const Playback&) = delete;
    Playback& operator=(const Playback&) = delete;

    // Copies `source` and starts the clock; false while one plays or when
    // the source has no sealed transcript and note
    bool Start(const store::SessionId& source) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (phase_ != Phase::kIdle) return false;
        }
        Join();
        Copy copy;
        try {
            copy = MakeCopy(source);
        } catch (const std::exception& e) {
            std::fprintf(stderr, "ambient-engine: playback refused: %s\n", e.what());
            return false;
        }
        std::fprintf(stderr, "ambient-engine: playback of %s as demo %s\n", source.c_str(),
                     copy.id.c_str());
        {
            std::lock_guard<std::mutex> lock(mutex_);
            phase_ = Phase::kListening;
            stop_ = cancel_ = paused_ = false;
            current_ = copy.id;
        }
        thread_ = std::thread([this, copy = std::move(copy)] { Run(copy); });
        return true;
    }

    // Ends listening and returns once finalise has run; the documents
    // follow on the playback thread
    void Stop() {
        std::unique_lock<std::mutex> lock(mutex_);
        if (phase_ == Phase::kIdle) return;
        stop_ = true;
        cv_.notify_all();
        cv_.wait(lock, [this] { return phase_ == Phase::kWriting || phase_ == Phase::kIdle; });
    }

    // Abandons the run; a copy still listening is erased
    void Cancel() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            cancel_ = true;
            cv_.notify_all();
        }
        Join();
    }

    void SetPaused(bool paused) {
        std::lock_guard<std::mutex> lock(mutex_);
        paused_ = paused;
        cv_.notify_all();
    }

    bool Listening() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return phase_ == Phase::kListening;
    }

    // Anything in flight, listening through the patient sheet
    bool Active() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return phase_ != Phase::kIdle;
    }

    store::SessionId Current() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return current_;
    }

   private:
    enum class Phase { kIdle, kListening, kFinalising, kWriting };

    struct Copy {
        store::SessionId id;
        double audio_seconds = 0;
        std::string note;
        std::string patient;
    };

    static std::string Iso8601(std::chrono::sys_seconds at) {
        return std::format("{:%FT%T}Z", at);
    }

    // A finalised, demo-flagged twin of the source, dated as if just recorded
    Copy MakeCopy(const store::SessionId& source) {
        using store::DocumentKind;
        const auto turns = store_.ReadTurns(source);
        const auto note = store_.ReadDocument(source, DocumentKind::kNote);
        if (turns.empty() || note.text.empty()) {
            throw std::runtime_error("no sealed transcript and note to play back");
        }
        Copy copy;
        for (const auto& turn : turns) {
            copy.audio_seconds =
                std::max(copy.audio_seconds,
                         static_cast<double>(turn.first_frame + turn.frame_count) / kSampleRate);
        }
        const auto now = std::chrono::floor<std::chrono::seconds>(std::chrono::system_clock::now());
        store::SessionSeed seed;
        seed.started_at =
            Iso8601(now - std::chrono::seconds(static_cast<long long>(copy.audio_seconds)));
        seed.ended_at = Iso8601(now);
        seed.turns = turns;
        copy.id = store_.Seed(seed);
        copy.note = note.text;
        store_.SaveDocument(copy.id, DocumentKind::kNote, note);
        if (const auto label = store_.ReadDocument(source, DocumentKind::kLabel);
            !label.text.empty()) {
            store_.SaveDocument(copy.id, DocumentKind::kLabel, label);
        }
        if (const auto patient = store_.ReadDocument(source, DocumentKind::kPatient);
            !patient.text.empty()) {
            copy.patient = patient.text;
            store_.SaveDocument(copy.id, DocumentKind::kPatient, patient);
        }
        return copy;
    }

    void Run(const Copy& copy) {
        Listen(copy.audio_seconds);
        {
            std::unique_lock<std::mutex> lock(mutex_);
            cv_.wait(lock, [this] { return stop_ || cancel_; });
            if (cancel_) {
                lock.unlock();
                Erase(copy.id);
                Finish();
                return;
            }
            phase_ = Phase::kFinalising;
        }
        Finalise(copy.id);
        Write(copy);
        Finish();
    }

    // Level readings carry the racing clock; a speech-shaped envelope, since
    // a meter pinned at one height reads as broken
    void Listen(double audio_seconds) {
        const int ticks = std::max<int>(1, static_cast<int>(pacing_.listen / pacing_.tick));
        for (int i = 1; i <= ticks; ++i) {
            {
                std::unique_lock<std::mutex> lock(mutex_);
                cv_.wait(lock, [this] { return !paused_ || stop_ || cancel_; });
                if (stop_ || cancel_) return;
            }
            const auto beat = static_cast<double>(i);
            const float level =
                static_cast<float>(0.25 + 0.55 * std::fabs(std::sin(beat * 0.6)) *
                                              (0.6 + 0.4 * std::fabs(std::sin(beat * 0.13))));
            events_.OnPlaybackLevel({level, false}, audio_seconds * i / ticks);
            std::this_thread::sleep_for(pacing_.tick);
        }
    }

    void Finalise(const store::SessionId& id) {
        events_.OnProgress("transcript");
        std::this_thread::sleep_for(pacing_.transcript);
        // No per-turn re-decode stage: the shell labels it as the transcript again
        events_.OnProgress("speakers");
        std::this_thread::sleep_for(pacing_.speakers);
        Call(hooks_.finalised, id, "review");
        std::lock_guard<std::mutex> lock(mutex_);
        phase_ = Phase::kWriting;
        cv_.notify_all();
    }

    void Write(const Copy& copy) {
        std::this_thread::sleep_for(pacing_.first_token);
        Stream(copy.note, [this](const std::string& text) { events_.OnNotePartial(text); });
        if (cancel_) return;
        events_.OnNoteReady(copy.note);
        std::this_thread::sleep_for(pacing_.guidance);
        Call(hooks_.guidance, copy.id, "guidance");
        std::this_thread::sleep_for(pacing_.first_token);
        Stream(copy.patient, [this](const std::string& text) { events_.OnPatientPartial(text); });
        if (cancel_) return;
        events_.OnPatientReady(copy.patient);
    }

    // Growing prefixes on word boundaries at the partial cadence
    void Stream(const std::string& text, const std::function<void(const std::string&)>& emit) {
        std::vector<std::size_t> ends;
        for (std::size_t i = 1; i < text.size(); ++i) {
            if (std::isspace(static_cast<unsigned char>(text[i])) != 0 &&
                std::isspace(static_cast<unsigned char>(text[i - 1])) == 0) {
                ends.push_back(i);
            }
        }
        if (text.empty()) return;
        ends.push_back(text.size());
        const auto over = std::chrono::milliseconds(static_cast<long long>(
            1000.0 * static_cast<double>(ends.size()) / pacing_.words_per_second));
        const auto steps = std::max<std::size_t>(1, static_cast<std::size_t>(over / pacing_.tick));
        const auto per = std::max<std::size_t>(1, (ends.size() + steps - 1) / steps);
        const auto pause = over / static_cast<int>((ends.size() + per - 1) / per);
        for (std::size_t i = per - 1; i < ends.size(); i += per) {
            if (cancel_) return;
            emit(text.substr(0, ends[i]));
            std::this_thread::sleep_for(pause);
        }
        if (ends.size() % per != 0) emit(text);
    }

    // A hook failing never ends the playback
    static void Call(const std::function<void(const store::SessionId&)>& hook,
                     const store::SessionId& id, const char* what) {
        if (!hook) return;
        try {
            hook(id);
        } catch (const std::exception& e) {
            std::fprintf(stderr, "ambient-engine: playback %s hook failed: %s\n", what, e.what());
        }
    }

    void Erase(const store::SessionId& id) {
        try {
            store_.Delete(id);
        } catch (const std::exception& e) {
            std::fprintf(stderr, "ambient-engine: playback copy not erased: %s\n", e.what());
        }
    }

    void Finish() {
        std::lock_guard<std::mutex> lock(mutex_);
        phase_ = Phase::kIdle;
        cv_.notify_all();
    }

    void Join() {
        if (thread_.joinable()) thread_.join();
    }

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

}  // namespace ambient::audio
