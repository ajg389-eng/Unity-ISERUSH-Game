# Production scheduling

Production uses `ProductionSimulation` by default. It is a plain C# event simulation
without Unity objects, frame time, random choices, or navigation callbacks.
`DeterministicProductionController` supplies the scene configuration and presents
the results. The previous production planner is bypassed while this mode is enabled.
Register/customer service continues through its existing service runner.

## Timing and execution

- Time is an integer number of milliseconds. Configured durations round up to that
  precision. Rendering follows the clock and never decides when a transfer completes.
- Every trip reserves the exact source items and destination input slots before
  departure. Shared source stock is reserved globally across all flows.
- Each worker walks to the source, picks up, walks to the destination, and places
  its cargo. Repositioning between trips is included. The default carry is four and
  the default pickup and placement interactions are each 0.5 seconds per batch.
- Grid paths are compiled when layout/assignment/configuration changes and reused.
  Unreachable flow nodes cannot receive deliveries. Each physical station has its
  own state even when its type and recipe match another station.
- Training enforces the existing 60/80/100/120 items/min transfer limit by spacing
  trip starts according to the transferred quantity. It does not shorten the
  configured 0.5-second interactions. Travel can further reduce actual throughput.
- Station cycles produce exactly one output each and consume their recipe's exact
  input quantities. Autonomous station processing overlaps worker trips.
- For shared grill slots, input reservations leave room for missing recipe inputs,
  preventing one ingredient from filling the grill and blocking its partner.
- Completion ties resolve in stable station order, worker order, then edge order.
  Finish processing, finish trip phases, start eligible station cycles, dispatch workers.
- Production workers use scheduled transforms with a kinematic body/trigger capsule
  while managed. Crowd physics cannot add an unscheduled delay. Those settings are
  restored when the worker leaves scheduled production.

## Forecasts and assumptions

The next-60-seconds forecast clones the *current* live simulation, including cargo,
remaining processing/trip times, reserved slots, stock, and pickup contents, then
calls the same `AdvanceTo` method that gameplay calls. The forecast includes
completions at the endpoint and does not change the live state.

It assumes no additional customer collections, stock deliveries, or player edits
during that window. These outside events invalidate the cached forecast. The UI
states that it uses current stock and pickup space. Ordinary rolling forecasts
refresh at most once per simulated second.

The rate readout is the delivered count in a ten-minute simulated plan after a
two-minute warm-up, divided by ten. That plan uses available ingredients and an
unblocked pickup. It is a scheduled average, not a claim that the absolute optimal
or mathematical maximum has been found. It is cached until configuration changes.
Do not replace it with the old station-sum, slowest-station, or staffing-ratio math.

Changing paths, recipes, staffing, movement speed, or interaction duration builds a
new schedule. Picked cargo survives compatible connected route/recipe changes.
Removing its source or firing/unassigning its carrier returns that cargo to kitchen
stock. Removing a delivery connection leaves carried items on the worker until a
compatible connection is restored.

Save version 16 records station buffers, processing time remaining, dispenser
credits, pickup food and freshness, carried items, and remaining transport pacing.
Loading reconstructs paths and replans unpicked reservations from saved positions.
It preserves items and processing progress; it does not promise identical future
trip ordering across a save/load boundary.

## Regression checks

Run the independent tests using the installed .NET 8 SDK:

```powershell
powershell -NoProfile -File Tools/TestProductionSimulation.ps1
```

The checks cover frame partition independence, forecast/execution equality, travel
and interaction delays, parallel merges, shared ingredient buffers, shared stock,
pickup blocking, unreachable routes, reservation cancellation, and flow identity.

`ProductionEditorChecks.cs` also provides a Unity integration fixture. It creates
a temporary Play Mode start scene while preserving the current scene, and never
selects or writes a player save slot. Run its `Prepare`, enter Play Mode, run
`VerifyLive`, exit Play Mode, then run `Cleanup`, using Unity CLI `run_script`.
The test verifies advancing frames, visible worker movement and deliveries, grill
buffer projection, and station/cargo checkpoint JSON round trips. Always clean up
the fixture and restore any generated TMP fallback-font changes after running it.

`ShakeSupplyEditorChecks.cs` reproduces the boundary-shelf placement that prevented
one worker from fetching Empty Cups for a Shake Station already holding milk. It
uses the real Pantry, Freezer, and Shake prefabs, verifies cup pickup and finished
shake delivery, and confirms removing the cup connection removes that route.
`MenuSelectionEditorChecks.cs` verifies existing menu checkboxes follow restored
selections without firing toggle/save callbacks. Temporarily copy either file
into `Assets/Editor` and execute `ShakeSupplyEditorChecks.Run` or
`MenuSelectionEditorChecks.Verify` in a batch Editor. These checks use transient
scenes/objects and never select or overwrite a player save. Remove the temporary
Editor copies afterward.
