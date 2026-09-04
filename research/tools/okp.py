import re
d = open(r"C:\igs\GSA SAS Host Simulator v1.1\One Key Polls.doc",'rb').read()
i = d.find(b'One Key Polls, Polling Loops, and Filters')
print("marker at", i, "filesize", len(d))
# find extent of printable ascii region
j = i
while j < len(d):
    b = d[j]
    if b in (9,10,13) or 32 <= b < 127:
        j += 1
    else:
        # allow short runs of nulls/control
        k = j
        while k < len(d) and not (d[k] in (9,10,13) or 32 <= d[k] < 127):
            k += 1
        if k - j > 40: break
        j = k
print("region", i, j, "len", j-i)
raw = d[i:j]
s = raw.decode('cp1252','replace')
s = s.replace('\r','\n').replace('\x07','\t|\t').replace('\x0b','\n').replace('\x0c','\n---PAGE---\n')
open('okp_body.txt','w',encoding='utf-8').write(s)
print("wrote okp_body.txt", len(s))
