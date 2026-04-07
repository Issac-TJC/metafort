# MetaFort Architecture Review

Date: 2026-04-07

## Findings
1. High: item data-driving was blocked by an invalid `item_config.json`, which could prevent planner and item features from loading at all.
2. High: `EntityManager.DestroyEntity()` recycled entity ids without removing attached components, leaving stale component records behind.
3. High: several systems depended directly on `GameEntry.Instance`, which reduced hot-plug flexibility and made pure logic harder to test in isolation.
4. Medium: planner input previously bound UI and controller with direct C# events instead of a shared bus/interface boundary.
5. Medium: item behaviors were selected via string `if/else` branches inside `ItemSystemNode`, which prevented plugin-style extension.
6. Medium: `PlanningControlTestVillager` and `VillagerCanvasRenderer` redrew every frame, even when their visual state had not changed.
7. Medium: an excluded duplicate UI file, `Scripts/UI/BuildingPlannerUI.cs`, risked implementation drift and duplicate definitions.

## Addressed In This Pass
- Repaired item config loading and added validation so invalid JSON now fails fast.
- Added component cleanup during entity destruction.
- Moved key pure systems toward explicit dependency injection instead of singleton lookup.
- Routed planner selection/cancel actions through the event bus.
- Replaced hardcoded item behavior branching with a behavior registry.
- Changed the main villager overlay and villager renderer to redraw on state changes instead of unconditional per-frame redraws.
- Neutralized the legacy `BuildingPlannerUI.cs` file so it no longer competes with the active planner implementation.

## Remaining Follow-ups
- `TerrainVisualizer2D` still has room for further allocation reduction in some tile update paths.
- `PlanningControlTestVillager` is still a broad test-scene controller; it should eventually split input, assignment, and overlay drawing into smaller adapters.
- Test-scene startup inventory and scene-profile decisions are still partly hardcoded and should move into explicit profile/config data.
