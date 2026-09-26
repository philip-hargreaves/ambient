#include "adapters/guidance/ingest_host.hpp"

#include <gtest/gtest.h>

#include <chrono>
#include <string>
#include <vector>

#include "tiny_pdf.hpp"

namespace clinicavt::guidance {
namespace {

std::vector<std::uint8_t> Bytes(const std::string& s) {
    return {s.begin(), s.end()};
}

IngestHost Fake(HostLimits limits = {}) {
    return IngestHost(CLINICAVT_FAKE_INGEST_HOST, limits);
}

std::string ReasonOf(const IngestHost& host, const std::string& input) {
    try {
        host.Extract(Bytes(input));
    } catch (const HostError& e) {
        return e.Reason();
    }
    return "";
}

TEST(IngestHost, CannedPagesComeBackAsFractions) {
    const auto pages = Fake().Extract(Bytes("%PDF canned"));
    ASSERT_EQ(pages.size(), 2u);
    EXPECT_FLOAT_EQ(pages[0].width, 595);
    ASSERT_EQ(pages[0].lines.size(), 2u);
    EXPECT_EQ(pages[0].lines[0].text, "1.1 Offer allopurinol after a first attack.");
    EXPECT_NEAR(pages[0].lines[0].box.left, 72.0F / 595, 1e-4);
    EXPECT_NEAR(pages[0].lines[0].box.bottom, 84.0F / 842, 1e-4);
    EXPECT_EQ(pages[1].images, 1);
    EXPECT_TRUE(pages[1].lines.empty());
}

TEST(IngestHost, ExitCodesNameTheRefusal) {
    EXPECT_EQ(ReasonOf(Fake(), "FAKE exit 2"), "cannotOpen");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE exit 3"), "password");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE exit 4"), "outputBound");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE exit 7"), "crashed");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE crash"), "crashed");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE garbage"), "badOutput");
    EXPECT_EQ(ReasonOf(Fake(), "FAKE partial"), "badOutput");
}

TEST(IngestHost, AStallIsKilledAtTheTimeout) {
    HostLimits limits;
    limits.timeout = std::chrono::milliseconds(500);
    const auto start = std::chrono::steady_clock::now();
    EXPECT_EQ(ReasonOf(Fake(limits), "FAKE sleep"), "timeout");
    EXPECT_LT(std::chrono::steady_clock::now() - start, std::chrono::seconds(10));
}

TEST(IngestHost, OutputPastTheCapIsRefused) {
    HostLimits limits;
    limits.output_cap = 1 << 20;
    EXPECT_EQ(ReasonOf(Fake(limits), "FAKE huge"), "outputBound");
}

TEST(IngestHost, ALargeDocumentReachesTheHostWhole) {
    std::string input = "FAKE echo";
    input.resize(5 << 20, 'p');
    const auto pages = Fake().Extract(Bytes(input));
    ASSERT_EQ(pages.size(), 1u);
    ASSERT_EQ(pages[0].lines.size(), 1u);
    EXPECT_EQ(pages[0].lines[0].text, std::to_string(5 << 20) + " bytes");
}

TEST(IngestHost, TheRealHostReadsAPdf) {
    const IngestHost host(CLINICAVT_INGEST_HOST);
    const auto pages = host.Extract(fixture::TinyPdf(
        {"Offer allopurinol after a first attack.", "Check urate six weeks later."}));
    ASSERT_EQ(pages.size(), 1u);
    EXPECT_NEAR(pages[0].width, 595, 0.5);
    EXPECT_EQ(pages[0].rotation, 0);
    ASSERT_EQ(pages[0].lines.size(), 2u);
    EXPECT_EQ(pages[0].lines[0].text, "Offer allopurinol after a first attack.");
    EXPECT_EQ(pages[0].lines[1].text, "Check urate six weeks later.");
    EXPECT_LT(pages[0].lines[0].box.top, pages[0].lines[1].box.top);
    EXPECT_NEAR(pages[0].lines[0].box.left, 72.0F / 595, 0.01);
    EXPECT_EQ(ReasonOf(host, "not a pdf"), "cannotOpen");
}

TEST(IngestHost, SpacesAroundALineShapeNeitherItsBoxNorItsText) {
    const IngestHost host(CLINICAVT_INGEST_HOST);
    const auto pages =
        host.Extract(fixture::TinyPdf({"Offer allopurinol.", "   Offer allopurinol.        "}));
    ASSERT_EQ(pages.size(), 1u);
    ASSERT_EQ(pages[0].lines.size(), 2u);
    const auto& plain = pages[0].lines[0].box;
    const auto& spaced = pages[0].lines[1].box;
    EXPECT_EQ(pages[0].lines[1].text, "Offer allopurinol.");
    EXPECT_GT(spaced.left, plain.left);
    EXPECT_NEAR(spaced.right - spaced.left, plain.right - plain.left, 1e-3);
}

TEST(IngestHost, TheRealHostDrawsAPage) {
    const IngestHost host(CLINICAVT_INGEST_HOST);
    const auto pdf = fixture::TinyPdf({"Offer allopurinol after a first attack."});
    const auto bitmap = host.Render(pdf, 0, 72);
    EXPECT_EQ(bitmap.width, 595);
    EXPECT_EQ(bitmap.height, 842);
    ASSERT_EQ(bitmap.bmp.size(), 54u + 595u * 842u * 4u);
    EXPECT_EQ(bitmap.bmp[0], 'B');
    // White paper, ink somewhere in the first line's band
    EXPECT_EQ(bitmap.bmp[54], 0xFF);
    bool ink = false;
    for (std::size_t i = 54; i + 4 <= bitmap.bmp.size(); i += 4) ink = ink || bitmap.bmp[i] < 0x80;
    EXPECT_TRUE(ink);
    try {
        host.Render(pdf, 1, 72);
        FAIL() << "page 1 does not exist";
    } catch (const HostError& e) {
        EXPECT_EQ(e.Reason(), "badPage");
    }
}

}  // namespace
}  // namespace clinicavt::guidance
