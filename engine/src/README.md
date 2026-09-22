# The engine

Ports and adapters (ADR-0003): `core/` is pure logic with no runtime dependency, `ports/` the
interfaces it drives, `adapters/` the implementations over OpenVINO, WASAPI, SQLite and the pipe.
Five `*_main.cpp` files are the executables: the engine, the note worker, the document ingest
host, the corpus indexer and the units tool.

```
core/            header-only, one folder per stage; namespace = folder
  audio/         capture ring, endpointing, level meter, playback, the session controller
  transcription/ turn assembly from decoded windows
  diarisation/   speaker regions, per-turn refinement, role naming, transcript tidy
  note/          the note gate, label and summary scrub
  guidance/      query, ranking and scan; added-document units and page text
  metrics/       counters and throughput
  common/        utf8, env flags, version, argv
ports/           the seams of the hexagon, flat: eleven interfaces and the store error type
adapters/        one folder per seam, matching core/ where a stage has one
  audio/ vad/ transcription/ diarisation/ note/ translate/ guidance/ storage/ ipc/ models/
  system/        GPU lease, power requests, process scan
  demo/
```

Tests mirror this tree under `engine/tests/`: `core/<stage>/`, `ports/`, `adapters/<seam>/`,
with stand-in hosts in `support/`, fixtures in `fixtures/` and the evaluation runner in `tools/`.
Test binaries are split by what they need, not by folder: `engine_tests` runs anywhere,
`models_tests` needs staged weights, `gpu_tests` the Intel GPU, `capture_tests` a microphone;
`ctest -LE 'gpu|models|microphone'` is the CPU-only set.
