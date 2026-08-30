# Learning Column Generation by Rebuilding Cutting Stock in C#

*A journal of what I understood, what I had wrong, and what finally clicked.*

I have used CPLEX for years, but mostly by writing a model and letting the solver do the rest.
Column generation was a gap: I knew the shape of it, I could recite "master and pricing," and
I could not have told you why the pricing problem turns out to be a knapsack. So I rebuilt the
classic cutting stock example from scratch in C#, and wrote down what I learned on the way.

---

## The instance

```
Roll Width = 110

Demand:
Width 20 : 48
Width 45 : 35
Width 50 : 24
Width 55 : 10
Width 75 :  8
```

Cut rolls of width 110 into these pieces. Minimize the number of rolls.

A **pattern** is one feasible way to cut a single roll — a count of each width whose total
does not exceed 110:

```
Pattern A: [1,2,0,0,0]     1 × 20, 2 × 45     20 + 45 + 45 = 110
Pattern B: [3,0,1,0,0]     3 × 20, 1 × 50     20 + 20 + 20 + 50 = 110
```

A pattern is **maximal** if nothing else fits — the leftover is smaller than the narrowest
piece. Non-maximal patterns are wasteful versions of maximal ones, so a good model only needs
the maximal ones. That was the first small thing I had not thought about carefully.

---

## Two different ways to be naive

My first draft of this writeup treated "the naive approach" as one thing: list every pattern,
make a variable for each, solve. That is *a* naive approach. It is not the one I would
actually have written first, and pretending otherwise skipped the more interesting comparison.

### Naive #1 — think about rolls (the assignment model)

Don't think about patterns at all. Give every roll its own variables.

```
y_r  ∈ {0,1}    roll r is used
x_ir ∈ Z≥0      pieces of width i cut from roll r

minimize   Σ_r y_r

subject to
   Σ_i w_i · x_ir  ≤  110 · y_r      for each roll r     (capacity)
   Σ_r x_ir        ≥  d_i            for each width i    (demand)
```

You need an upper bound on the number of rolls up front — total demand always works.

This is the model I would have written on day one, and it has a real virtue: **nothing is
enumerated**. It is small. For this instance it is a few hundred variables, and CPLEX accepts
it without complaint.

It is also close to useless at scale, for two reasons I had to see before I believed them:

1. **The LP relaxation tells you nothing.** Let the `y_r` go fractional and demand smears
   across partial rolls. The LP optimum drops to `Σ d_i w_i / 110` = **44.41** — the total
   material divided by the roll width. That is a bound I can compute on a napkin without any
   model at all. Branch-and-bound with a bound that weak has nothing to prune with.
2. **Every roll looks like every other roll.** Rolls are interchangeable, so any solution has
   an enormous number of identical relabelings and the search revisits the same assignment
   over and over. Symmetry-breaking constraints help and belong in any fair benchmark of this
   model, but they don't fix the bound.

### Naive #2 — think about patterns (the Gilmore–Gomory model)

Now let each variable represent a *pattern*.

```
x_p = number of rolls cut using pattern p
```

```
dvar float+ Cut[Patterns];

minimize
   sum(p in Patterns) p.cost * Cut[p];

subject to
{
   forall(i in Items)
      sum(p in Patterns) p.fill[i] * Cut[p] >= Demand[i];
}
```

The variables no longer describe individual pieces. They answer a different question: *how
many times should each pattern be used?*

The symmetry disappears — identical rolls collapse into a count. And the LP bound is much
better; in practice it lands within about a roll of the integer answer. That tightness is the
whole reason to want this formulation.

The price is that the number of patterns explodes as the instance grows.

### Side by side

| | Assignment model | Pattern model |
| --- | --- | --- |
| Size | small, polynomial | one variable per pattern |
| Enumeration needed | none | all of them |
| LP bound | the trivial material bound | tight |
| Symmetry | severe | none |
| Usable as-is? | builds fine, solves badly | only if the pattern list is short |

Seeing them next to each other is what made the method make sense to me. One model is cheap to
build and useless to solve. The other is expensive to build and excellent to solve. **Column
generation is a way to get the second model's bound without paying its build cost.**

---

## An honest look at my own instance

