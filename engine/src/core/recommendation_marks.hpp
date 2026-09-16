#pragma once

#include <cctype>
#include <string>
#include <string_view>

namespace ambient::guidance {

// The leading dotted number of a paragraph ("1.2.3"), or empty
inline std::string LeadingNumber(const std::string& paragraph) {
    std::size_t i = 0;
    while (i < paragraph.size() && std::isdigit(static_cast<unsigned char>(paragraph[i]))) ++i;
    if (i == 0 || i >= paragraph.size() || paragraph[i] != '.') return "";
    std::size_t end = i;
    while (end < paragraph.size() && paragraph[end] == '.') {
        std::size_t k = end + 1;
        while (k < paragraph.size() && std::isdigit(static_cast<unsigned char>(paragraph[k]))) ++k;
        if (k == end + 1) break;
        end = k;
    }
    if (end < paragraph.size() && std::isalnum(static_cast<unsigned char>(paragraph[end])))
        return "";
    return paragraph.substr(0, end);
}

// "Recommendation 12", "recommendation 3a", "1.2", "1.2.3 Offer": a paragraph
// that opens a numbered recommendation
inline bool StartsRecommendation(const std::string& paragraph) {
    if (!LeadingNumber(paragraph).empty()) return true;
    static constexpr std::string_view kWord = "recommendation";
    if (paragraph.size() <= kWord.size()) return false;
    for (std::size_t i = 0; i < kWord.size(); ++i) {
        if (std::tolower(static_cast<unsigned char>(paragraph[i])) != kWord[i]) return false;
    }
    std::size_t i = kWord.size();
    if (!std::isspace(static_cast<unsigned char>(paragraph[i]))) return false;
    while (i < paragraph.size() && std::isspace(static_cast<unsigned char>(paragraph[i]))) ++i;
    std::size_t digits = i;
    while (digits < paragraph.size() &&
           std::isdigit(static_cast<unsigned char>(paragraph[digits]))) {
        ++digits;
    }
    if (digits == i) return false;
    if (digits < paragraph.size() && std::isalpha(static_cast<unsigned char>(paragraph[digits]))) {
        ++digits;
    }
    return digits >= paragraph.size() ||
           !std::isalnum(static_cast<unsigned char>(paragraph[digits]));
}

}  // namespace ambient::guidance
