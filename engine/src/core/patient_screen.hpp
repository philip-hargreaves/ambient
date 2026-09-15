#pragma once

#include <cctype>
#include <string>
#include <string_view>

namespace ambient::guidance {

namespace detail {

// Ten digits, spaces allowed between them, whose check digit holds
inline bool HoldsNhsNumber(std::string_view text) {
    int digits[10];
    int count = 0;
    const auto valid = [&] {
        if (count != 10) return false;
        int sum = 0;
        for (int d = 0; d < 9; ++d) sum += digits[d] * (10 - d);
        const int check = (11 - sum % 11) % 11;
        return check != 10 && check == digits[9];
    };
    for (const char c : text) {
        if (std::isdigit(static_cast<unsigned char>(c))) {
            if (count < 10) digits[count] = c - '0';
            ++count;
        } else if (c != ' ' || count == 0) {
            if (valid()) return true;
            count = 0;
        }
    }
    return valid();
}

}  // namespace detail

// Text that looks like it is about a patient: an NHS number, a date of birth
// label, a letter's opening or a discharge heading. Guidelines carry none
inline bool LooksLikePatientData(std::string_view text) {
    std::string lower(text);
    for (auto& c : lower) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    static const char* const kPhrases[] = {
        "date of birth", "dob:",        "nhs number",        "nhs no",
        "dear dr",       "dear doctor", "discharge summary", "discharge letter"};
    for (const char* phrase : kPhrases) {
        if (lower.find(phrase) != std::string::npos) return true;
    }
    return detail::HoldsNhsNumber(text);
}

}  // namespace ambient::guidance
