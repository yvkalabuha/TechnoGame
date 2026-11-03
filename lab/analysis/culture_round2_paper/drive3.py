# Paper estimate r3 driver. Usage: python3 drive3.py <alphas> <out.json> [k_line]
import sys, json
from multiprocessing import Pool
from hstar3 import run, ACT, PRC
PA = [40,49,41,51,52]; PB = [38,44,45,50,53,54,55,57]
PAIRS = [(a,p) for a in ACT for p in PA+PB]
C = {'N':dict(act=1,wl=0,lic=0),'W':dict(act=1,wl=1,lic=0),'A75':dict(act=.75,wl=0,lic=0),'A50':dict(act=.5,wl=0,lic=0),
     'L':dict(act=1,wl=0,lic=1),'LW':dict(act=1,wl=1,lic=1)}
VARS = [('A',None)] + [('B',(s,rc,rk,l)) for s in ('LIN','SMOOTH') for rc in (5,6.25,10) for rk in (2,3.125,6.25) for l in (0,0.1)]
KL = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
def floor_r2(a,p):  # r2 print group: min_d (r_d - H_floor) <= 0.4
    H0,hcap = ACT[a]; req = PRC[p][1]
    return min(r - 0.6*hcap for r in req if r > 0) <= 0.4
def eval_variant(args):
    (rule,var), alpha = args
    o = dict(k2a_n=0, k2a_abs=[], k2a_rel=[], k2a_r2grp=[], hk8_n=0, hk8a=[], hk8b=[], k2b_n=0, k2bt=[], k2bq=[],
             cost_n=0, cost_ai=[], cost_max=[], cqa=[], cqm=[], cq_noline=[])
    for (a,p) in PAIRS:
        ctrl = run(rule,var,a,p,0,alpha,0.0017,dict(act=1,wl=0,lic=0,zeroE=True),'front')
        gc = ctrl['g']
        if gc < 0.9:
            for cn in ('N','W','A75','A50','L'):
                o['k2a_n'] += 1
                r = run(rule,var,a,p,1.25,alpha,0.0017,C[cn],'front')
                rec = (a,p,cn,round(gc,3),round(r['g'],3))
                if r['g'] >= 0.9: o['k2a_abs'].append(rec)
                if (r['g']-gc)/(1-gc) >= 0.9: o['k2a_rel'].append(rec)          # #105, group g_ctrl > 0
                if (floor_r2(a,p) and (r['g']-gc)/(1-gc) >= 0.9) or (not floor_r2(a,p) and r['g'] >= 0.9):
                    o['k2a_r2grp'].append(rec)                                    # #105, r2 group definition
        n25 = run(rule,var,a,p,25,alpha,0.0043,C['N'],'edge')
        if n25['T'] is not None:
            o['hk8_n'] += 1
            s2 = run(rule,var,a,p,2,alpha,0.0043,C['L'],'edge')
            if s2['T'] is None: o['hk8a'].append((a,p,round(s2['g'],3)))
            elif s2['T'][0] > 3*n25['T'][0]: o['hk8b'].append((a,p,round(s2['T'][0]/n25['T'][0],2)))
        for cn in ('N','W','A75','A50','L','LW'):
            for mode in (('AI','MAX') if rule=='B' else ('AI',)):
                nn = run(rule,var,a,p,25,alpha,0.0017,C[cn],'edge',k_line=KL,mode=mode)
                tk = run(rule,var,a,p,1.25,alpha,0.0017,C[cn],'edge',k_line=KL,mode=mode)
                if mode=='AI' and nn['Thalf'] is not None:
                    o['k2b_n'] += 1
                    qt = 99 if tk['Thalf'] is None else tk['Thalf']/nn['Thalf']
                    o['k2bq'].append(qt)
                    if qt < 2: o['k2bt'].append((a,p,cn,round(qt,2)))
                if nn['T'] is None or nn['G'] < 0.05: continue   # cost: same exclusion as time (G < 0.05) + T(25) finite
                if mode=='AI': o['cost_n'] += 1
                if tk['T'] is None: q = 99; q0 = 99
                else:
                    q = sum(tk['T'][1:]) / sum(nn['T'][1:])
                    q0 = (tk['T'][1]+tk['T'][3]) / (nn['T'][1]+nn['T'][3])
                if mode=='AI':
                    o['cqa'].append(q); o['cq_noline'].append(q0)
                    if q < 1.5: o['cost_ai'].append((a,p,cn,round(q,2), nn['T'][0], None if tk['T'] is None else tk['T'][0]))
                else:
                    o['cqm'].append(q)
                    if q < 1.5: o['cost_max'].append((a,p,cn,round(q,2)))
    return (rule,var,alpha,o)
if __name__=='__main__':
    alphas = [float(x) for x in sys.argv[1].split(',')]
    jobs = [(v,al) for v in VARS for al in alphas]
    with Pool(16) as pool: res = pool.map(eval_variant, jobs)
    json.dump(res, open(sys.argv[2],'w'))
