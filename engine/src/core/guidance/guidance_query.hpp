#pragma once

#include <string>
#include <string_view>
#include <vector>

namespace ambient::guidance {

// Sub-queries for retrieval: each sentence of the note on its own, plus the
// whole note. The boundaries mirror the evaluation harness's splitter so the
// engine and the harness build the same candidate lists
inline constexpr int kMinSentenceWords = 3;

// A newline always ends a sentence. A full stop, exclamation mark or question
// mark ends one when whitespace and a capital, digit, quote or bracket follow
// and the word before is not an abbreviation. Fragments under
// kMinSentenceWords are dropped
std::vector<std::string> SplitSentences(std::string_view note);

// A sentence that must not retrieve: it records a negated finding, family
// history or a hypothetical, so guidance for that condition would be for a
// patient who does not have it. Conservative: only openings and unambiguous
// phrases count
bool IsExcluded(std::string_view sentence);

// The sentences that pass the filter, then the whole note as one more query
std::vector<std::string> SubQueries(std::string_view note);

// A capital, digit, quote or bracket: what a sentence may begin with
bool OpensSentence(unsigned char c);

}  // namespace ambient::guidance
