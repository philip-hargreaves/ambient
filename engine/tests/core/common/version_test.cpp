#include "core/common/version.hpp"

#include <gtest/gtest.h>

TEST(Version, NameIsSet) {
    EXPECT_STREQ(clinicavt::kName, "clinicavt");
}

TEST(Version, MatchesCMakeProjectVersion) {
    EXPECT_STREQ(clinicavt::kVersion, CLINICAVT_CMAKE_VERSION);
}
