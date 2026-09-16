#include "core/reference_tail.hpp"

#include <gtest/gtest.h>

#include <string>
#include <vector>

namespace ambient::guidance {
namespace {

Paragraph Para(const std::string& text) {
    Paragraph p{0, text, {}, {}};
    p.lines.push_back({text, {}});
    return p;
}

std::vector<Paragraph> Body(int count) {
    std::vector<Paragraph> out;
    for (int i = 0; i < count; ++i) out.push_back(Para("Offer allopurinol after a first attack."));
    return out;
}

std::vector<Paragraph> Citations(int count) {
    std::vector<Paragraph> out;
    for (int i = 0; i < count; ++i) {
        out.push_back(
            Para(std::to_string(i + 1) + " Kuo CF, Grainge MJ. Gout. Lancet 2015;74:661."));
    }
    return out;
}

TEST(ReferenceTail, TheHeadingAndItsCitationsGoAndAnAppendixAfterThemStays) {
    auto paragraphs = Body(30);
    paragraphs.push_back(Para("8 References"));
    const auto citations = Citations(8);
    paragraphs.insert(paragraphs.end(), citations.begin(), citations.end());
    paragraphs.push_back(Para("Supplementary Table 1 Doses in pregnancy"));
    paragraphs.push_back(
        Para("Hydroxychloroquine 400 mg daily is compatible (GRADE 1B, SoA 100%)."));
    DropReferenceTail(paragraphs);
    ASSERT_EQ(paragraphs.size(), 32u);
    EXPECT_EQ(paragraphs[30].text, "Supplementary Table 1 Doses in pregnancy");
}

TEST(ReferenceTail, AHeadingEndingAParagraphTakesOnlyTheTail) {
    auto paragraphs = Body(30);
    Paragraph last = Para("Review the plan at every appointment.");
    last.lines.push_back({"References", {}});
    last.text += " References";
    paragraphs.push_back(last);
    const auto citations = Citations(8);
    paragraphs.insert(paragraphs.end(), citations.begin(), citations.end());
    DropReferenceTail(paragraphs);
    ASSERT_EQ(paragraphs.size(), 31u);
    EXPECT_EQ(paragraphs.back().text, "Review the plan at every appointment.");
    EXPECT_EQ(paragraphs.back().lines.size(), 1u);
}

TEST(ReferenceTail, TheWordAloneOrEarlyDropsNothing) {
    auto pathway = Body(2);
    pathway.push_back(Para("References"));
    pathway.push_back(Para("See the local formulary for doses."));
    DropReferenceTail(pathway);
    EXPECT_EQ(pathway.size(), 4u);

    auto early = Citations(8);
    early.insert(early.begin(), Para("References"));
    const auto body = Body(20);
    early.insert(early.end(), body.begin(), body.end());
    DropReferenceTail(early);
    EXPECT_EQ(early.size(), 29u);
}

}  // namespace
}  // namespace ambient::guidance
