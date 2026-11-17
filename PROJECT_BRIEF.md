# Source architecture and invariants

Trade Ledger is a client-side journal of observed markets, shown requests and
personally discovered places/devices. It is not an autonomous player or production
planner. This brief is for contributors; the [README](README.md) is for players.

The public package is TradeLedger. Historical ProbeB namespaces and test assembly
names are internal identifiers, not another mod to install.

## Boundaries to preserve

- Save/player scope and exact target identity gate records and callbacks. Names or
  proximity alone never merge places, requests or saves.
- Observations remain passive and limited to legitimately shown/discovered data.
  Unknown, observed zero, stale data and current observations are distinct.
- Native feature contracts guard obfuscated bindings. An optional failure must not
  invent empty/fresh data or disable independently valid stored-data display.
- Save-local Mods/TradeLedger holds journal authority. Publication, mirrors and
  worker generations must preserve native-backup barriers and pending-write truth.
- Owned marker identity, one-shot completion, suppression and explicit manual state
  survive refresh/reload. Background work must stay bounded and revision-driven.

## Where to look

The implementation is under `src/TradeLedgerProbeB/`. `LedgerHost.cs` coordinates
lifecycle, `LedgerPortableSave.cs` and `LedgerStore.cs` handle persistence,
`RuntimeBootstrap.cs` starts guarded native observation, and
`LedgerPlaceBrowserWindow.cs` implements Places interaction. Synthetic regressions
are under `tests/poc-b/`.

See [BUILDING](docs/BUILDING.md) for commands and outputs, and
[CONTRIBUTING](CONTRIBUTING.md) for changes and publication boundaries. Build/test
scripts do not deploy or launch the game; the developer updater is a separate action.
