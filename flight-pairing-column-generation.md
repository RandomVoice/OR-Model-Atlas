# ✈️ Column Generation on Steroids

*How airline crew pairing turns the textbook decomposition into a self-bootstrapping column
factory — a CP + LP case study*

> Most optimization models we formulate are **monolithic** — write down all the variables, all
> the constraints, hand the whole thing to a solver, done. The **cutting stock problem** is
> different, and that's why it's something of a **crown jewel of optimization**: it doesn't
> solve the problem in one shot — it solves it **iteratively**, splitting the work into a
> **master problem** (which patterns to use) and a **sub-problem** (invent a new pattern worth
> adding). That master/sub-problem decomposition already makes it *architecturally* more
> interesting than almost any single-shot model we build.
>
> **This problem is that — on steroids.** Airline crew pairing keeps the same elegant
> master/sub-problem skeleton as cutting stock, but the textbook version adds only *one*
> improving column per iteration and grinds through hundreds of LP solves. This one does three
> things that push it far past the classroom:
>
> 1. **The pricing sub-problem is a Constraint Program**, not a knapsack — because "is this a
>    legal crew route?" is a tangle of airport-chain continuity, time windows, and duty-time
>    limits that CP expresses natively.
> 2. **Instead of adding one column at a time, it bootstraps a whole batch.** It finds the
>    single best negative-reduced-cost pairing, then *reuses that pairing's cost as a
>    constraint* to mass-produce more provably-improving columns.
> 3. **It uses randomization to keep the batch diverse** — random seeds plus a tabu on the best
>    pairing's length so the batch isn't a cluster of near-identical routes.
>
> The elegant move worth owning: **a reduced-cost *validation check* becomes a *search-space
> constraint*.** You don't generate pairings and then test them — you push the guarantee into
> the solver so it *only ever explores improving pairings*. That's the same instinct as reaching
> for `NoOverlap` instead of post-checking collisions: **let the constraint carry the logic.**

---

## 🎯 The problem — airline crew pairing

An airline runs a fixed daily schedule of flights across a set of airports. Crews must be
assigned to fly every flight. A **pairing** is a legal sequence of flights flown by a single
crew that:

- **starts and ends at the same crew base** airport,
- contains at least **3 legs** (individual flights),
- respects **connection time windows** at each airport (min/max layover),
- obeys regulatory **work-time and flying-time limits**.

*The goal:* choose a set of pairings that **covers every scheduled flight at least once** at
**minimum total crew cost**. This is a classic **set-covering problem**.

The data here comes from a real regional airline flying **5 Hawaiian airports** (HNL, OGG, LIH,
ITO, KOA) with **~150 flights** in a single operating day.

> **Cost model.** A crew earns whichever is largest — a minimum flat pay, a work-time rate, or a
> flying-time rate: `cost = max(minPay, workRate * workMin, flyRate * flyMin)`.

---

## ✳️ What a *pairing* really is — the pattern ↔ column ↔ pairing chain

The single most important idea for understanding this problem: **a pairing is a column, and a
column is a pattern.** The same abstraction wears three costumes:

| Domain | The "building block" | The master picks |
| --- | --- | --- |
| Cutting stock | a **pattern** — "cut this raw roll into these widths" | how many of each pattern to run |
| Column generation (generic) | a **column** — a candidate variable | how much of each column to use |
| Crew pairing | a **pairing** — "this crew flies these flights" | which pairings to fly |

In cutting stock the master covers *width demand*; in crew pairing it covers *every scheduled
flight at least once*. Same shape — a **set-covering / covering master** that selects building
blocks to satisfy demand at minimum cost.

> **The connection to remember:** *pattern* (cutting stock) = *column* (Dantzig–Wolfe) =
> *pairing* (crew scheduling). Once you see it, every covering problem looks the same: a master
> that chooses building blocks, and a sub-problem that invents new ones on demand.

---

## 🚫 Why you can't enumerate all pairings

