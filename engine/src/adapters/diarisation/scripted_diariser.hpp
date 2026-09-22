#pragma once

#include <span>
#include <vector>

#include "ports/diariser.hpp"

namespace ambient::diar {

// CI stand-in when no speaker models are staged: the whole recording is one
// speaker, so the finalise decode yields one turn
class ScriptedDiariser : public IDiariser {
   public:
    DiariseResult Diarise(std::span<const float> audio) override {
        DiariseResult result;
        result.cluster_count = 1;
        result.slices = {{0, audio.size(), 0}};
        return result;
    }

    std::vector<double> AnchorSimilarities(std::span<const float>,
                                           const std::vector<LabelledSlice>&, int) override {
        return {};
    }
};

}  // namespace ambient::diar
