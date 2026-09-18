#pragma once

#include <algorithm>
#include <cctype>
#include <cstddef>
#include <map>
#include <set>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

#include "core/guidance_query.hpp"
#include "core/page_text.hpp"
#include "core/utf8.hpp"

namespace ambient::guidance {

// A running header or footer repeats on this share of the pages, at least
// kFurnitureMinPages, inside the top or bottom band of each
inline constexpr float kFurnitureShare = 0.4F;
inline constexpr int kFurnitureMinPages = 3;
inline constexpr float kFurnitureBand = 0.15F;

// An unmapped glyph that ends this many lines before a lower-case
// continuation is the document's hyphen
inline constexpr int kHyphenLineEnds = 3;

namespace detail {

// A glyph the reader could not name: a control code, a private-use
// character or U+FFFD
inline bool Unmapped(char32_t cp) {
    return (cp < 0x20 && cp != '\t') || (cp >= 0x7F && cp < 0xA0) ||
           (cp >= 0xE000 && cp <= 0xF8FF) || cp == 0xFFFD;
}

inline bool Digit(char32_t cp) {
    return cp >= '0' && cp <= '9';
}

inline bool Alpha(char32_t cp) {
    return (cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z');
}

inline bool LowerCase(char32_t cp) {
    return cp >= 'a' && cp <= 'z';
}

inline std::string_view Ligature(char32_t cp) {
    switch (cp) {
        case 0xFB00:
            return "ff";
        case 0xFB01:
            return "fi";
        case 0xFB02:
            return "fl";
        case 0xFB03:
            return "ffi";
        case 0xFB04:
            return "ffl";
        case 0xFB05:
        case 0xFB06:
            return "st";
        default:
            return {};
    }
}

// Every word the document uses, lower case, ASCII letters only
inline std::unordered_set<std::string> DocumentWords(const std::vector<Page>& pages) {
    std::unordered_set<std::string> words;
    for (const auto& page : pages) {
        for (const auto& line : page.lines) {
            std::string word;
            for (const char c : line.text + " ") {
                if (Alpha(static_cast<unsigned char>(c))) {
                    word.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
                } else {
                    if (word.size() > 1) words.insert(word);
                    word.clear();
                }
            }
        }
    }
    return words;
}

// The line's text with digits as '#' and spaces squeezed, the same on every page
inline std::string Form(std::string_view text) {
    std::string out;
    for (const char c : Trim(text)) {
        if (std::isdigit(static_cast<unsigned char>(c))) {
            out.push_back('#');
        } else if (std::isspace(static_cast<unsigned char>(c))) {
            if (!out.empty() && out.back() != ' ') out.push_back(' ');
        } else {
            out.push_back(c);
        }
    }
    return out;
}

inline bool InBand(const Box& box) {
    return box.top < kFurnitureBand || box.bottom > 1 - kFurnitureBand;
}

}  // namespace detail

// The unmapped glyphs that stand for the document's hyphen: a font without
// names for its glyphs hands the same code back at every hyphenated line end
inline std::set<char32_t> HyphenCodes(const std::vector<Page>& pages) {
    std::map<char32_t, int> ends;
    for (const auto& page : pages) {
        for (std::size_t k = 0; k + 1 < page.lines.size(); ++k) {
            const auto cps = utf8::CodePoints(page.lines[k].text);
            const auto& next = page.lines[k + 1].text;
            if (cps.size() < 2 || !detail::Unmapped(cps.back()) ||
                !detail::Alpha(cps[cps.size() - 2]) || next.empty() ||
                !detail::LowerCase(static_cast<unsigned char>(next[0]))) {
                continue;
            }
            ++ends[cps.back()];
        }
    }
    std::set<char32_t> out;
    for (const auto& [cp, count] : ends) {
        if (count >= kHyphenLineEnds) out.insert(cp);
    }
    return out;
}

// The document's hyphen code becomes a hyphen, or an en dash between digits.
// Any other unmapped glyph stays as U+FFFD only next to a digit, where it was
// a symbol that changed the meaning. Between letters it was an accent and
// alone it was a bullet or a footnote mark, so it goes. Ligatures are spelt
// out, no-break spaces made plain and spaces squeezed. A soft hyphen goes
// unless it ends the line, where the join reads it
inline std::string RepairText(std::string_view text, const std::set<char32_t>& hyphens = {}) {
    const auto cps = utf8::CodePoints(text);
    std::string out;
    for (std::size_t i = 0; i < cps.size(); ++i) {
        const auto cp = cps[i];
        if (const auto ligature = detail::Ligature(cp); !ligature.empty()) {
            out += ligature;
            continue;
        }
        if (cp == 0xAD && i + 1 < cps.size()) continue;
        if (hyphens.contains(cp)) {
            const bool range = i > 0 && i + 1 < cps.size() && detail::Digit(cps[i - 1]) &&
                               detail::Digit(cps[i + 1]);
            utf8::Encode(out, range ? 0x2013 : '-');
            continue;
        }
        if (detail::Unmapped(cp)) {
            if (i > 0 && detail::Unmapped(cps[i - 1])) continue;
            std::size_t after = i + 1;
            while (after < cps.size() && detail::Unmapped(cps[after])) ++after;
            const bool digit_before = i > 0 && detail::Digit(cps[i - 1]);
            const bool digit_after = after < cps.size() && detail::Digit(cps[after]);
            if (digit_before || digit_after) utf8::Encode(out, 0xFFFD);
            continue;
        }
        if (cp == 0xA0 || cp == ' ' || cp == '\t') {
            if (!out.empty() && out.back() != ' ') out.push_back(' ');
            continue;
        }
        utf8::Encode(out, cp);
    }
    if (!out.empty() && out.back() == ' ') out.pop_back();
    return out;
}

// A word broken at a line end rejoins when the document uses it whole
// elsewhere. A soft hyphen always rejoins. A plain hyphen stays when the
// joined word is unknown, as in anti-inflammatory
inline void JoinBrokenWords(Page& page, const std::unordered_set<std::string>& words) {
    auto& lines = page.lines;
    for (std::size_t k = 0; k + 1 < lines.size();) {
        auto& text = lines[k].text;
        auto& next = lines[k + 1].text;
        const bool soft = text.ends_with("\xC2\xAD");
        const bool hard = !soft && text.ends_with('-') && !next.empty() &&
                          detail::LowerCase(static_cast<unsigned char>(next[0]));
        if (!soft && !hard) {
            ++k;
            continue;
        }
        const auto cut = next.find(' ');
        const auto token = next.substr(0, cut);
        if (soft) {
            text.resize(text.size() - 2);
        } else {
            std::size_t start = text.size() - 1;
            while (start > 0 && detail::Alpha(static_cast<unsigned char>(text[start - 1]))) {
                --start;
            }
            std::string joined = text.substr(start, text.size() - 1 - start);
            for (const char c : token) {
                if (!detail::Alpha(static_cast<unsigned char>(c))) break;
                joined.push_back(c);
            }
            if (!joined.empty() && words.contains(detail::Lower(joined))) text.pop_back();
        }
        text += token;
        next.erase(0, cut == std::string::npos ? next.size() : cut + 1);
        if (detail::Trim(next).empty()) {
            lines.erase(lines.begin() + static_cast<std::ptrdiff_t>(k) + 1);
        } else {
            ++k;
        }
    }
}

// Lines in the top or bottom band whose form repeats across enough pages go.
// A heading that repeats mid-page, like Recommendation or Rationale, stays
inline void RemoveFurniture(std::vector<Page>& pages) {
    std::map<std::string, std::set<std::size_t>> seen;
    for (std::size_t p = 0; p < pages.size(); ++p) {
        for (const auto& line : pages[p].lines) {
            if (detail::InBand(line.box)) seen[detail::Form(line.text)].insert(p);
        }
    }
    const auto needed =
        std::max(static_cast<float>(kFurnitureMinPages), kFurnitureShare * pages.size());
    for (auto& page : pages) {
        std::erase_if(page.lines, [&](const PageLine& line) {
            return detail::InBand(line.box) &&
                   static_cast<float>(seen[detail::Form(line.text)].size()) >= needed;
        });
    }
}

// The host's pages before anything reads them: text repaired, empty lines
// gone, broken words rejoined, furniture removed
inline void CleanPages(std::vector<Page>& pages) {
    const auto hyphens = HyphenCodes(pages);
    for (auto& page : pages) {
        for (auto& line : page.lines) line.text = RepairText(line.text, hyphens);
        std::erase_if(page.lines, [](const PageLine& line) { return line.text.empty(); });
    }
    const auto words = detail::DocumentWords(pages);
    for (auto& page : pages) JoinBrokenWords(page, words);
    RemoveFurniture(pages);
}

}  // namespace ambient::guidance