My draft said the number of feasible patterns is "astronomically large." So I counted them.

```
Roll width 110, widths {20, 45, 50, 55, 75}

Feasible patterns:  26
Maximal patterns:   11
```

Eleven. I could write them on an index card.

The exponential blowup is real, but it is a statement about how the problem grows, not about
this instance. On this instance, enumerating everything is not just possible, it is the
sensible engineering answer.

I decided to keep that fact in rather than hide it, for two reasons. Knowing when the fancy
method is unnecessary seems like the more useful skill. And because the full pattern list is
buildable here, I can solve that LP directly and check that column generation reaches the same
objective — which turns this from a demonstration into a test.

---

## The question that took me longest

**Is enumerating all the patterns an optimization problem, or a data problem?**

I went back and forth on this, and settling it is what made the rest fall into place.

Listing the patterns is not algorithmically hard. The obvious recursion — pick how many
20s, recurse on what's left — never gets stuck, never backtracks into a dead end, and produces
each new pattern with a small, bounded amount of work. There is no cleverness in it and no
struggle.

The problem is purely that the output is enormous. So the failure mode of the naive pattern
model is not "the enumerator runs forever." It is "the list doesn't fit, and neither does the
LP built from it." That is a **data problem**: the cost is in materializing and storing the
set, not in searching it.

Which is exactly why column generation works. It refuses to build the list, and replaces the
question

> give me every pattern

with

> give me the single best pattern, according to prices I'll hand you

That swap is the whole trick: a data problem becomes an optimization problem, and that
optimization turns out to be cheap.

There's an architectural echo of this in the code. There is no `PatternRepository`, no pattern
cache, no generation step anywhere. The pattern set is never data sitting at rest. It only
exists as the feasible region of the pricing model, and patterns appear one at a time as the
*output* of solving something.

---

## The idea

Instead of:

```
Generate all variables
     ↓
Solve
```

do:

```
Generate a few variables
     ↓
Solve
     ↓
Generate useful new variables
     ↓
Solve
     ↓
Repeat
```

Put another way: **column generation is simplex with the variables created on demand.**

In ordinary simplex every column is known up front, and each iteration scans them to pick an
entering variable. Here most columns don't exist yet, so the scan has to become a search:
rather than checking reduced cost across a known list, we optimize reduced cost over a set we
never write down.

---

## The master problem

The restricted master contains only the patterns generated so far. Each solve gives:

```
Objective Value
Dual Values
Pattern Usage (x)
```

The output I had always ignored is the one that matters: the duals.

```
Master
   ↓
Dual Information
```

The dual on the demand constraint for width `i` is the marginal value of producing one more
piece of that width, measured in rolls. The master isn't only producing a solution — it's
producing a price list.

---

## The pricing problem

*Is there a pattern, not currently in the master, that would improve things?*

```
reduced cost = pattern cost − value the pattern provides

c̄ = 1 − Σ dual[i] · fill[i]
```

A pattern is worth adding if `c̄ < 0`.

Maximizing the value term, subject to the pattern physically fitting on a roll, gives:

```
maximize
   sum(i in Items) Duals[i] * Use[i];

subject to
{
   sum(i in Items) Size[i] * Use[i] <= RollWidth;
}
```

This is a knapsack problem. Value per item comes from the duals; weight is the piece width;
capacity is the roll.

This is the part I genuinely did not see coming. I knew pricing was "a subproblem," but the
fact that it lands exactly on knapsack — and that the duals are what supply the item values —
was the moment the method stopped being a recipe and started being an idea.

The duals shift every iteration, so it is never the same knapsack twice. The prices move as
the master's needs move.

---

## Why the stopping rule is a proof

```
Pattern value > pattern cost
     ↓
Reduced cost < 0
     ↓
Add the column
```

If the pricing problem comes back with no negative reduced cost, then *no* pattern has one —
including all the ones never generated, since pricing searched over all of them implicitly.
So the LP is optimal over the full pattern set, not just the handful we built.

That surprised me. The stopping condition is a certificate, not a guess.

---

## Why knapsack being NP-hard doesn't hurt

Knapsack is weakly NP-hard, which means pseudo-polynomial algorithms exist: the runtime scales
with the *magnitude* of the numbers rather than their number of digits.

