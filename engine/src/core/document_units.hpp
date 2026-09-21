#pragma once

#include <cctype>
#include <initializer_list>
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
inline constexpr int kTailWords = 6;     // fewer, unnumbered: a sentence's tail or a running head
inline constexpr int kCountRun = 8;      // consecutive integers in a row: a proof's line numbers
inline constexpr int kLegendPairs = 4;   // "ADA: adalimumab; CZP: ..." under a table
inline constexpr int kAffiliations = 3;  // institution words in a block of authors' addresses
inline constexpr int kAuthors = 5;       // names carrying an address number: "Skeoch32,"

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
                                                    "submitted",
                                                    "supplementary data",
                                                    "supplementary material",
                                                    "e-mail",
                                                    "\xC2\xA9",  // the copyright sign
                                                    "this is an open access",
                                                    "all other authors",
                                                    "for permissions",
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

// Short, without a mark, a sentence end or a bullet: a title, a caption or a label.
// A short question is a heading too
inline bool IsHeading(const Paragraph& paragraph, Scheme scheme) {
    const std::string_view text = paragraph.text;
    return WordCount(text) < kHeadingWords && (!EndsSentence(text) || text.ends_with('?')) &&
           MarkOf(text, scheme).empty() && !Continues(text);
}

inline std::vector<std::string_view> Tokens(std::string_view text) {
    std::vector<std::string_view> out;
    std::size_t i = 0;
    while (i < text.size()) {
        while (i < text.size() && text[i] == ' ') ++i;
        const auto start = i;
        while (i < text.size() && text[i] != ' ') ++i;
        if (i > start) out.push_back(text.substr(start, i - start));
    }
    return out;
}

// The token as a whole number, or -1
inline long Integer(std::string_view token) {
    if (token.empty() || token.size() > 6) return -1;
    long value = 0;
    for (const char c : token) {
        if (!std::isdigit(static_cast<unsigned char>(c))) return -1;
        value = value * 10 + (c - '0');
    }
    return value;
}

// Without its runs of consecutive integers, the line numbers of a proof copy
inline std::string WithoutCounts(std::string_view text) {
    const auto tokens = Tokens(text);
    std::string out;
    for (std::size_t i = 0; i < tokens.size();) {
        std::size_t end = i + 1;
        while (end < tokens.size() && Integer(tokens[end - 1]) >= 0 &&
               Integer(tokens[end]) == Integer(tokens[end - 1]) + 1) {
            ++end;
        }
        if (end - i >= static_cast<std::size_t>(kCountRun)) {
            i = end;
            continue;
        }
        if (!out.empty()) out += ' ';
        out += tokens[i++];
    }
    return out;
}

// Figures, ranges and citation numbers: "59", "(81.9)", "[26-29,", "40.5%"
inline bool Numeric(std::string_view token) {
    bool digit = false;
    for (const unsigned char c : token) {
        if (std::isdigit(c)) {
            digit = true;
        } else if (std::isalpha(c)) {
            return false;
        }
    }
    return digit;
}

// How many of the text's words are among these
inline int CountWords(std::string_view lower, std::initializer_list<std::string_view> among,
                      int* total = nullptr) {
    int found = 0;
    std::string word;
    for (const char c : std::string(lower) + " ") {
        if (std::isalpha(static_cast<unsigned char>(c))) {
            word.push_back(c);
            continue;
        }
        if (word.empty()) continue;
        if (total != nullptr) ++*total;
        for (const auto one : among) found += word == one;
        word.clear();
    }
    return found;
}

// Says what to do, so it stays whatever it looks like
inline bool Guides(std::string_view lower) {
    return CountWords(lower, {"should", "recommend", "recommended", "offer", "consider", "refer"}) >
           0;
}

// A table's cells read across, or the abbreviations printed under it
inline bool IsTable(const Unit& unit) {
    const auto tokens = Tokens(unit.text);
    if (tokens.empty()) return false;
    int numeric = 0;
    for (const auto token : tokens) numeric += Numeric(token);
    if (unit.number.empty() && numeric * 10 >= static_cast<int>(tokens.size()) * 4) return true;
    int pairs = 0;
    for (std::size_t i = 0; i + 1 < tokens.size(); ++i) {
        const bool opens = i == 0 || tokens[i - 1].ends_with(';');
        pairs += opens && tokens[i].ends_with(':') && tokens[i].size() <= 13;
    }
    return pairs >= kLegendPairs && tokens[0].ends_with(':');
}

// The author list: name after name ending in the number of its address
inline bool IsAuthors(std::string_view text) {
    int names = 0;
    for (const auto token : Tokens(text)) {
        auto end = token.size();
        while (end > 0 && (token[end - 1] == ',' ||
                           std::isdigit(static_cast<unsigned char>(token[end - 1])))) {
            --end;
        }
        const bool numbered = token.find_first_of("0123456789", end) != std::string_view::npos;
        names += numbered && end > 2 && std::isalpha(static_cast<unsigned char>(token[end - 1]));
    }
    return names >= kAuthors;
}

// Authors' addresses: institution after institution
inline bool IsAffiliations(std::string_view lower) {
    int words = 0;
    const int institutions = CountWords(
        lower,
        {"university", "hospital", "hospitals", "department", "institute", "centre", "center",
         "nhs", "trust", "school", "college", "foundation", "division", "faculty"},
        &words);
    return institutions >= kAffiliations && institutions * 14 >= words;
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
// labels or a table fragment, a shorter one a sentence's tail; front matter and
// captions are known by their opening; tables and addresses by what they hold,
// unless they say what to do
inline bool IsFragment(const Unit& unit) {
    const int words = detail::WordCount(unit.text);
    if (unit.number.empty() &&
        (words < kTailWords || (words < kMinUnitWords && !detail::EndsSentence(unit.text)))) {
        return true;
    }
    // "5.5 Diagnosis": a numbered heading the scheme took for a recommendation
    if (!unit.number.empty() && words < kHeadingWords && !detail::EndsSentence(unit.text) &&
        !unit.text.ends_with(':')) {
        return true;
    }
    if (detail::Contains(unit.text, ".....")) return true;  // a contents page's dot leaders
    const auto lower = detail::Lower(unit.text);
    if (!detail::Guides(lower) &&
        (detail::IsTable(unit) || detail::IsAffiliations(lower) || detail::IsAuthors(unit.text) ||
         detail::Contains(lower, "creative commons"))) {
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
    for (auto& unit : units) unit.text = detail::WithoutCounts(unit.text);
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
