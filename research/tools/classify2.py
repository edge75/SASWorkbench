# -*- coding: utf-8 -*-
import re
from collections import Counter

STIM = ['投入','插入','cash out','cashout','Cash out','Cashout','Cash Out','遊玩','開主門','開門','開啟再關閉','Power cycle','power cycle',
        'RAM clear','Reset handpay','reset handpay','重置 handpay','調整','調低','設定 EGM','按 cashout','按 Service','取得一張','取得上一張',
        '停用 handpay','下注','切換','列印 tickets','設定並啟用','重新調整','重新設定','紙鈔','現金','代幣','按 cash']
HUMAN = ['列印 ticket 包含','列印票券','列印出','EGM 列印','觀察 Meter Set','與 EGM 上對應的計量器','可遊玩','validation tilt',
         'ticket 上的 validation number','ticket validation number','顯示','鎖定為 handpay','不列印','barcode','列印為十進位',
         '與列印 ticket 號碼一致','Ticket title','Location =','Address1','Address2','收據','receipt','Receipt','chirp',
         '列印正確金額','列印 cashout','列印 $','列印剩餘','列印 restricted','列印 handpay','列印 jackpot','記錄目前的值','各 meter code 的值']

def classify(step_text, pass_text):
    both = step_text + ' ' + pass_text
    hum  = any(k in both for k in HUMAN)
    stim = any(k in step_text for k in STIM)
    if hum: return 'needs human judgement'
    if stim: return 'needs EGM stimulus'
    return 'wire-only'

rows=[]
# 10.2
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
        rows.append([cur,n,classify(st,pc),st,pc])
# 10.1
t1=open('gsa-10-1.txt',encoding='utf-8').read()
cur=None; exp=1
for line in t1.split('\n'):
    s=line.strip()
    m=re.match(r'^(10\.1\.\d)\s', s)
    if m and '分鐘' not in s:
        cur=m.group(1); exp=1; continue
    mm=re.match(r'^(\d{1,2}) (\S.*)$', s)
    if cur and mm and int(mm.group(1))==exp:
        txt=mm.group(2)
        rows.append([cur,exp,classify(txt,txt),txt,''])
        exp+=1

c=Counter(r[2] for r in rows)
per={}
for r in rows: per.setdefault(r[0],Counter())[r[2]]+=1
def key(x):
    a,b,cc=x.split('.'); return (int(a),int(b),int(cc))
with open('step-classification.txt','w',encoding='utf-8') as f:
    f.write("TOTAL STEPS %d\n"%len(rows))
    for k,v in c.most_common(): f.write("%-24s %3d  %.0f%%\n"%(k,v,100*v/len(rows)))
    f.write("\nPER TEST  (wire / stimulus / human)\n")
    for k in sorted(per,key=key):
        f.write("%-9s %3d %3d %3d   total %d\n" % (k, per[k]['wire-only'], per[k]['needs EGM stimulus'], per[k]['needs human judgement'], sum(per[k].values())))
    f.write("\nDETAIL\n")
    for r in rows:
        f.write("%-9s %2d  %-22s | %s || %s\n" % (r[0],r[1],r[2],r[3][:150],r[4][:200]))
print("TOTAL", len(rows))
for k,v in c.most_common(): print("  %-24s %3d  %.0f%%"%(k,v,100*v/len(rows)))
for k in sorted(per,key=key):
    print("  %-9s wire=%-3d stim=%-3d human=%-3d total=%d" % (k, per[k]['wire-only'], per[k]['needs EGM stimulus'], per[k]['needs human judgement'], sum(per[k].values())))
