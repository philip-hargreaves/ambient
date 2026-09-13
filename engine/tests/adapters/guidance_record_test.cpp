#include "adapters/guidance/guidance_record.hpp"

#include <gtest/gtest.h>

namespace ambient::guidance {
namespace {

Results Sample() {
    Results out;
    out.considered = 42;
    out.floor = 0.85;
    out.abstained = false;

    Corpus corpus;
    corpus.id = "nice-2026-08-25";
    corpus.name = "NICE guidance";
    corpus.licence = "OGL v3.0";
    corpus.attribution = "Contains public sector information";
    corpus.source = "nice";
    corpus.embedder = "gte-large-int8";
    corpus.sha256 = "e4f1be59be8647759ccd16d916ba9504b464f39a2799cb598ca5f0e4dc779a9f";
    corpus.chunks = 22991;
    corpus.built_at = "2026-08-25T00:00:00Z";
    out.searched.push_back(corpus);

    Result full;
    full.corpus = "nice-2026-08-25";
    full.chunk_id = "ng100-1_1_1";
    full.code = "ng100";
    full.number = "1.1.1";
    full.title = "Rheumatoid arthritis in adults: management";
    full.section = "1.1 Referral";
    full.text = "Refer for specialist opinion any adult with suspected persistent synovitis.";
    full.url = "https://www.nice.org.uk/guidance/ng100";
    full.last_updated = "2020-07-01";
    full.update_tag = "2020";
    full.source = "nice";
    full.citation = "NG100 1.1.1, Rheumatoid arthritis in adults: management";
    full.score = 0.8971234;  // rounded to 3 dp on the wire
    full.trigger = "Synovitis of the small joints of both hands.";
    out.shown.push_back(full);

    Result sparse;  // the optional fields empty, to exercise the null round-trip
    sparse.corpus = "nice-2026-08-25";
    sparse.chunk_id = "ng100-1_2_1";
    sparse.code = "ng100";
    sparse.number = "1.2.1";
    sparse.title = "Rheumatoid arthritis in adults: management";
    sparse.section = "1.2 Investigations";
    sparse.text = "Offer a full blood count.";
    sparse.source = "nice";
    sparse.citation = "NG100 1.2.1, Rheumatoid arthritis in adults: management";
    sparse.score = 0.861;
    sparse.trigger = "Bloods requested.";
    out.shown.push_back(sparse);
    return out;
}

TEST(GuidanceRecord, ReadingBackAndWritingAgainChangesNothing) {
    const json wire = ToJson(Sample());
    EXPECT_EQ(wire["version"], kRecordVersion);
    const Results back = FromJson(wire);
    EXPECT_EQ(ToJson(back), wire) << wire.dump(2);
}

TEST(GuidanceRecord, EmptyFieldsStayEmptyStrings) {
    const json wire = ToJson(Sample());
    EXPECT_EQ(wire["shown"][1]["url"], "");
    EXPECT_EQ(wire["shown"][1]["updateTag"], "");
    EXPECT_TRUE(wire["searched"][0]["unavailable"].is_null());
    EXPECT_EQ(FromJson(wire).shown[1].url, "");
}

TEST(GuidanceRecord, RoundsTheScore) {
    EXPECT_EQ(FromJson(ToJson(Sample())).shown[0].score, 0.897);
}

TEST(GuidanceRecord, CarriesTheNoteRevision) {
    const Record record{Sample(), 7};
    const json wire = ToJson(record);
    EXPECT_EQ(wire["noteRevision"], 7);
    const Record back = RecordFromJson(wire);
    EXPECT_EQ(back.note_revision, 7);
    EXPECT_EQ(ToJson(back), wire);
}

TEST(GuidanceRecord, CarriesTheSearchedCorpusAndFloor) {
    const Results back = FromJson(ToJson(Sample()));
    EXPECT_EQ(back.floor, 0.85);
    EXPECT_EQ(back.considered, 42);
    ASSERT_EQ(back.searched.size(), 1u);
    EXPECT_EQ(back.searched[0].id, "nice-2026-08-25");
    EXPECT_EQ(back.searched[0].chunks, 22991);
    EXPECT_TRUE(back.searched[0].unavailable.empty());
}

}  // namespace
}  // namespace ambient::guidance
