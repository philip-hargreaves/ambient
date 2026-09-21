"""Offline study of guidance retrieval over a clinician's own folder.

  python folder_study.py units      engine chunker over every PDF in the folder -> units.jsonl
  python folder_study.py embed      engine embedder over units (text, and heading + text) and queries; `embed queries` for the queries alone
  python folder_study.py run        every variant over every query -> runs.jsonl, per-sentence votes
  python folder_study.py pool       every card any variant showed for an in-scope note -> pool.txt, to be judged
  python folder_study.py score      variants against rag/gold/folder-notes/judgments.jsonl, and the abstention signal
  python folder_study.py models     embeds units and queries with each shortlisted embedder -> runs-models.jsonl
  python folder_study.py compare    the embedders against each other: ranking with the floor off, and the abstention signal
  python folder_study.py remap OLD  gold moved from an old units.jsonl's numbering to the current one, by text
  python folder_study.py parity     the baseline variant against a folder_eval.py note-mode run

Working data goes to build/retrieval/study, or to build/retrieval/$FOLDER_STUDY for the same
study over a changed folder or chunker. The splitter, the exclusion filter, the vote and
the floor are ports of engine/src/core/guidance_query.hpp and guidance_rank.hpp, and parity
proves the port before any variant is trusted. Run with the harness venv (openvino_genai).
"""

import json
import os
import re
import subprocess
import sys
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[2]
# FOLDER_STUDY names a second working directory, for the same study over a changed folder
STUDY = REPO / "build" / "retrieval" / os.environ.get("FOLDER_STUDY", "study")
FOLDER = Path(os.environ["USERPROFILE"]) / "Documents" / "Ambient guidelines"
UNITS_EXE = REPO / "build" / "release" / "engine" / "ambient_units.exe"
MODEL = REPO / "models" / "gte-large-int8"
GOLD = REPO / "rag" / "gold"

FLOOR = 0.85
UNION = 50
RRF_K = 60.0
LIMIT = 3
DUPLICATE = 0.5
INSIDE = ("cases", "folder-notes")
OUTSIDE = ("gp-notes", "near-misses", "negatives")

ABBREVIATIONS = {"e.g", "i.e", "dr", "mr", "mrs", "ms", "prof", "vs", "approx", "etc", "no", "hx", "pt", "rx",
                 "mg", "mcg", "ml", "kg", "cm", "mm", "bd", "tds", "od", "prn", "st", "ca", "cf", "wk", "wks",
                 "yr", "yrs", "mth"}
OPENINGS = ("no ", "nil ", "not ", "denies", "denied", "never ", "without ", "negative for", "no history",
            "family history", "fh:", "fh ", "fhx", "if ", "unless ", "should she", "should he", "in case")
PHRASES = (" mother had", " father had", " mother has", " father has", " sister had", " brother had",
           "grandmother", "grandfather", "family history of", "no evidence of", "were to develop")


# ---- ports of the engine's query side

def split_sentences(note: str) -> list[str]:
    out, start, i = [], 0, 0

    def flush(a, b):
        piece = note[a:b].strip()
        if len(piece.split()) >= 3:
            out.append(piece)

    while i < len(note):
        c = note[i]
        if c == "\n":
            flush(start, i)
            start = i + 1
        elif c in ".!?":
            j = i + 1
            while j < len(note) and note[j] in " \t\r":
                j += 1
            if j > i + 1 and j < len(note) and (note[j].isupper() or note[j].isdigit() or note[j] in "\"'("):
                token = note[:i].split()[-1].lower() if note[:i].split() else ""
                token = token.rstrip(".")
                abbreviation = c == "." and (token in ABBREVIATIONS or (len(token) == 1 and token.isalpha()))
                if not abbreviation:
                    flush(start, i + 1)
                    start = j
        i += 1
    flush(start, len(note))
    return out


def is_excluded(sentence: str) -> bool:
    s = sentence.strip().lower()
    return s.startswith(OPENINGS) or any(p in s for p in PHRASES)


def sub_queries(note: str) -> list[str]:
    out = [s for s in split_sentences(note) if not is_excluded(s)]
    whole = note.strip()
    if len(whole.split()) >= 3 and whole not in out:
        out.append(whole)
    return out


