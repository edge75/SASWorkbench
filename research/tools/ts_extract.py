"""Extract GSA EGM test-script cases from the PDFs by word coordinates.

A line-based extractor shears these documents: in the Test Steps | Pass Criteria
table a criterion cell is routinely taller than its step cell, so criteria land
on the wrong step (CLAUDE.md constraint 5). It is demonstrable on TSE0001 p.24,
where step 4 occupies two lines and its pass criteria occupy seven.

So the page is walked top to bottom as a state machine. Inside the steps table
the two columns are read as independent streams, each with its own row anchors --
a numbered "N." on the left, a Wingdings bullet on the right -- and a criterion is
attached to the step whose row it falls in. "Notes:" is full width and ends the
table; a case ends where the next case's "Requirements Tested:" begins, which may
be halfway down a page.

Usage:
    python ts_extract.py <pdf> [--page N] [--json out.json]
"""
import os
import re
import json
import argparse
import pdfplumber

GUTTER = 315.0     # fallback only; the real one is read per table, see gutter_of()
BODY_LEFT = 120.0  # body indent; section headings sit left of it
HEAD_TOP = 78.0
FOOT_TOP = 725.0
ROW_TOL = 3.0      # words within this many points are one visual row

STEP_RE = re.compile(r"^(\d{1,2})\.$")
SECTION_RE = re.compile(r"^(\d{1,2}(?:\.\d{1,2}){1,3})$")
FIELD_RE = re.compile(
    r"^(Requirements?\s+Tested|EGM\s+Set-?\s?up|HostSim\s+Set-?\s?up|One\s+Key\s+Polls|"
    r"Polling\s+Loops|Filters|Meter\s+Set|Test\s+Steps|Pass\s+Criteria|Notes)\s*:",
    re.I)
SUBFIELDS = ("one key polls", "polling loops", "filters", "meter set")
BULLETS = {"(cid:190)", "(cid:216)", "", "¾", "●", "•"}


def norm_label(raw):
    lab = re.sub(r"\s+", " ", raw).strip().lower()
    lab = lab.replace("set-up", "setup").replace("set up", "setup")
    return lab


def rows_of(page):
    """Visual rows [(top, [words])], clustered with tolerance, header/footer dropped."""
    ws = [w for w in page.extract_words(use_text_flow=False)
          if HEAD_TOP <= w["top"] <= FOOT_TOP]
    ws.sort(key=lambda w: (w["top"], w["x0"]))
    rows, cur, top = [], [], None
    for w in ws:
        if top is None:
            cur, top = [w], w["top"]
        elif w["top"] - top <= ROW_TOL:
            cur.append(w)
        else:
            rows.append((top, sorted(cur, key=lambda x: x["x0"])))
            cur, top = [w], w["top"]
    if cur:
        rows.append((top, sorted(cur, key=lambda x: x["x0"])))
    return rows


def join(ws):
    t = " ".join(w["text"] for w in ws)
    return t.replace("–", "-").replace("’", "'").strip()


def label_x(words):
    """x of the "Pass Criteria:" label on a table header row, if it is one."""
    for i, w in enumerate(words):
        if w["text"] == "Pass" and i + 1 < len(words) \
           and words[i + 1]["text"].lower().startswith("criteria"):
            return w["x0"]
    return None


def gutter_of(page_rows):
    """Left edge of the Pass Criteria column for a page.

    It is not a document constant: the label sits anywhere from x=259 to x=349
    across these documents, and a fixed gutter puts the criteria bullets into the
    step column on about a fifth of the tables. Nor is the label alone enough --
    the outermost bullets are sometimes a few points left of it (TSE0001 p.92,
    label at 284.6, bullets at 279.2), so the leftmost of the two wins.
    """
    xs = []
    for _, words in page_rows:
        lx = label_x(words)
        if lx is not None:
            xs.append(lx)
        xs += [w["x0"] for w in words if w["text"] in BULLETS and w["x0"] > 200.0]
    return min(xs) - 2.0 if xs else None


def find_subcols(words):
    """x positions of the One Key Polls / Polling Loops / Filters / Meter Set labels."""
    cols = []
    for i, w in enumerate(words):
        tail = join(words[i:])
        m = FIELD_RE.match(tail)
        if m:
            lab = norm_label(m.group(1))
            if lab in SUBFIELDS and (not cols or cols[-1][0] != lab):
                cols.append((lab, w["x0"]))
    return cols


