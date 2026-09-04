import sys, pdfplumber
from itertools import groupby

page_no, ytop, ybot = int(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])
p = pdfplumber.open(r"C:\Progetti\libsas\docs\SAS-6-02-Protocol.pdf").pages[page_no-1]
ws = [w for w in p.extract_words() if ytop <= w['top'] <= ybot]
ws.sort(key=lambda w: (round(w['top'],1), w['x0']))

# group words into visual lines
lines = []
for _, g in groupby(ws, key=lambda w: round(w['top'],1)):
    g = list(g)
    lines.append((g[0]['top'], g))

# column gutters read off the header row (Field/Bytes/Value/Description)
COLS = [(50,175), (175,265), (265,330), (330,600)]
def cell(g, lo, hi):
    return " ".join(w['text'] for w in g if lo <= w['x0'] < hi)

rows = [[top] + [cell(g,lo,hi) for lo,hi in COLS] for top,g in lines]

# a new field row starts when column 1 (Field) is non-empty; other lines
# are continuations that belong to the field row above them
out, cur = [], None
for top,f,b,v,d in rows:
    if f:
        if cur: out.append(cur)
        cur = [f,b,v,[d] if d else []]
    elif cur:
        if b: cur[1] += " " + b
        if v: cur[2] += " " + v
        if d: cur[3].append(d)
if cur: out.append(cur)

w = [20,10,12,52]
def fmt(r): return "| " + " | ".join(str(c).ljust(x)[:x] for c,x in zip(r,w)) + " |"
print(fmt(["Field","Bytes","Value","Description"]))
print("|" + "|".join("-"*(x+2) for x in w) + "|")
for f,b,v,ds in out:
    print(fmt([f,b,v,ds[0] if ds else ""]))
    for extra in ds[1:]:
        print(fmt(["","","",extra]))
