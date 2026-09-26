# translation

The study that chose the translation model and the measurements behind the translator's lifecycle. Data, models and results live under `MT_ROOT` (default `D:\clinicavt-mt`).

## Setup

```
py -3.12 -m venv %MT_ROOT%\venv
%MT_ROOT%\venv\Scripts\python -m pip install -r tools\translation\requirements.txt
py -3.12 -m venv %MT_ROOT%\venv-comet
%MT_ROOT%\venv-comet\Scripts\python -m pip install -r tools\translation\requirements-comet.txt
```

Checkpoints download with `local_dir`, so an exFAT drive works. Downloads use IPv4. FLORES-200 and TICO-19 go under `MT_ROOT\data`. The patient sheets are the accuracy tier's in the research repository. `score.py refs` needs `SACREBLEU` set to a folder holding the FLORES-200 SentencePiece model.

## Selection study

```
python export.py <id>                    export a candidate from candidates.json to models/<id>-int8
python translate.py <id> sheets|flores|tico
python qwen_translate.py [--set flores]  the note model as an LLM reference (GPU, app closed)
python score.py refs flores|tico         chrF++ and spBLEU with a paired bootstrap
python score.py integrity                loops, script, numbers and lengths on the sheets
python judge.py plant | validity         validate the judge on planted errors
python judge.py tasks | score | repeat   blind judging of the sheets, scores and a blind repeat
python judge.py llm-tasks | llm-score    NLLB against the note model
python comet_qe.py                       reference-free COMET (venv-comet)
python cost.py                           cold start, seconds per sheet and memory per model
python parity.py                         the app's IR against the study's export
```

Each judge verdict comes from an isolated agent that reads only `judge-prompt.md` and one task file.

## Translator lifecycle

```
python tokenizer_convert.py <model dir> <out dir>        build the shipped tokenizer (venv-comet)
python tokenizer_parity.py <reference dir> <candidate>   first call and ids, sentence by sentence
python plain_punctuation_check.py translate | tasks | score
python lifecycle_probe.py matrix <out.jsonl> <work dir>  cold start, speed and memory per CPU setting
python lifecycle_smoke.py <work dir> [wav]               the lifecycle in a real engine over the pipe
```