The naïve approach — list every pairing, hand the giant set-cover to a MIP — is **impossible**,
and worse than in cutting stock:

- In **cutting stock**, a pattern is just a **multiset of widths** that fits the roll — already
  astronomically many.
- In **crew pairing**, a pairing is not a subset — it's an **ordered, time-feasible path**
  through the flight network. Every legal chain of connections, layovers, and return-to-base
  routes multiplies the count.

For a real schedule that's **billions** of feasible pairings. You can't list them, can't store
them, can't solve a MIP over them. So you **generate columns on demand** — and only the ones
that can actually improve the solution.

> This is the whole reason column generation exists: *most columns are useless.* The art is
> generating only the few that matter.

---

## 💰 Reduced cost — the "is this pairing worth it?" test

Column generation alternates between two coupled problems:

**Master (LP relaxation)** — *"Which pairings to use?"* Minimise total cost so every flight is
covered. Solved as a continuous LP during iteration, then once as an integer MIP at the very
end.

**Pricing sub-problem** — *"Is there a better pairing?"* After solving the LP, each flight's
covering constraint has a **dual price** `π_f` — the marginal value of covering flight `f`. A
new pairing is worth adding only if its **reduced cost is negative**:

```
reduced_cost(p) = cost(p) - Σ_f π_f · [pairing p covers flight f]  <  0
```

In plain English: **does this crew route cover enough high-value flights to more than pay for
itself?** If no pairing has negative reduced cost, the LP is **provably optimal** and column
generation stops.

So far, this is identical in spirit to cutting stock. Here's where crew pairing diverges — in
two big ways.

---

## ⚡ Twist #1 — the pricing problem is a **CP**, not a knapsack

In **cutting stock**, the pricing sub-problem is a clean **knapsack**: pack widths to maximise
dual value under a length budget — a textbook DP.

In **crew pairing**, "is this a *legal* pairing?" is a knot of combinatorial feasibility:

- **airport-chain continuity** — `toAirport(leg L) == fromAirport(leg L+1)`,
- **connection windows** — `arrival(L) + transitMin ≤ departure(L+1) ≤ arrival(L) + transitMax`,
- **return to base** — `fromAirport(first) == toAirport(last)`,
- **minimum leg count** and **work/fly-time caps**.

Encoding all of that in a MIP means piles of **auxiliary binaries and big-M switches** that
wreck the LP relaxation. **CP is the natural fit** — these constraints are native, they
**propagate**, and the solver searches the feasible *path space* directly. The pricing objective
becomes a CP model:

```opl
// GenerateBestPairing.mod (pricing) — maximise dual value captured
dexpr float dualObj = sum(f in FlightRange) duals[f] * (count(flightVars, f) >= 1);
minimize 1 - dualObj;   // negative optimum ⇒ an improving column exists
```

> **The judgment:** cutting stock's pricing is a *packing* problem (knapsack); crew pairing's
> pricing is a *routing-feasibility* problem (CP). Recognising that the sub-problem's
> **structure** dictates the solver technology is the senior modelling call.

---

## 🚀 Twist #2 — bootstrap a **batch** from the best column

Naïve column generation adds **one** column per iteration → hundreds of LP solves. This
architecture mass-produces columns in two steps:

### Step 1 — Find the single best column

The CP solver minimises `1 - Σ π_f · covered(f)` and returns the pairing with the **most
negative reduced cost**. Record its cost as `costMax`.

### Step 2 — Turn that result into a constraint and harvest a batch

The key observation: the best pairing already satisfied `Σ π_f > cost` (negative reduced cost).
Therefore **any feasible pairing cheaper than `costMax` is automatically also
negative-reduced-cost** — no re-check needed. Re-running the CP with a **hard cost ceiling**
then yields an entire batch:

```opl
// GenerateOnePairingWithMaxCost.mod (random batch generator)
costVar < costMax;                    // ← the best column's cost becomes the guarantee
pairingLengthExp != tabuLength;       // ← crude diversity: avoid the best pairing's length
// ... run repeatedly with a different RandomSeed each time
```

