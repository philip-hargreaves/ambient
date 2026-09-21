#pragma once

#include <cctype>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "core/guidance_query.hpp"
#include "core/page_clean.hpp"
#include "core/page_text.hpp"
#include "core/recommendation_marks.hpp"
#include "core/reference_tail.hpp"

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

// A unit opening with one of these is front matter or a caption, not guidance
inline constexpr std::string_view kFrontMatter[] = {"key words",
                                                    "keywords",
                                                    "correspondence",
                                                    "received",
                                                    "accepted",
                                                    "conflict of interest",
                                                    "conflicts of interest",
                                                    "funding",
                                                    "disclosure",
                                                    "how to cite",
                                                    "doi",
                                                    "copyright",
                                                    "fig.",
                                                    "fig ",
                                                    "figure ",
                                                    "table "};

namespace detail {

inline std::string WithoutStop(std::string_view s) {
    while (!s.empty() && (s.back() == '.' || s.back() == ':' || s.back() == ' ')) {
        s.remove_suffix(1);
    }
    return std::string(s);
}

// A bullet or a lower-case start carries a recommendation's own list on
inline bool Continues(std::string_view text) {
    return text.starts_with("\xE2\x80\xA2") || text.starts_with("\xE2\x80\x93") ||
           text.starts_with('-') || text.starts_with('*') ||
           (!text.empty() && std::islower(static_cast<unsigned char>(text.front())));
}

// Short, without a mark, a sentence end or a bullet: a title, a caption or a label
inline bool IsHeading(const Paragraph& paragraph, Scheme scheme) {
    return WordCount(paragraph.text) < kHeadingWords && !EndsSentence(paragraph.text) &&
           MarkOf(paragraph.text, scheme).empty() && !Continues(paragraph.text);
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

// Whether a token closes the unit within the paragraphs ahead, before a
// heading, a mark or kMaxUnitWords, so a graded table row stays whole
inline bool TokenAhead(const std::vector<Paragraph>& paragraphs, std::size_t from, int words,
                       Scheme scheme) {
    for (std::size_t i = from; i < paragraphs.size(); ++i) {
        const auto& paragraph = paragraphs[i];
        if (i > from && (IsHeading(paragraph, scheme) || !MarkOf(paragraph.text, scheme).empty())) {
            return false;
        }
        words += WordCount(paragraph.text);
        if (words > kMaxUnitWords) return false;
        if (EndsWithToken(paragraph.text)) return true;
    }
    return false;
}

}  // namespace detail

// The scheme most paragraphs open with, counting a mark only when it stands
// alone or a capital follows, so "R33, R39" in a list of amendments does not
inline Scheme DetectScheme(const std::vector<Paragraph>& paragraphs) {
    static constexpr Scheme kAll[] = {Scheme::kDotted,   Scheme::kRoman, Scheme::kBracketed,
                                      Scheme::kNumbered, Scheme::kWord,  Scheme::kLetterR};
    Scheme best = Scheme::kNone;
    int best_count = kSchemeMarks - 1;
    for (const auto scheme : kAll) {
        int count = 0;
        for (const auto& paragraph : paragraphs) {
            const auto mark = MarkOf(paragraph.text, scheme);
            if (mark.empty()) continue;
            const auto rest = detail::Trim(std::string_view(paragraph.text).substr(mark.size()));
            if (rest.empty() || std::isupper(static_cast<unsigned char>(rest[0])) ||
                rest[0] == '"') {
                ++count;
            }
        }
        if (count > best_count) {
            best = scheme;
            best_count = count;
        }
    }
    return best;
}

// Paragraphs in reading order become units. A heading or a bare mark labels
// the next unit and its lines join that unit's. A marked paragraph opens a
// unit that takes its own bullets, or in a document with closing tokens runs
// to the paragraph ending with one. Unmarked paragraphs merge forward to
// kMinUnitWords, and a paragraph past kMaxUnitWords is split
inline std::vector<Unit> UnitsFromParagraphs(const std::vector<Paragraph>& paragraphs) {
    const auto scheme = DetectScheme(paragraphs);
    int closed = 0;
    for (const auto& paragraph : paragraphs) closed += EndsWithToken(paragraph.text);
    const bool tokens = closed >= kTokenParagraphs;

    std::vector<Unit> out;
    Unit current;
    std::string heading;
    std::string number;
    const Paragraph* label = nullptr;
    int words = 0;
    bool open = false;
    bool marked = false;
    const auto close = [&] {
        if (open) out.push_back(std::move(current));
        current = Unit{};
        words = 0;
        open = false;
        marked = false;
    };
    const auto mark_lines = [&](const Paragraph& paragraph) {
        for (const auto& line : paragraph.lines) {
            if (line.box.right > line.box.left) {
                current.boxes.emplace_back(paragraph.page, line.box);
            }
        }
    };
    const auto take = [&](const Paragraph& paragraph, std::string_view mark) {
        if (!open) {
            current.page = paragraph.page;
            current.section = heading;
            current.number = number.empty() ? detail::WithoutStop(mark) : number;
            marked = !current.number.empty();
            if (label != nullptr) mark_lines(*label);
            heading.clear();
            number.clear();
            label = nullptr;
            open = true;
        } else {
            current.text += ' ';
        }
        current.text += paragraph.text;
        mark_lines(paragraph);
        words += detail::WordCount(paragraph.text);
    };
    for (std::size_t i = 0; i < paragraphs.size(); ++i) {
        const auto& paragraph = paragraphs[i];
        auto mark = MarkOf(paragraph.text, scheme);
        if (detail::IsHeading(paragraph, scheme)) {
            close();
            label = &paragraph;
            heading = detail::WithoutStop(paragraph.text);
            continue;
        }
        if (!mark.empty() && mark.size() == paragraph.text.size()) {
            close();
            label = &paragraph;
            number = detail::WithoutStop(mark);
            continue;
        }
        if (!mark.empty()) {
            close();
        } else if (marked && !tokens) {
            if (!detail::Continues(paragraph.text)) close();
        } else if (words >= kMinUnitWords &&
                   !(tokens && detail::TokenAhead(paragraphs, i, words, scheme))) {
            close();
        }
        if (detail::WordCount(paragraph.text) > kMaxUnitWords) {
            close();
            for (const auto& piece : detail::Split(paragraph)) {
                take(piece, mark);
                mark = {};
                close();
            }
            continue;
        }
        take(paragraph, mark);
        if (tokens && EndsWithToken(paragraph.text)) close();
    }
    close();
    return out;
}

// Not guidance: a short unmarked run that never ends a sentence is figure
// labels or a table fragment; front matter and captions are known by their opening
inline bool IsFragment(const Unit& unit) {
    if (unit.number.empty() && detail::WordCount(unit.text) < kMinUnitWords &&
        !detail::EndsSentence(unit.text)) {
        return true;
    }
    std::string head;
    for (const char c : std::string_view(unit.text).substr(0, 24)) {
        head.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
    }
    for (const auto label : kFrontMatter) {
        if (head.starts_with(label)) return true;
    }
    return false;
}

inline std::vector<Unit> DropFragments(std::vector<Unit> units) {
    std::erase_if(units, IsFragment);
    return units;
}

// From the host's pages to the units stored: cleaned, in paragraphs, the
// reference tail dropped
inline std::vector<Unit> UnitsFromPages(std::vector<Page>& pages) {
    CleanPages(pages);
    auto paragraphs = ParagraphsFromPages(pages);
    DropReferenceTail(paragraphs);
    return DropFragments(UnitsFromParagraphs(paragraphs));
}

inline std::vector<Unit> UnitsFromText(const std::string& text) {
    auto paragraphs = ParagraphsFromText(text);
    DropReferenceTail(paragraphs);
    return DropFragments(UnitsFromParagraphs(paragraphs));
}

}  // namespace ambient::guidance
