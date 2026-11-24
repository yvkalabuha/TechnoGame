import csv,sys,collections as C
pre=sys.argv[1]
def yr(m): return 1945+int(m)//12
P=list(csv.DictReader(open(pre+'.prc.csv'))); Cl=list(csv.DictReader(open(pre+'.cell.csv'))); J=list(csv.DictReader(open(pre+'.prj.csv')))
print('== projects holding 43/73 (actor, design, stage: first-last year, months)')
g=C.OrderedDict()
for r in J: 
  k=(r['actor'],r['design'],r['stage']); g.setdefault(k,[r['month'],r['month'],0,r['prcset']]); g[k][1]=r['month']; g[k][2]+=1
for k,v in g.items(): print(k, yr(v[0]),'-',yr(v[1]), v[2], 'prc_set', v[3])
lead=C.defaultdict(C.Counter)
for r in Cl:
  if r['cluster']=='MET': lead[(r['actor'],r['dim'])][r['lead']]+=1
for a in ['USA','GBR','SUN','FRA','SWE']:
  for pn in ['42','43','73']:
    rows=[r for r in P if r['actor']==a and r['prc']==pn]
    kn=[r for r in rows if r['knows']=='1']
    gn=[r for r in rows if float(r['gn'])>0]
    el=[r for r in rows if r['eligible']=='1']
    lim=C.Counter((r['lim0_0'],r['lim0_1'],r['lim0_2']) for r in gn)
    limk=C.Counter((r['lim0_0'],r['lim0_1'],r['lim0_2']) for r in el)
    print(a,pn,'knows',yr(kn[0]['month']) if kn else '-', 'gn>0 months',len(gn),(yr(gn[0]['month']),yr(gn[-1]['month'])) if gn else '', 'gn',gn[0]['gn'] if gn else '', 'elig',len(el),
      'E>0',sum(1 for r in rows if float(r['E'])>0),'v>0',sum(1 for r in rows if float(r['v'])>0),'maxY %.3f'%max(float(r['yield']) for r in rows))
    print('   lim0 (prec,pur,qc) when gn>0:',dict(lim.most_common(4)))
    print('   lim0 when eligible:',dict(limk.most_common(4)))
  print(a,'MET lead counts per dim:',{d:dict(lead[(a,d)].most_common(4)) for d in '012'})
