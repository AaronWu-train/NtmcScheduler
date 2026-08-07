# Solver Readability Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make the M and T solver source readable as the model specification without changing solver behavior or public contracts.

**Architecture:** Keep M and T as independent partial static classes. Split each into main flow, input handling, hard rules, and soft rules; delete accessor wrappers and inline CP-SAT Boolean equations where a helper currently hides the model.

**Tech Stack:** .NET 10, C# 14, Google OR-Tools CP-SAT 9.15.6755, MSTest

## Global Constraints

- Do not change constraints, objective priorities, weights, candidate generation, public contracts, or status behavior.
- Do not share modeling code between M and T.
- Do not add a helper class/file, rule class, catalog, encoder, DI, Rule ID, or external configuration.
- Use English identifiers and comments; comments state business scope or non-obvious mathematics.
- Do not modify LaTeX or PDF files.

## File Map

```text
MSolver.cs             M solve flow, candidates, output
MSolver.Input.cs       M validation, history, R/R1 and time calculations
MSolver.HardRules.cs   M variables and hard constraints
MSolver.SoftRules.cs   M objectives and soft violations
TSolver.cs             T solve flow, candidates, output
TSolver.Input.cs       T validation, history, R/R1 and time calculations
TSolver.HardRules.cs   T variables and hard constraints
TSolver.SoftRules.cs   T objectives and soft violations
```

Delete `MSolver.Rules.cs` and `TSolver.Rules.cs` after moving their contents. Existing tests are the characterization tests for this behavior-preserving refactor.

---

### Task 1: Establish the green baseline

**Files:**
- Test: `tests/NtmScheduler.Solvers.Tests/MSolverTests.cs`
- Test: `tests/NtmScheduler.Solvers.Tests/TSolverTests.cs`

**Interfaces:**
- Consumes: existing `MSolver.Solve(...)` and `TSolver.Solve(...)`
- Produces: verified behavior before source-only refactoring

- [x] **Step 1: Run all existing solver tests**

```bash
dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false
```

Expected: all tests pass. Diagnose any failure before refactoring.

- [x] **Step 2: Keep the test scope minimal**

Confirm the suite covers legal M/T solving, input validation, cancellation, time limits, historical R/R1 continuation, and candidate generation. Add no test for private names or file organization.

---

### Task 2: Split and simplify MSolver

**Files:**
- Modify: `src/NtmScheduler.Solvers/MSolver.cs`
- Modify: `src/NtmScheduler.Solvers/MSolver.Input.cs`
- Create: `src/NtmScheduler.Solvers/MSolver.HardRules.cs`
- Create: `src/NtmScheduler.Solvers/MSolver.SoftRules.cs`
- Delete: `src/NtmScheduler.Solvers/MSolver.Rules.cs`
- Test: `tests/NtmScheduler.Solvers.Tests/MSolverTests.cs`

**Interfaces:**
- Consumes: `ScheduleInput`, `SolverOptions`, existing M constants and private records
- Produces: unchanged `MSolveResult Solve(ScheduleInput, SolverOptions?, CancellationToken)`

- [x] **Step 1: Move code by responsibility without changing statements**

Move variables and hard constraints to `MSolver.HardRules.cs`; objectives and soft violations to `MSolver.SoftRules.cs`. Keep solving, candidate search and result mapping in `MSolver.cs`. Keep validation, history, rest usage and time calculations in `MSolver.Input.cs`.

Use only short section comments: `Decision variables`, `Hard constraints`, `Soft objectives`, `Candidate generation`, and `Result mapping`.

- [x] **Step 2: Delete syntax-only wrappers**

Delete `MonthStart`, `Employees`, `Sum`, `Invalid`, `Cell`, and `IsFixed`. Replace them with the direct expression:

```csharp
var monthStart = input.DemandMonth.MonthStart;
foreach (var employee in input.DemandMonth.Employees) { }
model.Add(count == LinearExpr.Sum(dayVariables));
var cell = employee.Assignments.GetValueOrDefault(date);
return new(SolveStatus.InvalidInput, [], errors);
```

Keep the repeated business predicate as `IsEmployedOn`.

- [x] **Step 3: Rename retained M operations by result**

```text
TargetDates             -> TargetMonthDates
ModelDates              -> PlanningHorizonDates
LegalStations           -> StationsInSameGroup
StationGroup            -> StationGroupIndex
RequiredCoverage        -> RequiredHeadcount
IsHoliday               -> IsWeekendOrNationalHoliday
ShiftInterval           -> NormalShiftInterval
CellInterval            -> ResolvedWorkInterval
HasInsufficientRest     -> OverlapsOrLeavesLessThanMinimumRest
TrailingWorkDays        -> HistoricalWorkStreakLength
LastShiftSinceRest      -> HistoricalLastShiftSinceRest
LastEffectiveShift      -> HistoricalLastNormalShift
ShiftExpression         -> WorkShiftIndicator
AnyRestExpression       -> RestIndicator
MonthlyRestTarget       -> ExpectedMonthlyRestCount
OpeningUsage            -> OpeningRestUsage
PriorUsage              -> RestUsageBeforeModeledDates
StandardUsage           -> StandardRestCredit
TrySetRemainingTime     -> ConfigureRemainingSearchTime
SelectedChoices         -> SelectedAssignmentVariables
```

