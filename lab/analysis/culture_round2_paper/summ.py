import json, sys, statistics as st
from collections import Counter
res = json.load(open(sys.argv[1]))
def q(xs):
    xs = sorted(xs); 
    if not xs: return '-'
    return f"min {xs[0]:.2f} p50 {xs[len(xs)//2]:.2f} max {xs[-1]:.2f}"
for rule,var,alpha,o in res:
    name = rule if var is None else f"{var[0][0]}{var[1]}/{var[2]}/{var[3]}"
    print(f"{name:16s} a={alpha:.2f} K2a {len(o['k2a_fail'])}/{o['k2a_n']} HK8a {len(o['hk8a_fail'])}/{o['hk8_n']} HK8b {len(o['hk8b_fail'])} K2b't {len(o['k2bt_fail'])}/{o['k2b_n']} K2b'p {len(o['k2bp_fail'])} | hk8q {q(o['hk8q'])} | k2bq {q(o['k2bq'])} | k2bpq {q(o['k2bpq'])}")