Run this `NumberOfRandomColumns` times (default **12**) with a different `RandomSeed` each pass.
**Randomisation forces structural diversity** — the batch spreads across different routes
instead of collapsing onto near-duplicates of the best one.

| Source | Columns added | Negative reduced cost guaranteed by |
| --- | --- | --- |
| `GenerateBestPairing.mod` | 1 (global best) | Minimising `1 - Σ π_f · covered(f)` directly |
| `GenerateOnePairingWithMaxCost.mod` | up to `NumberOfRandomColumns` | Hard constraint `costVar < costMax` |

> **The elegant move:** a reduced-cost *check* (an a-posteriori validation) becomes a
> cost-ceiling *constraint* (a-priori search pruning). The solver never even explores
> non-improving pairings. **One proven column bootstraps a whole batch.**

---

## 🔁 The full loop

```
PHASE 1 — Initialisation (CP)
  For each flight, CP generates ≥1 feasible pairing covering it
  → a small but feasible starting column pool

PHASE 2 — Column generation loop
  repeat:
    LP (LPCovering):     solve relaxation, extract duals π_f, dualHash = Σ π_f
    if duals barely changed since last iteration: break      ← converged
    CP (GenerateBestPairing):            find best reduced-cost column, cost = costMax
    CP (GenerateOnePairingWithMaxCost):  batch of random columns with costVar < costMax
    append all new columns to the pool

PHASE 3 — Integer solution (MIP)
  MIP (MIPCovering): solve 0-1 set cover over the full column pool
  → final optimal integer crew assignment
```

**Termination** is detected by the **stability of the dual hash** `Σ π_f` (more numerically
stable than watching the objective). Stable duals ⇒ the LP is no longer improving ⇒ no column
with negative reduced cost can exist.

---

## 🔬 Worked example — six real flights, start to finish

The following traces the full algorithm on **six real flights** from the schedule, all based at
Honolulu (HNL), so each step maps to concrete numbers.

### The mini-schedule

| # | Flight | Route | Departs | Arrives |
| --- | --- | --- | --- | --- |
| 1 | HNL_OGG_302 | HNL → OGG | 5:15 AM (315) | 5:49 AM (349) |
| 2 | HNL_LIH_201 | HNL → LIH | 5:30 AM (330) | 6:05 AM (365) |
| 3 | OGG_HNL_33 | OGG → HNL | 6:12 AM (372) | 6:45 AM (405) |
| 4 | LIH_HNL_272 | LIH → HNL | 6:30 AM (390) | 7:00 AM (420) |
| 5 | HNL_OGG_204 | HNL → OGG | 7:45 AM (465) | 8:19 AM (499) |
| 6 | OGG_HNL_275 | OGG → HNL | 9:03 AM (543) | 9:35 AM (575) |

Times in parentheses are **minutes from midnight**. Connection rules: **HNL** requires a
**20–60 min** layover; **OGG** and **LIH** require **10–60 min**.

### Phase 1 — CP generates initial pairings

The CP model is called once per flight to find at least one valid pairing covering it. A pairing
must start and end at the same base (HNL here), respect the connection windows, and contain
**≥ 3 legs**.

> **Why 3 legs minimum?** The constraint `pairingLengthExp >= 3` prevents trivial 2-leg
> out-and-back pairings.

A real 4-leg pairing the CP finds, `HNL → LIH → HNL → OGG → HNL`, shows the feasibility checks
in action:

```
Leg 1  HNL_LIH_201   HNL → LIH   dep 5:30  arr 6:05
       layover at LIH: 6:30 - 6:05 = 25 min  ✓ (10-60)
Leg 2  LIH_HNL_272   LIH → HNL   dep 6:30  arr 7:00
       layover at HNL: 7:45 - 7:00 = 45 min  ✓ (20-60)
Leg 3  HNL_OGG_204   HNL → OGG   dep 7:45  arr 8:19
       layover at OGG: 9:03 - 8:19 = 44 min  ✓ (10-60)
Leg 4  OGG_HNL_275   OGG → HNL   dep 9:03  arr 9:35
Returns to HNL ✓
```

