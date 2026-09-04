# -*- coding: utf-8 -*-
"""Condense decoded DFM into a UI tree: name, class, caption/text, hint, items, handler."""
import re, sys, os, glob
BASE = r"C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research"
KEEP = ('Caption','Text','Hint','Items.Strings','Lines.Strings','OnClick','OnChange','Enabled',
        'Checked','Visible','ShortCut','Interval','Filter','Down','State','ItemIndex','Title',
        'DefaultExt','Value','Min','Max','Position','Associate','GroupIndex')
out=[]
for f in ['form_TFORM1.dfm','form_TPOPUP.dfm','form_TINPUTDIALOG.dfm','form_TSASABOUTBOX.dfm']:
    path=os.path.join(BASE,f)
    if not os.path.exists(path): continue
    out.append("")
    out.append("#"*78)
    out.append("# %s" % f)
    out.append("#"*78)
    lines=open(path,encoding='utf-8').read().split('\n')
    i=0
    pend=None
    stack=[]
    while i < len(lines):
        L=lines[i]
        m=re.match(r'^(\s*)object (\S*): (\S+)$', L)
        if m:
            ind, nm, cls = m.group(1), m.group(2), m.group(3)
            # collect properties of this object until nested object or end
            props={}
            j=i+1
            depth_ind=ind+'  '
            while j < len(lines):
                L2=lines[j]
                if re.match(r'^\s*object \S*: \S+$', L2): break
                if L2.strip()=='end' and len(L2)-len(L2.lstrip())<=len(ind): break
                pm=re.match(r'^\s*([A-Za-z0-9_.]+) = (.*)$', L2)
                if pm and (len(L2)-len(L2.lstrip()))==len(depth_ind):
                    k,v=pm.group(1),pm.group(2)
                    if v=='<':
                        pass
                    props[k]=v
                    if v.startswith('(') and not v.endswith(')'):
                        # multiline list
                        acc=[v]
                        j+=1
                        while j<len(lines) and not lines[j].rstrip().endswith(')'):
                            acc.append(lines[j].strip()); j+=1
                        if j<len(lines): acc.append(lines[j].strip())
                        props[k]=' '.join(acc)
                j+=1
            bits=[]
            for k in KEEP:
                if k in props:
                    v=props[k]
                    if len(v)>1500: v=v[:1500]+' ...TRUNC'
                    bits.append("%s=%s" % (k,v))
            out.append("%s%s : %s" % (ind, nm if nm else '(unnamed)', cls))
            for b in bits:
                out.append("%s    %s" % (ind, b))
        i+=1
open(os.path.join(BASE,'ui-tree.txt'),'w',encoding='utf-8').write("\n".join(out))
print("\n".join(out))
