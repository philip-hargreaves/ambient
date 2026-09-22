#pragma once

#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace ambient::asr {

struct Turn {
    std::uint64_t first_frame = 0;
    std::uint64_t frame_count = 0;
    std::string speaker;
    std::string text;
};

// The text of a decode: its chunks joined
inline std::string JoinedText(const std::vector<Turn>& chunks) {
    std::string text;
    for (const auto& chunk : chunks) {
        if (chunk.text.empty()) continue;
        if (!text.empty()) text += ' ';
        text += chunk.text;
    }
    return text;
}

// Receives finalised turns, in order, possibly on the transcriber's thread
class ITurnSink {
   public:
    virtual ~ITurnSink() = default;

    virtual void OnTurn(const Turn& turn) = 0;
};

class ITranscriber {
   public:
    virtual ~ITranscriber() = default;

    virtual void Begin(ITurnSink& sink) = 0;

    // first_new_frame marks where unheard audio begins; turns wholly before
    // it are not emitted again
    virtual void Submit(std::span<const float> frames, std::uint64_t first_frame,
                        std::uint64_t first_new_frame = 0) = 0;

    virtual void Finish() = 0;

    // Decode one clip off the live turn stream, safe mid-session, as Whisper's
    // chunks with absolute frames; empty when unsupported (a re-split then
    // keeps the original turn)
    virtual std::vector<Turn> DecodeClipChunks(std::span<const float>, std::uint64_t) {
        return {};
    }

    // The same decode as one text
    std::string DecodeClip(std::span<const float> frames, std::uint64_t first_frame) {
        return JoinedText(DecodeClipChunks(frames, first_frame));
    }

    // Chunk edges (absolute frames, inside the clip) from every decode since
    // the last call; the diariser takes them as cut points. Empty
    // when unsupported
    virtual std::vector<std::uint64_t> TakeClipCuts() {
        return {};
    }
};

}  // namespace ambient::asr
