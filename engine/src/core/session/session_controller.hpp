#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <memory>
#include <mutex>
#include <optional>
#include <span>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "core/audio/buffered_sink.hpp"
#include "core/audio/level_meter.hpp"
#include "core/audio/resume_source.hpp"
#include "core/diarisation/tidy_transcript.hpp"
#include "core/metrics/metrics.hpp"
#include "core/session/enrolment.hpp"
#include "core/session/note_lane.hpp"
#include "core/session/session_events.hpp"
#include "core/session/transcribe_recording.hpp"
#include "ports/audio_source.hpp"
#include "ports/diariser.hpp"
#include "ports/note_writer.hpp"
#include "ports/session_store.hpp"
#include "ports/transcriber.hpp"

namespace ambient::session {

// One session at a time; every ending has a storage outcome: Stop
// finalises, Cancel erases, an interruption abandons recoverable
class SessionController {
   public:
    static constexpr std::size_t kMinNoteWords = NoteLane::kMinNoteWords;
    // Audio the capture thread can run ahead of the pipeline before frames are
    // lost; a first-launch model compile stalls for seconds, not tens
    static constexpr std::size_t kCaptureBufferFrames = 30 * audio::kSampleRate;

    SessionController(SourceFactory factory, ISessionEvents& events, store::ISessionStore& store,
                      asr::ITranscriber& transcriber, audio::IStreamingVad& vad,
                      diar::IDiariser& diariser,
                      std::chrono::milliseconds settle_timeout = std::chrono::seconds(3),
                      std::uint64_t diar_advance_frames = 5 * audio::kSampleRate,
                      note::INoteWriter* note_writer = nullptr,
                      metrics::Registry* metrics = nullptr,
                      std::size_t min_note_words = kMinNoteWords)
        : factory_(std::move(factory)),
          events_(events),
          store_(store),
          transcriber_(transcriber),
          diariser_(diariser),
          note_writer_(note_writer),
          metrics_(metrics),
          diar_advance_frames_(diar_advance_frames),
          settle_timeout_(settle_timeout),
          note_lane_(note_writer, store, events, min_note_words),
          enrolment_(factory_, vad, diariser, events) {
        store_.SetFaultListener(
            [this](const store::StoreError& fault) { events_.OnStorageFault(fault.what()); });
    }

    // Every lane is joined before the members they read are destroyed
    ~SessionController() {
        Stop();
        enrolment_.Cancel();
        enrolment_.Join();
        note_lane_.Join();
    }
    SessionController(const SessionController&) = delete;
    SessionController& operator=(const SessionController&) = delete;

