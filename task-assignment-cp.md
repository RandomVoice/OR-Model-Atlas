# 🏃 A Pattern Emerges — and a Complexity Cliff Appears

*From studying more LeetCode problems to seeing how a tiny parameter change flips
P → NP-complete (a CP case study)*

> Work through enough LeetCode problems and a **pattern keeps emerging**: many of them share
> the *same underlying shape* — pick values for some decision variables, respect a few
> relationships, optionally optimize — which is exactly what a **CP model** expresses. Two Sum,
> Coin Change, stable marriage, scheduling with cooldowns… once you see it, it is hard to
> unsee.
>
> But **this problem is particularly interesting** because it lets you *see the shape of
> complexity itself change with a small parameter change*. The headline isn't "model this in
> CP" — a greedy for pairing tasks is easy to produce. The idea worth owning is the **judgment
> underneath**: recognizing that a *tiny parameter change flips the entire complexity class* of
> a problem.
>
> **Pairing** items (group size 2) is a **matching** problem — polynomial, often a one-line
> greedy. Change the group size to 3 and the "same" problem becomes **3-Partition /
> 3-Dimensional Matching** — **NP-complete**, with no known efficient algorithm. That
> P → NP-complete cliff from a single knob (2 → 3) is the same flavor as **2-SAT vs. 3-SAT**.
> *Knowing where that cliff is, and why, is the kind of modeling judgment that outlasts any
> single algorithm.*
>
> The **standard way** to solve the easy size-2 case is a classic **sort-then-greedy** (sort the
> durations, then pair smallest with largest) — the idiomatic Python answer a coding interview
> expects. It is then re-framed as a **CP model** — because CP is the formulation that
> **survives the cliff**: change one constant (`count(where, w) == 2` → `== k`) and let the
> solver's search absorb the now-NP-hard problem, instead of inventing a new algorithm. It is
> one more piece of evidence that most **decision & optimization problems are the same
> underlying shape**.

---

## 🎯 The problem

Assign a set of tasks to workers so that the **time to complete all tasks is minimized**, given:

- a count of **workers**,
- an array where each element is the **duration of a task**,
- the rule that **each worker must work on exactly two tasks**.

Tasks are **independent**, and each worker's finish time is the **sum of their two task
durations**. Because workers run in **parallel**, the overall completion time is the **slowest
worker** — the **makespan**. So we are minimizing the **maximum per-worker load**.

- **Decision:** which worker each task is assigned to.
- **Objective:** minimize `max over workers (sum of that worker's task times)`.
- **Structural constraint:** exactly **two** tasks per worker (implies
  `numTasks == 2 * numWorkers`).

> **The classic connection:** with exactly two tasks per worker this is **LeetCode 1877 —
> "Minimize Maximum Pair Sum in Array"**: partition the array into pairs, minimize the largest
> pair sum. The *general version* (arbitrary tasks per worker) is **NP-hard** multiprocessor
> scheduling — the two-per-worker restriction is what collapses it to a polynomial greedy.

---

## 🔢 Small examples

**Example 1 — 2 workers, 4 tasks** `taskTime = [1, 3, 5, 9]`

- Bad pairing: `(5,9)=14` and `(1,3)=4` → makespan **14**.
- Balanced pairing: `(9,1)=10` and `(5,3)=8` → makespan **10**. ✅
- Pair the biggest (9) with the smallest (1); the rest fall into place.

**Example 2 — 3 workers, 6 tasks** `taskTime = [8, 2, 7, 3, 6, 4]`

- Sorted: `[2, 3, 4, 6, 7, 8]`.
- Pair extremes inward: `(2,8)=10`, `(3,7)=10`, `(4,6)=10` → makespan **10**. ✅
- Every worker gets exactly 10; you cannot do better here.

**Example 3 — why balancing matters** `taskTime = [1, 1, 1, 9]` (2 workers)

- Balanced: `(1,9)=10`, `(1,1)=2` → makespan **10**.
- No pairing avoids putting the 9 with *something*, so the best is to give it the **smallest**
  partner — the greedy's whole idea in one line.

---

## 🐍 The Python way (greedy: sort + pair the extremes)

The two-per-worker restriction gives a beautiful **O(n log n)** greedy: **sort the durations,
then pair the smallest with the largest** (two pointers moving inward). The makespan is the
maximum of those pair sums.

```python
def min_makespan_pairs(task_times):
    """Each worker gets exactly two tasks; minimize the slowest worker.
    Returns (makespan, pairing)."""
    times = sorted(task_times)
    i, j = 0, len(times) - 1
    makespan = 0
    pairing = []
    while i < j:
        pair_sum = times[i] + times[j]      # smallest with largest
        makespan = max(makespan, pair_sum)
        pairing.append((times[i], times[j]))
        i += 1
        j -= 1
    return makespan, pairing


# Example: 4 workers, 8 tasks
durations = [8, 4, 5, 2, 7, 1, 6, 3]
print(min_makespan_pairs(durations))
# makespan = 9  (pairs: (1,8), (2,7), (3,6), (4,5) — every pair sums to 9)
```

---

## 🧩 The CP formulation

The greedy is elegant but **brittle**: add one wrinkle — three tasks for some workers,
incompatible task–worker pairs, per-worker capacity, precedence — and the closed-form
collapses. The **CP model scales to all of those** because the constraints carry the logic and
the solver owns the search.

