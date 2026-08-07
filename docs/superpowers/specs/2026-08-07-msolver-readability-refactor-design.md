# MSolver readability refactor

## Scope

Refactor `MSolver` without changing solver behavior, constraints, objective weights, candidate generation, or public contracts. `TSolver` is outside this pass.

## Considered structures

1. Keep three files and add section comments. Smallest file change, but leaves hard and soft rules together in a 628-line file.
2. Use four partial files: main flow, input, hard rules, and soft rules. This is the selected structure because each file answers one clear question without creating a helper layer.
3. Create one file per rule. Rejected because it fragments the source and makes the complete model harder to scan.

## File responsibilities

```text
MSolver.cs             Solve flow, lexicographic optimization, candidate search, result reading
MSolver.Input.cs       Snapshotting, validation, history and R/R1 input calculations
MSolver.HardRules.cs   Decision variables and hard constraints
MSolver.SoftRules.cs   Named soft violations and objective construction
```

No `Helpers`, rule class, catalog, encoder, DI, or per-rule file is added.

## Function boundaries

A function remains only when it performs an independently nameable operation, is reused without hiding the rule, or isolates non-model data preparation.

- Delete aliases that only shorten member access, such as `MonthStart`, `Employees`, and `Sum`.
- Keep top-level phases such as validation, variable creation, hard-constraint construction, objective construction, lexicographic solving, candidate search, and result reading.
- Keep time/history calculations when they express an independent business operation; rename them to state exactly what they return.
- Put a one-use calculation inside its consuming rule instead of creating a file-level helper.
- Write reified Boolean relationships directly beside the rule that uses them when a helper such as `And` or `IsEqual` would hide the equation.
- Keep a shared CP-SAT helper only when direct repetition would be longer and the helper name still exposes the mathematical meaning.

## Naming and comments

- Rule functions use business meanings, for example `AddExactlyOneAssignmentPerActiveDay` and `MeasureRequestedRestViolations`.
- Boolean and expression variables describe the represented quantity, not implementation mechanics.
- Each rules file has short section comments for variables, hard constraints, or soft objectives.
- Each rule has one concise English comment stating its scope and mathematical relationship.
- Abstract or non-obvious helpers explain their input, output, and why the abstraction exists. Obvious accessors and syntax are not commented.
- Avoid Rule IDs, `J1`–`J5`, decorative separators, and `#region` blocks.

## Verification

This refactor must preserve behavior. Existing solver tests and a Release build must pass. No new test is needed unless moving code reveals an untested behavioral branch or requires a logic change.
