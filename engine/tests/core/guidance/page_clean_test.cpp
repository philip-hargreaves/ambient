#include "core/guidance/page_clean.hpp"

#include <gtest/gtest.h>

#include <set>
#include <string>
#include <vector>

namespace ambient::guidance {
namespace {

PageLine Line(const std::string& text, float top = 0.5F) {
    return {text, {0.1F, top, 0.9F, top + 0.01F}};
}

Page PageOf(std::vector<PageLine> lines) {
    Page page;
    page.lines = std::move(lines);
    return page;
}

std::vector<std::string> Texts(const Page& page) {
    std::vector<std::string> out;
    for (const auto& line : page.lines) out.push_back(line.text);
    return out;
}

TEST(PageClean, AnUnmappedGlyphStaysOnlyNextToADigit) {
    EXPECT_EQ(RepairText("Pu\xEF\xBF\xBD"
                         "echal and Hern\x04"
                         "an"),
              "Puechal and Hernan");
    EXPECT_EQ(RepairText("(\xEF\xBF\xBD"
                         "16%) or \x04"
                         "95%"),
              "(\xEF\xBF\xBD"
              "16%) or \xEF\xBF\xBD"
              "95%");
    EXPECT_EQ(RepairText("\xEF\xBF\xBD\xEF\xBF\xBD"
                         "20%"),
              "\xEF\xBF\xBD"
              "20%");
    EXPECT_EQ(RepairText("1,\xEF\xBF\xBD,\xE2\x80\xA0"), "1,,\xE2\x80\xA0");
    EXPECT_EQ(RepairText("\xEF\x81\xAF"), "");
    EXPECT_EQ(RepairText("  \xEF\x81\xAF  Offer  colchicine "), "Offer colchicine");
}

TEST(PageClean, LigaturesSoftHyphensAndNoBreakSpacesBecomePlain) {
    EXPECT_EQ(RepairText("con\xEF\xAC\x81"
                         "dent \xEF\xAC\x82"
                         "are"),
              "confident flare");
    EXPECT_EQ(RepairText("soft\xC2\xADware"), "software");
    EXPECT_EQ(RepairText("soft\xC2\xAD"), "soft\xC2\xAD");
    EXPECT_EQ(RepairText("15\xC2\xA0mg"), "15 mg");
    EXPECT_EQ(RepairText("Sj\xC3\xB6gren\xE2\x80\x99s"), "Sj\xC3\xB6gren\xE2\x80\x99s");
}

TEST(PageClean, TheDocumentsHyphenCodeIsLearnedFromItsLineEnds) {
    std::vector<Page> pages{
        PageOf({Line("Start treat\x02"), Line("ment early, then treatment of the condition"),
                Line("of the condi\x02"),
                Line("tion in 63\x02"
                     "100% of anti\x02"
                     "inflammatory"),
                Line("cases, P \x02 0.001, ap\x02"),
                Line("propriate. Rarely (\x03"
                     "0.1%)")})};
    EXPECT_EQ(HyphenCodes(pages), (std::set<char32_t>{2}));
    CleanPages(pages);
    EXPECT_EQ(Texts(pages[0]),
              (std::vector<std::string>{"Start treatment", "early, then treatment of the condition",
                                        "of the condition",
                                        "in 63\xE2\x80\x93"
                                        "100% of anti-inflammatory",
                                        "cases, P - 0.001, ap-propriate.",
                                        "Rarely (\xEF\xBF\xBD"
                                        "0.1%)"}));
}

TEST(PageClean, ABrokenWordRejoinsWhenTheDocumentUsesItWhole) {
    auto page = PageOf({Line("Start treat-"), Line("ment early. Later treatment stops."),
                        Line("Use an anti-"), Line("inflammatory drug."), Line("Stop at the end-"),
                        Line("Point defined above."), Line("hyphen\xC2\xAD"), Line("ated")});
    JoinBrokenWords(page, detail::DocumentWords({page}));
    EXPECT_EQ(Texts(page),
              (std::vector<std::string>{"Start treatment", "early. Later treatment stops.",
                                        "Use an anti-inflammatory", "drug.", "Stop at the end-",
                                        "Point defined above.", "hyphenated"}));
}

TEST(PageClean, RunningHeadersAndFootersGoAndMidPageHeadingsStay) {
    std::vector<Page> pages;
    for (int p = 0; p < 5; ++p) {
        pages.push_back(PageOf({Line("Gout: diagnosis and management (NG219)", 0.03F),
                                Line("Recommendation", 0.5F), Line("Offer allopurinol.", 0.52F),
                                Line("Page " + std::to_string(p + 1) + " of 5", 0.96F)}));
    }
    pages[1].lines.insert(pages[1].lines.begin() + 1, Line("Check urate again.", 0.05F));
    RemoveFurniture(pages);
    for (const auto& page : pages) {
        ASSERT_GE(page.lines.size(), 2u);
        EXPECT_TRUE(page.lines[0].text == "Recommendation" ||
                    page.lines[0].text == "Check urate again.");
        EXPECT_EQ(page.lines.back().text, "Offer allopurinol.");
    }
    EXPECT_EQ(pages[1].lines.size(), 3u);
}

TEST(PageClean, TwoPagesKeepTheirHeaders) {
    std::vector<Page> pages{PageOf({Line("Pathway", 0.03F), Line("Refer.", 0.5F)}),
                            PageOf({Line("Pathway", 0.03F), Line("Treat.", 0.5F)})};
    RemoveFurniture(pages);
    EXPECT_EQ(pages[0].lines.size(), 2u);
    EXPECT_EQ(pages[1].lines.size(), 2u);
}

TEST(PageClean, CleaningRepairsThenJoinsThenDropsEmptyLines) {
    std::vector<Page> pages{PageOf({Line("Check the pa\xC2\xAD"), Line("tient \xEF\x81\xAF"),
                                    Line("\xEF\x81\xAF"), Line("Sj\xC3\xB6gren\xE2\x80\x99s")})};
    CleanPages(pages);
    EXPECT_EQ(Texts(pages[0]),
              (std::vector<std::string>{"Check the patient", "Sj\xC3\xB6gren\xE2\x80\x99s"}));
}

}  // namespace
}  // namespace ambient::guidance