At the end of Phase 1 the initial column pool might look like:

| Pairing | Legs (route) | Covers flights | Cost |
| --- | --- | --- | --- |
| P₁ | HNL→OGG→HNL (early) | 1, 3 | 148 |
| P₂ | HNL→LIH→HNL | 2, 4 | 144 |
| P₃ | HNL→LIH→HNL→OGG→HNL | 2, 4, 5, 6 | 290 |

**Cost calculation** for P₁ (`HNL_OGG_302` + `OGG_HNL_33`):

```
work time    = 405 - 315 = 90 min                        →  floor(1.11 × 90)  =  99
flying time  = (349-315) + (405-372) = 34 + 33 = 67 min   →  floor(2.22 × 67)  = 148
cost         = max(minPay=100, 99, 148) = 148
```

Flights 5 and 6 are only covered by P₃ so far. The LP master is **feasible** — every flight has
at least one pairing covering it.

### Phase 2 — the column generation loop

**LP Iteration 1 — solve the relaxation.** Minimise `148·x₁ + 144·x₂ + 290·x₃` subject to each
flight being covered at least once:

```
Flight 1 (HNL_OGG_302):  x₁                ≥ 1
Flight 2 (HNL_LIH_201):        x₂ + x₃     ≥ 1
Flight 3 (OGG_HNL_33):   x₁                ≥ 1
Flight 4 (LIH_HNL_272):        x₂ + x₃     ≥ 1
Flight 5 (HNL_OGG_204):             x₃     ≥ 1
Flight 6 (OGG_HNL_275):             x₃     ≥ 1
```

**LP optimum:** `x₁ = x₂ = x₃ = 1`, objective = **582**. The LP hands back the dual values `π_f`
— the marginal value of covering each flight:

| Flight | π_f (dual) | Meaning |
| --- | --- | --- |
| 1 | 148 | Covering this flight is expensive |
| 2 | 0 | Already cheaply covered by x₂ |
| 3 | 0 | Covered by the same pairing as flight 1 |
| 4 | 0 | Already cheaply covered by x₂ |
| 5 | 145 | Only covered by expensive P₃ |
| 6 | 145 | Only covered by expensive P₃ |

**CP pricing — find the best new column.** The CP minimises `1 - Σ π_f · covered(f)`, hunting
for a pairing that covers the *high-dual* flights. A pairing covering only flights 5 and 6 would
score:

```
reduced cost = 1 - (π₅ + π₆) = 1 - (145 + 145) = -289   ← very negative → great column!
```

Suppose CP finds **P₄**: `HNL → OGG → HNL` (the *later* out-and-back covering flights 5 and 6):

```
Leg 1  HNL_OGG_204   HNL → OGG   dep 7:45  arr 8:19
       layover at OGG: 9:03 - 8:19 = 44 min  ✓
Leg 2  OGG_HNL_275   OGG → HNL   dep 9:03  arr 9:35

work time    = 575 - 465 = 110 min                       →  floor(1.11 × 110) = 122
flying time  = (499-465) + (575-543) = 34 + 32 = 66 min   →  floor(2.22 × 66)  = 146
cost = max(100, 122, 146) = 146
```

P₄ (cost 146) is **cheaper** than P₃ (cost 290) for covering flights 5 and 6. It's added to the
pool — **and this is exactly where Twist #2 fires**: with `costMax = 146` locked in, the random
generator (`costVar < 146`) harvests a *batch* of extra diverse pairings at similar cost, all
guaranteed improving, in the same iteration.

**LP Iteration 2 — re-solve.** With P₄ available, the LP swaps P₃ out for P₂ + P₄:

```
x₁ = 1  (P₁, covers 1,3,  cost 148)
x₂ = 1  (P₂, covers 2,4,  cost 144)
x₄ = 1  (P₄, covers 5,6,  cost 146)
Total = 438   (was 582, saving 144)
```

