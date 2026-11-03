from multiprocessing import Pool
from hstar import run
from drive import PAIRS, C
def job(a):
    rule,var,al = a; f=0; n=0; worst=[]
    for (ac,p) in PAIRS:
        ctrl = run(rule,var,ac,p,0,al,0.0017,dict(act=1,wl=0,lic=0,zeroE=True),'front')
        if ctrl['g']>=0.9: continue
        for cn in ('N','W','A75','A50','L'):
            n+=1; r=run(rule,var,ac,p,1.25,al,0.0017,C[cn],'front')
            if r['g']>=0.9: f+=1; worst.append((ac,p,cn,round(ctrl['g'],2),round(r['g'],2)))
    return (rule,var,al,f,n,worst)
if __name__=='__main__':
    jobs=[(r,v,al) for (r,v) in [('A',None),('B',('SMOOTH',10,6.25,0)),('B',('SMOOTH',10,2,0))] for al in (0.02,0.035,0.05)]
    with Pool(9) as p:
        for r in p.map(job,jobs): print(r[0],r[1],r[2],f'{r[3]}/{r[4]}', r[5][:12])