def content_words(text: str) -> set[str]:
    return {w for w in re.findall(r"[a-z0-9]+", text.lower()) if len(w) > 3}


def near_duplicate(a: str, b: str) -> bool:
    wa, wb = content_words(a), content_words(b)
    smaller = min(len(wa), len(wb))
    return smaller >= 5 and len(wa & wb) / smaller >= DUPLICATE


# ---- data

def load_units() -> list[dict]:
    return [json.loads(line) for line in open(STUDY / "units.jsonl", encoding="utf-8")]


def load_queries() -> list[dict]:
    """In scope: the clinician's four cases. Out of scope: the app's own notes of general-practice
    consultations, which a rheumatology folder does not cover, and the harness negatives."""
    queries = []
    for row in (json.loads(line) for line in open(GOLD / "st-georges-cases" / "cases.jsonl", encoding="utf-8")):
        queries.append({"qid": row["qid"], "set": "cases", "text": row["text"]})
    for row in (json.loads(line) for line in open(GOLD / "folder-notes" / "notes.jsonl", encoding="utf-8")):
        expected = [f"{e['doc']}#{o}" for e in row["expected"] for o in e["ords"]]
        queries.append({"qid": row["qid"], "set": "folder-notes", "text": row["text"], "expected": expected})
    for path in sorted((REPO / "demo" / "reflections").glob("*.json")):
        sample = json.load(open(path, encoding="utf-8"))
        queries.append({"qid": "gp-" + path.stem[:2], "set": "gp-notes", "label": sample["label"], "text": sample["note"]})
    # The hard case: musculoskeletal problems next door to the folder that none of it covers
    for row in (json.loads(line) for line in open(GOLD / "folder-notes" / "near-misses.jsonl", encoding="utf-8")):
        queries.append({"qid": row["qid"], "set": "near-misses", "text": row["text"]})
    for row in (json.loads(line) for line in open(GOLD / "negatives" / "negatives.jsonl", encoding="utf-8")):
        queries.append({"qid": row.get("qid", f"neg-{len(queries)}"), "set": "negatives", "text": row["text"]})
    return queries


# ---- steps

