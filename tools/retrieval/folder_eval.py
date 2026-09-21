"""Scores the guidance tab on the St George's cases against the clinician's own folder, through
the engine the app ships: each case is searched as a note and every card is checked against
the documents the gold names as `expected_documents`.

    python tools/retrieval/folder_eval.py [--tag before]

Close the app first. Writes build/retrieval/folder-<date>-<tag>.jsonl with every card, and prints
the table: hit@1 and hit@3 by document, cards under the prose floor, abstentions.
"""

import json
import os
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools", "demo"))
import record_masters as rm  # noqa: E402

GOLD = os.path.join(ROOT, "rag", "gold", "st-georges-cases", "cases.jsonl")


def wait_for_index(engine, minutes=20):
    """Waits until every document in the folder is indexed: a chunker or embedder change
    rebuilds the whole index at startup, and searching before that reads a partial one."""
    settled = 0
    for _ in range(minutes * 12):
        docs = engine.request("guidance/documents", None, 30).get("documents", [])
        if docs and all(d.get("state") in ("ready", "failed", "unsupported") for d in docs):
            settled += 1
            if settled >= 3:
                return docs
        else:
            settled = 0
        time.sleep(5)
    raise TimeoutError("the folder index did not settle")


def main():
    tag = sys.argv[sys.argv.index("--tag") + 1] if "--tag" in sys.argv else "run"
    rm.LOG = os.path.join(ROOT, "build", f"folder-eval-{tag}.log")
    cases = [json.loads(l) for l in open(GOLD, encoding="utf-8")]
    engine = rm.Engine()
    rows = []
    try:
        for _ in range(600):
            try:
                engine.request("engine/echo", {"payload": "up"}, 2)
                break
            except (TimeoutError, RuntimeError):
                time.sleep(0.1)
        for _ in range(240):
            msg = engine.wait_for({"guidance/model"}, 1)
            if msg and msg["params"].get("phase") == "ready":
                break
        docs = wait_for_index(engine)
        rm.log(f"{len(docs)} documents indexed, {sum(d.get('chunks', 0) for d in docs)} units")
        for case in cases:
            engine.request("guidance/search", {"text": case["text"], "limit": 3})
            msg = engine.wait_for({"guidance/ready", "guidance/failed"}, 120)
            shown = (msg or {}).get("params", {}).get("shown", [])
            expected = case.get("expected_documents", [])
            titles = [c.get("title", "") for c in shown]
            row = {
                "qid": case["qid"],
                "expected": expected,
                "cards": [{"title": c.get("title"), "page": c.get("page", 0) + 1, "score": c.get("score"),
                           "words": len((c.get("text") or "").split()), "text": c.get("text")} for c in shown],
                "hit1": bool(titles) and titles[0] in expected,
                "hit3": any(t in expected for t in titles),
                "abstained": not shown,
            }
            rows.append(row)
            rm.log(f"{case['qid']}: hit@1 {row['hit1']}, hit@3 {row['hit3']}, "
                   f"cards {[(t, c['page']) for t, c in zip(titles, row['cards'])]}")
    finally:
        engine.close()
    out_dir = os.path.join(ROOT, "build", "retrieval")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, f"folder-{time.strftime('%Y%m%d')}-{tag}.jsonl")
    with open(out, "w", encoding="utf-8") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    scored = [r for r in rows if r["expected"]]
    short = sum(1 for r in rows for c in r["cards"] if c["words"] < 25)
    rm.log(f"hit@1 {sum(r['hit1'] for r in scored)}/{len(scored)}, hit@3 {sum(r['hit3'] for r in scored)}/{len(scored)}, "
           f"cards under 25 words {short}, abstained {sum(r['abstained'] for r in rows)}; {out}")


if __name__ == "__main__":
    main()
