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

TEST(DocumentUnits, AMarkedRecommendationTakesItsBulletsAndNotTheNextSentence) {
    const auto units = UnitsFromParagraphs(
        {Para("1.2.3 Offer allopurinol to people with:"),
         Para("\xE2\x80\xA2 two or more flares a year"), Para("\xE2\x80\xA2 tophi"),
         Para("For a short explanation of why the committee made this recommendation, see the "
              "rationale."),
         Para("1.2.4 Consider febuxostat.")});
    ASSERT_EQ(units.size(), 3u);
    EXPECT_EQ(units[0].number, "1.2.3");
    EXPECT_EQ(units[0].text,
              "1.2.3 Offer allopurinol to people with: \xE2\x80\xA2 two or more flares a year "
              "\xE2\x80\xA2 tophi");
    EXPECT_EQ(units[1].number, "");
    EXPECT_EQ(units[2].number, "1.2.4");
}

TEST(DocumentUnits, ANiceDateTagClosesTheRecommendationWithItsBulletsAndLastSentence) {
    const auto units = UnitsFromParagraphs(
        {Para("1.1.5 Refer the person for an assessment if 4 or more criteria are present:"),
         Para("\xE2\x80\xA2 buttock pain"), Para("\xE2\x80\xA2 improvement with movement."),
         Para("If exactly 3 of the criteria are present, perform an HLA-B27 test. [2017]"),
         Para("For a short explanation of why the committee made this recommendation, see the "
              "rationale."),
         Para(
             "1.1.6 Advise the person to seek repeat assessment if new symptoms develop. [2017]")});
    ASSERT_EQ(units.size(), 3u);
    EXPECT_EQ(units[0].number, "1.1.5");
    EXPECT_TRUE(units[0].text.ends_with("perform an HLA-B27 test. [2017]"));
    EXPECT_EQ(units[1].number, "");
    EXPECT_EQ(units[2].number, "1.1.6");
}

TEST(DocumentUnits, AGradedRecommendationRunsToItsToken) {
    const auto units = UnitsFromParagraphs(
        {Para("(i) Offer urate lowering therapy after a first flare. LoE: Ia; SOR: 95% (range "
              "80-100%)."),
         Para("(ii) Start allopurinol at 100 mg"), Para("and titrate monthly. LoE: IIb; SOR: 90%."),
         Para("Rationale"),
         Para("Allopurinol is the first-line urate lowering therapy in most patients.")});
    ASSERT_EQ(units.size(), 3u);
    EXPECT_EQ(units[0].number, "(i)");
    EXPECT_EQ(units[1].number, "(ii)");
    EXPECT_EQ(units[1].text,
              "(ii) Start allopurinol at 100 mg and titrate monthly. LoE: IIb; SOR: 90%.");
    EXPECT_EQ(units[2].section, "Rationale");
}

TEST(DocumentUnits, ABareLabelNumbersTheNextUnitAndJoinsItsLines) {
    const auto units = UnitsFromParagraphs(
        {Para("Recommendation 3", 0, 0.30F, 0.32F),
         Para("All people should be assessed for disease (GRADE 1C, SoA 98%).", 0, 0.34F, 0.40F),
         Para("Recommendation 4", 0, 0.42F, 0.44F),
         Para("Treat early (GRADE 2B, SoA 95%).", 0, 0.46F, 0.50F)});
    ASSERT_EQ(units.size(), 2u);
    EXPECT_EQ(units[0].number, "Recommendation 3");
    EXPECT_EQ(units[0].text, "All people should be assessed for disease (GRADE 1C, SoA 98%).");
    ASSERT_EQ(units[0].boxes.size(), 2u);
    EXPECT_FLOAT_EQ(units[0].boxes[0].second.top, 0.30F);
    EXPECT_EQ(units[1].number, "Recommendation 4");
}

TEST(DocumentUnits, AGradedRowWithoutMarksHoldsUntilItsToken) {
    const auto units = UnitsFromParagraphs(
        {Para("Offer sulfasalazine when methotrexate is contraindicated or not tolerated in people "
              "with active disease despite other treatment options being considered carefully "
              "first in every single case."),
         Para("Review at three months (GRADE 2C, SoA 92%)."),
         Para("Offer leflunomide as an alternative when sulfasalazine fails or is not tolerated by "
              "the person after an adequate trial at a full dose for three months."),
         Para("Review again (GRADE 2C, SoA 90%).")});
    ASSERT_EQ(units.size(), 2u);
    EXPECT_TRUE(units[0].text.ends_with("(GRADE 2C, SoA 92%)."));
    EXPECT_TRUE(units[1].text.ends_with("(GRADE 2C, SoA 90%)."));
}

TEST(DocumentUnits, FragmentsFrontMatterAndCaptionsAreDropped) {
    // A caption with its flowchart labels, a keyword line, a short sentence, and
    // numbered recommendations, one short without a full stop
    const auto units = DropFragments(UnitsFromParagraphs(
        {Para("Fig. 1 Approach to the evaluation of proximal pain and stiffness. ACJ: joint."),
         Para("Predominant peripheral joint symptoms, X-rays RA, other inflammatory arthritis "
              "Inflammatory Morning stiffness Joint swelling Peripheral hand/foot oedema"),
         Para("Treatment:"),
         Para("Key words: Guidelines, Polymyalgia rheumatica, Diagnosis, Treatment."),
         Para("Monitoring:"), Para("Aim for a target serum urate level below 360 micromol/litre."),
         Para("1.1 Offer allopurinol after a first attack when urate stays high."),
         Para("1.2 Image the femur when thigh pain develops on bisphosphonate therapy"),
         Para("1.3 Offer colchicine or an NSAID for an acute flare of gout.")}));

    ASSERT_EQ(units.size(), 4u);
    EXPECT_EQ(units[0].text, "Aim for a target serum urate level below 360 micromol/litre.");
    EXPECT_EQ(units[1].number, "1.1");
    EXPECT_EQ(units[2].number, "1.2");
    EXPECT_EQ(units[3].number, "1.3");
}

}  // namespace
}  // namespace ambient::guidance
