# -*- coding: utf-8 -*-
import re, io
def load(p): return open(p, encoding='utf-8').read()

t1 = load('gsa-10-1.txt')
t2 = load('gsa-10-2.txt')

# 10.1 file is one-line-per-record with leading space; steps look like " N 步驟文字 ..." inside a big block
# Simpler: count " 步驟 測試步驟 通過標準 P/F " sections then numbered steps.
def steps_101(t):
    # tests delimited by "10.1.N Title"
    out={}
    parts = re.split(r'(?m)^\s*(10\.1\.\d)\s', t)
    for i in range(1,len(parts),2):
        tid=parts[i]; body=parts[i+1]
        # steps are " <n> <text>" separated; the block after '步驟 測試步驟 通過標準 P/F'
        m = re.search(r'步驟 測試步驟 通過標準 P/F(.*)', body, re.S)
        if not m: continue
        blk = m.group(1)
        nums = re.findall(r'(?:^|\s)(\d{1,2}) (?=[^\s])', blk)
        # dedupe monotonic
        seq=[]; exp=1
        for n in nums:
            if int(n)==exp: seq.append(int(n)); exp+=1
        out[tid]=len(seq)
    return out

def steps_102(t):
    out={}
    cur=None
    for line in t.split('\n'):
        m=re.match(r'^(10\.2\.\d+)\s', line)
        if m: cur=m.group(1); out.setdefault(cur,0); continue
        if cur and re.match(r'^\d{1,2} \|', line):
            out[cur]=max(out[cur], int(line.split('|')[0].strip()))
    return out

a=steps_101(t1); b=steps_102(t2)
print("10.1 steps per test:", a, "TOTAL", sum(a.values()))
print("10.2 steps per test:", b, "TOTAL", sum(b.values()))
print("GRAND TOTAL steps:", sum(a.values())+sum(b.values()))