    // True once audio flows. resume replays stored audio ahead of the live
    // source; retain false erases once the consultation is left
    bool Start(std::optional<ReplaySpec> replay = std::nullopt,
               const store::SessionId& resume_from = {}, bool retain = true,
               const MicSelection& mic = {}) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (running_ || enrolment_.Running()) {
                return false;
            }
            running_ = true;
            reviewing_ = false;  // Record wins over a review
            got_audio_ = false;
            ended_ = false;
            stop_requested_ = false;
            diar_stop_ = false;
            lost_frames_ = 0;
            end_ = {};
            meter_ = audio::LevelMeter{};
        }
        std::vector<float> resumed_audio;
        try {
            if (!resume_from.empty()) {
                resumed_audio = store_.ReadAudio(resume_from);
                std::fprintf(stderr, "ambient-engine: resuming %s with %.1f s of stored audio\n",
                             resume_from.c_str(),
                             static_cast<double>(resumed_audio.size()) / audio::kSampleRate);
            }
            store_.EraseUnretained();  // the previous consultation is left
            store::SessionMeta meta{audio::kSampleRate, "", ""};
            if (!replay.has_value()) {
                // What was actually opened, so a default fallback is on record
                meta.device_id = mic.id;
                meta.device_name = mic.name;
            }
            meta.retain = retain;
            const store::SessionId id = store_.Begin(meta);
            std::lock_guard<std::mutex> lock(mutex_);
            session_id_ = id;
            resumed_from_ = resume_from;
            note_prepared_ = false;
            session_audio_.clear();
        } catch (const std::exception& e) {
            std::fprintf(stderr, "ambient-engine: session start failed: %s\n", e.what());
            std::lock_guard<std::mutex> lock(mutex_);
            running_ = false;
            end_ = {audio::SourceEndReason::kFailed,
                    std::string("session setup failed: ") + e.what()};
            return false;
        }
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!resumed_audio.empty()) {
                if (replay.has_value()) {
                    replay->start_frame += resumed_audio.size();
                }
                source_ = std::make_unique<audio::ResumeSource>(std::move(resumed_audio),
                                                                factory_(replay, mic.id));
            } else {
                source_ = factory_(replay, mic.id);
            }
        }
        worker_ = std::thread([this] { GuardedRun(); });
        diar_thread_ = std::thread([this] { DiarLoop(); });
        if (metrics_ != nullptr) {
            metrics_->BeginSession(replay.has_value(), replay.has_value() ? replay->speed : 0.0);
        }

        std::unique_lock<std::mutex> lock(mutex_);
        cv_.wait_for(lock, settle_timeout_, [this] { return got_audio_ || ended_; });
        if (got_audio_) {
            return true;
        }
        lock.unlock();
        Cancel();  // a session that never produced audio leaves no trace

        std::lock_guard<std::mutex> relock(mutex_);
        if (end_.reason == audio::SourceEndReason::kStopped) {
            end_ = {audio::SourceEndReason::kFailed, "no audio arrived before the deadline"};
        }
        return false;
    }

    // Idempotent; a stop is the user's, so it never counts as an interruption.
    // The recording is kept
    void Stop() {
        // Re-warm in parallel with finalise; a long session may have evicted
        if (note_writer_ != nullptr && Running()) {
            note_writer_->Prepare();
        }
        EndCapture();
        FinishSession(Outcome::kFinalise);
    }

    // Idempotent; the recording is erased (D5)
    void Cancel() {
        EndCapture();
        FinishSession(Outcome::kCancel);
    }

    // Hold the source's delivery; stop and cancel always win
    void SetPaused(bool paused) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (source_ && running_) source_->SetPaused(paused);
    }

    void SetMonitor(bool monitor) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (source_ && running_) source_->SetMonitor(monitor);
    }

    bool Running() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return running_ && !ended_;
    }

    // Evaluation only: the print never learns, so a held-out run is reproducible
    void FreezeAnchor() {
        learn_anchor_ = false;
    }

    // Enrolment is refused while a consultation runs, and recording while an
    // enrolment runs; the lock orders the two checks
    bool StartEnrolment(double seconds, const MicSelection& mic = {},
                        double min_speech_s = audio::kEnrolMinSpeechSeconds) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (running_) return false;
        return enrolment_.Start(seconds, mic, min_speech_s);
    }

    void CancelEnrolment() {
        enrolment_.Cancel();
    }

    void FinishEnrolment() {
        enrolment_.Finish();
    }

    bool Enrolling() const {
        return enrolment_.Running();
    }

    audio::SourceEnd LastEnd() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return end_;
    }

    std::uint64_t LostFrames() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return lost_frames_;
    }

    // The most recently finalised session, for the shell's post-stop
    // transcript fetch; empty until a session has finalised
    store::SessionId LastFinalised() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return last_finalised_;
    }

    // Reopens a stored session as the regenerate target; refused while
    // recording or writing
    bool Open(const store::SessionId& id) {
        if (Running() || note_lane_.Busy()) {
            return false;
        }
        try {
            (void)store_.ReadTurns(id);
        } catch (...) {
            return false;
        }
        std::lock_guard<std::mutex> lock(mutex_);
        last_finalised_ = id;
        reviewing_ = true;
        note_lane_.ClearRefusal();
        return true;
    }

    // Leaving the consultation: ends a review (regenerate refuses until the
    // next finalise or open), deletes a session that ended in a refusal, and
    // erases what was recorded with retain off
    void Close() {
        store::SessionId refused;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (note_lane_.Refused()) {
                refused = std::exchange(last_finalised_, {});
                note_lane_.ClearRefusal();
            }
            if (reviewing_) {
                reviewing_ = false;
                last_finalised_.clear();
            }
        }
        if (!refused.empty()) {
            try {
                store_.Delete(refused);
            } catch (...) {  // NOLINT(bugprone-empty-catch)
            }
        }
        if (!Running()) {
            try {
                store_.EraseUnretained();
            } catch (...) {  // NOLINT(bugprone-empty-catch)
            }
        }
    }

    bool Reviewing() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return reviewing_;
    }

    // The recording session's id, so the shell can resume it after a crash
    store::SessionId CurrentSession() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return session_id_;
    }

    // Applied to the next note; the shell sets these ahead of the stop
    void SetNoteOptions(note::NoteOptions options) {
        note_lane_.SetOptions(std::move(options));
    }

    note::NoteOptions CurrentNoteOptions() const {
        return note_lane_.Options();
    }

    bool HasNoteWriter() const {
        return note_lane_.Available();
    }

    // Rewrites the last finalised session's note; false when busy - the RPC
    // thread never blocks on the lane
    bool RegenerateNote(note::NoteOptions options) {
        if (!note_lane_.Available() || Running() || note_lane_.Busy()) {
            return false;
        }
        store::SessionId id = LastFinalised();
        if (id.empty()) {
            return false;
        }
        std::vector<asr::Turn> turns;
        try {
            turns = store_.ReadTurns(id);
        } catch (...) {
            return false;
        }
        note_lane_.SetOptions(std::move(options));
        note_lane_.WriteNote(std::move(id), std::move(turns));
        return true;
    }

    // Case summary from the stored note, edits included, for any stored
    // session. False when busy or without a note
    bool WriteSummary(store::SessionId id) {
        if (!note_lane_.Available() || Running() || note_lane_.Busy()) {
            return false;
        }
        std::string note = StoredNote(id);
        if (note.empty()) {
            return false;
        }
        note_lane_.WriteSummary(std::move(id), std::move(note));
        return true;
    }

    // The sheet rewritten from the stored note, clinician edits included
    bool RegeneratePatient() {
        if (!note_lane_.WritesPatient() || Running() || note_lane_.Busy()) {
            return false;
        }
        store::SessionId id = LastFinalised();
        if (id.empty()) {
            return false;
        }
        std::string note = StoredNote(id);
        if (note.empty()) {
            return false;
        }
        note_lane_.WritePatient(std::move(id), std::move(note));
        return true;
    }

   private:
    enum class Outcome { kFinalise, kCancel, kAbandon };

    // The pipeline thread: the store, the diariser's audio, the meter
    struct PipelineSink : audio::IAudioSink {
        SessionController& controller;

        explicit PipelineSink(SessionController& owner) : controller(owner) {}

        void OnAudio(std::span<const float> frames, std::uint64_t lost_frames) override {
            store::SessionId id;
            {
                std::lock_guard<std::mutex> lock(controller.mutex_);
                controller.lost_frames_ += lost_frames;
                controller.got_audio_ = true;
                id = controller.session_id_;
                // Under the lock: the diarisation thread snapshots this
                if (!id.empty()) {
                    controller.session_audio_.insert(controller.session_audio_.end(),
                                                     frames.begin(), frames.end());
                }
            }
            controller.cv_.notify_all();
            if (!id.empty()) {
                controller.store_.Append(id, frames, lost_frames);
            }
            for (const auto& reading : controller.meter_.Push(frames)) {
                controller.events_.OnLevel(reading);
            }
        }

        void OnEnd(const audio::SourceEnd& end) override {
            bool interrupted = false;
            {
                std::lock_guard<std::mutex> lock(controller.mutex_);
                controller.end_ = end;
                controller.ended_ = true;
                interrupted = controller.got_audio_ && !controller.stop_requested_ &&
                              (end.reason == audio::SourceEndReason::kDeviceLost ||
                               end.reason == audio::SourceEndReason::kFailed);
            }
            controller.cv_.notify_all();
            // Only an interruption decides its own outcome; a stopped source leaves
            // keep-or-discard to Stop or Cancel
            if (interrupted) {
                controller.FinishSession(Outcome::kAbandon);
                controller.events_.OnInterrupted(end.reason, end.detail);
            }
        }
    };

    // The source's thread only fills the ring; the pipeline runs behind it.
    // An escape from a thread function is std::terminate, so nothing escapes
    void GuardedRun() {
        PipelineSink sink(*this);
        audio::BufferedSink buffered(sink, kCaptureBufferFrames);
        try {
            source_->Run(buffered);
        } catch (const std::exception& e) {
            buffered.OnEnd({audio::SourceEndReason::kFailed,
                            std::string("capture thread threw: ") + e.what()});
        } catch (...) {
            buffered.OnEnd({audio::SourceEndReason::kFailed, "capture thread threw"});
        }
    }

    // Diarisation's causal work, spread over the recording; the heavy
    // Advance runs outside the lock, off the pipeline thread
    void DiarLoop() {
        // Accelerated replay delivers audio faster than real time; a wall
        // floor keeps the tick rate sane at any speed
        constexpr auto kMinTickGap = std::chrono::seconds(1);
        std::vector<float> audio;
        std::unique_lock<std::mutex> lock(mutex_);
        for (;;) {
            cv_.wait(lock, [this, &audio] {
                return diar_stop_ || session_audio_.size() >= audio.size() + diar_advance_frames_;
            });
            if (diar_stop_) {
                return;
            }
            audio = session_audio_;
            ++diar_ticks_;
            lock.unlock();
            // Deferred until whisper is decoding so the GPU never compiles
            // two models at once; still minutes ahead of any real stop
            if (!note_prepared_ && note_writer_ != nullptr) {
                note_prepared_ = true;
                note_writer_->Prepare();
            }
            try {
                const auto decode = [this](std::span<const float> clip,
                                           std::uint64_t first) -> std::vector<asr::Turn> {
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        // A stop must not wait behind a speculation pass
                        if (diar_stop_) return {};
                    }
                    return transcriber_.DecodeClipChunks(clip, first);
                };
                diariser_.Advance(audio, decode);
                // This tick's chunk edges re-slice the audio; the pieces decode
                // in the same tick, so a stop never waits for them
                const auto cuts = transcriber_.TakeClipCuts();
                if (!cuts.empty()) {
                    diariser_.AddCutPoints(cuts);
                    diariser_.Advance(audio, decode);
                }
                // The note host extends its KV over the settled opening between
                // whisper decodes; the finalise tidies its turns, so the prefix must
                // read the same
                if (note_writer_ != nullptr) {
                    auto guess = diar::TidyTranscript(diariser_.SpeculativeTranscript());
                    if (!guess.empty()) note_writer_->Prefill(guess, note_lane_.Options());
                }
            } catch (...) {  // NOLINT(bugprone-empty-catch)
            }
            lock.lock();
            cv_.wait_for(lock, kMinTickGap, [this] { return diar_stop_; });
            if (diar_stop_) {
                return;
            }
        }
    }

    void JoinDiarThread() {
        std::thread diar;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            diar_stop_ = true;
            diar = std::move(diar_thread_);
        }
        cv_.notify_all();
        if (diar.joinable()) {
            diar.join();
        }
    }

    void EndCapture() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_requested_ = true;
        }
        if (source_) {
            source_->RequestStop();
        }
        if (worker_.joinable()) {
            worker_.join();
        }
        std::lock_guard<std::mutex> lock(mutex_);
        running_ = false;
    }

    // Stop, cancel and abandon all end here; the store outcome always holds even
    // if the bookkeeping around it fails
    void FinishSession(Outcome outcome) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (session_id_.empty()) {
                return;
            }
        }
        // No capture work may run once finalise starts; stage timings let a
        // slow finalise name its stage
        const auto finalise_start = std::chrono::steady_clock::now();
        const auto stage = [this, &finalise_start](const char* name) {
            const double seconds =
                std::chrono::duration<double>(std::chrono::steady_clock::now() - finalise_start)
                    .count();
            std::fprintf(stderr, "ambient-engine: finalise %s at %.1f s\n", name, seconds);
            if (metrics_ != nullptr) {
                metrics_->RecordStage(name, seconds);
            }
        };
        JoinDiarThread();
        stage("capture joined");
        std::fprintf(stderr, "ambient-engine: session audio %.1f s, %d capture ticks\n",
                     static_cast<double>(session_audio_.size()) / audio::kSampleRate, diar_ticks_);
        if (metrics_ != nullptr) {
            metrics_->RecordSession(static_cast<double>(session_audio_.size()) / audio::kSampleRate,
                                    lost_frames_, diar_ticks_);
        }
        // Capture decodes a few spans per tick and can lag; the rest decodes
        // now, so the cuts reach the diariser at every replay speed
        if (outcome == Outcome::kFinalise) {
            try {
                diariser_.Settle(session_audio_,
                                 [this](std::span<const float> clip, std::uint64_t first) {
                                     return transcriber_.DecodeClipChunks(clip, first);
                                 });
                const auto cuts = transcriber_.TakeClipCuts();
                if (!cuts.empty()) diariser_.AddCutPoints(cuts);
            } catch (...) {  // NOLINT(bugprone-empty-catch)
            }
            stage("capture settled");
            events_.OnProgress("transcript");
        }

        store::SessionId id;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            id = std::exchange(session_id_, {});
            if (outcome == Outcome::kFinalise) {
                last_finalised_ = id;
                note_lane_.ClearRefusal();
            }
        }
        if (id.empty()) {
            return;
        }
        // The note lane's input is the attributed transcript; a diarisation
        // failure leaves it empty, so the note is refused as too thin and the
        // session is never lost
        std::vector<asr::Turn> note_input;
        std::vector<float> doctor_voiceprint;
        if (outcome == Outcome::kFinalise && !session_audio_.empty()) {
            try {
                auto transcript = TranscribeRecording(session_audio_, diariser_, transcriber_,
                                                      events_, metrics_, stage);
                if (!transcript.turns.empty()) {
                    store_.ReplaceTurns(id, transcript.turns);
                    note_input = std::move(transcript.turns);
                }
                stage("transcript sealed");
                // The print learns only from named sessions, never a guess, and
                // only once the note lane agrees this was a consultation
                if (transcript.doctor_cluster >= 0 && learn_anchor_) {
                    doctor_voiceprint = diariser_.DoctorVoiceprint(
                        session_audio_, transcript.diarised.slices, transcript.doctor_cluster);
                    if (note_writer_ == nullptr) {
                        diariser_.AccrueVoiceprint(doctor_voiceprint);
                        doctor_voiceprint.clear();
                    }
                }
                stage("anchor accrued");
            } catch (const std::exception& e) {
                std::fprintf(stderr, "ambient-engine: transcription failed: %s\n", e.what());
            } catch (...) {
                std::fprintf(stderr, "ambient-engine: transcription failed\n");
            }
        }
        // Capture state a finalise did not consume must not leak into the
        // next session (cancel, abandon, a diarisation failure)
        diariser_.DiscardCapture();
        session_audio_.clear();
        session_audio_.shrink_to_fit();
        try {
            switch (outcome) {
                case Outcome::kFinalise:
                    store_.Finalise(id);
                    break;
                case Outcome::kCancel:
                    store_.Cancel(id);
                    break;
                case Outcome::kAbandon:
                    store_.Abandon(id);
                    break;
            }
        } catch (const std::exception& e) {
            ReportStoreFailure(events_, "outcome", e);
        }
        stage("stored");
        // The resumed-from session is superseded: everything it held flowed
        // into this one before any outcome could be reached
        std::string resumed;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            resumed = std::exchange(resumed_from_, {});
        }
        if (!resumed.empty()) {
            try {
                store_.Delete(resumed);
            } catch (...) {  // NOLINT(bugprone-empty-catch)
            }
        }
        if (outcome == Outcome::kFinalise && note_writer_ != nullptr) {
            stage("note lane started");
            note_lane_.WriteNote(id, std::move(note_input),
                                 [this, print = std::move(doctor_voiceprint)] {
                                     if (!print.empty()) diariser_.AccrueVoiceprint(print);
                                 });
        }
    }

    // Empty when the session has no note or cannot be read
    std::string StoredNote(const store::SessionId& id) const {
        try {
            return store_.ReadDocument(id, store::DocumentKind::kNote).text;
        } catch (...) {
            return {};
        }
    }

    SourceFactory factory_;
    ISessionEvents& events_;
    store::ISessionStore& store_;
    asr::ITranscriber& transcriber_;
    diar::IDiariser& diariser_;
    note::INoteWriter* note_writer_;
    metrics::Registry* metrics_;
    std::uint64_t diar_advance_frames_;
    std::chrono::milliseconds settle_timeout_;
    NoteLane note_lane_;
    Enrolment enrolment_;
    std::unique_ptr<audio::IAudioSource> source_;
    std::thread worker_;
    std::thread diar_thread_;
    bool diar_stop_ = false;  // under mutex_
    int diar_ticks_ = 0;      // under mutex_; diagnostics
    audio::LevelMeter meter_;
    bool learn_anchor_ = true;  // set before Start
    mutable std::mutex mutex_;
    std::condition_variable cv_;
    bool running_ = false;
    bool got_audio_ = false;
    bool ended_ = false;
    bool stop_requested_ = false;
    std::uint64_t lost_frames_ = 0;
    store::SessionId session_id_;
    store::SessionId resumed_from_;
    bool note_prepared_ = false;  // diar thread only
    store::SessionId last_finalised_;
    bool reviewing_ = false;  // last_finalised_ came from Open, not a finalise
    // Appended under mutex_ (the diarisation thread snapshots it); finalise
    // reads it after every other thread has joined
    std::vector<float> session_audio_;
    audio::SourceEnd end_{};
};

}  // namespace ambient::session