The duals shift; the next pricing round may surface still more columns. This repeats until the
**dual hash stabilises** — the signal that no pairing can improve the LP any further.

### Phase 3 — the final MIP

`MIPCovering` re-solves the covering problem over the full pool with **binary** variables (each
pairing is fully used or not). The integer-optimal solution:

```
P₁  HNL → OGG → HNL (early)     cost 148   covers flights 1, 3
P₂  HNL → LIH → HNL             cost 144   covers flights 2, 4
P₄  HNL → OGG → HNL (later)     cost 146   covers flights 5, 6

Total crew cost: 438    All 6 flights covered ✓
```

This is the **optimal crew assignment** for the mini-schedule.

> **Why column generation was necessary.** With 6 flights there are only a handful of valid
> pairings. But the real 150-flight schedule has *tens of thousands* — the full-column LP would
> be enormous. Column generation solves it **implicitly**: each iteration only ever adds columns
> that can actually improve the objective. By convergence the pool is small and manageable — yet
> **provably contains the optimal solution.**

---

## ✨ Why this formulation is strong

1. **Column generation makes the impossible tractable.** Billions of pairings can't be
   enumerated; pricing generates only the improving few. Same idea as cutting stock, one problem
   class up in difficulty.
2. **CP is the right tool for the pricing sub-problem.** Airport chains, time windows, and duty
   limits are *native* CP constraints with dedicated propagators — no auxiliary binaries, no
   big-M, no wrecked relaxation.
3. **The negative-cost-as-constraint trick amortises the search.** One proven column's cost
   becomes a ceiling that guarantees a whole batch is improving — turning a validation into a
   pruning constraint.
4. **Randomisation buys diversity for free.** Re-running the same CP with different seeds (plus a
   length tabu) yields structurally varied columns, so each iteration injects genuinely new
   information into the master.
5. **Dual-hash convergence is numerically robust.** Watching `Σ π_f` stabilise is steadier than
   chasing the objective value.

---

## 🌍 Where this sits in the OR landscape

Three properties make crew pairing a benchmark for *practical* column generation:

- **The same abstraction spans three domains.** Pattern → column → pairing. Every covering
  problem — cutting stock, vehicle routing, crew scheduling — reduces to the same structure: a
  master that selects building blocks and a sub-problem that generates them on demand.
  Recognising that structure across domains is what lets a single decomposition framework be
  *reused* rather than rebuilt per problem.
- **The sub-problem's structure picks the solver.** Knapsack pricing → DP; routing-feasibility
  pricing → CP. That's a *modelling* decision, not a syntax choice.
- **The accelerations are where theory meets production.** Multi-column batching and
  negative-cost-as-constraint are absent from most textbook treatments, yet they are what make
  column generation tractable on a real airline schedule.

> **The one thing to remember:** crew pairing is column generation where a "column" is a **legal,
> time-feasible crew route**. You price columns by reduced cost using LP duals; the pricing
> problem is a **CP** because feasibility is combinatorial; and instead of adding one column at a
> time, you **find one improving pairing, reuse its cost as a hard constraint, and randomise to
> bootstrap a diverse batch.** A reduced-cost *check* becomes a search *constraint* — that's the
> steroids.

---

## 🔍 Cutting stock vs. crew pairing — the full comparison

The two problems share one skeleton and diverge in the details. The table below is the reference
for the whole write-up — every row is a point where crew pairing either **matches** cutting stock
or **escalates** it.

