# M Objective Weight Rebalance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace M's `1000 * J4 + J5` scaling with the approved direct weights and reduce the LB02/LB04/LB11 external-staffing allowance from 70 to 60.

**Architecture:** Keep the existing `ScheduleQualityAndFairness` objective. Change its fixed weights and allowance, and replace the six M fairness ranges with one shared normalized-absolute-deviation expression in `MSolver.SoftRules.cs`; then synchronize the human-readable specifications and behavior-based solver tests.

**Tech Stack:** .NET 10, C#, Google OR-Tools CP-SAT, MSTest, Markdown, LaTeX

## Global Constraints

- Do not change M hard constraints, objective names, surplus-night behavior, or T solver behavior.
- Keep `NtmScheduler.Core` free of OR-Tools and EF Core references.
- Update documentation and `docs/10-decisions.md` before production rule behavior.
- Preserve unrelated edits in both example README files.
- Do not commit until the user explicitly requests a commit.

---

### Task 1: Synchronize the approved specification

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/05-soft-rules.md`
- Modify: `docs/07-architecture.md`
- Modify: `docs/08-frontend.md`
- Modify: `docs/09-acceptance.md`
- Modify: `docs/10-decisions.md`
- Modify: `docs/11-implementation-plan.md`
- Modify: `tex/main2.tex`

**Interfaces:**
- Consumes: approved weight table in `docs/superpowers/specs/2026-08-12-m-objective-weight-rebalance-design.md`
- Produces: one consistent source specification for the solver and tests

- [x] **Step 1: Replace the M combined-objective description**

Describe M as optimizing J1 followed by the direct sum `J4 + J5`, with no 1000 multiplier.

- [x] **Step 2: Publish all approved component weights**

Use this exact order and relative values: external staffing 24, monthly R 24, R1 balance 12, work streak 4, mixed shift 3, night-rest-early 30, night-rest-afternoon 20, shift change without rest 2, non-preferred rotation 1, non-home station 0.1, weekday rest fairness 10, holiday rest fairness 20, support fairness 1, early fairness 2, afternoon fairness 2, and night fairness 30. Multiply ordinary weights by 10 in CP-SAT and Objective output. Report fairness violations in tenths and retain their readable integer weights.

- [x] **Step 3: Change the external-staffing formula**

Specify `max(0, LB02/LB04/LB11 external assignments - 60) + LB09 external assignments` in Markdown and LaTeX.

- [x] **Step 4: Append the decision record**

Record the approved rationale: night fairness is 30, holiday/weekday rest fairness is 20/10, early/afternoon fairness is 2/2; the three minor rotation-pattern penalties are reduced; support fairness remains only as a weight-1 tie-breaker.

- [x] **Step 5: Check documentation consistency**

Run: `rg -n "1000×J4|1000 \\times J4|70 人次|3:3:6|24000|15000|7000" AGENTS.md docs tex/main2.tex`

Expected: matches remain only in historical decisions and the design's discussion of the old behavior, not in current requirements.

### Task 2: Drive the production change from failing tests

**Files:**
- Modify: `tests/NtmScheduler.Solvers.Tests/SolverAcceptanceAssertions.cs`
- Modify: `tests/NtmScheduler.Solvers.Tests/MSolverTests.cs`
- Modify: `src/NtmScheduler.Solvers/MSolver.SoftRules.cs`
- Modify: `src/NtmScheduler.Cli/Program.cs`

**Interfaces:**
- Consumes: existing `MCandidate.Objectives`, `ObjectiveComponent.Weight`, and `MCandidate.ExternalAssignments`
- Produces: unchanged public objective structure with approved weights and allowance behavior

- [x] **Step 1: Update the failing objective behavior assertions**

In `AssertMSoftRules`, expect:

```csharp
(4, "ScheduleQualityAndFairness", [("ExternalStaffing", 240), ("MonthlyRest", 240), ("SpecialRestBalance", 120), ("WorkStreak", 40), ("MixedShiftWorkStreak", 30), ("NightRestEarly", 300), ("NightRestAfternoon", 200), ("ShiftChangeWithoutRest", 20), ("NonPreferredRotation", 10), ("NonHomeStation", 1), ("WeekdayRestFairness", 10), ("HolidayRestFairness", 20), ("SupportFairness", 1), ("EarlyShiftFairness", 2), ("AfternoonShiftFairness", 2), ("NightShiftFairness", 30)])
```

Recompute external staffing with a literal allowance of 60. Recompute all six M fairness violations as `floor(10 × Σ|n × cᵢ − T| ÷ n)` per station group, and expect early/afternoon/night weights 2/2/30.

- [x] **Step 2: Run tests to verify RED**

Run: `dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj --no-restore --maxcpucount:1 --disable-build-servers --filter "Name=Solve_MonthlySchedules_ReturnsNamedCandidate"`

Expected: FAIL because production still reports range-based fairness values and the old multiplied fairness weights.

- [x] **Step 3: Apply the minimal production change**

Delete `scheduleQualityMultiplier`, use the approved weights in the existing objective expression/component list, replace the allowance subtraction `70` with `60`, and update the CLI descriptions. Add one private helper for the shared fairness formula; add no configuration type or objective group.

- [x] **Step 4: Run tests to verify GREEN**

Run the same focused test command.

Expected: PASS with the approved weights and recomputed violations.

### Task 3: Verify the complete change

**Files:**
- Verify all modified files; create no additional production abstractions

**Interfaces:**
- Consumes: completed documentation, solver constants, CLI description, and tests
- Produces: an uncommitted, verified working tree for user review

- [x] **Step 1: Run focused M solver tests**

Run: `dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj --no-restore --maxcpucount:1 --disable-build-servers --filter "Name=Solve_Lb09ExternalStaffingIsAllowedAndImmediatelyPenalized|Name=Solve_MonthlySchedules_ReturnsNamedCandidate"`

- [x] **Step 2: Run the complete solver test project**

Run: `dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj --no-restore --maxcpucount:1 --disable-build-servers`

- [x] **Step 3: Build the solution in Release**

Run: `dotnet build NtmScheduler.slnx -c Release --no-restore --maxcpucount:1 --disable-build-servers`

- [x] **Step 4: Check the diff**

Run: `git diff --check` and inspect `git diff`. Confirm the two example README diffs are unchanged from the user's pre-existing edits and that no file is staged or committed.
