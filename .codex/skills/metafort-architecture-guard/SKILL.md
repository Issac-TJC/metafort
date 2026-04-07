---
name: metafort-architecture-guard
description: Use when implementing or reviewing MetaFort gameplay, UI, rendering, or systems code. Enforces the project's ECS-plus-Node architecture, hot-plug feature boundaries, event bus communication rules, data-driven requirements, and performance constraints.
---

# MetaFort Architecture Guard

Use this skill whenever you touch MetaFort runtime code, gameplay features, UI flow, rendering, or system integration.

## Core rules
- Keep the architecture as `ECS core + Node adapters`.
- ECS owns entity state, simulation state, and pure update logic.
- Node scripts own Godot lifecycle, exported paths, scene wiring, and presentation.
- Pure systems must not depend on `GameEntry.Instance` or scene-tree lookups.
- If a feature needs engine access, split it into:
  - a pure logic class
  - a Node adapter that injects dependencies
  - an optional renderer/presenter

## Hot-plug requirement
- New features must be removable or addable by mounting a dedicated Node.
- Core logic must also be usable without that scene node when dependencies are injected directly.
- Prefer `Initialize(...)` with interfaces over hidden singleton access.
- Do not hardcode behavior dispatch inside monolithic system classes.
- If behavior varies by item, weather effect, or building type, use a registry or interface keyed by data.

## Communication rules
- Cross-module behavior uses either `IEventBus` or explicit interfaces.
- Avoid direct UI-to-controller event wiring when the behavior crosses subsystem boundaries.
- Keep the `EventBus` for gameplay events and commands.
- Do not use the event bus for per-frame rendering churn.

## Data-driven rules
- Treat JSON/config validity as a startup requirement, not a best-effort load.
- New tunable gameplay parameters must default to config, not hardcoded constants.
- If a value is temporarily hardcoded for testing, mark it clearly and isolate it behind a test-only path or profile.
- Scene-name string matching is not an acceptable runtime profile selector.

## Performance rules
- Never add unconditional per-frame `QueueRedraw()` unless the node is intentionally immediate-mode debug UI.
- Prefer dirty flags, signatures, or event-triggered redraws.
- Avoid allocating large temporary collections in hot loops when a direct iteration path is possible.
- Avoid full-map or full-layer recomputation unless the driving state actually changed.
- Review any `GetDenseEntityIds<T>()` consumer for unnecessary secondary scans or repeated component checks.

## Review checklist
- Does the pure logic class avoid `GameEntry.Instance` and `GetNode*`?
- Can the feature be mounted as a standalone Node with exported dependencies?
- Are gameplay decisions driven by config or registries instead of `if/else` string branches?
- Are UI actions crossing module boundaries through `IEventBus` or an explicit interface?
- Does rendering redraw only when visible state changes?
- Are entity destruction and lifecycle semantics still correct after the change?
- Did you preserve buildability and validate config loading if config files changed?

If you need a compact pass before coding or review, read [`metafort-checklist.md`](metafort-checklist.md).

## Expected output style
- When reviewing code, list findings first with severity and file references.
- When implementing, mention any place where MetaFort still violates this skill and either fix it or leave a clear follow-up note.
- Favor small composable systems over enlarging scene controllers.
