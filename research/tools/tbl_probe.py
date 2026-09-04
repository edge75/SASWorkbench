import sys, pdfplumber
from collections import defaultdict

page_no = int(sys.argv[1])
pdf = pdfplumber.open(r"C:\Progetti\libsas\docs\SAS-6-02-Protocol.pdf")
p = pdf.pages[page_no-1]
words = p.extract_words(use_text_flow=False, keep_blank_chars=False)

# Show the x-histogram of word start positions -> reveals column gutters
hist = defaultdict(int)
for w in words:
    hist[round(w['x0']/5)*5] += 1
print("x0 histogram (bucket:count):")
print(" ".join(f"{k}:{v}" for k,v in sorted(hist.items()) if v >= 2))
print()
print("sample words with coords (y, x0, x1, text):")
for w in words:
    if 300 < w['top'] < 560:
        print(f"  top={w['top']:7.2f} x0={w['x0']:7.2f} x1={w['x1']:7.2f}  {w['text']}")
