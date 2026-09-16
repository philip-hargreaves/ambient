#include "core/document_units.hpp"

#include <gtest/gtest.h>

#include <string>

namespace ambient::guidance {
namespace {

Paragraph Para(const char* text, int page = 0, float top = 0.1F, float bottom = 0.12F) {
    Paragraph p{page, text, {0.1F, top, 0.9F, bottom}, {}};
    p.lines.push_back({text, p.box});
    return p;
}

const char* const kLong =
    "Offer allopurinol after a first attack when urate stays high and the patient agrees.";

TEST(DocumentUnits, AHeadingLabelsTheNextUnitAndARecommendationStandsAlone) {
    const auto units = UnitsFromParagraphs(
        {Para("Gout guideline"), Para("Urate lowering therapy:"),
         Para("1.1 Offer allopurinol after a first attack when urate stays high."),
         Para("1.2 Offer colchicine or an NSAID for an acute flare of gout.")});
    ASSERT_EQ(units.size(), 2u);
    EXPECT_EQ(units[0].section, "Urate lowering therapy");
    EXPECT_EQ(units[0].number, "1.1");
    EXPECT_EQ(units[0].text, "1.1 Offer allopurinol after a first attack when urate stays high.");
    EXPECT_EQ(units[1].section, "");
    EXPECT_EQ(units[1].number, "1.2");
}

TEST(DocumentUnits, AHeadingsLinesJoinTheUnitItLabels) {
    const auto units = UnitsFromParagraphs(
        {Para("Prompt 1:", 0, 0.30F, 0.32F),
         Para("1.1 Offer allopurinol after a first attack when urate stays high.", 0, 0.34F,
              0.40F)});
    ASSERT_EQ(units.size(), 1u);
    EXPECT_EQ(units[0].section, "Prompt 1");
    ASSERT_EQ(units[0].boxes.size(), 2u);
    EXPECT_FLOAT_EQ(units[0].boxes[0].second.top, 0.30F);
    EXPECT_FLOAT_EQ(units[0].boxes[1].second.bottom, 0.40F);
}

TEST(DocumentUnits, ShortParagraphsMergeForwardUntilTheFloor) {
    const auto units = UnitsFromParagraphs(
        {Para("Check urate six weeks after any dose change.", 0, 0.10F, 0.12F),
         Para("Titrate the dose in 100 mg steps until urate falls.", 0, 0.13F, 0.15F),
         Para("Stop if a rash appears and refer for advice on alternatives.", 0, 0.16F, 0.18F),
         Para("Review the plan at every appointment with the patient.", 0, 0.19F, 0.21F)});
    ASSERT_EQ(units.size(), 2u);
    EXPECT_EQ(units[0].text,
              "Check urate six weeks after any dose change. Titrate the dose in 100 mg steps "
              "until urate falls. Stop if a rash appears and refer for advice on alternatives.");
    ASSERT_EQ(units[0].boxes.size(), 3u);
    EXPECT_FLOAT_EQ(units[0].boxes[0].second.top, 0.10F);
    EXPECT_FLOAT_EQ(units[0].boxes[2].second.bottom, 0.18F);
    EXPECT_EQ(units[1].text, "Review the plan at every appointment with the patient.");
}

TEST(DocumentUnits, AMergedUnitOverTwoPagesKeepsEachLinesPage) {
    const auto units = UnitsFromParagraphs(
        {Para("Check urate six weeks after any dose change.", 0, 0.9F, 0.95F),
         Para("Titrate the dose in 100 mg steps until urate falls to target.", 1, 0.05F, 0.1F)});
    ASSERT_EQ(units.size(), 1u);
    EXPECT_EQ(units[0].page, 0);
    ASSERT_EQ(units[0].boxes.size(), 2u);
    EXPECT_EQ(units[0].boxes[1].first, 1);
}

TEST(DocumentUnits, ALongParagraphSplitsAtSentenceEndsAlongItsLines) {
    Paragraph paragraph{0, "", {}, {}};
    for (int i = 0; i < 30; ++i) {
        const float top = 0.1F + i * 0.02F;
        const std::string line = std::string(kLong) + (i % 3 == 2 ? "" : " and");
        if (i) paragraph.text += ' ';
        paragraph.text += line;
        const Box box{0.1F, top, 0.9F, top + 0.015F};
        paragraph.lines.push_back({line, box});
        paragraph.box = i ? Union(paragraph.box, box) : box;
    }
    const auto units = UnitsFromParagraphs({paragraph});
    ASSERT_GE(units.size(), 2u);
    std::size_t lines = 0;
    for (const auto& unit : units) {
        EXPECT_LE(detail::WordCount(unit.text), kMaxUnitWords);
        ASSERT_FALSE(unit.boxes.empty());
        lines += unit.boxes.size();
    }
    EXPECT_EQ(lines, 30u);
    EXPECT_LT(units[0].boxes.back().second.bottom, units[1].boxes.front().second.top + 0.001F);
    EXPECT_TRUE(units[0].text.ends_with("agrees."));
}

TEST(DocumentUnits, TextWithoutBoxesGivesUnitsWithoutBoxes) {
    const auto paragraphs = ParagraphsFromText(
        "Gout guideline\n\n1.1 Offer allopurinol after a first attack when urate stays high.\n"
        "\n1.2 Offer colchicine or an NSAID for an acute flare.\n");
    ASSERT_EQ(paragraphs.size(), 3u);
    const auto units = UnitsFromParagraphs(paragraphs);
    ASSERT_EQ(units.size(), 2u);
    EXPECT_EQ(units[0].section, "Gout guideline");
    EXPECT_TRUE(units[0].boxes.empty());
}

}  // namespace
}  // namespace ambient::guidance
