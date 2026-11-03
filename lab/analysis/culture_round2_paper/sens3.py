# k_line sensitivity of K2b' cost (r3), print only. Usage: python3 sens3.py <out.json>
import sys, json
from multiprocessing import Pool
from hstar3 import run
from drive3 import PAIRS, C
VS = [('A',None),('B',('SMOOTH',10,6.25,0)),('B',('SMOOTH',10,3.125,0)),('B',('SMOOTH',10,2,0))]
def job(args):
    (rule,var),al,kl = args; f={'AI':0,'MAX':0}; n=0
    for (a,p) in PAIRS:
        for cn in ('N','W','A75','A50','L','LW'):
            for mode in (('AI','MAX') if rule=='B' else ('AI',)):
                nn = run(rule,var,a,p,25,al,0.0017,C[cn],'edge',k_line=kl,mode=mode)
                if nn['T'] is None or nn['G'] < 0.05: continue
                if mode=='AI': n += 1
                tk = run(rule,var,a,p,1.25,al,0.0017,C[cn],'edge',k_line=kl,mode=mode)
                if tk['T'] is not None and sum(tk['T'][1:]) < 1.5*sum(nn['T'][1:]): f[mode] += 1
    return (rule,var,al,kl,n,f)
if __name__=='__main__':
    jobs=[(v,al,kl) for v in VS for al in (0.07,0.15) for kl in (0.0,0.125,0.5)]
    with Pool(16) as pool: json.dump(pool.map(job,jobs), open(sys.argv[1],'w'))
