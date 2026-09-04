# -*- coding: utf-8 -*-
import re, json

STIM = ['投入','插入','cash out','cashout','Cash out','Cashout','Cash Out','遊玩','開主門','開門','開啟再關閉','Power cycle','power cycle',
        'RAM clear','Reset handpay','reset handpay','調整','調低','設定 EGM','按 cashout','按 Service','取得一張','取得上一張',
        '停用 handpay','啟用','下注','切換','列印 tickets','設定並啟用','重新調整','重新設定','紙鈔','現金','代幣']
HUMAN = ['列印 ticket 包含','列印票券','列印出','EGM 列印','觀察 Meter Set','與 EGM 上對應的計量器','可遊玩','validation tilt',
         'ticket 上的 validation number','ticket validation number','顯示','鎖定為 handpay','不列印','barcode','列印為十進位',
         '與列印 ticket 號碼一致','Ticket title','Location =','Address1','Address2','收據','receipt','Receipt','chirp']

def classify(step_text, pass_text):
    both = step_text + ' ' + pass_text
    stim = any(k in step_text for k in STIM)
    hum  = any(k in both for k in HUMAN)
    if hum and stim: return 'needs human judgement'   # strongest
    if hum: return 'needs human judgement'
    if stim: return 'needs EGM stimulus'
    return 'wire-only'

rows=[]
# --- 10.2 (pipe-delimited) ---
t=open('gsa-10-2.txt',encoding='utf-8').read()
cur=None; exp=1
for line in t.split('\n'):
    m=re.match(r'^(10\.2\.\d+)\s', line)
    if m: cur=m.group(1); exp=1; continue
    if cur and re.match(r'^\d{1,2} \|', line):
        parts=[p.strip() for p in line.split('|')]
        n=int(parts[0])
        if n!=exp: continue
        exp+=1
        st=parts[1] if len(parts)>1 else ''
        pc=' '.join(parts[2:]) if len(parts)>2 else ''
        rows.append((cur,n,classify(st,pc),st[:90],pc[:110]))

# --- 10.1 (single-blob lines) ---
t1=open('gsa-10-1.txt',encoding='utf-8').read()
parts=re.split(r'(?m)(10\.1\.\d)\s', t1)
for i in range(1,len(parts),2):
    tid=parts[i]; body=parts[i+1]
    m=re.search(r'步驟 測試步驟 通過標準 P/F(.*?)(?=\n|$)', body, re.S)
    if not m: continue
    blk=m.group(1)
    # split on " <n> " where n increments
    toks=re.split(r'(?<=\s)(\d{1,2}) (?=\S)', blk)
    exp=1; buf=[]
    seq=[]
    j=1
    while j < len(toks):
        if int(toks[j])==exp:
            seq.append((exp, toks[j+1] if j+1<len(toks) else ''))
            exp+=1
        j+=2
    for n,txt in seq:
        rows.append((tid,n,classify(txt,txt),txt[:90],''))

from collections import Counter
c=Counter(r[2] for r in rows)
print("TOTAL STEPS:", len(rows))
for k,v in c.most_common(): print("  %-24s %3d  (%.0f%%)" % (k,v,100*v/len(rows)))
per={}
for r in rows: per.setdefault(r[0],Counter())[r[2]]+=1
with open('step-classification.txt','w',encoding='utf-8') as f:
    f.write("TOTAL %d\n"%len(rows))
    for k,v in c.most_common(): f.write("%-24s %d\n"%(k,v))
    f.write("\nPER TEST\n")
    for k in sorted(per, key=lambda x:(len(x),x)):
        f.write("%-9s wire=%-3d stim=%-3d human=%-3d  total=%d\n" % (k, per[k]['wire-only'], per[k]['needs EGM stimulus'], per[k]['needs human judgement'], sum(per[k].values())))
    f.write("\nDETAIL\n")
    for r in rows:
        f.write("%-9s %2d  %-24s | %s | %s\n" % r)
print(open('step-classification.txt',encoding='utf-8').read()[:1800])
