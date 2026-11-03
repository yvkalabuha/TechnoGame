# Paper estimate r3 (culture-round2.md §1.9, decisions #105-#107). Float iteration of the cell formulas; NOT the Lab stand.
# Differences from hstar.py (r2, kept unchanged): #107 licence channel in maintenance (m_p from v~ = (1+k_tr*lic)*v);
# #106 money to mastery = E (development) + line upkeep + output cost v*base_cost/yc, yc = max(yield, eps).
from hstar import ACT, PRC, CL, Rv, RHO, SIG, PHI, g, smooth
BASE = {38:75,39:28,40:75,41:225,42:843,43:468,44:18,45:28,46:150,47:0,48:56,49:93,50:150,51:281,52:656,
        53:462,54:168,55:431,56:937,57:112}  # base_cost_p raw (1e-6 budget index), product_inputs.md §4
E_FULL = 0.001838; EPS = 0.01; R_REF = 25.0

def run(rule, var, actor, prc, rate, alpha, dH, cond, start, horizon=1200, k_line=0.25, ktr=1.0, mode='AI'):
    H0, hcap = ACT[actor]; cl, req = PRC[prc]
    act, wl, lic = cond['act'], cond['wl'], cond['lic']
    floor = PHI*hcap; kc = 0.5*alpha
    dims = [d for d in range(3) if req[d] > 0]
    H = {(c,d): H0[d] for c in CL for d in range(3)}
    for d in dims:
        H[(cl,d)] = req[d] if start=='front' else max(H0[d], req[d]-0.9)
    Hs = {d: H[(cl,d)] for d in dims}
    v = rate; veff = (1+ktr*lic)*v
    if rule=='A':
        cpot = smooth(min(1, veff/6.25)) if v>0 else 0.0
        mfrac = min(1, (smooth(min(1, veff/6.25)) if v>0 else 0.0)/0.5)   # #107: u from v~
        ell = 0.0
    else:
        shape, rcap, rkeep, ell = var
        x = min(1, veff/rcap) if v>0 else 0.0
        cpot = x if shape=='LIN' else smooth(x)
        mfrac = min(1, veff/rkeep) if v>0 else 0.0                       # #107: m_p from v~
    dL = 0.5
    if cond.get('zeroE', False): cpot = 0.0; ell = 0.0
    b = BASE[prc]*1e-6
    c_line = (k_line*R_REF)*b if v>0 else 0.0
    sE = sL = sO = 0.0; Thalf = None; Tm = None
    G = sum(max(0, req[d]-Hs[d]) for d in dims)
    def gH():
        p = 1.0
        for d in dims: p *= g(H[(cl,d)]-req[d])
        return p
    for t in range(1, horizon+1):
        y_prev = gH()                      # yield from snapshot t-1 (ramp = 1, D-1)
        newH = dict(H); eu_m = 0.0
        for c in CL:
            for d in range(3):
                h = H[(c,d)]
                spill = sum(Rv(c,c2)*max(0, H[(c2,d)]-h) for c2 in CL if c2!=c)
                sat = max(0, 1-h/hcap)
                drive = catch = 0.0; maint = floor
                if c==cl and d in dims and v>0:
                    r = req[d]
                    gap = max(0, r+0.25-h); gapL = max(0, (r-dL)-h)
                    if rule=='B' and ell>0 and ell*gapL > cpot*gap: tp = ell*gapL; eu = ell
                    else: tp = cpot*gap; eu = cpot
                    drive = tp; catch = tp if wl else 0.0
                    maint = floor + (max(r*min(1,act), floor)-floor)*mfrac
                    eu_m = max(eu_m, eu)
                dplus = sat*((alpha*act)*1.0*drive + kc*catch + SIG*spill)
                dminus = dH*max(0, h-max(maint, floor))
                newH[(c,d)] = h + min(dplus, RHO) - dminus
        H = newH
        if rule=='B' and v>0 and not cond.get('zeroE', False):
            sE += E_FULL*(1.0 if mode=='MAX' else eu_m)
        sL += c_line
        sO += (v*b)/max(y_prev, EPS)
        closed = sum(max(0, min(H[(cl,d)], req[d]) - Hs[d]) for d in dims)
        if Thalf is None and G >= 0.05 and closed >= 0.5*G: Thalf = t
        if Tm is None and gH() >= 0.9: Tm = (t, sE, sL, sO)
    return dict(g=gH(), Thalf=Thalf, T=Tm, G=G)
