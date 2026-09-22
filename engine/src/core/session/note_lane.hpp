#pragma once

#include <atomic>
#include <cstddef>
#include <cstdio>
#include <exception>
#include <functional>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "core/common/strings.hpp"
#include "core/note/note_gate.hpp"
#include "core/note/note_label.hpp"
#include "core/note/summary_scrub.hpp"
#include "core/session/session_events.hpp"
#include "ports/note_writer.hpp"
#include "ports/session_store.hpp"
#include "ports/transcriber.hpp"

namespace ambient::session {

// The documents written from a finished transcript on their own thread, after
// finalise: the note, then the patient sheet, then the title. One write at a
// time; a new write cancels one still running
class NoteLane {
   public:
    // Below this the model writes from its prompt, not the consultation
    // (measured on 14 s); refusing is the only safe output
    static constexpr std::size_t kMinNoteWords = 25;

    NoteLane(note::INoteWriter* writer, store::ISessionStore& store, ISessionEvents& events,
             std::size_t min_note_words = kMinNoteWords)
        : writer_(writer), store_(store), events_(events), min_note_words_(min_note_words) {}

    ~NoteLane() {
        Join();
    }
    NoteLane(const NoteLane&) = delete;
    NoteLane& operator=(const NoteLane&) = delete;

    bool Available() const {
        return writer_ != nullptr;
    }

    bool WritesPatient() const {
        return writer_ != nullptr && writer_->WritesPatient();
    }

    // True while a document is being written; callers never block on the lane
    bool Busy() const {
        return busy_.load();
    }

    // The last note was refused (too thin, or not a consultation), so the
    // session it belongs to holds no note
    bool Refused() const {
        return refused_.load();
    }

    void ClearRefusal() {
        refused_ = false;
    }

    // Applied to the next note; the shell sets these ahead of the stop
    void SetOptions(note::NoteOptions options) {
        std::lock_guard<std::mutex> lock(mutex_);
        options_ = std::move(options);
    }

    note::NoteOptions Options() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return options_;
    }

    // The note, then the sheet and the title. Too thin is refused without
    // asking the model; the model's own refusal can be overridden.
    // `accepted` runs once the note is stored: the session was a consultation
    void WriteNote(store::SessionId id, std::vector<asr::Turn> transcript,
                   std::function<void()> accepted = {}) {
        Join();
        const auto options = Options();
        if (const auto words = TranscriptWords(transcript); words < min_note_words_) {
            refused_ = true;
            events_.OnNoteRefused(std::to_string(words) + " words; a note needs at least " +
                                      std::to_string(min_note_words_),
                                  false);
            return;
        }
        Run([this, id = std::move(id), turns = std::move(transcript), options,
             accepted = std::move(accepted)] {
            std::string note;
            try {
                // A refusal never streams as if it were the note
                note::RefusalFilter forward(
                    [this](const std::string& partial) { events_.OnNotePartial(partial); });
                note = writer_->Write(turns, options,
                                      [&forward](const std::string& partial) { forward(partial); });
                if (const auto reason = note::RefusalReason(note);
                    reason.has_value() && !options.confirmed) {
                    std::fprintf(stderr, "ambient-engine: note refused: %s\n", reason->c_str());
                    refused_ = true;
                    events_.OnNoteRefused(*reason, true);
                    return;  // no note, no sheet, no label, and the print learns nothing
                }
                if (const auto stored = SaveNote(id, note, options)) {
                    // What follows the note must never cost the note
                    try {
                        events_.OnNoteSaved(id, *stored);
                    } catch (const std::exception& e) {
                        std::fprintf(stderr,
                                     "ambient-engine: work after the note not started: %s\n",
                                     e.what());
                    } catch (...) {
                        std::fprintf(stderr, "ambient-engine: work after the note not started\n");
                    }
                }
                refused_ = false;
                events_.OnNoteReady(note);
                if (accepted) accepted();
            } catch (const std::exception& e) {
                events_.OnNoteFailed(e.what());
                return;
            } catch (...) {
                events_.OnNoteFailed("note generation failed");
                return;
            }
            // Patient information follows from the finished note; a failure
            // here leaves the note intact
            if (writer_->WritesPatient() && !note.empty()) {
                WritePatientNow(id, note);
            }
            // The title comes last and holds nothing up: open and summary go
            // ahead while it is written
            busy_ = false;
            if (!note.empty()) {
                SaveLabel(id, note);
            }
        });
    }

