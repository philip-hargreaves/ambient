#pragma once

#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <vector>

#include "core/diarisation/diar_regions.hpp"
#include "ports/diariser.hpp"

namespace ambient::diar {

// 0.30 s: at 0.40 a clinical "No." was dropped and the note fabricated the denial
inline constexpr std::uint64_t kPerTurnMinClipFrames = 4800;

inline constexpr std::size_t kPerTurnMaxRepeat = 4;  // 5-gram degeneracy guard

// Finalise and speculation must merge identically or cache keys stop matching
std::vector<LabelledSlice> MergeByCluster(const std::vector<LabelledSlice>& slices);

// Spans double as cache keys; heads clamp past the previous end, a nested
// overlap turn clamps to nothing
std::vector<Region> DecodeSpans(const std::vector<LabelledSlice>& turns,
                                std::uint64_t audio_frames);

// A span whose edges sit on a cached decode's chunk edges (a cut re-sliced a
// decoded turn) takes those chunks instead of a second decode
inline constexpr std::uint64_t kAssembleTolFrames = 5600;  // 0.35 s: snap window plus span clamp

std::optional<std::vector<asr::Turn>> AssembleFromChunks(const TurnChunks& cache, std::uint64_t a,
                                                         std::uint64_t b);

// Each merged turn gets the text of its own audio; empty means dropped.
// Cached texts are used only on an exact key match, so any hit rate is safe
std::vector<std::string> DecodeTurnTexts(const std::vector<LabelledSlice>& turns,
                                         std::span<const float> audio, const DecodeClipFn& decode,
                                         const TurnTexts* cache = nullptr,
                                         const TurnChunks* chunk_cache = nullptr,
                                         std::vector<std::vector<asr::Turn>>* chunks_out = nullptr);

// Merged turns whose text the cache already holds, in order, up to the first
// span finalise would have to decode: from there on the sealed transcript is
// unknowable. Spans below the clip floor are skipped as finalise skips them
std::vector<LabelledSlice> SpeculatedTurns(const std::vector<LabelledSlice>& merged,
                                           std::uint64_t audio_frames, const TurnTexts& cache,
                                           std::vector<std::string>* texts);

// Most-repeated 5-gram, sliding; legitimate speech peaks at 2
std::size_t MaxRepeatedNgram(const std::string& text);

}  // namespace ambient::diar
