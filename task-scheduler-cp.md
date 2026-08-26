# Task Scheduler (LeetCode 621), CP style

*One `allDifferent`, one separation constraint, and the LeetCode formula falls out as the
closed-form optimum of the model.*

Given a list of tasks and a cooldown `n` between two **identical** tasks, find the minimum
total time to finish all of them (idle slots count as time). Each task takes 1 unit of time.

LeetCode wants the greedy/counting answer. The more useful framing is that this is a
**1-machine sequencing problem with a separation constraint** — and once you say it that
way, the constraint-programming model writes itself in two lines.

---

## The modeling decision

The MIP instinct is a binary `x[t][time] in {0,1}` for every task-slot pair, plus
`sum_time x[t][time] == 1` bookkeeping, plus a big-M disjunction (`before` OR `after`) for
every identical pair.

The CP instinct is to make the *time slot itself* the decision variable:

```
start[t] = the time slot in which task t runs
```

Everything else follows:

| Rule | CP expression |
| --- | --- |
| One task per slot | `allDifferent(start)` |
| Identical tasks ≥ `n+1` apart | `abs(start[t1] - start[t2]) >= n + 1` |
| Objective | `minimize max(start)` |

That's the whole model. No assignment matrix, no big-M, no indicator binaries.

---

## The model in OPL (CP Optimizer)

```opl
using CP;

int    numTasks = ...;
range  Tasks    = 1..numTasks;
string taskName[Tasks] = ...;   // e.g. ["A","A","A","B","B","B"]
int    n        = ...;          // cooldown between identical tasks

// Upper bound on the horizon: the worst case is (maxFreq-1)*(n+1)+maxFreq,
// but numTasks*(n+1) is a safe, simple bound.
range Time = 1..numTasks*(n+1);

dvar int start[Tasks] in Time;   // start[t] = the time slot task t runs in

dexpr int makespan = max(t in Tasks) start[t];

minimize makespan;
subject to {
   // Each task occupies its own unit slot.
   allDifferent(all(t in Tasks) start[t]);

   // Identical tasks must be at least (n+1) apart.
   forall(t1 in Tasks, t2 in Tasks:
          t1 < t2 && taskName[t1] == taskName[t2])
     abs(start[t1] - start[t2]) >= n + 1;
}
```

### Why this is the elegant core

- **`allDifferent` over start times** replaces "one task per slot" — no binary assignment
  matrix, no `sum(x[t][time]) == 1` bookkeeping.
- **`abs(start[t1] - start[t2]) >= n+1`** is the entire cooldown rule in one line. It is the
  CP analogue of the big-M disjunction you would need in MIP.
- **`max` over decision variables** gives the makespan for free as a `dexpr` — no extra
  variable, no `makespan >= start[t]` linking constraints.

---

## One refinement worth making

Identical tasks are **interchangeable**, so the model above contains pure symmetry: swapping
the start times of two `"A"` tasks gives a different solution with the same cost, and the
solver will explore both. Since we may assume without loss of generality that identical
tasks run in index order, the `abs` can be dropped entirely:

```opl
   forall(t1 in Tasks, t2 in Tasks:
          t1 < t2 && taskName[t1] == taskName[t2])
     start[t2] - start[t1] >= n + 1;   // WLOG ordered => symmetry broken, abs unneeded
```

This is strictly stronger than the `abs` version: it enforces the same separation *and*
removes the factorial symmetry among identical tasks. The `abs` form is the honest
first-pass model; this is the one you'd ship.

---

## The same model in CP-SAT

```python
from ortools.sat.python import cp_model


def min_total_time(task_name: list[str], n: int) -> int:
    m = len(task_name)
    horizon = m * (n + 1)

    model = cp_model.CpModel()
    start = [model.new_int_var(0, horizon - 1, f"s{t}") for t in range(m)]

    model.add_all_different(start)

    for i in range(m):
        for j in range(i + 1, m):
            if task_name[i] == task_name[j]:
                model.add(start[j] - start[i] >= n + 1)   # symmetry-broken form

    makespan = model.new_int_var(0, horizon - 1, "makespan")
    model.add_max_equality(makespan, start)
    model.minimize(makespan)

    solver = cp_model.CpSolver()
    status = solver.solve(model)
    if status not in (cp_model.OPTIMAL, cp_model.FEASIBLE):
        raise ValueError("infeasible")
    return solver.value(makespan) + 1          # 0-based slots -> total elapsed time
```

Two portability notes between the two CP stacks:

- OPL's `abs()` is usable directly inside a constraint. CP-SAT needs an auxiliary variable:
  `model.add_abs_equality(d, start[i] - start[j])` followed by `model.add(d >= n + 1)`.
  The symmetry-broken form above sidesteps this entirely.
- OPL's `dexpr max(...)` becomes `add_max_equality` on an explicit variable.

---

## The same logic in idiomatic Python (a checker, not a solver)

Given a candidate `start` assignment, these snippets verify feasibility and score it — the
same fluency idioms, one level down from the model:

```python
task_name = ["A", "A", "A", "B", "B", "B"]
n = 2
start = [1, 4, 7, 2, 5, 8]      # a candidate schedule (1-based times)

# allDifferent -> set-size equals list-size
feasible_slots = len(set(start)) == len(start)

# cooldown -> all() over identical-name pairs, chained with abs()
cooldown_ok = all(
    abs(start[i] - start[j]) >= n + 1
    for i in range(len(start))
    for j in range(i + 1, len(start))
    if task_name[i] == task_name[j]
)

makespan = max(start)           # the objective, as a one-liner
is_valid = feasible_slots and cooldown_ok
```

`len(set(x)) == len(x)` is the Python idiom for `allDifferent`; a generator inside `all()`
is the idiom for `forall`. The code reads like the model, not like solver plumbing.

---

## Why the LeetCode formula is the optimum of this model

The accepted closed form is

```
answer = max(len(tasks), (f_max - 1) * (n + 1) + k)
```

where `f_max` is the highest task frequency and `k` is how many task types hit it.

That is not a separate trick — it is the optimal value of exactly the model above. The most
frequent task forces `f_max - 1` gaps of length `n + 1`, then the `k` tasks tied at `f_max`
fill the final block. Everything else fits into the idle slots created by that skeleton,
unless there are simply more tasks than the skeleton has room for, which is the
`len(tasks)` branch.

Useful property for a portfolio piece: **you can validate the CP model against the closed
form** across random instances. If the solver ever disagrees with the formula, one of the
two is wrong, and you have a reproducible test rather than an opinion.

---

## Contrast with MIP

| | MIP (assignment matrix) | CP (slot-as-value) |
| --- | --- | --- |
| Variables | `numTasks × horizon` binaries | `numTasks` integers |
| One-task-per-slot | `sum` constraints in both directions | `allDifferent` |
| Cooldown | big-M disjunction + indicator binary per pair | one separation constraint per pair |
| Makespan | extra var + linking constraints | `dexpr max(...)` |
| Search | LP relaxation is fractional and weak here | domain filtering propagates immediately |

The point isn't that MIP can't do it — it's that the MIP encoding spends most of its size on
bookkeeping that CP gets from the choice of variable.


