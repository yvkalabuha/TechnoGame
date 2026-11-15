#!/usr/bin/env python3
"""Frontier size and APP budget share of an actor, reconstructed from node_times.csv of a T12 calibration point (knowledge-pacing-proposal §1.2).
Usage: python3 lab/analysis/pacing/frontier_share.py p00 [USA]  (reads lab/results/t12/c3c4/m-<p>/node_times.csv)."""
import csv,sys,os,statistics as st
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'..','..','..'))
from collections import defaultdict
N={}
for r in csv.DictReader(open(os.path.join(ROOT,'docs/research/data/jet-slice/nodes.csv'))):
    N[r['id']]=r
idx={r['id']:int(r['idx']) for r in N.values()}
req={}
for r in N.values():
    groups=[]
    for g in r['requires'].split(';'):
        if g: groups.append([idx[x] for x in g.split('|')])
    req[int(r['idx'])]=groups
typ={int(r['idx']):r['type'] for r in N.values()}
haz={int(r['idx']):r['hazard'] for r in N.values()}
gen={int(r['idx']):r['gen'] for r in N.values()}
p=sys.argv[1]; actor=sys.argv[2] if len(sys.argv)>2 else 'USA'
runs=defaultdict(dict)
for r in csv.DictReader(open(os.path.join(ROOT,f'lab/results/t12/c3c4/m-{p}/node_times.csv'))):
    if r['actor']!=actor: continue
    t=r['t_known']; runs[r['run']][int(r['node'])]= int(t) if t!='' else 10**6
def frontier(kn,t):
    out=[]
    for n,tk in kn.items():
        if tk<=t: continue
        if all(any(kn.get(x,10**6)<=t for x in g) for g in req[n]): out.append(n)
    return out
# APP windows
for app in (59,60,61):
    sizes=[];shares1=[];shares3=[];lag=[]
    for run,kn in runs.items():
        avail=max([min(kn[x] for x in g) for g in req[app]])
        if kn[app]>=10**6 or avail>=10**6: continue
        lag.append(kn[app]-avail)
        fs=[];s1=[];s3=[]
        for t in range(avail,kn[app]):
            f=frontier(kn,t); fs.append(len(f))
            w1=sum(2 if (typ[n]=='ENG' or haz[n]=='key') else 1 for n in f)
            s1.append(1/w1); s3.append(1/len(f))
        sizes.append(st.mean(fs)); shares1.append(st.mean(s1)); shares3.append(st.mean(s3))
    print(p,actor,'APP',app,'n',len(lag),'lag med',st.median(lag),'frontier mean med',round(st.median(sizes),1),'share P1',round(st.median(shares1),3),'share P3',round(st.median(shares3),3))
# frontier size over time
for t in (0,24,48,96,144,192,240,288,336):
    fs=[len(frontier(kn,t)) for kn in runs.values()]
    print('t',t,'frontier med',st.median(fs))
