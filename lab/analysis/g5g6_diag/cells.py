#!/usr/bin/env python3
"""TASK-0133 diagnosis: per actor, median over seeds of yield, ramp and the capability factor g at 2030-12 from diag.csv of Program.cs.
Usage: python3 lab/analysis/g5g6_diag/cells.py DIAG_CSV. capability_req is hard-coded from mods/core/nodes.yaml (d194d2a)."""

import csv,sys,collections,statistics as st
f=sys.argv[1]
req={41:{'purity':5,'qc':4.5},42:{'prec':4,'purity':5,'qc':4},43:{'prec':5,'purity':5,'qc':5},51:{'prec':5,'purity':4.5,'qc':5},52:{'prec':5.5,'qc':5},73:{'prec':5.5,'purity':5.5,'qc':5.5},75:{'prec':5,'purity':5.5,'qc':5.5},80:{'prec':5.5,'purity':5.5,'qc':5.5},74:{'prec':4.5,'qc':4.5}}
cl={41:'MET',42:'MET',43:'MET',73:'MET',51:'MECH',52:'MECH',75:'CHEM',80:'CHEM',74:'AERO'}
dimi={'prec':0,'purity':1,'qc':2}
cell={};prc={};maxy=collections.defaultdict(float);mr=collections.Counter()
for r in csv.DictReader(open(f)):
  s,m,a=int(r['seed']),int(r['month']),r['actor']
  if r['kind']=='cell': cell[(s,m,a,r['key'])]=(float(r['a']),float(r['b']),float(r['c']))
  else:
    p=int(r['key']); y=float(r['a'])
    maxy[(s,a,p)]=max(maxy[(s,a,p)],y)
    if m==1019: prc[(s,a,p)]=(y,float(r['b']),float(r['c']),float(r['d']),float(r['e']),r['f'])
    if r['f'].endswith('mr6'): mr[(s,a,p)]+=1
acts=['USA','GBR','SUN','FRA','SWE']
print('hcap', {a:cell[(1,1019,a,'MET.0')][2] for a in acts})
print('Per actor, 2030-12 (month 1019), median over seeds 1-10: yield / ramp / g-product, max yield ever, seeds with mastered at a sampled Dec')
for a in acts:
  for p in [41,42,43,51,52,73,75,80,74]:
    ys=[];rs=[];gs=[];mx=[];hs=collections.defaultdict(list);ms=0
    for s in range(1,11):
      y,ramp,X,V,E,f=prc[(s,a,p)]
      ys.append(y);rs.append(ramp);mx.append(maxy[(s,a,p)])
      g=1
      for d,rv in req[p].items():
        H,M,Hc=cell[(s,1019,a,cl[p]+'.'+str(dimi[d]))]
        c=min(H,M);gap=c-rv; gg=0 if gap<-1 else ((1+gap)**2 if gap<0 else 1); g*=gg
        hs[d].append(c)
      gs.append(g)
      ms+= 1 if mr[(s,a,p)]>0 else 0
    print(a,p,'y %.3f'%st.median(ys),'ramp %.2f'%st.median(rs),'g %.3f'%st.median(gs),'maxY %.3f (max %.3f)'%(st.median(mx),max(mx)),'mastered-seeds',ms,' c=min(H,M): '+' '.join('%s %.2f..%.2f'%(d,min(v),max(v)) for d,v in hs.items()))
