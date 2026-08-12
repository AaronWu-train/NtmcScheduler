# M objective weight rebalance

## Goal

Rebalance the combined M schedule-quality and fairness objective so that the solver can trade minor work-pattern preferences for materially better staffing and fairness. The intended order inside fairness is rest fairness, then night-shift fairness, then early- and afternoon-shift fairness.

This change does not alter hard constraints, violation formulas, requested-rest priority, or T solver behavior.

## Problem

M currently minimizes `1000 * J4 + J5` after requested rest. The multiplier makes nearly every schedule-quality point more important than the entire practical range of fairness scores. As a result, the solver can preserve low-importance work-pattern preferences while producing visibly unfair schedules.

The LB11 observation has an additional cause. External staffing for LB02, LB04, and LB11 has a shared unpenalized allowance, so staffing within that allowance has zero objective cost. Reducing the allowance from 70 to 60 makes the penalty start sooner, but does not penalize the first 60 assignments.

## Considered approaches

1. Replace `1000` with `10`. This is the smallest conceptual change, but schedule-quality terms can still dominate fairness without an understandable item-by-item trade-off.
2. Keep separate multipliers for selected J4 terms. This provides detailed control but adds another objective tier and makes the effective weights harder to explain.
3. Remove the multiplier and directly weight every component. This is selected because each trade-off is visible in one table and low-importance pattern rules can be reduced independently.

## Selected objective

M continues to optimize requested rest first. Its second objective is one directly weighted sum containing the existing J4 and J5 violation measurements:

| Component | Weight |
|---|---:|
| External staffing above the allowance | 24 |
| Monthly general R deviation | 24 |
| Eight-week R1 balance | 12 |
| Work-streak length | 4 |
| Mixed-shift work streak | 3 |
| Night-rest-early pattern | 30 |
| Night-rest-afternoon pattern | 20 |
| Shift change without rest | 2 |
| Non-preferred rotation | 1 |
| Non-home-station assignment | 1 |
| Weekday rest fairness | 10 |
| Holiday rest fairness | 20 |
| Cross-station support fairness | 1 |
| Early-shift count fairness | 2 |
| Afternoon-shift count fairness | 2 |
| Night-shift count fairness | 8 |

The shared unpenalized external-staffing allowance for LB02, LB04, and LB11 changes from 70 to 60 assignments. LB09 remains penalized from its first external assignment.

## Rationale

- Rest fairness is deliberately strongest among fairness terms: holiday rest has weight 20 and weekday rest has weight 10.
- Night-shift fairness has weight 8, above early and afternoon fairness at 2 each, because uneven night work has the greater practical burden.
- Cross-station support fairness has weight 1. It only breaks otherwise similar solutions, while still discouraging the solver from concentrating all support work on a few employees.
- Mixed-shift work streaks, changes without rest, and non-preferred rotations fall to 3, 2, and 1. These remain tie-breaking preferences but should not justify obvious staffing or fairness defects.
- Night-rest-early and night-rest-afternoon retain weights 30 and 20 because avoiding those recovery patterns remains more important than ordinary shift consistency.
- Monthly R, eight-week R1, work-streak, and non-home-station weights retain their current unmultiplied ratios and definitions.
- External staffing retains weight 24 once it exceeds the allowance. A staffing improvement may therefore justify several low-importance pattern violations.

For example, after the allowance is exhausted, avoiding one external assignment is worth 24 points. Even if moving a worker to LB11 adds one mixed-shift streak, one shift change without rest, and one non-preferred rotation, the added cost is only `3 + 2 + 1 = 6`, so the solver prefers the staffing improvement when no stronger consequence prevents it.

The same conclusion does not apply while the shared allowance is unused: any of the first 60 LB02/LB04/LB11 external assignments still costs zero. If LB11 remains understaffed within that range, a later rule decision will be required to penalize all external staffing or distinguish LB11 from the shared allowance.

## Synchronized changes

Implementation must update the source-as-spec M objective construction and every layer that publishes or asserts the weights:

- M solver objective construction and allowance value
- soft-rule documentation and architecture/frontend descriptions of the combined objective
- decision record with the approved business rationale
- mathematical specification in `tex/main2.tex`
- CLI rule display
- focused and acceptance-test weight expectations

No violation measurement is renamed or moved to a new public objective group. The existing `ScheduleQualityAndFairness` output name remains.

## Verification

- Add or update a focused assertion that the LB02/LB04/LB11 shared allowance is 60 and that assignment 61 is the first penalized assignment.
- Assert the complete M component weight table in acceptance coverage.
- Recompute all reported component violations from returned schedules as existing acceptance coverage does.
- Run the focused M solver tests, the whole-month M acceptance test, the complete solver test project, and a Release build through `NtmScheduler.slnx`.
- Report `TimeLimit` separately from `Infeasible`; a legal incumbent remains valid even when optimality is not proved.

Existing example README edits are outside this change and must not be included in its commits.
