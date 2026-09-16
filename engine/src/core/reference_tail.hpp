#pragma once

#include <cctype>
#include <cstddef>
#include <string_view>
#include <vector>

#include "core/guidance_query.hpp"
#include "core/page_text.hpp"

namespace ambient::guidance {

// The reference list: its heading sits in the second half of the paragraphs,
// on its own or as the last line of one, and numbered citations follow. The
// list goes and whatever follows it stays, since two guidelines set their
// recommendation tables after the references. The word alone, as on a
// pathway, drops nothing
inline constexpr int kCitationLines = 6;
inline constexpr int kCitationWindow = 40;

namespace detail {

// "References", "Reference list" or "Bibliography", with or without a section number
inline bool IsReferencesHeading(std::string_view text) {
    auto s = Trim(text);
    std::size_t i = 0;
    while (i < s.size() && (std::isdigit(static_cast<unsigned char>(s[i])) || s[i] == '.')) ++i;
    const auto lower = Lower(Trim(s.substr(i)));
    return lower == "references" || lower == "reference list" || lower == "bibliography";
}

// "12 Kuo CF", "[12] Kuo" or "12. Kuo": a numbered entry
inline bool IsCitationLine(std::string_view text) {
    std::size_t i = text.starts_with('[') ? 1 : 0;
    const auto start = i;
    while (i < text.size() && i - start < 3 && std::isdigit(static_cast<unsigned char>(text[i]))) {
        ++i;
    }
    if (i == start) return false;
    if (i < text.size() && (text[i] == ']' || text[i] == '.')) ++i;
    return i < text.size() && text[i] == ' ';
}

}  // namespace detail

namespace detail {

inline bool CitationsFollow(const std::vector<Paragraph>& paragraphs, std::size_t heading) {
    int seen = 0;
    int citations = 0;
    for (std::size_t j = heading + 1; j < paragraphs.size() && seen < kCitationWindow; ++j) {
        for (const auto& line : paragraphs[j].lines) {
            if (seen++ >= kCitationWindow) break;
            citations += IsCitationLine(line.text);
        }
    }
    return citations >= kCitationLines;
}

// The paragraph without its last line
inline void DropLastLine(Paragraph& paragraph) {
    paragraph.lines.pop_back();
    paragraph.text.clear();
    for (const auto& line : paragraph.lines) {
        if (!paragraph.text.empty()) paragraph.text += ' ';
        paragraph.text += line.text;
    }
}

}  // namespace detail

inline void DropReferenceTail(std::vector<Paragraph>& paragraphs) {
    for (std::size_t i = paragraphs.size() / 2; i < paragraphs.size(); ++i) {
        auto& paragraph = paragraphs[i];
        const bool whole = detail::IsReferencesHeading(paragraph.text);
        const bool last_line = !whole && paragraph.lines.size() > 1 &&
                               detail::IsReferencesHeading(paragraph.lines.back().text);
        if ((!whole && !last_line) || !detail::CitationsFollow(paragraphs, i)) continue;
        auto end = i + 1;
        while (end < paragraphs.size() &&
               detail::IsCitationLine(paragraphs[end].lines.front().text)) {
            ++end;
        }
        if (last_line) detail::DropLastLine(paragraph);
        paragraphs.erase(paragraphs.begin() + static_cast<std::ptrdiff_t>(i + (last_line ? 1 : 0)),
                         paragraphs.begin() + static_cast<std::ptrdiff_t>(end));
        return;
    }
}

}  // namespace ambient::guidance
