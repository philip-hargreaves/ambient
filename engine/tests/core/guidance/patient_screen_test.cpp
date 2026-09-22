#include "core/guidance/patient_screen.hpp"

#include <gtest/gtest.h>

namespace ambient::guidance {
namespace {

TEST(PatientScreen, FindsAnNhsNumberByItsCheckDigit) {
    EXPECT_TRUE(LooksLikePatientData("Patient 943 476 5919 attended today."));
    EXPECT_TRUE(LooksLikePatientData("ref 9434765919"));
    EXPECT_FALSE(LooksLikePatientData("ref 9434765918")) << "the check digit fails";
    EXPECT_FALSE(LooksLikePatientData("a nurse led Helpline (0808 800035)."))
        << "ten digits spaced as a phone number, check digit holding by chance";
    EXPECT_FALSE(LooksLikePatientData("ISBN 978 0 19 923 5"));
    EXPECT_FALSE(LooksLikePatientData("GRADE 1A, SOA 100%, 2017, doi 10.1093/rheumatology/kex250"));
}

TEST(PatientScreen, FindsLetterAndRecordPhrasesAndNothingInAGuideline) {
    EXPECT_TRUE(LooksLikePatientData("Dear Dr Smith, thank you for seeing this man."));
    EXPECT_TRUE(LooksLikePatientData("DISCHARGE SUMMARY\nWard 4"));
    EXPECT_TRUE(LooksLikePatientData("DOB: 12/03/1961"));
    EXPECT_TRUE(
        LooksLikePatientData("Ambient export - not for the guidelines folder.\n\nPlan: ..."))
        << "the app's own exports are refused if filed as guidance";
    EXPECT_FALSE(LooksLikePatientData(
        "We recommend initiation of low-dose steroid therapy with gradually tailored tapering in "
        "straightforward PMR (B). Daily prednisolone 15 mg for 3 weeks."));
}

}  // namespace
}  // namespace ambient::guidance
