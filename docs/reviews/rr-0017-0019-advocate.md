# Gate RR-0017 / RR-0019 — advocate (2026-11-18)

Both drafts are a sound base; both still assume the monthly tick (pre ADR-0019) and lack the province / owner–controller link (ADR-0020, #349–#351).

## RR-0017 (intelligence)
1. **strong — number of levers.** "≤ 3 knobs per pair" (#19) does not bound the number of pairs (20 networks = 60 knobs). Alternative: one national intelligence budget and up to 3 priority targets (like #214); other pairs get an automatic "background" by threat and interest, by the same deterministic AI rule. Keep the sparse `Net[o,t]`. Separate agents rightly rejected.
2. **strong — target undefined for provinces and controllers.** DEU and KOR are not actors (ADR-0020 p. 17), Berlin has 4 sectors (#350). Rule: `t` = controlling actor (#349); on `create_polity` (FRG, GDR 1949) the network splits by `Apportion` over region economy shares, `A` preserved; on `transfer_controller` the network moves to the new controller.
3. **strong — ADR-0019 mismatch.** Network growth `N` as an annual rate sliced by day; `intel.expose` roll monthly on a fixed day staggered by `(o,t)` like AI (#344); AI response in its weekly slot.
4. **strong — link to scripted events.** No intelligence atoms, but #299 branches need them (e.g. Soviet bomb earlier or later: Fuchs, Venona). Proposal: M2+ atoms `net_eff(o,t,≥k)`, `scandal_fired(o,t)`, `known(o,node)`, class like `metric`, forbidden in M1 (#340). Conflict: exposure is random (#260) but events have no RNG (#342) — an event reading a roll result imports randomness indirectly. Alternative: historical exposures (Gouzenko 1945, Venona/Fuchs 1950) as deterministic scripted events cutting `N` and `A`; RNG only for non-historical networks. → owner question.
5. moderate — `dir[o,t]` defaults to the thief's priority clusters (#214); manual change only to deviate.
6. moderate — per #322 a stolen node is realised with the thief's engineering school (Tu-144 is not a copy); one line in the rule.
7. moderate — RR-0019 `comm` multiplies layer-3 visibility and RR-0017 `intel_conf` does too (double count, D01); remove `comm` from visibility.
8. weak — turned networks (#261) are cheap and meaningful; keep.

## RR-0019 (logistics, communications)
1. **strong — scale is wrong.** "~1000 regions, ≈ 4k links" → 150–250 regions + 15–30 sectors, ~600–1000 links; recompute the budget; full-graph path recomputation per event ~1 ms [Предп.].
2. **strong — flow assignment with congestion needs inter-regional CMP flows E1 does not have** (`m_d = m_1·d` is only an energy mark-up). Cheaper v1: path cost `Φ[r,r']` (shortest-path cache) replaces `d(r)` in `m_d`; region market access `MA_r = Σ_j Y_j·Φ_rj^−θ` (Donaldson–Hornbeck 2016, QJE). No flows, no congestion, no `Allocate`; defer flow assignment to E2 and combat (M2–M3, #321). The player still decides which links to grow.
3. **strong — controller border (#349/#351).** A link between regions of different controllers goes through the RR-0014 gateway; upkeep by the controller, condominium by #351 shares; on a scripted controller change the network moves with the region. West Berlin is the reference case. Contradiction: "Berlin airlift" in v1 but link blockade in FUTURE — move the airlift to FUTURE or add blockade as an M2 atom/effect (`link_open(a,b,mode)`, `airfield_class(set,≥k)`), forbidden in M1 (#340).
4. **strong — start with communications and science.** Of the four `comm` effects, `c2` (no war) and `β` (no flows) have nothing to act on in M1; `k_diff` and `spec` work on today's core, and science is the centre of the game. Order: `comm` → `k_diff`/`spec` first.
5. moderate — priority corridor (≤ 3) as a new knob; derive corridors from #214 clusters, manual only for the army (#263 kept).
6. moderate — specify daily slicing of annual upkeep and wear (#345); replace monthly congestion smoothing with a cache refreshed on a fixed day.
7. weak — manual route drawing is excluded by #263 and worse; airfield class as a region attribute — keep.

Owner questions: Q1 may a scripted event or #299 branch depend on a random network exposure (#260 vs #342)? Q2 #263 build animation without PD/CC0/CC BY 1945 rail/road geometry — accept schematic segments between region centres for v1? Q3 accept "Φ + market access, no flow assignment" for v1, congestion to E2/M2?