| Dimension | Cutting stock (the classic) | Crew pairing (on steroids) |
| --- | --- | --- |
| Building block | a **pattern** — a multiset of widths cut from one roll | a **pairing** — an ordered, time-feasible sequence of flights for one crew |
| What "a column" encodes | how much of each width the pattern yields | which flights the crew covers, and in what legal order |
| Master problem | covering: meet each width's demand | covering: cover **every flight** at least once |
| Master variable | `x_i` = how many times pattern *i* is run | `x_p` = whether/how much pairing *p* is used |
| Column feasibility | fits the roll length — a simple capacity check | return-to-base + connection windows + work/fly caps + ≥3 legs |
| Pricing sub-problem | a **knapsack** (pack widths under a length budget) | a **CP feasibility/optimisation** model (search legal routes) |
| Pricing solved by | dynamic programming | constraint propagation + search |
| Why the sub-problem differs | feasibility = one linear capacity constraint | feasibility = a web of combinatorial scheduling rules |
| Reduced-cost test | `1 - Σ π_w · (widths in pattern) < 0` | `cost(p) - Σ π_f · covered(f) < 0` |
| Columns added per iteration | classically **one** (best entering column) | a **batch** — 1 best + N random, via a cost-ceiling constraint |
| Termination | no negative-reduced-cost pattern exists | dual hash `Σ π_f` stabilises (no improving pairing) |
| Final integer step | round/branch the pattern counts | MIP over the harvested pairing pool |

### The three things that stay *identical*

Despite the escalation, the **algorithmic contract is the same** — this is why learning cutting
stock transfers directly:

1. **Generate on demand, never enumerate.** Both have astronomically many building blocks; both
   create columns only when a column can improve the objective.
2. **Reduced cost is the entrance exam.** A new column is admitted **iff** its reduced cost is
   negative — `reduced cost = own cost - dual value captured`. Same formula, same meaning,
   different building block.
3. **"No improving column ⇒ stop" is the same stopping rule.** When pricing can't find a
   negative reduced-cost column, the LP relaxation is provably optimal. Cutting stock and crew
   pairing share the exact termination test.

### The three things that *escalate*

1. **Knapsack → Constraint Program.** Cutting stock's pricing is a clean packing DP. Crew
   pairing's pricing is a routing-feasibility search — the sub-problem's *structure* forces the
   solver technology to change (DP → CP).
2. **One column → a bootstrapped batch.** Cutting stock adds the single best entering column.
   Crew pairing turns that best column's cost into a **ceiling constraint** (`costVar < costMax`)
   and mass-produces a whole diverse batch — provably improving, no re-check needed.
3. **Deterministic → randomised diversity.** Cutting stock's entering column is deterministic.
   Crew pairing deliberately injects **random seeds + a length tabu** so the batch spreads across
   structurally different routes.

> **The realization in one line:** column generation is *domain-agnostic* — swap the
> pattern-knapsack pricing sub-problem for a CP feasibility model and cutting stock becomes crew
> pairing. The *"reduced cost < 0 ⇒ add column, else stop"* contract is identical; a cost ceiling
> below the best column is a free way to mass-produce more improving columns.

### The deeper root: it's all simplex

One level down, even cutting stock isn't new — it's **simplex with variables generated on
demand**. Standard simplex assumes every variable exists upfront and picks the entering one by
*scanning* reduced costs. Column generation replaces that scan with a **pricing sub-problem** that
*searches* for the most-negative-reduced-cost column, because the full variable set is far too
large to list. Cutting stock searches with a knapsack; crew pairing searches with a CP. **Same
engine, three gears.**

---

## 📋 Techniques summary

| Aspect | Technique |
| --- | --- |
| Problem type | Set Covering (NP-hard) |
| Decomposition | Dantzig–Wolfe column generation |
| Master problem | LP relaxation → MIP |
| Pricing sub-problem | **Constraint Programming** (routing feasibility) |
| Convergence criterion | Stability of the LP dual-variable hash `Σ π_f` |
| Acceleration | **Multi-column entry** — 1 best + random batch per iteration |
| Batch guarantee | **Negative-cost-as-constraint** — `costVar < costMax` |
| Diversity | Randomised seeds + length tabu |
| Industry | Transportation / airline operations |

---

## 🔗 Related write-ups

- `task_assignment_cp.md` — where a tiny parameter change (group size 2 → 3) flips a problem from
  P to NP-complete, and why CP is the formulation that survives the cliff. Same theme: *let the
  constraints carry the logic.*
