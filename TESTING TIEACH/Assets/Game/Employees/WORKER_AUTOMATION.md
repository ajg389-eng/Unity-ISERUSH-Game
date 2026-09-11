# Worker automation — rebuild spec

Use this after the current Management Workers flow-path work is deleted. The game already had workers who **walk a job pipeline** if they are assigned to stations and those stations have **output links**. Automation is the layer that **picks a product path in the Workers tab**, then **assigns stations + outputs for you**.

## What the player sees

1. Worker names are **not editable**.
2. Workers tab has one row per product:
   - **Burger** — Freezer → Grill → Assembly → Heat Lamp
   - **Fries** — Fryer → Heat Lamp
   - **Drinks** — Drink station → pickup line
   - **Register** — take orders; also drink station if one exists
3. Clicking a product sets `ProductionManager.hireFlow` (new hires join that path).
4. **Assign idle** runs that path on every hired worker with **zero stations**.
5. The worker then walks those stations automatically when `ProductionManager` gives them a matching job.

## Files

Create:

- `Assets/Game/Employees/Scripts/WorkerFlowAssigner.cs`

Edit:

- `KitchenEmployee.cs` — `assignedFlow`, `ClearAllOperatedStations()`, job walking already exists
- `ProductionManager.cs` — `hireFlow`; after hire call `WorkerFlowAssigner.Apply(emp, hireFlow)`
- `WorkersUI.cs` — `EnsureFlowSection()` product rows
- `WorkerCardUI.cs` — name field `readOnly` / `interactable = false`; do not hook rename
- `StationNode.cs` — already has `SetWorker` / `SetOutput` (do not reinvent)

## Product paths (hard-coded)

| Kind | Stations to find | Outputs to wire |
|---|---|---|
| BurgerLine | Freezer, Grill, Assembly, HeatLamp | Freezer→Grill, Grill→Assembly, Assembly→HeatLamp. Set grill + assembly `selectedProduct` to burger from `orderConfig.burgerBase`. Do **not** `SetWorker` on Heat Lamp. |
| FriesLine | Fryer, HeatLamp | Fryer→HeatLamp |
| Drinks | DrinkStation | none (worker pours and walks to pickup) |
| Register | Register, plus DrinkStation if present | Register→Drink if both |

`KitchenEmployee.MaxStations` is **3**. Burger uses 3 work stations; heat lamp is output only.

One worker per station: `StationNode.SetWorker` steals the station from whoever had it. Prefer a station whose `assignedWorker` is null or is this employee. If every station of that type is taken, **fail** the apply (do not steal) so a second burger hire needs another freezer/grill/assembly.

## `WorkerFlowAssigner.Apply`

```
ClearAllOperatedStations()
Find each station type (open first)
If any missing → fail, assignedFlow = None
ConfigureProducts (burger only)
For i in stations:
  if not heat lamp: node.SetWorker(emp)
  if next exists: node.SetOutput(next)
assignedFlow = kind
SyncFromOperatedStations()
```

`ApplyToIdleWorkers(kind)`: every `production.employees` with `OperatedStationCount == 0`.

## Why they move by themselves

Already in `KitchenEmployee` + `ProductionManager` (keep this if you wipe only the UI/assigner):

1. `ProductionManager.Update` → `CollectProductionJobs` (one job per missing burger/fries item vs heat lamp) → `AssignJobsToEmployees`.
2. Job is given if `CanTakeJobStep` — worker’s `assignedStations` includes the job’s **current** station type (grill/assembly also need `CanProcess` product).
3. `AssignJob` starts the freezer/grill/assembly/fryer step machine.
4. After a station finishes, `FinishStepAndHandoff` uses **that station’s `StationNode.outputTarget`**. The worker walks there. If the output is on the product pipeline, the job’s step index jumps to that station. If the output is a heat lamp, they deliver the meal.

So automation = **assign the right operated stations + output chain**. Do not rewrite the walk/cook loop unless you deleted that too.

Pipelines from `CustomerOrderConfig.GetPipeline`:

- Burger: Freezer, Grill, Assembly (then heat lamp via output)
- Fries: Fryer
- Drink: empty pipeline (cashier/drink runner, not a cook job)

Drinks are skipped in `CollectProductionJobs`. Serve them in `RunRegisterDuty` (pour at drink station, walk to pickup customer).

## Hire hook

In `HireWorker` and `HireWorkerFree`, after `RegisterEmployee(emp)`:

```
WorkerFlowAssigner.Apply(emp, hireFlow);
```

`hireFlow` default `KitchenFlowKind.BurgerLine`.

## Workers UI

Runtime panel `FlowPathPanel` under the Workers tab (hint under hire header, list below).

Each row: product button (sets `hireFlow`) + path label + **Assign idle**.

Highlight the selected product with `HudTabColors.Apply`.

Status line: missing stations, or “New hires follow: Burger — Freezer → …”

Leave room in the worker card scroll (`offsetMax.y` around **-310**) so the panel is not covered.

## Names

On worker cards, `TMP_InputField.readOnly = true` and `interactable = false`. Do not listen to `onEndEdit`. Names still come from `KitchenEmployee.AssignRandomName()` on hire.

## Serving (if you also wipe pickup/delivery tweaks)

Minimum that made delivery easier:

- Idle **kitchen** workers (not only cashiers) may take ready heat-lamp items and walk to the **pickup customer** (`RunRegisterDuty` when `currentJob == null`).
- Cashiers send the front order-line customer to pickup; drink-only workers only pour drinks; other idle workers only fetch **food** from the heat lamp (`foodOnly`).
- `Register.TryDeliverItem` succeeds for **any** customer in the pickup list (not only index 0, no tiny arrival radius).
- Walk to `cashierCustomer.transform.position` and hand off within ~1.5 m (do not require standing on the register tile).

## Manual fallback (no automation)

Management mode: click station → Assign Worker (max 3) and Assign Output. That is the same data `WorkerFlowAssigner` writes. If automation is gone, that path still works.

## Check after rebuild

1. Place freezer, grill, assembly, fryer, drink, heat lamp, register.
2. Workers tab → Burger → Hire. Worker card shows Flow: Burger and Freezer → Grill, etc.
3. Unpause: worker walks freezer → grill → assembly → heat lamp without world-clicking.
4. Fries / Drinks / Register **Assign idle** puts a second idle hire on that line if stations are free.
5. Name field cannot be edited.
6. Food from the heat lamp can be walked to a waiting customer by an idle cook, not only the cashier.
