#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace ambient::diar {

// Below this lexical margin no one is named. Chosen from the observed
// margin range without a sweep. Revisit first if it abstains too often
inline constexpr double kRoleMinMargin = 0.5;

// Below this the stored print is someone else's and the content decides
// instead. Measured on the 57 with an enrolled print: the enrolled clinician's
// own consultations scored 0.84 and up, every other clinician 0.75 and below
inline constexpr double kAnchorMinSimilarity = 0.80;

struct RoleTurn {
    int cluster = 0;
    std::uint64_t frame_count = 0;
    std::string text;
};

struct RoleResult {
    std::vector<std::string> role_of_cluster;  // doctor | patient | speaker N | unknown
    int doctor_cluster = -1;                   // -1: abstained
    int patient_cluster = -1;
    double margin = 0.0;
    bool from_anchor = false;  // the print decided, else the content did or abstained
};

// Cold-start scorer without question features: they invert where the patient
// asks the questions (measured 6/6 -> 0/6)
double LexicalDoctorScore(const std::string& text);

// The two dominant clusters are the candidates. Anchor rank names the
// doctor, else lexical score with abstention, since a confident inversion
// is the one failure that corrupts the record
RoleResult NameRoles(const std::vector<RoleTurn>& turns, int cluster_count,
                     const std::vector<double>& anchor_similarity = {});

}  // namespace ambient::diar