    // The sheet rewritten from the stored note - clinician edits included -
    // so an edited note can bring the sheet back into agreement
    void WritePatient(store::SessionId id, std::string note) {
        Run([this, id = std::move(id), note = std::move(note)] { WritePatientNow(id, note); });
    }

    // The appraisal case summary from the stored note, edits included
    void WriteSummary(store::SessionId id, std::string note) {
        Run([this, id = std::move(id), note = std::move(note)] {
            try {
                const std::string summary = note::ScrubSummary(writer_->WriteSummary(note));
                if (summary.empty()) {
                    throw std::runtime_error("the model wrote nothing");
                }
                store::Document document;
                document.text = summary;
                store_.SaveDocument(id, store::DocumentKind::kSummary, document);
                events_.OnSummaryReady(id, summary);
            } catch (const std::exception& e) {
                events_.OnSummaryFailed(id, e.what());
            } catch (...) {
                events_.OnSummaryFailed(id, "case summary failed");
            }
        });
    }

    // Cancels a write in progress and waits for its thread
    void Join() {
        std::thread thread;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            thread = std::move(thread_);
        }
        if (thread.joinable()) {
            abort_ = true;
            if (writer_ != nullptr) writer_->Cancel();
            thread.join();
            abort_ = false;
        }
    }

   private:
    // One write on the lane's thread; busy until it returns. A join racing
    // the thread's start must win: the writer's per-generation cancel reset
    // would otherwise erase the cancel
    void Run(std::function<void()> work) {
        Join();
        std::lock_guard<std::mutex> lock(mutex_);
        busy_ = true;
        thread_ = std::thread([this, work = std::move(work)] {
            struct BusyGuard {
                std::atomic<bool>& flag;
                ~BusyGuard() {
                    flag = false;
                }
            } busy_guard{busy_};
            if (abort_.load()) {
                return;
            }
            work();
        });
    }

    void WritePatientNow(const store::SessionId& id, const std::string& note) {
        try {
            const std::string patient = writer_->WritePatient(
                note, [this](const std::string& partial) { events_.OnPatientPartial(partial); });
            try {
                store::Document document;
                document.text = patient;
                store_.SaveDocument(id, store::DocumentKind::kPatient, document);
            } catch (const std::exception& e) {
                ReportStoreFailure(events_, "patient sheet", e);
            }
            events_.OnPatientReady(patient);
        } catch (const std::exception& e) {
            events_.OnPatientFailed(e.what());
        } catch (...) {
            events_.OnPatientFailed("patient information failed");
        }
    }

    // A store refusal never costs the note: the text still reaches the shell.
    // Returns the note as stored, nothing when the store refused it or its
    // revision could not be read back
    std::optional<store::Document> SaveNote(const store::SessionId& id, const std::string& text,
                                            const note::NoteOptions& options) {
        store::Document document;
        document.text = text;
        document.style = options.style;
        document.detail = options.detail;
        try {
            store_.SaveDocument(id, store::DocumentKind::kNote, document);
        } catch (const std::exception& e) {
            ReportStoreFailure(events_, "note", e);
            return std::nullopt;
        }
        try {
            return store_.ReadDocument(id, store::DocumentKind::kNote);
        } catch (const std::exception& e) {
            ReportStoreFailure(events_, "note revision", e);
            return std::nullopt;
        }
    }

    // Asked once the documents are done; a title failing the sanitiser is not
    // stored, and a typed label is never overwritten
    void SaveLabel(const store::SessionId& id, const std::string& note_text) {
        try {
            if (!store_.ReadDocument(id, store::DocumentKind::kLabel).edited_at.empty()) {
                return;
            }
            const std::string label = note::SanitiseLabel(writer_->WriteLabel(note_text));
            if (label.empty()) {
                return;
            }
            store::Document document;
            document.text = label;
            store_.SaveDocument(id, store::DocumentKind::kLabel, document);
        } catch (const std::exception& e) {
            ReportStoreFailure(events_, "label", e);
        }
    }

    static std::size_t TranscriptWords(const std::vector<asr::Turn>& turns) {
        std::size_t words = 0;
        for (const auto& turn : turns) {
            words += static_cast<std::size_t>(strings::WordCount(turn.text));
        }
        return words;
    }

    note::INoteWriter* writer_;
    store::ISessionStore& store_;
    ISessionEvents& events_;
    std::size_t min_note_words_;
    mutable std::mutex mutex_;  // options_ and thread_
    note::NoteOptions options_;
    std::thread thread_;  // moved out under mutex_, joined outside it
    std::atomic<bool> busy_{false};
    std::atomic<bool> abort_{false};
    std::atomic<bool> refused_{false};
};

}  // namespace ambient::session
