"""Scores every sheet translation with reference-free COMET, a second opinion beside the judge.

    python comet_qe.py

Writes results/comet/<system>.jsonl, then prints a table. The model is wmt20-comet-qe-da. It
scores each sentence pair from the source alone, so it cannot see whether a translation is
complete, and its scale differs by language. Compare systems within a language only.
"""

import json
import os
import socket
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np

ROOT = Path(os.environ.get("MT_ROOT", r"D:\ambient-mt"))
# Assembled by hand, since the HF cache's symlinks fail on exFAT
CHECKPOINT = ROOT / "models-eval" / "wmt20-comet-qe-da" / "checkpoints" / "model.ckpt"
REFERENCE = "nllb-600m-int8"
BOOTSTRAP = 2000

# IPv6 drops on this network
_lookup = socket.getaddrinfo
socket.getaddrinfo = lambda host, port, family=0, *rest, **more: _lookup(
    host, port, socket.AF_INET, *rest, **more)
os.environ.setdefault("HF_HOME", str(ROOT / "hf-cache"))
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


def score_all():
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from translate import sentences
    from comet import load_from_checkpoint
    model = load_from_checkpoint(str(CHECKPOINT))
    out_dir = ROOT / "results" / "comet"
    out_dir.mkdir(parents=True, exist_ok=True)
    for path in sorted((ROOT / "results" / "sheets").glob("*.jsonl")):
        target = out_dir / path.name
        if target.exists():
            continue
        rows = [json.loads(line) for line in open(path, encoding="utf-8")]
        pairs, owners = [], []
        for i, row in enumerate(rows):
            src, mt = sentences(row["source"]), sentences(row["translation"])
            # Counts differ when a model merges or loops. The integrity check's length ratio covers that
            for s, m in zip(src, mt):
                pairs.append({"src": s, "mt": m})
                owners.append(i)
        scores = model.predict(pairs, batch_size=16, gpus=0, progress_bar=False).scores
        per_row = defaultdict(list)
        for owner, s in zip(owners, scores):
            per_row[owner].append(s)
        with open(target, "w", encoding="utf-8") as f:
            for i, row in enumerate(rows):
                f.write(json.dumps({"id": row["id"], "language": row["language"],
                                    "comet": float(np.mean(per_row[i])) if per_row[i] else None}) + "\n")
        print("scored", path.stem, flush=True)


def table():
    rows = defaultdict(dict)
    for path in sorted((ROOT / "results" / "comet").glob("*.jsonl")):
        for r in map(json.loads, open(path, encoding="utf-8")):
            if r["comet"] is not None:
                rows[(path.stem, r["language"])][r["id"]] = r["comet"]
    rng = np.random.default_rng(7)
    print(f"{'system':<20} {'language':<10}  COMET-QE   n | minus {REFERENCE} (95%)")
    for (system, language), by_id in sorted(rows.items()):
        base = rows.get((REFERENCE, language), {})
        shared = sorted(set(by_id) & set(base))
        interval = ""
        if system != REFERENCE and shared:
            delta = np.array([by_id[i] - base[i] for i in shared])
            draws = rng.integers(0, len(shared), size=(BOOTSTRAP, len(shared)))
            low, high = np.percentile(delta[draws].mean(axis=1), [2.5, 97.5])
            interval = f"{low:+.3f} to {high:+.3f}"
        mean = np.mean(list(by_id.values()))
        print(f"{system:<20} {language:<10}  {mean:8.3f}  {len(by_id):3d} | {interval}")


if __name__ == "__main__":
    score_all()
    table()
