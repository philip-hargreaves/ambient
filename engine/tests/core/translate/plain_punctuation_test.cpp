#include "core/translate/plain_punctuation.hpp"

#include <gtest/gtest.h>

namespace ambient::translate {
namespace {

TEST(PlainPunctuation, CurlyQuotesDashesSpacesAndEllipsisBecomePlain) {
    EXPECT_EQ(PlainPunctuation("Bell\xE2\x80\x99s palsy"), "Bell's palsy");
    EXPECT_EQ(PlainPunctuation("\xE2\x80\x9CRest\xE2\x80\x9D, \xE2\x80\x98now\xE2\x80\x99"),
              "\"Rest\", 'now'");
    EXPECT_EQ(PlainPunctuation("two\xE2\x80\x93six puffs \xE2\x80\x94 as needed"),
              "two-six puffs - as needed");
    EXPECT_EQ(PlainPunctuation("400\xC2\xA0mg"), "400 mg");
    EXPECT_EQ(PlainPunctuation("and so on\xE2\x80\xA6"), "and so on...");
}

TEST(PlainPunctuation, OtherTextIsUntouched) {
    EXPECT_EQ(PlainPunctuation("Take ibuprofen 400mg twice a day."),
              "Take ibuprofen 400mg twice a day.");
    EXPECT_EQ(PlainPunctuation("caf\xC3\xA9 costs \xE2\x82\xAC five"),
              "caf\xC3\xA9 costs \xE2\x82\xAC five");
    EXPECT_EQ(PlainPunctuation(""), "");
}

}  // namespace
}  // namespace ambient::translate