```opl
using CP;

int numWorkers = ...;
int numTasks   = ...;          // must equal 2 * numWorkers
int taskTime[1..numTasks] = ...;

// DECISION: which worker each task is assigned to.
// NOTE the domain `in 1..numWorkers` — an unbounded `dvar int` would let the
// solver point a task at a nonexistent worker (the CP cousin of forgetting x>=0).
dvar int where[1..numTasks] in 1..numWorkers;

// Per-worker load via BOOLEAN-AS-INT: taskTime is counted only when the
// 0/1 predicate (where[task] == w) is true. No `if`, no big-M, no aux binary.
dexpr int timeSpent[w in 1..numWorkers] =
   sum(task in 1..numTasks) taskTime[task] * (where[task] == w);

// OBJECTIVE: minimize the makespan = the slowest worker.
minimize max(w in 1..numWorkers) timeSpent[w];

subject to {
   // Each worker gets EXACTLY two tasks.
   // `count(where, w)` = how many tasks have value w — a GLOBAL constraint with
   // a dedicated propagator, tighter than a reified sum of (where[task]==w).
   forall(w in 1..numWorkers)
     count(where, w) == 2;
}
```

---

## ✨ Why this formulation is strong

1. **Bounded decision variable** — `where[...] in 1..numWorkers`. The most important line. An
   unrestricted `dvar int` ranges over all integers; restricting the domain to the worker set
   is what makes the model well-defined. *This is the CP discipline to internalize — the
   equivalent of never forgetting `x >= 0` in an LP.*

2. **Boolean-as-int objective — no big-M.** `taskTime[task] * (where[task] == w)` multiplies a
   duration by a **0/1 predicate**, so a task contributes to a worker's load **only if
   assigned**. In MIP I'd reach for an indicator or big-M with an auxiliary binary
   `x[task][w]`; CP inlines the logic and keeps the relaxation clean.

3. **Global constraint for cardinality** — `count(where, w) == 2`. "Each worker used exactly
   twice" is precisely what `count` / `distribute` expresses. It ships a **specialized
   propagator** that prunes harder than a hand-rolled `sum(task) (where[task]==w) == 2` (which
   is correct, but is a reified sum — the loose, generalist version).

4. **`max` in the objective is native.** CP minimizes `max(w) timeSpent[w]` directly. In MIP
   this needs an auxiliary variable `Cmax` plus `Cmax >= timeSpent[w]` for every `w` — CP skips
   the bookkeeping.

---

## 🌍 How this problem fits in the world of P vs NP

Three ideas make this problem a good teaching example:

- **A structural constraint can change the complexity class.** The general k-worker makespan is
  **NP-hard** (the partition / bin-packing family). The *exactly-two-per-worker* restriction
  collapses it to a **polynomial greedy**. Noticing that a single rule flips the difficulty is
  the core insight worth carrying to other problems.
- **For the clean size-2 case, the `O(n log n)` greedy is perfect.** But if the problem is
  likely to grow new rules (three tasks per worker, task–worker eligibility, capacities,
  precedence), the ~10-line CP model is the safer bet — it survives those additions unchanged.
- **Constraints carry the logic.** Bounded domains, boolean-as-int expressions, and global
  constraints (`count`, `inverse`, `NoOverlap`) let the *model* state the rules directly,
  instead of hand-rolling bookkeeping with auxiliary variables and big-M.

> **What I take away from this one:** my instinct used to jump straight to the greedy — sort,
> pair the extremes, done — and for the restricted case that still feels like the right first
> move. What changed for me is noticing *why* I now reach for the CP model instead when I sense
> the rules might grow: the greedy answers *this exact question*, but the CP model — bounded
> `where`, boolean-as-int makespan, and a `count` global constraint — still answers the question
> *after the rules change* (`count(where, w) == 2` → `== k`). The CP way of writing it is what
> survives the complexity cliff — one constant changes, and the same model keeps standing while
> the greedy underneath it has already collapsed.

---

## 📚 References

- **LeetCode 1877 — Minimize Maximum Pair Sum in Array.** Pair the array to minimize the
  largest pair sum. This IS the two-tasks-per-worker makespan.
- **LeetCode 881 — Boats to Save People.** *The same problem dressed differently:* sort, then
  two pointers pairing the heaviest person with the lightest (a boat holds two, under a weight
  limit). Same **sort + pair-extremes** greedy, same exchange-argument proof — only the story
  (boats/weights vs workers/durations) changes.
- **LeetCode 1723 — Find Minimum Time to Finish All Jobs.** The *general* k-worker makespan
  (arbitrary jobs per worker) — **NP-hard**; needs DP-over-subsets or binary-search +
  backtracking. This is what the two-per-worker restriction simplifies away.

---

## ⚠️ The complexity cliff: group size 2 is easy, ≥ 3 is NP-complete

What stuck with me on this problem is how **fragile** the easy case turned out to be. I'd have
called "assign tasks to workers to balance load" a single problem — but it isn't. With
**exactly two tasks per worker** it's polynomial (sort + pair extremes), and I could stop there
feeling like I'd solved it. The unsettling part was realizing that the moment the group size
moves — **three tasks per worker, or four, or an arbitrary number of jobs per machine** — the
problem **changes shape entirely** and becomes **NP-complete/NP-hard**. Same sentence describing
the problem, completely different world underneath:

- **Pairing (group size = 2):** this is a **matching** problem. General graph matching is in
  **P** (polynomial) — and our weighted "minimize the max pair sum" specialization even has a
  simple greedy.
- **Grouping into triples or more (group size ≥ 3):** this becomes **3-Partition /
  3-Dimensional Matching** territory — classic **NP-complete** problems. "Partition tasks into
  groups of 3 with balanced load" has **no known polynomial algorithm**.

That jump — **P at size 2, NP-complete at size ≥ 3** — is a famous complexity cliff (matching
vs. 3-dimensional matching; 2-SAT vs. 3-SAT is the same flavor).
