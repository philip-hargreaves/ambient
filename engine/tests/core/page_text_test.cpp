#include "core/page_text.hpp"

#include <gtest/gtest.h>

namespace ambient::guidance {
namespace {

PageLine Line(const char* text, float top, float bottom, float left = 0.1F, float right = 0.9F) {
    return {text, {left, top, right, bottom}};
}

TEST(PageText, LinesCloseTogetherFormOneParagraph) {
    Page page;
    page.lines = {Line("Offer allopurinol after", 0.10F, 0.12F),
                  Line("a first attack.", 0.125F, 0.145F),
                  Line("Check urate six weeks later.", 0.20F, 0.22F)};
    const auto paragraphs = ParagraphsFromPages({page});
    ASSERT_EQ(paragraphs.size(), 2u);
    EXPECT_EQ(paragraphs[0].text, "Offer allopurinol after a first attack.");
    EXPECT_EQ(paragraphs[0].page, 0);
    EXPECT_FLOAT_EQ(paragraphs[0].box.top, 0.10F);
    EXPECT_FLOAT_EQ(paragraphs[0].box.bottom, 0.145F);
    EXPECT_EQ(paragraphs[0].lines.size(), 2u);
    EXPECT_EQ(paragraphs[1].text, "Check urate six weeks later.");
}

TEST(PageText, TextSplitsOnBlankLinesAndKeepsEachLine) {
    const auto paragraphs = ParagraphsFromText("Gout  guideline\n\n1.1 Offer\nallopurinol.\n\n\n");
    ASSERT_EQ(paragraphs.size(), 2u);
    EXPECT_EQ(paragraphs[0].text, "Gout guideline");
    EXPECT_EQ(paragraphs[1].text, "1.1 Offer allopurinol.");
    EXPECT_EQ(paragraphs[1].lines.size(), 2u);
    EXPECT_EQ(paragraphs[1].page, 0);
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

TEST(PageText, EmptyLinesAndEmptyPagesLeaveNothing) {
    Page page;
    page.lines = {Line("", 0.1F, 0.12F)};
    EXPECT_TRUE(ParagraphsFromPages({page, Page{}}).empty());
}

}  // namespace
}  // namespace ambient::guidance
