#include "core/guidance/page_text.hpp"

#include <gtest/gtest.h>

namespace clinicavt::guidance {
namespace {

PageLine Line(const char* text, float top, float bottom, float left = 0.1F, float right = 0.9F) {
    return {text, {left, top, right, bottom}};
}

TEST(PageText, LinesAtThePagesLeadingFormOneParagraphAndAWiderGapCloses) {
    Page page;
    page.lines = {Line("Offer allopurinol after a first attack when", 0.10F, 0.11F),
                  Line("urate stays high, and titrate the dose to", 0.12F, 0.13F),
                  Line("target.", 0.14F, 0.15F, 0.1F, 0.3F),
                  Line("Check urate six weeks later.", 0.18F, 0.19F)};
    const auto paragraphs = ParagraphsFromPages({page});
    ASSERT_EQ(paragraphs.size(), 2u);
    EXPECT_EQ(paragraphs[0].text,
              "Offer allopurinol after a first attack when urate stays high, and titrate the dose "
              "to target.");
    EXPECT_EQ(paragraphs[0].page, 0);
    EXPECT_FLOAT_EQ(paragraphs[0].box.top, 0.10F);
    EXPECT_FLOAT_EQ(paragraphs[0].box.bottom, 0.15F);
    EXPECT_EQ(paragraphs[0].lines.size(), 3u);
    EXPECT_EQ(paragraphs[1].text, "Check urate six weeks later.");
}

TEST(PageText, AShortLineFollowedByACapitalClosesAtTheLeading) {
    Page page;
    page.lines = {Line("Rationale", 0.10F, 0.11F, 0.1F, 0.25F),
                  Line("Allopurinol lowers urate in most people who take", 0.12F, 0.13F),
                  Line("it daily and keep taking it through flares as", 0.14F, 0.15F),
                  Line("advised.", 0.16F, 0.17F, 0.1F, 0.25F),
                  Line("Febuxostat is an alternative for the rest of", 0.18F, 0.19F),
                  Line("them when allopurinol cannot be used at all", 0.20F, 0.21F),
                  Line("safely.", 0.22F, 0.23F, 0.1F, 0.2F)};
    const auto paragraphs = ParagraphsFromPages({page});
    ASSERT_EQ(paragraphs.size(), 3u);
    EXPECT_EQ(paragraphs[0].text, "Rationale");
    EXPECT_EQ(paragraphs[1].text,
              "Allopurinol lowers urate in most people who take it daily and keep taking it "
              "through flares as advised.");
    EXPECT_EQ(paragraphs[2].text,
              "Febuxostat is an alternative for the rest of them when allopurinol cannot be used "
              "at all safely.");
}

TEST(PageText, AClosingTokenOrAMarkedOpeningEndsTheParagraphAtTheSameLeading) {
    Page page;
    page.lines = {Line("1.1.8 Refer people with suspected new onset inflammatory", 0.10F, 0.11F),
                  Line("arthritis to a rheumatology service within 3 weeks. [2017]", 0.12F, 0.13F),
                  Line("1.1.9 Refer people with dactylitis to a rheumatologist for", 0.14F, 0.15F),
                  Line("a spondyloarthritis assessment and give 3.5 mg of the", 0.16F, 0.17F),
                  Line("usual dose to those over 65 years.", 0.18F, 0.19F),
                  Line("1.1.10 Refer people with enthesitis for an assessment.", 0.20F, 0.21F),
                  Line("2. Offer colchicine for a flare when NSAIDs are unsuitable", 0.22F, 0.23F),
                  Line("3. Offer an NSAID otherwise, with a proton pump inhibitor", 0.24F, 0.25F)};
    const auto paragraphs = ParagraphsFromPages({page});
    ASSERT_EQ(paragraphs.size(), 5u);
    EXPECT_TRUE(paragraphs[1].text.starts_with("1.1.9"));
    EXPECT_TRUE(paragraphs[1].text.ends_with("over 65 years."));
    EXPECT_TRUE(paragraphs[2].text.starts_with("1.1.10"));
    EXPECT_TRUE(paragraphs[4].text.starts_with("3. Offer"));
}

TEST(PageText, TextSplitsOnBlankLinesAndKeepsEachLine) {
    const auto paragraphs = ParagraphsFromText("Gout  guideline\n\n1.1 Offer\nallopurinol.\n\n\n");
    ASSERT_EQ(paragraphs.size(), 2u);
    EXPECT_EQ(paragraphs[0].text, "Gout guideline");
    EXPECT_EQ(paragraphs[1].text, "1.1 Offer allopurinol.");
    EXPECT_EQ(paragraphs[1].lines.size(), 2u);
    EXPECT_EQ(paragraphs[1].page, 0);
}

TEST(PageText, AParagraphRunsOnAcrossAColumnAndAPageWhenItStopsMidSentence) {
    Page first;
    first.lines = {Line("Offer allopurinol when urate stays", 0.80F, 0.82F),
                   Line("high after a flare.", 0.10F, 0.12F, 0.55F, 0.72F),
                   Line("Rationale", 0.15F, 0.17F, 0.55F, 0.65F),
                   Line("Febuxostat is an alternative", 0.20F, 0.22F, 0.55F, 0.95F),
                   Line("when allopurinol fails or is", 0.25F, 0.27F, 0.55F, 0.95F)};
    Page second;
    second.lines = {Line("not tolerated.", 0.10F, 0.12F)};
    const auto paragraphs = ParagraphsFromPages({first, second});
    ASSERT_EQ(paragraphs.size(), 3u);
    EXPECT_EQ(paragraphs[0].text, "Offer allopurinol when urate stays high after a flare.");
    EXPECT_EQ(paragraphs[1].text, "Rationale");
    EXPECT_EQ(paragraphs[2].text,
              "Febuxostat is an alternative when allopurinol fails or is not tolerated.");
    EXPECT_EQ(paragraphs[2].page, 0);
    EXPECT_EQ(paragraphs[2].lines.size(), 3u);
}

TEST(PageText, AJumpBackUpThePageAndANewPageBothClose) {
    Page first;
    first.lines = {Line("Left column ends here.", 0.80F, 0.82F),
                   Line("Right column starts here.", 0.10F, 0.12F, 0.55F, 0.95F)};
    Page second;
    second.lines = {Line("Second page.", 0.10F, 0.12F)};
    const auto paragraphs = ParagraphsFromPages({first, second});
    ASSERT_EQ(paragraphs.size(), 3u);
    EXPECT_EQ(paragraphs[1].text, "Right column starts here.");
    EXPECT_EQ(paragraphs[2].page, 1);
}

TEST(PageText, AGradeTokenWrappedInANarrowColumnStaysOneParagraph) {
    Page page;
    page.lines = {Line("Education about treatment should be provided to promote", 0.10F, 0.11F),
                  Line("self-management (GRADE 1B,", 0.12F, 0.13F, 0.1F, 0.35F),
                  Line("SoA 100%).", 0.14F, 0.15F, 0.1F, 0.25F),
                  Line("Support should be provided around transition of care.", 0.16F, 0.17F)};
    const auto paragraphs = ParagraphsFromPages({page});
    ASSERT_EQ(paragraphs.size(), 2u) << "the wrapped tail leaves no fragment paragraph";
    EXPECT_EQ(paragraphs[0].text,
              "Education about treatment should be provided to promote self-management "
              "(GRADE 1B, SoA 100%).");
    EXPECT_EQ(paragraphs[1].text, "Support should be provided around transition of care.");
}

TEST(PageText, EmptyLinesAndEmptyPagesLeaveNothing) {
    Page page;
    page.lines = {Line("", 0.1F, 0.12F)};
    EXPECT_TRUE(ParagraphsFromPages({page, Page{}}).empty());
}

}  // namespace
}  // namespace clinicavt::guidance
