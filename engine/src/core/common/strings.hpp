#pragma once

#include <cctype>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// ASCII text helpers shared by the stages; bytes above 127 pass through untouched
namespace ambient::strings {

inline std::string Lower(std::string_view s) {
    std::string out(s);
    for (auto& c : out) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return out;
}

inline std::string_view Trim(std::string_view s) {
    while (!s.empty() && std::isspace(static_cast<unsigned char>(s.front()))) s.remove_prefix(1);
    while (!s.empty() && std::isspace(static_cast<unsigned char>(s.back()))) s.remove_suffix(1);
    return s;
}

// Whitespace-delimited words, as typed
inline std::vector<std::string> Words(std::string_view s) {
    std::vector<std::string> words;
    std::string word;
    for (const char c : s) {
        if (std::isspace(static_cast<unsigned char>(c)) != 0) {
            if (!word.empty()) words.push_back(std::move(word));
            word.clear();
        } else {
            word.push_back(c);
        }
    }
    if (!word.empty()) words.push_back(std::move(word));
    return words;
}

inline int WordCount(std::string_view s) {
    int words = 0;
    bool in_word = false;
    for (const unsigned char c : s) {
        const bool space = std::isspace(c) != 0;
        if (!space && !in_word) ++words;
        in_word = !space;
    }
    return words;
}

// A full stop, question or exclamation mark once trailing spaces, closing
// quotes and brackets and any non-ASCII closer are set aside
inline bool EndsSentence(std::string_view s) {
    for (auto it = s.rbegin(); it != s.rend(); ++it) {
        const auto c = static_cast<unsigned char>(*it);
        if (std::isspace(c) != 0 || c > 127) continue;
        if (*it == '"' || *it == '\'' || *it == ')') continue;
        return *it == '.' || *it == '?' || *it == '!';
    }
    return false;
}

}  // namespace ambient::strings