Comment `ConfigureRemainingSearchTime`, `WorkShiftIndicator`, and `RestIndicator`; their search/month boundary is not obvious from syntax.

- [x] **Step 4: Rename M rules to state their operation**

```text
AddOneAssignmentPerDay              -> AddExactlyOneAssignmentPerActiveDay
AddExactCoverage                    -> RequireExactStationCoverage
AddMinimumRestBetweenWork           -> ForbidOverlappingOrInsufficientlySeparatedWork
AddMaximumSixDaysWithoutGeneralRest -> RequireGeneralRestInEverySevenDayWindow
AddEightWeekRestQuotas              -> EnforceEightWeekRestQuotas
RequestedRestViolation              -> CountUnfulfilledRequestedRests
ExternalStaffingViolation           -> CountExternalStaffing
MonthlyRestDistributionViolation    -> MeasureMonthlyRestDeviation
NonHomeStationViolation             -> CountCrossStationAssignments
WorkStreakViolation                 -> MeasureWorkStreakPenalties
SameShiftBlockViolation             -> MeasureSameShiftBlockPenalties
ThreeDayPatternViolation            -> CountNightRestShiftPatterns
ShiftChangeWithoutRestViolation     -> CountShiftChangesWithoutRest
NonPreferredRotationViolation       -> CountNonPreferredRotations
RestFairnessViolation               -> MeasureRestCountRangeByStationGroup
SupportFairnessViolation            -> MeasureSupportCountRangeByStationGroup
```

Add one English comment above each rule stating its date/employee scope and enforced equation or measured amount.

- [x] **Step 5: Inline CP-SAT helpers that hide equations**

Delete `And`, `AndNot`, and `IsEqual`. Write their equations beside the consuming rule:

```csharp
var conjunction = model.NewBoolVar(name);
model.Add(conjunction <= left);
model.Add(conjunction <= right);
model.Add(conjunction >= left + right - 1);

var leftAndNotRight = model.NewBoolVar(name);
model.Add(leftAndNotRight <= left);
model.Add(leftAndNotRight + right <= 1);
model.Add(leftAndNotRight >= left - right);

var equalsShift = model.NewBoolVar(name);
model.Add(state == shiftCode).OnlyEnforceIf(equalsShift);
model.Add(state != shiftCode).OnlyEnforceIf(equalsShift.Not());
```

Keep the independent numeric formula as `BlockLengthPenaltyValue(int length)`. Inline the `AddElement` and conditional multiplication equations currently hidden by `LengthPenalty` into both consuming rules.

- [x] **Step 6: Show weights directly in objective construction**

Delete `MakeObjective`. Construct every `ObjectiveGroup` in `BuildObjectiveGroups` so the weighted total is visible beside its components:

```csharp
new(3, "MonthlyRestDistribution",
    monthlyRest * 4 + monthlySpecialRest * 8,
    [("MonthlyRest", 4, monthlyRest), ("MonthlySpecialRest", 8, monthlySpecialRest)])
```

Place `ModelVariables` at the bottom of `MSolver.HardRules.cs` and `ObjectiveGroup` at the bottom of `MSolver.SoftRules.cs`.

- [x] **Step 7: Run M tests**

```bash
dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MSolverTests" -m:1 /nodeReuse:false
```

Expected: all M tests pass unchanged.

---

### Task 3: Split and simplify TSolver

**Files:**
- Modify: `src/NtmScheduler.Solvers/TSolver.cs`
- Modify: `src/NtmScheduler.Solvers/TSolver.Input.cs`
- Create: `src/NtmScheduler.Solvers/TSolver.HardRules.cs`
- Create: `src/NtmScheduler.Solvers/TSolver.SoftRules.cs`
- Delete: `src/NtmScheduler.Solvers/TSolver.Rules.cs`
- Test: `tests/NtmScheduler.Solvers.Tests/TSolverTests.cs`

**Interfaces:**
- Consumes: `ScheduleInput`, `SolverOptions`, existing T constants and private records
- Produces: unchanged `TSolveResult Solve(ScheduleInput, SolverOptions?, CancellationToken)`

- [x] **Step 1: Apply the four-file split without sharing M code**

Move variables and hard constraints to `TSolver.HardRules.cs`; objectives and soft violations to `TSolver.SoftRules.cs`. Keep solving, candidate search and result mapping in `TSolver.cs`. Keep validation, history, rest usage and time calculations in `TSolver.Input.cs`. Delete the `MonthStart`, `Employees`, `Sum`, `Invalid`, `Cell`, and `IsFixed` wrappers and use direct member access, `LinearExpr.Sum`, direct result construction, and dictionary lookup. Keep the repeated business predicate as `IsEmployedOn`.

- [x] **Step 2: Apply T-specific names**

