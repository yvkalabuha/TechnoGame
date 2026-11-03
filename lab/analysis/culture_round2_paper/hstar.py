# Paper estimate of culture dynamics for round 2 (float iteration of the cell formula; NOT the Lab stand).
import itertools, sys, math
ACT = {  # H0 prec,pur,qc ; Hcap
 'USA': ((4.00,3.80,3.70),5.50), 'GBR': ((3.82,3.62,3.52),5.32), 'SUN': ((3.20,3.00,2.90),4.70),
 'FRA': ((3.34,3.14,3.04),4.84), 'SWE': ((3.67,3.47,3.37),5.17)}
CL = ['MET','CHEM','MECH','AERO']
PRC = { # cluster, req (prec,pur,qc) 0 = not required
 38:('MET',(3,3,2.5)),39:('MET',(0,4,3.5)),40:('MET',(0,4.5,4)),41:('MET',(0,5,4.5)),42:('MET',(4,5,4)),43:('MET',(5,5,5)),
 44:('CHEM',(0,3.5,3)),45:('CHEM',(0,4.5,3.5)),46:('CHEM',(4,4,3.5)),
 47:('MECH',(3.5,0,3)),48:('MECH',(4,0,3.5)),49:('MECH',(4.5,3,4)),50:('MECH',(4.5,0,4)),51:('MECH',(5,4.5,5)),52:('MECH',(5.5,0,5)),
 53:('AERO',(3,0,3)),54:('AERO',(3.5,0,4)),55:('AERO',(4,0,3.5)),56:('AERO',(4.5,0,4)),57:('AERO',(4,4,3.5))}
R = {frozenset(('MET','CHEM')):0.3, frozenset(('MET','MECH')):0.2, frozenset(('MECH','AERO')):0.3, frozenset(('CHEM','AERO')):0.1}
def Rv(a,b): return R.get(frozenset((a,b)),0.05)
RHO, SIG, PHI = 0.021, 0.005, 0.6
def g(x): return 0.0 if x < -1 else ((1+x)**2 if x < 0 else 1.0)
def smooth(x): return x*x*(3-2*x)

def run(rule, var, actor, prc, rate, alpha, dH, cond, start, horizon=1200, want='T', ktr=1.0):
    """rule 'A' (R1c) or 'B' (C-B). var for B: (shape, r_cap, r_keep, ell). cond: dict act, wl, lic.
    start 'front' (H=r_d) or 'edge' (max(H0, r_d-0.9)). Returns dict."""
    H0, hcap = ACT[actor]; cl, req = PRC[prc]
    act, wl, lic = cond['act'], cond['wl'], cond['lic']
    floor = PHI*hcap; kc = 0.5*alpha
    dims = [d for d in range(3) if req[d] > 0]
    H = {(c,d): H0[d] for c in CL for d in range(3)}
    for d in dims:
        H[(cl,d)] = req[d] if start=='front' else max(H0[d], req[d]-0.9)
    Hs = {d: H[(cl,d)] for d in dims}
    v = rate
    veff = (1+ktr*lic)*v
    if rule=='A':
        u = smooth(min(1, veff/6.25)) if v>0 else 0.0
        um = smooth(min(1, v/6.25)) if v>0 else 0.0
        mfrac = min(1, um/0.5)
        cpot = u; ell = 0.0; dL = 0.5
    else:
        shape, rcap, rkeep, ell = var
        x = min(1, veff/rcap) if v>0 else 0.0
        cpot = x if shape=='LIN' else smooth(x)
        mfrac = min(1, v/rkeep) if v>0 else 0.0
        dL = 0.5
    if v == 0: cpot = 0.0
    E_zero = cond.get('zeroE', False)
    if E_zero: cpot = 0.0; ell = 0.0
    sumE = 0.0; Thalf = None; Tm = None
    G = sum(max(0, req[d]-Hs[d]) for d in dims)
    for t in range(1, horizon+1):
        newH = dict(H); Emonth = 0.0
        for c in CL:
            for d in range(3):
                h = H[(c,d)]
                spill = sum(Rv(c,c2)*max(0, H[(c2,d)]-h) for c2 in CL if c2!=c)
                sat = max(0, 1-h/hcap)
                drive = catch = 0.0; maint = floor
                if c==cl and d in dims and v>0:
                    r = req[d]
                    gap = max(0, r+0.25-h); gapL = max(0, (r-dL)-h)
                    if rule=='B' and ell>0 and ell*gapL > cpot*gap:
                        tp = ell*gapL; eu = ell
                    else:
                        tp = cpot*gap; eu = cpot
                    drive = tp; catch = tp if wl else 0.0
                    ar = min(1, act)
                    maint = floor + (max(r*ar, floor)-floor)*mfrac
                    Emonth = max(Emonth, eu)  # one process; E_useful in units of E_full (AI)
                dplus = sat*((alpha*act)*1.0*drive + kc*catch + SIG*spill)
                dminus = dH*max(0, h-max(maint, floor))
                newH[(c,d)] = h + min(dplus, RHO) - dminus
        H = newH
        sumE += Emonth if not E_zero else 0.0
        gH = 1.0
        for d in dims: gH *= g(H[(cl,d)]-req[d])
        closed = sum(max(0, min(H[(cl,d)], req[d]) - Hs[d]) for d in dims)
        if Thalf is None and G >= 0.05 and closed >= 0.5*G: Thalf = (t, sumE)
        if Tm is None and gH >= 0.9: Tm = (t, sumE)
    gH = 1.0
    for d in dims: gH *= g(H[(cl,d)]-req[d])
    return dict(g=gH, Thalf=Thalf, T=Tm, G=G, y={d:H[(cl,d)]-req[d] for d in dims})
