#pragma once

#include <algorithm>
#include <cctype>
#include <string>
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

// Lines in the order the host gave them. A paragraph closes at the end of a
// page, when the next line jumps back up the page, or when the gap down to it
// is more than half a line
inline std::vector<Paragraph> ParagraphsFromPages(const std::vector<Page>& pages) {
    std::vector<Paragraph> out;
    for (std::size_t p = 0; p < pages.size(); ++p) {
        bool open = false;
        float last_bottom = 0;
        float last_height = 0;
        for (const auto& line : pages[p].lines) {
            if (line.text.empty()) continue;
            const float height = line.box.bottom - line.box.top;
            const float gap = line.box.top - last_bottom;
            const bool follows =
                open && gap > -0.5F * height && gap <= 0.5F * std::max(height, last_height);
            if (follows) {
                auto& para = out.back();
                para.text += ' ';
                para.text += line.text;
                para.box = Union(para.box, line.box);
                para.lines.push_back(line);
            } else {
                out.push_back({static_cast<int>(p), line.text, line.box, {line}});
                open = true;
            }
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
