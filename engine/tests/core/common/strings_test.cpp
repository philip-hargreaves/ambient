#include "core/common/strings.hpp"

#include <gtest/gtest.h>

#include <string>
#include <vector>

namespace ambient::strings {
namespace {

TEST(Strings, LowerLeavesNonAsciiBytesAlone) {
    EXPECT_EQ(Lower("Dr Smith 5mg"), "dr smith 5mg");
    EXPECT_EQ(Lower("caf\xC3\xA9"), "caf\xC3\xA9");
}

TEST(Strings, TrimStripsBothEnds) {
    EXPECT_EQ(Trim("  a b \t\n"), "a b");
    EXPECT_EQ(Trim("   "), "");
    EXPECT_EQ(Trim(""), "");
}

TEST(Strings, WordsSplitOnAnyWhitespace) {
    EXPECT_EQ(Words("  one two\tthree\n"), (std::vector<std::string>{"one", "two", "three"}));
    EXPECT_TRUE(Words(" \t").empty());
}

TEST(Strings, WordCountMatchesWords) {
    EXPECT_EQ(WordCount(""), 0);
    EXPECT_EQ(WordCount("one"), 1);
    EXPECT_EQ(WordCount("  one  two three "), 3);
}

TEST(Strings, EndsSentenceSetsClosersAside) {
    EXPECT_TRUE(EndsSentence("Done."));
    EXPECT_TRUE(EndsSentence("Really?\")  "));
    EXPECT_TRUE(EndsSentence("He said no.\xE2\x80\x9D"));  // closing curly quote
    EXPECT_FALSE(EndsSentence("and then"));
    EXPECT_FALSE(EndsSentence("e.g. this,"));
    EXPECT_FALSE(EndsSentence(""));
}

}  // namespace
}  // namespace ambient::strings
