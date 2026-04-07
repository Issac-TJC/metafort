# MetaFort Checklist

## Architecture
- Pure systems take interfaces and data, not scene nodes.
- Node adapters resolve dependencies and forward data into pure systems.
- Avoid new singleton reads outside compatibility wrappers.

## Data-driven
- Config parses successfully.
- Config fields have validation.
- Runtime registries line up with config keys.

## Performance
- No new unconditional `QueueRedraw()` in gameplay or world overlays.
- No large temporary list/hash allocations in hot loops without a reason.
- No avoidable full-map scans on fixed timers if state did not change.

## Plug-in quality
- Feature can be removed by deleting its Node.
- Feature can be mounted by adding its Node and wiring exported dependencies.
- Behavior extension points use interfaces/registries, not string `switch` blocks inside core systems.