```text
TargetDates                      -> TargetMonthDates
ModelDates                       -> PlanningHorizonDates
IsHoliday                        -> IsWeekendOrNationalHoliday
ShiftInterval                    -> NormalShiftInterval
CellInterval                     -> ResolvedWorkInterval
HasInsufficientRest              -> OverlapsOrLeavesLessThanMinimumRest
TrailingWorkDays                 -> HistoricalWorkStreakLength
MonthlyRestTarget                -> ExpectedMonthlyRestCount
OpeningUsage                     -> OpeningRestUsage
PriorUsage                       -> RestUsageBeforeModeledDates
StandardUsage                    -> StandardRestCredit
TrySetRemainingTime              -> ConfigureRemainingSearchTime
SelectedChoices                  -> SelectedAssignmentVariables
FixedShift                        -> ShiftAssignedOnDate
Rotate                            -> NextMonthlyShift
AddOneAssignmentPerDay            -> AddExactlyOneAssignmentPerActiveDay
AddMonthlyShift                   -> RestrictWorkToAssignedMonthlyShift
AddMinimumRestBetweenWork         -> ForbidOverlappingOrInsufficientlySeparatedWork
AddMaximumSixDaysWithoutGeneralRest -> RequireGeneralRestInEverySevenDayWindow
AddEightWeekRestQuotas            -> EnforceEightWeekRestQuotas
RequestedRestViolation            -> CountUnfulfilledRequestedRests
MonthlyRestDistributionViolation  -> MeasureMonthlyRestDeviation
WorkStreakViolation               -> MeasureWorkStreakPenalties
AttendanceViolation              -> MeasureAttendanceShortfall
SpecialtyViolation               -> CountMissingSpecialties
AbilityViolation                 -> MeasureAbilityShortfall
NightToEarlyRestViolation        -> MeasureNightToEarlyRestShortfall
MonthBoundaryRestBalanceViolation -> MeasureMonthBoundaryRestDifference
RestFairnessViolation            -> MeasureRestCountRangeByMonthlyShift
```

- [x] **Step 3: Inline T equations and objective weights**

Delete T's `AndNot`, `LengthPenalty`, and `MakeObjective`. Inline the streak-end equations and show objective weights directly:

```csharp
var streakEnds = model.NewBoolVar(name);
model.Add(streakEnds <= work);
model.Add(streakEnds + nextWork <= 1);
model.Add(streakEnds >= work - nextWork);

new(2, "StaffingQuality",
    attendance * 9 + specialty * 3 + ability,
    [("Attendance", 9, attendance), ("Specialty", 3, specialty), ("Ability", 1, ability)])
```

Keep `BlockLengthPenaltyValue`, `ConfigureRemainingSearchTime`, and independently named time/history calculations.

- [x] **Step 4: Add only non-obvious T comments**

Comment that `ShiftAssignedOnDate` uses T月班別 in the target month and the next rotation in the extension; an actual historical Night cell alone starts night-to-early counting; X counts as actual work for rest spacing but not as a normal monthly-shift assignment.

- [x] **Step 5: Run T tests**

```bash
dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TSolverTests" -m:1 /nodeReuse:false
```

Expected: all T tests pass unchanged.

---

### Task 4: Verify both solvers together

**Files:**
- Inspect: `src/NtmScheduler.Solvers/MSolver*.cs`
- Inspect: `src/NtmScheduler.Solvers/TSolver*.cs`
- Test: `tests/NtmScheduler.Solvers.Tests/MSolverTests.cs`
- Test: `tests/NtmScheduler.Solvers.Tests/TSolverTests.cs`

**Interfaces:**
- Consumes: refactored M and T partial classes
- Produces: Release-buildable solver library with unchanged behavior

- [x] **Step 1: Check obsolete files and helpers are gone**

```bash
test ! -e src/NtmScheduler.Solvers/MSolver.Rules.cs
test ! -e src/NtmScheduler.Solvers/TSolver.Rules.cs
rg -n 'private static .*\b(MonthStart|Employees|Sum|MakeObjective|LengthPenalty|AndNot|IsEqual)\(' src/NtmScheduler.Solvers/MSolver*.cs src/NtmScheduler.Solvers/TSolver*.cs
```

Expected: file checks succeed and `rg` finds no obsolete helper calls.

- [x] **Step 2: Check structure and comments**

```bash
wc -l src/NtmScheduler.Solvers/MSolver*.cs src/NtmScheduler.Solvers/TSolver*.cs
rg -n '#region|Rule ID|J[1-5]|---{10,}' src/NtmScheduler.Solvers/MSolver*.cs src/NtmScheduler.Solvers/TSolver*.cs
```

Expected: eight responsibility files exist; the second command finds no forbidden markers.

- [x] **Step 3: Run all tests and the Release build**

```bash
dotnet test tests/NtmScheduler.Solvers.Tests/NtmScheduler.Solvers.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false
dotnet build NtmScheduler.slnx -c Release --no-restore -m:1 /nodeReuse:false
```

Expected: all tests pass and the solution has zero compilation errors. `NU1900` is acceptable only if the NuGet vulnerability feed is unavailable.

- [x] **Step 4: Inspect the final diff**

```bash
git diff --check
git diff --stat
```

Expected: no whitespace errors and no LaTeX/PDF or public-contract changes from this refactor.
