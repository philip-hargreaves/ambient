#pragma once

// The ingest host's exit codes, read back by the engine as refusal reasons,
// so the numbers are fixed on both sides of the pipe
namespace clinicavt::guidance::ingest_exit {

inline constexpr int kOk = 0;
inline constexpr int kBadArgs = 1;
inline constexpr int kCannotOpen = 2;
inline constexpr int kPassword = 3;
inline constexpr int kOutputBound = 4;
inline constexpr int kBadPage = 6;

}  // namespace clinicavt::guidance::ingest_exit
