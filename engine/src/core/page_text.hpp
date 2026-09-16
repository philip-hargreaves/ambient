#pragma once

#include <algorithm>
#include <array>
#include <cctype>
#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

#include "core/guidance_query.hpp"
#include "core/recommendation_marks.hpp"

namespace ambient::guidance {

// A region of a page as fractions of its displayed size, origin top left
struct Box {
    float left = 0;
    float top = 0;
    float right = 0;
    float bottom = 0;
};

struct PageLine {
    std::string text;
    Box box;
};

// One page as the ingest host read it
struct Page {
    float width = 0;  // points, as displayed
    float height = 0;
    int rotation = 0;  // quarter turns clockwise
    int images = 0;
    std::vector<PageLine> lines;
};

// A run of lines on one page, joined with spaces, the lines kept for splitting
struct Paragraph {
    int page = 0;
    std::string text;
    Box box;
    std::vector<PageLine> lines;
};

inline Box Union(const Box& a, const Box& b) {
    return {std::min(a.left, b.left), std::min(a.top, b.top), std::max(a.right, b.right),
            std::max(a.bottom, b.bottom)};
}

namespace detail {

// Closing quotes, brackets and spaces stripped, then a full stop, question or
// exclamation mark
inline bool EndsSentence(std::string_view s) {
    while (!s.empty() && (s.back() == '"' || s.back() == '\'' || s.back() == ')' ||
                          s.back() == ' ' || static_cast<unsigned char>(s.back()) > 127)) {
        s.remove_suffix(1);
    }
    return !s.empty() && (s.back() == '.' || s.back() == '?' || s.back() == '!');
}

}  // namespace detail

// A line follows the one before within this many of the page's leading
inline constexpr float kAdjacentPitch = 1.3F;
// A line ending this far short of its column's edge closes a paragraph when a
// capital follows
inline constexpr float kShortLine = 0.1F;

namespace detail {

// The page's leading: the median rise from one line's top to the next
inline float Pitch(const Page& page) {
    std::vector<float> rises;
    for (std::size_t k = 0; k + 1 < page.lines.size(); ++k) {
        const float rise = page.lines[k + 1].box.top - page.lines[k].box.top;
        if (rise > 0) rises.push_back(rise);
    }
    if (rises.empty()) return 0;
    const auto middle = rises.begin() + static_cast<std::ptrdiff_t>(rises.size() / 2);
    std::nth_element(rises.begin(), middle, rises.end());
    return *middle;
}

// Where lines end on each half of the page: the median right edge
inline std::array<float, 2> ColumnEdges(const Page& page) {
    std::array<std::vector<float>, 2> rights;
    for (const auto& line : page.lines) {
        rights[line.box.left > 0.5F].push_back(line.box.right);
    }
    std::array<float, 2> edges{1, 1};
    for (std::size_t side = 0; side < 2; ++side) {
        auto& r = rights[side];
        if (r.empty()) continue;
        const auto middle = r.begin() + static_cast<std::ptrdiff_t>(r.size() / 2);
        std::nth_element(r.begin(), middle, r.end());
        edges[side] = *middle;
    }
    return edges;
}

inline bool Short(const PageLine& line, const std::array<float, 2>& edges) {
    return line.box.right < edges[line.box.left > 0.5F] - kShortLine;
}

// "1.5.7 Consider", "3. Offer" or "(iv) Start": a recommendation mark opening
// the line with a capital after it, as NICE and numbered lists set one item
// after another at the same leading
inline bool OpensMarked(const std::string& text) {
    for (const auto scheme : {Scheme::kDotted, Scheme::kRoman, Scheme::kBracketed,
                              Scheme::kNumbered, Scheme::kWord, Scheme::kLetterR}) {
        const auto mark = MarkOf(text, scheme);
        if (!mark.empty() && mark.size() + 1 < text.size() && text[mark.size()] == ' ' &&
            std::isupper(static_cast<unsigned char>(text[mark.size() + 1]))) {
            return true;
        }
    }
    return false;
}

}  // namespace detail

// Lines in the host's order, which is the reading order of the PDFs measured.
// A paragraph closes when the next line rises more than the page's leading
// allows, jumps back up the page, opens with a capital after a short line,
// opens a marked recommendation, or follows a closing token. It runs on
// regardless when it stopped mid-sentence and the next line begins in lower
// case, across a column or a page
inline std::vector<Paragraph> ParagraphsFromPages(const std::vector<Page>& pages) {
    std::vector<Paragraph> out;
    int last_page = -1;
    float last_top = 0;
    bool last_short = false;
    for (std::size_t p = 0; p < pages.size(); ++p) {
        const float pitch = detail::Pitch(pages[p]);
        const auto edges = detail::ColumnEdges(pages[p]);
        for (const auto& line : pages[p].lines) {
            if (line.text.empty()) continue;
            const float height = line.box.bottom - line.box.top;
            const float rise = line.box.top - last_top;
            const float limit = pitch > 0 ? kAdjacentPitch * pitch : 1.5F * height;
            const auto first = static_cast<unsigned char>(line.text.front());
            const bool adjacent = static_cast<int>(p) == last_page && rise > -0.5F * height &&
                                  rise <= limit && !(last_short && detail::OpensSentence(first)) &&
                                  !detail::OpensMarked(line.text) &&
                                  !EndsWithToken(out.back().text);
            const bool runs_on =
                !out.empty() && !detail::EndsSentence(out.back().text) && std::islower(first);
            if (adjacent || runs_on) {
                auto& para = out.back();
                para.text += ' ';
                para.text += line.text;
                para.box = Union(para.box, line.box);
                para.lines.push_back(line);
            } else {
                out.push_back({static_cast<int>(p), line.text, line.box, {line}});
            }
            last_page = static_cast<int>(p);
            last_top = line.box.top;
            last_short = detail::Short(line, edges);
        }
    }
    return out;
}

// Plain or Markdown text: paragraphs split on blank lines, each line kept,
// whitespace runs collapsed to one space, boxes empty
inline std::vector<Paragraph> ParagraphsFromText(const std::string& text) {
    std::vector<Paragraph> out;
    Paragraph current;
    const auto flush = [&] {
        if (!current.lines.empty()) out.push_back(std::move(current));
        current = Paragraph{};
    };
    std::size_t at = 0;
    while (at <= text.size()) {
        const auto end = text.find('\n', at);
        const auto raw = text.substr(at, end == std::string::npos ? std::string::npos : end - at);
        std::string line;
        bool space = true;
        for (const unsigned char c : raw) {
            if (std::isspace(c)) {
                if (!space) line.push_back(' ');
                space = true;
            } else {
                line.push_back(static_cast<char>(c));
                space = false;
            }
        }
        if (!line.empty() && line.back() == ' ') line.pop_back();
        if (line.empty()) {
            flush();
        } else {
            if (!current.lines.empty()) current.text += ' ';
            current.text += line;
            current.lines.push_back({line, {}});
        }
        if (end == std::string::npos) break;
        at = end + 1;
    }
    flush();
    return out;
}

}  // namespace ambient::guidance
