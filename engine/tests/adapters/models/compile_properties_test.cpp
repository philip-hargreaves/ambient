#include <gtest/gtest.h>

#include "adapters/models/ov_runtime.hpp"

namespace clinicavt::models {
namespace {

TEST(CompileProperties, TheCacheSitsBesideTheModelAndManifestPropertiesPassThrough) {
    ModelInfo info;
    info.dir = std::filesystem::path("C:/models/nllb");
    info.properties = {
        {"CACHE_MODE", "OPTIMIZE_SIZE"}, {"ACTIVATIONS_SCALE_FACTOR", 32}, {"ENABLE_MMAP", true}};

    const ov::AnyMap map = CompileProperties(info);

    EXPECT_EQ(map.at("CACHE_DIR").as<std::string>(), (info.dir / ".cache").string());
    EXPECT_EQ(map.at("CACHE_MODE").as<std::string>(), "OPTIMIZE_SIZE");
    EXPECT_EQ(map.at("ACTIVATIONS_SCALE_FACTOR").as<std::string>(), "32");
    EXPECT_TRUE(map.at("ENABLE_MMAP").as<bool>());
}

TEST(CompileProperties, NoPropertiesMeansTheCacheAlone) {
    ModelInfo info;
    info.dir = std::filesystem::path("C:/models/vad");
    EXPECT_EQ(CompileProperties(info).size(), 1u);
}

}  // namespace
}  // namespace clinicavt::models
