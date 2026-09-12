#include <gtest/gtest.h>

#include <filesystem>
#include <fstream>
#include <nlohmann/json.hpp>
#include <regex>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>

#include "ports/guidance_retriever.hpp"

namespace ambient::guidance {
namespace {

constexpr const char* kFixtureDir = AMBIENT_GUIDANCE_FIXTURE_DIR;

std::vector<nlohmann::json> ReadLines(const std::string& name) {
    std::ifstream in(std::filesystem::path(kFixtureDir) / name);
    if (!in.is_open()) throw std::runtime_error("missing guidance fixture: " + name);
    std::vector<nlohmann::json> rows;
    for (std::string line; std::getline(in, line);) {
        if (!line.empty()) rows.push_back(nlohmann::json::parse(line));
    }
    return rows;
}

TEST(GuidanceFixture, IdsAreUniqueAndCarryTheirGuidelineCode) {
    const auto corpus = ReadLines("corpus.jsonl");
    ASSERT_GE(corpus.size(), 40u);
    const std::regex shape("fx[0-9]{3}-[0-9]+(_[0-9]+)+");
    std::set<std::string> ids;
    for (const auto& chunk : corpus) {
        const auto id = chunk.at("id").get<std::string>();
        EXPECT_TRUE(std::regex_match(id, shape)) << id;
        EXPECT_EQ(id.substr(0, id.find('-')), chunk.at("code").get<std::string>()) << id;
        EXPECT_TRUE(ids.insert(id).second) << "duplicate " << id;
        EXPECT_FALSE(chunk.at("text").get<std::string>().empty()) << id;
    }
}

TEST(GuidanceFixture, EveryExpectedIdExistsAndTheHardCasesArePresent) {
    std::set<std::string> ids;
    for (const auto& chunk : ReadLines("corpus.jsonl")) ids.insert(chunk.at("id"));
    bool negated = false, non_clinical = false;
    for (const auto& note : ReadLines("notes.jsonl")) {
        for (const auto& id : note.at("expected")) EXPECT_TRUE(ids.count(id)) << id;
        negated |= !note.at("must_not").empty();
        non_clinical |= note.at("expected").empty();
    }
    EXPECT_TRUE(negated) << "a note with a negated finding";
    EXPECT_TRUE(non_clinical) << "a non-clinical text that retrieves nothing";
}

// The port's defaults and nothing else
struct StubRetriever : IGuidanceRetriever {
    Results Search(const std::string&, int) override {
        return {};
    }
    std::vector<Corpus> Corpora() override {
        return {};
    }
};

TEST(GuidancePort, DocumentCallsAreNotSupportedYet) {
    StubRetriever stub;
    EXPECT_THROW(stub.AddDocument("C:/somewhere/guideline.pdf"), std::logic_error);
    EXPECT_THROW(stub.RemoveDocument("uploads"), std::logic_error);
}

}  // namespace
}  // namespace ambient::guidance