For cutting stock that's benign. The roll width is a physical dimension — a few thousand at
worst — so the subproblem is genuinely fast.

The lesson I took: "NP-hard" was too blunt an instrument for the decision I was making. The
useful question wasn't hard-versus-easy, it was *which quantity* the difficulty scales with.

---

## The loop

```
Initial Patterns
     ↓
Solve Master (LP)
     ↓
Get Duals
     ↓
Solve Pricing
     ↓
Negative Reduced Cost?
   /        \
Yes          No
 ↓            ↓
Add Pattern  Stop
 ↓
Repeat
```

### A note on where I start

I start with one pattern per width, each cutting that width exactly once. That's deliberately
bad — a single 20 on a 110 roll wastes 90 — which makes the iteration trace worth watching. A
tighter start (as many copies as fit) converges faster and shows less. For learning, the weak
start was the right call. For production it wouldn't be.

---

## What I got wrong about the final solve

During column generation the master is an LP, because pricing needs duals and an integer
program doesn't have them. When pricing stops, I solve the master one last time as an integer
program over all generated patterns, and that gives the cutting plan you can actually execute.

My first draft said this produced *the* answer. It doesn't quite.

The stopping rule certifies that no missing column can improve the **LP**. It does not
certify that no missing column belongs in the best **integer** solution — a pattern that looks
unattractive at the LP optimum can still be the one you want once you're forced to whole
rolls.

So what this gives me is a good feasible solution plus the LP value underneath it, and
therefore a measurable optimality gap — not a proof of optimality. The proper name for what I
built is **price-and-branch**. Getting a proven-optimal integer answer needs
**branch-and-price**, re-solving pricing at every node of the search tree, with a branching
rule chosen so it doesn't wreck the knapsack structure.

On this instance the gap is negligible and price-and-branch is the right amount of machinery.
But writing "this produces the optimal plan" would have been wrong, and noticing that was one
of the more useful corrections I made to my own understanding.

---

## Why I rewrote it in C#

The IBM OPL example states the mathematics beautifully. What it hides is the *interaction*:

```
Master
Pricing
State
Results
Workflow
```

all of that lives inside OPL's scripting layer, where it stays implicit. Writing it in C#
forced me to name each piece:

```
State
-----
Pattern List

Workers
-------
MasterProblem
PricingProblem

Reports / DTOs
--------------
MasterResult
PricingResult

Orchestrator
------------
ColumnGenerationSolver
```

and the information flow became literal:

```
Patterns → MasterProblem → MasterResult → Duals
        → PricingProblem → PricingResult → New Pattern → Patterns
```

`MasterResult` and `PricingResult` are plain data objects with no solver dependency, so the
orchestration loop reads like the algorithm instead of like CPLEX plumbing. A side benefit I
didn't anticipate: the pricing solver could be swapped for a hand-written dynamic program
without touching anything else.

The point isn't that C# beats OPL for expressing a model. It's that OPL let me leave the
orchestration vague, and writing it out is what made me understand it.

---

## What I want to measure next

This writeup argues; it doesn't measure. The experiments I want to run:

- **Column generation vs. full enumeration** on this instance. With only 11 maximal patterns
  I can build the complete LP and confirm both reach the same objective — and count how many
  columns CG actually needed.
- **Pattern model vs. assignment model**, with symmetry-breaking added to the assignment model
  so the comparison isn't a strawman. Report LP bound, node count, and time.
- **How the LP–IP gap behaves** as I vary the number of widths and the piece-to-roll size
  ratio.
- **Starting basis sensitivity** — my weak start against a tight one, measured in iterations.

Reporting iteration and node counts rather than just wall-clock is what would make those
comparisons explain something instead of just ranking things.

---

## What I'd tell someone starting this

Three things I'd want to have known at the beginning:

1. Write both naive formulations first. The contrast between them *is* the motivation for
   column generation; starting from only one makes the method look like magic.
2. The duals are the product, not a byproduct. Once I saw the master as a device for producing
   prices, pricing-as-knapsack became obvious rather than surprising.
3. Count things about your own instance before describing them as intractable. Mine had 11
   useful patterns.
