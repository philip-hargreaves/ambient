#pragma once

#include <functional>
#include <span>
#include <vector>

#include "core/diarisation/resplit.hpp"
#include "core/diarisation/role_naming.hpp"
#include "core/diarisation/tidy_transcript.hpp"
#include "core/diarisation/turn_decode.hpp"
#include "core/metrics/metrics.hpp"
#include "core/session/session_events.hpp"
#include "ports/audio_source.hpp"
#include "ports/diariser.hpp"
#include "ports/transcriber.hpp"

namespace ambient::session {

struct Transcript {
    std::vector<asr::Turn> turns;  // attributed and tidied; empty when nothing was heard
    diar::DiariseResult diarised;
    int doctor_cluster = -1;  // -1: no speaker was named the doctor
};

// Named finalise stages as they complete, for the log and the metrics
using StageFn = std::function<void(const char*)>;

// A finished recording as one attributed transcript: speakers found, every merged turn
// given the text of its own audio (the capture cache means this mostly decodes
// the tail), edge chunks re-split by voice, roles named against the stored
// print, then tidied. Anchor voiceprints embed on the CPU alongside the GPU
// turn decode
Transcript TranscribeRecording(std::span<const float> audio, diar::IDiariser& diariser,
                               asr::ITranscriber& transcriber, ISessionEvents& events,
                               metrics::Registry* metrics, const StageFn& stage);

}  // namespace ambient::session
