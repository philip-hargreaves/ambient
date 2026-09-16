#pragma once

#include <algorithm>
#include <cctype>
#include <string>
#include <string_view>
#include <vector>

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

// Lines in the host's order, which is the reading order of the PDFs measured.
// A paragraph closes at a gap of more than half a line or a jump back up the
// page, unless it stopped mid-sentence and the next line begins in lower
// case, which carries it across a column or a page
inline std::vector<Paragraph> ParagraphsFromPages(const std::vector<Page>& pages) {
    std::vector<Paragraph> out;
    int last_page = -1;
    float last_bottom = 0;
    float last_height = 0;
    for (std::size_t p = 0; p < pages.size(); ++p) {
        for (const auto& line : pages[p].lines) {
            if (line.text.empty()) continue;
            const float height = line.box.bottom - line.box.top;
            const float gap = line.box.top - last_bottom;
            const bool adjacent = static_cast<int>(p) == last_page && gap > -0.5F * height &&
                                  gap <= 0.5F * std::max(height, last_height);
            const bool runs_on = !out.empty() && !detail::EndsSentence(out.back().text) &&
                                 std::islower(static_cast<unsigned char>(line.text.front()));
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
            last_bottom = line.box.bottom;
            last_height = height;
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
