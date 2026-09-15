#pragma once

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <stdexcept>
#include <string>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

// What the corpus and upload stores share when they load vectors: a refusal
// with its reason, the memory guard and the unit-length check
namespace ambient::guidance {

struct Refused : std::runtime_error {
    using std::runtime_error::runtime_error;
};

inline void Guard(bool ok, const std::string& why) {
    if (!ok) throw Refused(why);
}

inline std::uint64_t AvailablePhysicalMemory() {
    MEMORYSTATUSEX status{};
    status.dwLength = sizeof status;
    return GlobalMemoryStatusEx(&status) ? status.ullAvailPhys : 0;
}

// Before allocating: the matrix plus a citation's worth per row
inline void GuardMemory(std::size_t rows, std::size_t dim, const std::string& what) {
    const auto needed = static_cast<std::uint64_t>(rows) * dim * sizeof(float) + rows * 256;
    Guard(AvailablePhysicalMemory() > needed, what + " too large for the available memory");
}

inline void GuardUnitVectors(const float* matrix, std::size_t rows, std::size_t dim) {
    for (std::size_t r = 0; r < rows; ++r) {
        double norm = 0;
        const float* v = matrix + r * dim;
        for (std::size_t d = 0; d < dim; ++d) norm += static_cast<double>(v[d]) * v[d];
        Guard(std::fabs(norm - 1.0) <= 1e-3, "a vector is not unit length");
    }
}

}  // namespace ambient::guidance
