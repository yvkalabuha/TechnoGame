import json, sys
res = json.load(open(sys.argv[1]))
def q(xs):
    xs = sorted(xs)
    return '-' if not xs else f"min {xs[0]:.2f} p50 {xs[len(xs)//2]:.2f}"
for rule,var,alpha,o in res:
    name = 'C-A' if var is None else f"{var[0][0]}{var[1]}/{var[2]}/{var[3]}"
    ok = not o['k2a_rel'] and not o['hk8a'] and not o['hk8b'] and not o['k2bt'] and not o['cost_ai'] and not o['cost_max']
    print(f"{name:14s} a={alpha:.2f} K2a abs {len(o['k2a_abs'])} rel {len(o['k2a_rel'])} r2grp {len(o['k2a_r2grp'])} /{o['k2a_n']} | HK8 a {len(o['hk8a'])} b {len(o['hk8b'])} /{o['hk8_n']} | K2b't {len(o['k2bt'])}/{o['k2b_n']} | cost AI {len(o['cost_ai'])} MAX {len(o['cost_max'])} /{o['cost_n']} qAI {q(o['cqa'])} qMAX {q(o['cqm'])} q_noline {q(o['cq_noline'])} | {'PASS' if ok else ''}")
