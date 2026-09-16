#pragma once

#include <string>
#include <utility>
#include <vector>

#include "core/guidance_query.hpp"
#include "core/page_text.hpp"
#include "core/recommendation_marks.hpp"

namespace ambient::guidance {

// What an added document is searched and shown by: a paragraph, or a run of
// short ones, under the heading that preceded it. A box for each line it covers
struct Unit {
    int page = 0;
    std::string number;   // the recommendation's own, when it has one
    std::string section;  // the heading above it
    std::string text;
    std::vector<std::pair<int, Box>> boxes;  // each line's page and box
};

inline constexpr int kHeadingWords = 8;
inline constexpr int kMinUnitWords = 25;
inline constexpr int kMaxUnitWords = 200;
inline constexpr int kSplitWords = 150;

namespace detail {

// Short, unnumbered and not a sentence: a title, a caption or a label
inline bool IsHeading(const Paragraph& paragraph) {
    return WordCount(paragraph.text) < kHeadingWords && !EndsSentence(paragraph.text) &&
           !StartsRecommendation(paragraph.text);
}

// A long paragraph in pieces of whole lines, each closed at a sentence end
// once it holds kSplitWords, or at kMaxUnitWords regardless
inline std::vector<Paragraph> Split(const Paragraph& paragraph) {
    std::vector<Paragraph> out;
    Paragraph piece{paragraph.page, "", {}, {}};
    int words = 0;
    const auto close = [&] {
        if (piece.lines.empty()) return;
        out.push_back(std::move(piece));
        piece = Paragraph{paragraph.page, "", {}, {}};
        words = 0;
    };
    for (const auto& line : paragraph.lines) {
        if (!piece.lines.empty()) piece.text += ' ';
        piece.text += line.text;
        piece.box = piece.lines.empty() ? line.box : Union(piece.box, line.box);
        piece.lines.push_back(line);
        words += WordCount(line.text);
        if ((words >= kSplitWords && EndsSentence(line.text)) || words >= kMaxUnitWords) close();
    }
    close();
    return out;
}

}  // namespace detail

// Paragraphs in reading order become units: a heading labels the next unit
// and its lines join that unit's, a recommendation starts its own, short
// paragraphs merge forward to kMinUnitWords, and a paragraph past
// kMaxUnitWords is split
inline std::vector<Unit> UnitsFromParagraphs(const std::vector<Paragraph>& paragraphs) {
    std::vector<Unit> out;
    Unit current;
    std::string heading;
    const Paragraph* heading_paragraph = nullptr;
    int words = 0;
    bool open = false;
    const auto close = [&] {
        if (open) out.push_back(std::move(current));
        current = Unit{};
        words = 0;
        open = false;
    };
    const auto mark = [&](const Paragraph& paragraph) {
        for (const auto& line : paragraph.lines) {
            if (line.box.right > line.box.left) {
                current.boxes.emplace_back(paragraph.page, line.box);
            }
        }
    };
    const auto take = [&](const Paragraph& paragraph) {
        if (!open) {
            current.page = paragraph.page;
            current.section = heading;
            current.number = LeadingNumber(paragraph.text);
            if (heading_paragraph != nullptr) mark(*heading_paragraph);
            heading.clear();
            heading_paragraph = nullptr;
            open = true;
        } else {
            current.text += ' ';
        }
        current.text += paragraph.text;
        mark(paragraph);
        words += detail::WordCount(paragraph.text);
    };
    for (const auto& paragraph : paragraphs) {
        if (detail::IsHeading(paragraph)) {
            close();
            heading_paragraph = &paragraph;
            heading = paragraph.text;
            while (!heading.empty() && (heading.back() == ':' || heading.back() == ' ')) {
                heading.pop_back();
            }
            continue;
        }
        const bool recommendation = StartsRecommendation(paragraph.text);
        if (recommendation || words >= kMinUnitWords) close();
        if (detail::WordCount(paragraph.text) > kMaxUnitWords) {
            close();
            for (const auto& piece : detail::Split(paragraph)) {
                take(piece);
                close();
            }
            continue;
        }
        take(paragraph);
        if (recommendation) close();
    }
    close();
    return out;
}

}  // namespace ambient::guidance
