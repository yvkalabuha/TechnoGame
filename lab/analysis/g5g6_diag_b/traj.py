import csv,sys
pre,act,yrs=sys.argv[1],sys.argv[2],sys.argv[3].split('-')
Cl=[r for r in csv.DictReader(open(pre+'.cell.csv')) if r['actor']==act and r['cluster']=='MET']
P={(r['month'],r['prc']):r for r in csv.DictReader(open(pre+'.prc.csv')) if r['actor']==act}
for r in Cl:
  m=int(r['month']); y=1945+m//12
  if m%12!=11 and m%12!=5: continue
  if not (int(yrs[0])<=y<=int(yrs[1])): continue
  if r['dim']=='0':
    line=[str(y)+('.5' if m%12==5 else '')]
  line.append('d%s H%.2f M%.2f cap%.2f L%s dr%.4f'%(r['dim'],float(r['H']),float(r['M']),float(r['Hcap']),r['lead'],float(r['drive'])))
  if r['dim']=='2':
    for pn in ['43','73']:
      p=P[(r['month'],pn)]; line.append('%s: gn%s E%s v%s y%s %s'%(pn,p['gn'],p['E'],p['v'],p['yield'],p['limiter']))
    print(' | '.join(line))
