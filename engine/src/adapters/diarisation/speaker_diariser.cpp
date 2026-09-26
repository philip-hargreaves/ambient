#include "adapters/diarisation/speaker_diariser.hpp"

#include <algorithm>
#include <chrono>

#include "adapters/diarisation/cluster_voiceprint.hpp"
#include "adapters/diarisation/speaker_clustering.hpp"
#include "core/diarisation/clip_cuts.hpp"
#include "core/diarisation/diar_regions.hpp"
#include "core/diarisation/embeddings.hpp"
#include "core/diarisation/role_naming.hpp"
#include "core/diarisation/slice_refinement.hpp"

namespace clinicavt::diar {

SpeakerDiariser::SpeakerDiariser(const models::ModelStore& store, models::OvRuntime& runtime,
                                 AnchorStore& anchors)
    : vad_(store, runtime),
      segmenter_(store, runtime),
      embedder_(store, runtime),
      anchors_(anchors),
      worker_(vad_, segmenter_, embedder_) {}

DiariseResult SpeakerDiariser::Diarise(std::span<const float> audio) {
    DiariseResult result;
    if (audio.empty()) return result;
    // Stage laps for the finalise breakdown, measurement only
    auto lap_start = std::chrono::steady_clock::now();
    const auto lap = [&lap_start] {
        const auto now = std::chrono::steady_clock::now();
        const double seconds = std::chrono::duration<double>(now - lap_start).count();
        lap_start = now;
        return seconds;
    };

    // With capture-phase state, finalise only completes it. Without it, the
    // whole recording is processed here. Either way the maths is identical
    CaptureDiarisation capture;
    std::vector<float> probabilities;
    SegResult seg;
    if (worker_.Engaged()) {
        worker_.Finish(audio);
        capture = worker_.Take();
        probabilities = std::move(capture.vad_probabilities);
        seg = std::move(capture.seg);
        texts_ = std::move(capture.turn_texts);
        chunks_ = std::move(capture.turn_chunks);
        chunk_embeddings_ = std::move(capture.chunk_embeddings);
    } else {
        vad_.Reset();
        AppendVadHops(vad_, audio, probabilities, true);
        seg = segmenter_.Run(audio);
    }

    result.timing.finish_s = lap();
    const auto regions = SpeechRegions(probabilities, audio.size());
    {
        const auto cuts = SnapClipCuts(capture.clip_cuts, probabilities, seg.change_points);
        seg.change_points.insert(seg.change_points.end(), cuts.begin(), cuts.end());
    }
    std::sort(seg.change_points.begin(), seg.change_points.end());
    const auto slices = RefineRegions(regions, seg.change_points);

    std::vector<std::vector<float>> embeddings;
    std::vector<std::uint64_t> durations;
    std::vector<Region> kept;
    for (const Region& slice : slices) {
        const auto it = capture.embeddings.find({slice.first_frame, slice.end_frame});
        if (it != capture.embeddings.end()) {
            if (it->second.empty()) continue;  // was too short to embed
            embeddings.push_back(it->second);
            ++result.timing.embed_hits;
        } else {
            const auto ranges = EmbeddingRanges(slice, seg.overlap_spans);
            if (ranges.empty()) continue;
            const auto clip = Gather(audio, ranges);
            if (clip.size() < kEmbedMinFrames) continue;
            embeddings.push_back(embedder_.Embed(clip));
            ++result.timing.embed_misses;
        }
        durations.push_back(slice.end_frame - slice.first_frame);
        kept.push_back(slice);
    }

    result.timing.embed_s = lap();
    const auto clusters = ClusterSpeakers(embeddings, durations);
    centroids_ = clusters.centroids;
    result.timing.cluster_s = lap();
    std::vector<LabelledSlice> out;
    for (std::size_t i = 0; i < kept.size(); ++i) {
        out.push_back({kept[i].first_frame, kept[i].end_frame, clusters.labels[i]});
    }

    const auto overlaps = OverlapTurns(kept, clusters.labels, clusters.centroids, seg.overlap_spans,
                                       [&](std::uint64_t first, std::uint64_t end) {
                                           return embedder_.Embed(Gather(audio, {{first, end}}));
                                       });
    out.insert(out.end(), overlaps.begin(), overlaps.end());
    result.timing.overlap_s = lap();
    std::sort(out.begin(), out.end(), [](const LabelledSlice& a, const LabelledSlice& b) {
        return a.first_frame < b.first_frame;
    });
    result.slices = std::move(out);
    result.cluster_count = clusters.count;

    voiceprints_.clear();  // AnchorSimilarities refills them per finalise
    return result;
}

std::vector<double> SpeakerDiariser::AnchorSimilarities(std::span<const float> audio,
                                                        const std::vector<LabelledSlice>& slices,
                                                        int cluster_count) {
    // Each cluster's similarity to the accrued anchor. A cluster too short
    // for a voiceprint ranks below any real match
    const auto anchor = anchors_.Anchor();
    if (!anchor) return {};
    std::vector<double> similarity(static_cast<std::size_t>(cluster_count), -2.0);
    voiceprints_.assign(static_cast<std::size_t>(cluster_count), {});
    for (int c = 0; c < cluster_count; ++c) {
        auto voiceprint = ClusterVoiceprint(embedder_, audio, slices, c);
        if (voiceprint.empty()) continue;
        similarity[static_cast<std::size_t>(c)] = Dot(voiceprint, *anchor);
        voiceprints_[static_cast<std::size_t>(c)] = std::move(voiceprint);
    }
    return similarity;
}

std::vector<asr::Turn> SpeakerDiariser::SpeculativeTranscript() {
    const Speculation& spec = worker_.LastSpeculation();
    if (spec.turns.empty()) return {};
    std::vector<double> similarity;
    const auto anchor = anchors_.Anchor();
    if (anchor && spec.centroids.size() == static_cast<std::size_t>(spec.cluster_count)) {
        for (const auto& centroid : spec.centroids) similarity.push_back(Dot(centroid, *anchor));
    }
    std::vector<RoleTurn> role_turns;
    for (std::size_t i = 0; i < spec.turns.size(); ++i) {
        role_turns.push_back({spec.turns[i].cluster,
                              spec.turns[i].end_frame - spec.turns[i].first_frame, spec.texts[i]});
    }
    const auto roles = NameRoles(role_turns, spec.cluster_count, similarity);
    std::vector<asr::Turn> out;
    for (std::size_t i = 0; i < spec.turns.size(); ++i) {
        const auto cluster = static_cast<std::size_t>(spec.turns[i].cluster);
        asr::Turn turn;
        turn.first_frame = spec.turns[i].first_frame;
        turn.frame_count = spec.turns[i].end_frame - spec.turns[i].first_frame;
        turn.speaker =
            cluster < roles.role_of_cluster.size() ? roles.role_of_cluster[cluster] : "unknown";
        turn.text = spec.texts[i];
        out.push_back(std::move(turn));
    }
    return out;
}

std::vector<float> SpeakerDiariser::DoctorVoiceprint(std::span<const float> audio,
                                                     const std::vector<LabelledSlice>& slices,
                                                     int doctor_cluster) {
    const auto index = static_cast<std::size_t>(doctor_cluster);
    return index < voiceprints_.size() && !voiceprints_[index].empty()
               ? voiceprints_[index]
               : ClusterVoiceprint(embedder_, audio, slices, doctor_cluster);
}

std::vector<float> SpeakerDiariser::EmbedVoice(std::span<const float> audio) {
    if (audio.size() < kVoiceprintMinFrames) return {};
    // One long exposure, capped as a consultation's voiceprint is
    return embedder_.Embed(
        audio.subspan(0, std::min<std::size_t>(audio.size(), kVoiceprintCapFrames)));
}

}  // namespace clinicavt::diar