def units():
    STUDY.mkdir(parents=True, exist_ok=True)
    rows = []
    for pdf in sorted(FOLDER.glob("*.pdf")):
        done = subprocess.run([str(UNITS_EXE), str(pdf)], capture_output=True)
        if done.returncode != 0:
            print(f"skipped {pdf.name}: {done.stderr.decode(errors='replace').strip()[:80]}")
            continue
        for ord_, line in enumerate(done.stdout.decode("utf-8").splitlines()):
            unit = json.loads(line)
            rows.append({"id": f"{pdf.stem}#{ord_}", "doc": pdf.stem, "ord": ord_, **unit})
        print(f"{pdf.stem}: {sum(r['doc'] == pdf.stem for r in rows)} units")
    with open(STUDY / "units.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(len(rows), "units")


def pipeline():
    import openvino_genai as ov_genai
    config = ov_genai.TextEmbeddingPipeline.Config()
    config.pooling_type = ov_genai.TextEmbeddingPipeline.PoolingType.MEAN
    config.normalize = True
    config.max_length = 512
    return ov_genai.TextEmbeddingPipeline(str(MODEL), "CPU", config)


def embed():
    pipe = pipeline()
    rows = load_units()

    def documents(texts, name):
        out = []
        for i in range(0, len(texts), 32):
            out.extend(pipe.embed_documents(texts[i:i + 32]))
            if i % 640 == 0:
                print(name, i, "/", len(texts), flush=True)
        np.save(STUDY / f"{name}.npy", np.asarray(out, dtype=np.float32))

    # `embed queries` leaves the passages alone, for a working directory that holds only docs-text
    if "queries" not in sys.argv and not (STUDY / "docs-titled.npy").exists():
        documents([r["text"] for r in rows], "docs-text")
        documents([(r["section"] + ". " if r["section"] else "") + r["text"] for r in rows], "docs-headed")
        documents([r["doc"] + ". " + (r["section"] + ". " if r["section"] else "") + r["text"] for r in rows], "docs-titled")

    texts, index = [], []
    for q in load_queries():
        for sub in sub_queries(q["text"]):
            index.append({"qid": q["qid"], "sub": sub, "whole": sub == q["text"].strip()})
            texts.append(sub)
    # The engine embeds a query and a passage the same way: no instruction, mean pooling
    documents(texts, "queries")
    json.dump(index, open(STUDY / "queries.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(len(texts), "sub-queries")


def rank(variant: dict, lists: list[dict], units_: list[dict], background=None) -> dict:
    """lists: one per sub-query, {'sub', 'whole', 'order': unit indices, 'cos': cosines by unit index}."""
    floor = variant.get("floor", FLOOR)
    tally = {}
    whole = next((e for e in lists if e["whole"]), lists[-1])
    # The note as a whole decides whether the folder covers it at all
    if variant.get("note_gate") and float(whole["cos"].max()) < variant.get("note_floor", floor):
        return {"considered": 0, "shown": [], "note_best": round(float(whole["cos"].max()), 4)}
    neighbourhood = set(int(u) for u in np.argsort(-whole["cos"])[:UNION]) if variant.get("within_note") else None
    for entry in lists:
        if variant.get("whole_only") and not entry["whole"]:
            continue
        if variant.get("sentences_only") and entry["whole"] and len(lists) > 1:
            continue
        cos = entry["cos"]
        adjusted = cos - background if background is not None and variant.get("csls") else cos
        order = np.argsort(-adjusted)[:variant.get("union", UNION)]
        for position, u in enumerate(order):
            u = int(u)
            if neighbourhood is not None and u not in neighbourhood:
                continue  # sentences only reorder what the whole note already found
            if variant.get("gate") and cos[u] < floor and not (entry["whole"] and variant.get("note_votes")):
                continue  # a sentence votes only for what it actually resembles
            t = tally.setdefault(u, {"score": 0.0, "cos": -1.0, "best": 10 ** 9, "trigger": "", "votes": 0})
            weight = variant.get("note_weight", 1) if entry["whole"] else 1
            if variant.get("order") == "vote":
                t["score"] += weight / (RRF_K + position + 1.0)
            t["votes"] += 1
            if cos[u] > t["cos"]:
                t["cos"] = float(cos[u])
            if position < t["best"]:
                t["best"], t["trigger"] = position, "" if entry["whole"] else entry["sub"]
    for t in tally.values():
        if variant.get("order") != "vote":
            t["score"] = t["cos"]
    ranked = sorted(tally.items(), key=lambda kv: (-kv[1]["score"], -kv[1]["cos"]))[:UNION]
    kept = [(u, t) for u, t in ranked if t["cos"] >= floor]
    shown = []
    for u, t in kept:
        if len(shown) >= LIMIT:
            break
        if variant.get("dedupe", True) and any(near_duplicate(units_[s["unit"]]["text"], units_[u]["text"]) for s in shown):
            continue
        shown.append({"unit": u, "id": units_[u]["id"], "doc": units_[u]["doc"], "page": units_[u]["page"] + 1,
                      "section": units_[u]["section"], "cos": round(t["cos"], 4), "score": round(t["score"], 5),
                      "votes": t["votes"], "trigger": t["trigger"]})
    return {"considered": len(ranked), "shown": shown, "note_best": round(float(whole["cos"].max()), 4),
            "any_best": round(max((float(e["cos"].max()) for e in lists)), 4)}


VARIANTS = [
    {"name": "V0 engine: vote over sentences and note", "order": "vote", "docs": "docs-text"},
    {"name": "V1 whole note only", "order": "cosine", "whole_only": True, "docs": "docs-text"},
    {"name": "V2 best similarity over sentences and note", "order": "cosine", "docs": "docs-text"},
    {"name": "V3 vote, gated at the floor", "order": "vote", "gate": True, "docs": "docs-text"},
    {"name": "V4 vote, union of 10", "order": "vote", "union": 10, "docs": "docs-text"},
    {"name": "V5 vote, heading embedded with passage", "order": "vote", "docs": "docs-headed"},
    {"name": "V6 vote, title and heading embedded", "order": "vote", "docs": "docs-titled"},
    {"name": "V7 vote, hub-corrected similarity", "order": "vote", "csls": True, "docs": "docs-text"},
    {"name": "V8 gated vote, heading embedded", "order": "vote", "gate": True, "docs": "docs-headed"},
    {"name": "V9 best similarity, heading embedded", "order": "cosine", "docs": "docs-headed"},
    {"name": "V10 vote, but silent unless the whole note clears the floor", "order": "vote", "note_gate": True, "docs": "docs-text"},
    {"name": "V11 vote within the whole note's top 50", "order": "vote", "within_note": True, "note_gate": True, "docs": "docs-text"},
    {"name": "V12 best similarity within the whole note's top 50", "order": "cosine", "within_note": True, "note_gate": True, "docs": "docs-text"},
    {"name": "V13 gated vote, silent unless the whole note clears the floor", "order": "vote", "gate": True, "note_gate": True, "docs": "docs-text"},
    {"name": "V14 as V13 with the note floor at 0.84", "order": "vote", "gate": True, "note_gate": True, "note_floor": 0.84, "docs": "docs-text"},
    {"name": "V15 as V10 with the note floor at 0.84", "order": "vote", "note_gate": True, "note_floor": 0.84, "docs": "docs-text"},
    {"name": "V16 as V13, the whole note always votes", "order": "vote", "gate": True, "note_votes": True, "note_gate": True, "docs": "docs-text"},
    {"name": "V17 as V16 with the note floor at 0.84", "order": "vote", "gate": True, "note_votes": True, "note_gate": True, "note_floor": 0.84, "docs": "docs-text"},
    {"name": "V18 as V16, note weight 3", "order": "vote", "gate": True, "note_votes": True, "note_gate": True, "note_weight": 3, "docs": "docs-text"},
    {"name": "V19 as V17, note weight 3", "order": "vote", "gate": True, "note_votes": True, "note_gate": True, "note_floor": 0.84, "note_weight": 3, "docs": "docs-text"},
]


def run():
    units_ = load_units()
    queries = load_queries()
    index = json.load(open(STUDY / "queries.json", encoding="utf-8"))
    qvec = np.load(STUDY / "queries.npy")
    variants = [v for v in VARIANTS if (STUDY / f"{v['docs']}.npy").exists()]
    docs = {name: np.load(STUDY / f"{name}.npy") for name in {v["docs"] for v in variants}}
    by_query = {}
    for i, entry in enumerate(index):
        by_query.setdefault(entry["qid"], []).append((i, entry))

    # Hub correction: a unit's mean similarity to every sub-query of every other note
    background = {name: (qvec @ matrix.T).mean(axis=0) for name, matrix in docs.items()}

    rows, occurrence = [], {name: np.zeros(len(units_), dtype=int) for name in docs}
    for q in queries:
        for v in variants:
            matrix = docs[v["docs"]]
            lists = []
            for i, entry in by_query[q["qid"]]:
                cos = matrix @ qvec[i]
                lists.append({"sub": entry["sub"], "whole": entry["whole"], "cos": cos})
                if v is VARIANTS[0] or (v["docs"] != "docs-text" and v["name"].startswith("V5")):
                    occurrence[v["docs"]][np.argsort(-cos)[:10]] += 1
            result = rank(v, lists, units_, background[v["docs"]])
            rows.append({"qid": q["qid"], "set": q["set"], "variant": v["name"],
                         "expected": q.get("expected", []), **result})
    with open(STUDY / "runs.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    np.save(STUDY / "occurrence-text.npy", occurrence["docs-text"])
    if "docs-headed" in occurrence:
        np.save(STUDY / "occurrence-headed.npy", occurrence["docs-headed"])
    print(len(rows), "runs over", len(queries), "queries and", len(variants), "variants")


def remap(old_units: str):
    """After a chunker change renumbers the units: moves the expected ordinals of notes.jsonl and
    the units of judgments.jsonl from the old numbering to the new by their text. A unit the
    chunker no longer produces keeps its old id in the judgments, where it can no longer be
    shown, and is reported when a note expected it."""
    old = {u["id"]: u for u in (json.loads(line) for line in open(old_units, encoding="utf-8"))}
    by_text, by_head = {}, {}
    for u in load_units():
        by_text[(u["doc"], u["text"])] = u
        by_head.setdefault((u["doc"], u["text"][:60]), u)

    def moved(unit_id):
        was = old.get(unit_id)
        if was is None:
            return None
        now = by_text.get((was["doc"], was["text"])) or by_head.get((was["doc"], was["text"][:60]))
        return now["id"] if now else None

    notes_path, lost = GOLD / "folder-notes" / "notes.jsonl", 0
    notes = [json.loads(line) for line in open(notes_path, encoding="utf-8")]
    for note in notes:
        for entry in note["expected"]:
            ords = []
            for o in entry["ords"]:
                now = moved(f"{entry['doc']}#{o}")
                if now is None:
                    print(note["qid"], "expected unit is gone:", f"{entry['doc']}#{o}")
                    lost += 1
                else:
                    ords.append(int(now.rsplit("#", 1)[1]))
            entry["ords"] = ords
    with open(notes_path, "w", encoding="utf-8", newline="\n") as f:
        for note in notes:
            f.write(json.dumps(note, ensure_ascii=False) + "\n")

    path, kept, gone = GOLD / "folder-notes" / "judgments.jsonl", 0, 0
    rows = [json.loads(line) for line in open(path, encoding="utf-8")]
    for row in rows:
        now = moved(row["unit"])
        if now is None and row["unit"] in old:
            gone += 1
            row["unit"] = "gone:" + row["unit"]
        elif now is not None:
            kept += 1
            row["unit"] = now
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(f"judgments: {kept} moved, {gone} on units that are gone; expected units lost: {lost}")


def check():
    """Refuses a folder note that shares a four-word sequence with a unit it expects, and an
    expected unit that does not exist."""
    by_id = {u["id"]: u for u in load_units()}

    def grams(text):
        t = re.findall(r"[a-z0-9]+", text.lower())
        return {tuple(t[i:i + 4]) for i in range(len(t) - 3)}

    bad = 0
    for q in load_queries():
        for unit_id in q.get("expected", []):
            if unit_id not in by_id:
                print(q["qid"], "expects a unit that does not exist:", unit_id)
                bad += 1
                continue
            shared = grams(q["text"]) & grams(by_id[unit_id]["text"])
            if shared:
                print(q["qid"], "echoes", unit_id, ":", " ".join(sorted(shared)[0]))
                bad += 1
    print("clean" if not bad else f"{bad} problems")


def load_runs(name="runs.jsonl"):
    return [json.loads(line) for line in open(STUDY / name, encoding="utf-8")]


def load_judgments() -> dict:
    path = GOLD / "folder-notes" / "judgments.jsonl"
    if not path.exists():
        return {}
    return {(j["qid"], j["unit"]): j["label"] for j in (json.loads(line) for line in open(path, encoding="utf-8"))}


def pool(name="runs.jsonl"):
    """Cards not yet judged, pooled over every variant and shuffled within a note so the judge
    cannot tell which variant showed a card or where it ranked."""
    import random
    by_id = {u["id"]: u for u in load_units()}
    judged = load_judgments()
    cards = sorted({(r["qid"], c["id"]) for r in load_runs(name) if r["set"] in INSIDE
                    for c in r["shown"]} - set(judged))
    random.Random(7).shuffle(cards)
    cards.sort(key=lambda card: card[0])
    json.dump([{"qid": q, "unit": u} for q, u in cards], open(STUDY / "pool.json", "w", encoding="utf-8"), indent=0)
    with open(STUDY / "pool.txt", "w", encoding="utf-8", newline="\n") as f:
        for i, (q, u) in enumerate(cards):
            f.write(f"{i} {q} | {by_id[u]['doc']} p{by_id[u]['page'] + 1} | {by_id[u]['text']}\n")
    print(len(cards), "cards to judge")


def auc(positive: list[float], negative: list[float]) -> float:
    wins = sum((p > n) + 0.5 * (p == n) for p in positive for n in negative)
    return wins / (len(positive) * len(negative))


def score(name="runs.jsonl"):
    """Graded judgments: 2 the guidance the note calls for, 1 on the topic, 0 neither. nDCG@3 is
    against the best three judged cards for the note, so a silent variant scores zero on it."""
    import math
    judged = load_judgments()
    runs = load_runs(name)
    # The best a note could be shown is judged over this folder's units only
    present = {u["id"] for u in load_units()}
    ideal = {}
    for (qid, unit), label in judged.items():
        if unit in present:
            ideal.setdefault(qid, []).append(label)

    def dcg(labels):
        return sum((2 ** g - 1) / math.log2(i + 2) for i, g in enumerate(labels))

    print(f"{'variant':<62} ndcg3  p3   first2 first0 quiet | silent gp  near  neg | unjudged | ndcg3 - V0 (95%)")
    rng, baseline = np.random.default_rng(7), None
    for variant in dict.fromkeys(r["variant"] for r in runs):
        rows = [r for r in runs if r["variant"] == variant]
        scope = [r for r in rows if r["set"] in INSIDE]
        ndcg, precision, first2, first0, quiet, unjudged = [], [], 0, 0, 0, 0
        for r in scope:
            labels = [judged.get((r["qid"], c["id"])) for c in r["shown"]]
            unjudged += labels.count(None)
            labels = [g or 0 for g in labels]
            best = dcg(sorted(ideal.get(r["qid"], [0]), reverse=True)[:LIMIT])
            ndcg.append(dcg(labels) / best if best else 0.0)
            if labels:
                precision.append(sum(g > 0 for g in labels) / len(labels))
                first2 += labels[0] == 2
                first0 += labels[0] == 0
            else:
                quiet += 1
        silent = {s: (sum(not r["shown"] for r in rows if r["set"] == s), sum(r["set"] == s for r in rows))
                  for s in OUTSIDE}
        n = len(scope)
        ndcg = np.asarray(ndcg)
        baseline = ndcg if baseline is None else baseline
        draws = rng.integers(0, n, size=(5000, n))
        low, high = np.percentile((ndcg - baseline)[draws].mean(axis=1), [2.5, 97.5])
        print(f"{variant[:62]:<62} {ndcg.mean():.3f}  {np.mean(precision):.2f} {first2:>3}/{n} {first0:>3}/{n} {quiet:>3}/{n} |"
              f"  {'  '.join(f'{a}/{b}' for a, b in silent.values())} | {unjudged}"
              f" | {low:+.3f} to {high:+.3f}")

    base = [r for r in runs if r["variant"] == runs[0]["variant"]]
    if not any(r.get("note_best") for r in base):
        return  # an engine run carries cards only
    inside = [r for r in base if r["set"] in INSIDE]
    outside = [r for r in base if r["set"] in OUTSIDE]
    for key, label in (("note_best", "whole note"), ("any_best", "best sentence")):
        by_set = ", ".join(f"{s} {auc([r[key] for r in inside], [r[key] for r in outside if r['set'] == s]):.3f}"
                           for s in OUTSIDE)
        print(f"\nabstention signal, {label}: AUC {auc([r[key] for r in inside], [r[key] for r in outside]):.3f} ({by_set})")
        for floor in (0.83, 0.84, 0.85, 0.86, 0.87, 0.88):
            kept = sum(r[key] >= floor for r in inside)
            silent = sum(r[key] < floor for r in outside)
            print(f"   floor {floor:.2f}: answers {kept}/{len(inside)} in scope, silent on {silent}/{len(outside)} out of scope")


# The selection study's first-stage finalists, configured as tools/retrieval/shortlist.json has them
CANDIDATES = Path(os.environ.get("RETRIEVAL_CANDIDATES", r"D:\ambient-rag\candidates"))
EMBEDDERS = ["gte-large", "bge-large", "arctic-l-v2", "bge-base"]


def models():
    import openvino_genai as ov_genai
    shortlist = {e["id"]: e for e in json.load(open(Path(__file__).with_name("shortlist.json"), encoding="utf-8"))}
    rows = load_units()
    index = json.load(open(STUDY / "queries.json", encoding="utf-8"))
    for name in EMBEDDERS:
        docs_path, queries_path = STUDY / f"docs-{name}.npy", STUDY / f"queries-{name}.npy"
        if docs_path.exists() and queries_path.exists() and len(np.load(queries_path)) == len(index):
            continue
        entry = shortlist[name]
        config = ov_genai.TextEmbeddingPipeline.Config()
        config.pooling_type = getattr(ov_genai.TextEmbeddingPipeline.PoolingType, entry["pooling"].upper())
        config.normalize = True
        config.max_length = 512
        if entry.get("query_instruction"):
            config.query_instruction = entry["query_instruction"]
        pipe = ov_genai.TextEmbeddingPipeline(str(CANDIDATES / f"{name}-int8"), "CPU", config)
        texts, out = [r["text"] for r in rows], []
        for i in range(0, 0 if docs_path.exists() else len(texts), 32):
            out.extend(pipe.embed_documents(texts[i:i + 32]))
            if i % 640 == 0:
                print(name, i, "/", len(texts), flush=True)
        if out:
            np.save(docs_path, np.asarray(out, dtype=np.float32))
        np.save(queries_path, np.asarray([pipe.embed_query(e["sub"]) for e in index], dtype=np.float32))
        del pipe

    # A floor belongs to one model's similarity scale, so the embedders are ranked with it off
    units_, queries = rows, load_queries()
    by_query = {}
    for i, entry in enumerate(index):
        by_query.setdefault(entry["qid"], []).append((i, entry))
    out = []
    for name in EMBEDDERS:
        matrix, qvec = np.load(STUDY / f"docs-{name}.npy"), np.load(STUDY / f"queries-{name}.npy")
        for q in queries:
            lists = [{"sub": e["sub"], "whole": e["whole"], "cos": matrix @ qvec[i]} for i, e in by_query[q["qid"]]]
            whole = next((e for e in lists if e["whole"]), lists[-1])
            for label, variant in (("vote", {"order": "vote", "floor": -1.0}),
                                   ("whole note", {"order": "cosine", "whole_only": True, "floor": -1.0})):
                result = rank(variant, lists, units_)
                result["top10"] = [units_[int(u)]["id"] for u in np.argsort(-whole["cos"])[:10]]
                out.append({"qid": q["qid"], "set": q["set"], "variant": f"{name}, {label}",
                            "expected": q.get("expected", []), **result})
    with open(STUDY / "runs-models.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for row in out:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(len(out), "runs")


def compare():
    runs = load_runs("runs-models.jsonl")
    print(f"{'embedder, ranking':<28} s@1   s@3   r@10 | AUC whole note  best sentence")
    for variant in dict.fromkeys(r["variant"] for r in runs):
        rows = [r for r in runs if r["variant"] == variant]
        notes = [r for r in rows if r["set"] == "folder-notes"]
        s1 = np.mean([bool(r["shown"]) and r["shown"][0]["id"] in r["expected"] for r in notes])
        s3 = np.mean([any(c["id"] in r["expected"] for c in r["shown"]) for r in notes])
        r10 = np.mean([any(u in r["expected"] for u in r["top10"]) for r in notes])
        inside = [r for r in rows if r["set"] in INSIDE]
        outside = [r for r in rows if r["set"] in OUTSIDE]
        print(f"{variant:<28} {s1:.2f}  {s3:.2f}  {r10:.2f} |     {auc([r['note_best'] for r in inside], [r['note_best'] for r in outside]):.3f}"
              f"          {auc([r['any_best'] for r in inside], [r['any_best'] for r in outside]):.3f}")


def parity(path: str):
    runs = [json.loads(line) for line in open(STUDY / "runs.jsonl", encoding="utf-8")]
    ours = {r["qid"]: r for r in runs if r["variant"] == VARIANTS[0]["name"]}
    agree = total = 0
    for row in (json.loads(line) for line in open(path, encoding="utf-8")):
        engine = [(c["title"], c["page"]) for c in row["cards"]]
        mine = [(s["doc"], s["page"]) for s in ours[row["qid"]]["shown"]]
        total += 1
        agree += engine == mine
        print(row["qid"], "MATCH" if engine == mine else "DIFFER", "\n   engine:", engine, "\n   study: ", mine)
    print(f"{agree}/{total} cases identical")


if __name__ == "__main__":
    step = sys.argv[1] if len(sys.argv) > 1 else ""
    named = sys.argv[2:3]
    {"units": units, "embed": embed, "run": run, "check": check, "pool": lambda: pool(*named),
     "score": lambda: score(*named), "models": models, "compare": compare,
     "remap": lambda: remap(sys.argv[2])}.get(step, lambda: parity(sys.argv[2]))()
