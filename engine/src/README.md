# The engine

Ports and adapters (ADR-0003): `core/` is pure logic with no runtime dependency, `ports/` the
interfaces it drives, `adapters/` the implementations over OpenVINO, WASAPI, SQLite and the pipe.
Three `*_main.cpp` files are the executables: the engine, the note worker and the document
ingest host. The corpus builder and indexer (`engine/tools/corpus`) and the units tool
(`engine/tools`) are development tools the engine never runs. Speech reaches text one diarised turn at a time:
the diariser finds the turns, the transcriber decodes each turn's own audio, and there is no
other transcription path. Without models the engine runs on scripted stand-ins (CI).

```
core/            header-only, one folder per stage; namespace = folder
  audio/         capture ring, level meter, playback, enrolment, the session controller
  diarisation/   speaker regions, per-turn decode, re-split, role naming, transcript tidy
  note/          the note gate, label and summary scrub
  guidance/      query, ranking and scan; added-document units and page text
  metrics/       counters and throughput
  common/        utf8, version, argv
ports/           the seams of the hexagon, flat: eleven interfaces and the store error type
adapters/        one folder per seam, matching core/ where a stage has one
  audio/ vad/ transcription/ diarisation/ note/ translate/ guidance/ storage/ ipc/ models/
  system/        GPU lease, awake requests, process scan, executable paths
  demo/
```

Tests mirror this tree under `engine/tests/`: `core/<stage>/`, `ports/`, `adapters/<seam>/`,
with stand-in hosts in `support/`, fixtures in `fixtures/` and the evaluation runner in `tools/`.
Test binaries are split by what they need, not by folder: `engine_tests` runs anywhere,
`models_tests` needs staged weights, `gpu_tests` the Intel GPU, `capture_tests` a microphone;
`ctest -LE 'gpu|models|microphone'` is the CPU-only set.
