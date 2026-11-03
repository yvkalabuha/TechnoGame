# Paper estimate R2-9 (culture-round2.md §1.9). Float iteration of the cell formulas; not the Lab stand.
# Usage: python3 drive.py 0.07,0.15 /path/out.json && python3 summ.py /path/out.json ; python3 k2a.py
import sys, itertools, json
from multiprocessing import Pool
from hstar import run, ACT, PRC
PA = [40,49,41,51,52]; PB = [38,44,45,50,53,54,55,57]
PAIRS = [(a,p) for a in ACT for p in PA+PB]
C = {'N':dict(act=1,wl=0,lic=0),'W':dict(act=1,wl=1,lic=0),'A75':dict(act=.75,wl=0,lic=0),'A50':dict(act=.5,wl=0,lic=0),
     'L':dict(act=1,wl=0,lic=1),'LW':dict(act=1,wl=1,lic=1)}
VARS = [('A',None)] + [('B',(s,rc,rk,l)) for s in ('LIN','SMOOTH') for rc in (5,6.25,10) for rk in (2,3.125,6.25) for l in (0,0.1)]
def eval_variant(args):
    (rule,var), alpha = args
    out = dict(k2a_fail=[], hk8a_fail=[], hk8b_fail=[], k2bt_fail=[], k2bp_fail=[], k2a_n=0, hk8_n=0, k2b_n=0, hk8q=[], k2bq=[], k2bpq=[])
    for (a,p) in PAIRS:
        ctrl = run(rule,var,a,p,0,alpha,0.0017,dict(act=1,wl=0,lic=0,zeroE=True),'front')
        if ctrl['g'] < 0.9:
            for cn in ('N','W','A75','A50','L'):
                out['k2a_n'] += 1
                r = run(rule,var,a,p,1.25,alpha,0.0017,C[cn],'front')
                if r['g'] >= 0.9: out['k2a_fail'].append((a,p,cn,round(r['g'],3)))
        # HK8 at delta 0.0043
        n25 = run(rule,var,a,p,25,alpha,0.0043,C['N'],'edge')
        if n25['T'] is not None:
            out['hk8_n'] += 1
            s2 = run(rule,var,a,p,2,alpha,0.0043,C['L'],'edge')
            if s2['T'] is None: out['hk8a_fail'].append((a,p,round(s2['g'],3)))
            else:
                q = s2['T'][0]/n25['T'][0]; out['hk8q'].append(q)
                if q > 3: out['hk8b_fail'].append((a,p,round(q,2)))
        # K2b' at delta 0.0017 (higher learning for slow: worst), conditions all
        for cn in ('N','W','A75','A50','L','LW'):
            nn = run(rule,var,a,p,25,alpha,0.0017,C[cn],'edge')
            if nn['Thalf'] is None: continue
            out['k2b_n'] += 1
            tk = run(rule,var,a,p,1.25,alpha,0.0017,C[cn],'edge')
            if tk['Thalf'] is None: out['k2bq'].append(99); out['k2bpq'].append(99); continue
            qt = tk['Thalf'][0]/nn['Thalf'][0]; out['k2bq'].append(qt)
            if qt < 2: out['k2bt_fail'].append((a,p,cn,round(qt,2)))
            if rule=='B':
                qp = tk['Thalf'][1]/nn['Thalf'][1]; out['k2bpq'].append(qp)
                if qp < 1.5: out['k2bp_fail'].append((a,p,cn,round(qp,2)))
    return (rule,var,alpha,out)
if __name__=='__main__':
    alphas = [float(x) for x in sys.argv[1].split(',')]
    jobs = [(v,al) for v in VARS for al in alphas]
    with Pool(16) as pool:
        res = pool.map(eval_variant, jobs)
    json.dump(res, open(sys.argv[2],'w'))