def extract(path, only_page=None):
    doc = os.path.basename(path)
    cases, cur, section = [], None, None
    mode = "head"     # head | table | notes
    sub_cols = None
    last_field = None
    gutter = GUTTER   # carried across pages: a table keeps its columns

    with pdfplumber.open(path) as pdf:
        pages = pdf.pages if only_page is None else [pdf.pages[only_page - 1]]
        for page in pages:
            pno = page.page_number
            page_rows = rows_of(page)
            g = gutter_of(page_rows)
            if g is not None:
                gutter = g
            for top, words in page_rows:
                flat = join(words)
                if not flat:
                    continue

                # section heading: numbered, left of the body indent
                if (words[0]["x0"] < BODY_LEFT and len(words) > 1
                        and SECTION_RE.match(words[0]["text"])):
                    section = (words[0]["text"], join(words[1:]))
                    continue

                m = FIELD_RE.match(flat)
                label = norm_label(m.group(1)) if m else None

                if label:
                    rest = flat[m.end():].strip()
                    if label.startswith("requirement"):
                        cur = {"doc": doc, "page": pno,
                               "section": section[0] if section else None,
                               "title": section[1] if section else None,
                               "requirements": rest, "steps": [], "notes": []}
                        cases.append(cur)
                        mode, sub_cols, last_field = "head", None, "requirements"
                        continue
                    if cur is None:
                        continue
                    if label == "notes":
                        mode, last_field, sub_cols = "notes", None, None
                        cur["notes"].append(rest)
                        continue
                    if label in ("test steps", "pass criteria"):
                        mode, last_field, sub_cols = "table", None, None
                        continue
                    if label in SUBFIELDS:
                        sub_cols = find_subcols(words)
                        for lab, x in sub_cols:
                            cur.setdefault(lab, "")
                        # text that follows the last label on the same row
                        for i, (lab, x) in enumerate(sub_cols):
                            hi = sub_cols[i + 1][1] if i + 1 < len(sub_cols) else 10_000
                            seg = [w for w in words if x <= w["x0"] < hi]
                            mm = FIELD_RE.match(join(seg))
                            tail = join(seg)[mm.end():].strip() if mm else ""
                            if tail:
                                cur[lab] = (cur[lab] + " " + tail).strip()
                        mode, last_field = "head", None
                        continue
                    cur[label] = rest
                    mode, last_field, sub_cols = "head", label, None
                    continue

                if cur is None:
                    continue

                if mode == "notes":
                    cur["notes"][-1] = (cur["notes"][-1] + " " + flat).strip()
                    continue

                if mode == "table":
                    left = [w for w in words if w["x0"] < gutter]
                    right = [w for w in words if w["x0"] >= gutter]
                    if left:
                        mm = STEP_RE.match(left[0]["text"])
                        if mm:
                            cur["steps"].append({"n": int(mm.group(1)),
                                                 "text": join(left[1:]), "pass": []})
                        elif cur["steps"]:
                            s = cur["steps"][-1]
                            s["text"] = (s["text"] + " " + join(left)).strip()
                        else:
                            cur.setdefault("orphan_steps", []).append(join(left))
                    if right:
                        if right[0]["text"] in BULLETS:
                            txt = join(right[1:])
                            if cur["steps"]:
                                cur["steps"][-1]["pass"].append(txt)
                            else:
                                cur.setdefault("orphan_pass", []).append(txt)
                        else:
                            txt = join(right)
                            if cur["steps"] and cur["steps"][-1]["pass"]:
                                cur["steps"][-1]["pass"][-1] += " " + txt
                            else:
                                op = cur.setdefault("orphan_pass", [])
                                if op:
                                    op[-1] += " " + txt
                                else:
                                    op.append(txt)
                    continue

                # head mode: sub-table value rows, or a wrapped field value
                if sub_cols:
                    for i, (lab, x) in enumerate(sub_cols):
                        hi = sub_cols[i + 1][1] if i + 1 < len(sub_cols) else 10_000
                        seg = join([w for w in words if x - 8 <= w["x0"] < hi - 8])
                        if seg:
                            cur[lab] = (cur[lab] + " " + seg).strip()
                elif last_field:
                    cur[last_field] = (cur.get(last_field, "") + " " + flat).strip()

    return cases


def render(c):
    out = [f"=== {c['section']} {c['title']}   (p{c['page']})",
           f"    Requirements Tested: {c['requirements']}"]
    for k in ("egm setup", "hostsim setup") + SUBFIELDS:
        if c.get(k):
            out.append(f"    {k}: {c[k]}")
    for s in c["steps"]:
        out.append(f"  {s['n']:>3}. {s['text']}")
        for p in s["pass"]:
            out.append(f"       -> {p}")
    for n in c["notes"]:
        out.append(f"    Notes: {n}")
    return "\n".join(out)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("pdf")
    ap.add_argument("--page", type=int)
    ap.add_argument("--json")
    a = ap.parse_args()
    cs = extract(a.pdf, a.page)
    if a.json:
        json.dump(cs, open(a.json, "w", encoding="utf-8"), indent=1)
        print(f"{len(cs)} cases -> {a.json}")
    else:
        for c in cs:
            print("\n" + render(c))
