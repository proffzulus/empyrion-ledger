# Empyrion Trade Ledger

An in-game, client-side memory for **Empyrion - Galactic Survival / Reforged Eden 2**.
Remember who sells what, who requested it, where you discovered a place and which
personally observed devices may need attention. It does not play the game for you.

**Version:** 1.0 (package 1.0.0; assemblies 1.0.0.0). Reference setup: Empyrion **1.20.1 build 5232**, **RE2 Build 46**,
single player. Existing capture/device/marker fixes have user feedback; the latest
UI/metadata changes have offline regression and package verification, not complete
live Unity acceptance. Multiplayer is not certified.

## What it does

- **Market / Locations / Comparison:** remember actually opened vendor offers in both
  directions, price, stock/demand, location and observed freshness. Search/filter/sort,
  compare observed prices and inspect an item's recipe/native properties. Historical
  offers remain historical; unknown is never silently zero.
- **Requests:** remember shown NPC requests and raw evidence, per-item inventory
  progress, previously observed seller hints, reversible manual Done and narrowly
  confirmed NPC hand-in completion. Backpack hand-in readiness is separate from
  preparation stock. Repeated assignments retain distinct occurrence identities.
- **Production:** remember discovered deposits and normally opened personal miners,
  extractors and condensers; show observed stock, rate/fuel and conditional GAME-time
  estimates. Service warnings use the earliest fuel/storage downtime below the
  configurable hours horizon, not an independent fill-percent trigger.
- **Destinations:** owned one-shot visit purposes and reusable trade routes, explicit
  Mark/Show/Hide/Suppress/Force notify, compact Buy/Sell intent and safe reload
  reconciliation. Route purchase deficits use fresh known demand and admitted current
  inventory, without reserving stock or allocating purchases between sellers.
- **Places:** browse actually discovered objects through system/group/body geography;
  search known names/variants, filter faction/relation/trader potential, sort distance
  or maximum potential traders, and organize Favorites / Unsorted / Hidden. Type
  knowledge is distinct from actual observed wares or personnel.
- **Item Browser:** active-game item/recipe metadata, pan/zoom recipe graphs with
  compact overview, Market links and explicit framing controls.
- Optional bounded **performance profiling**, OFF at startup and session-only opt-in.

## Install and use

This repository publishes **source**, not game/scenario files or a prebuilt release.
Build locally as described in [BUILDING](docs/BUILDING.md). The generated ordinary
mod folder is `artifacts/release/stage/TradeLedger`.

The user ZIP contains a manual-install mod folder and no PowerShell scripts.
With the game fully closed, copy TradeLedger into Content/Mods, replacing supplied
files for an update without deleting the folder or logs. The supplied service
settings are normal defaults; retain an old settings file only if you deliberately
customized those options. Keep a settings file present so observation is enabled.
Save-local journal records remain in Mods/TradeLedger/TradeLedger.sqlite inside
the current save. See [manual instructions](docs/release/README.md).
Developer deployment scripts remain in source, outside the user ZIP.
Do not enable both public and legacy ProbeB loaders.
Download the mod from [Nexus Mods](https://www.nexusmods.com/empyriongalacticsurvival/mods/199).

Open/close with **`]` (RightBracket)**; Escape / Close dismiss. The native Escape-menu
**Trade Ledger** entry has a guarded deferred handoff; its actual visibility/click still
requires the owner's live confirmation.
Tabs begin Market, Requests, Production, Destinations. Observe a vendor/device normally;
the journal is memory of what you actually see, not a list of unseen opportunities.
Click resource cards to filter Devices. Relations use red Hostile, orange Unfriendly,
yellow Neutral and green Friendly (Honored included); unknown and inapplicable remain
separate. Explicit Suppress controls unwanted owned markers.

Places has compact relation/trader controls and direct Hide/Unhide and
Favorite/Unfavorite with Undo, without switching lists. Its right panel contains
Overview / Market / Requests for an exact POI or the selected body/sector/system.
Separate observed prices/stock/demand and Pending/Done remain distinct. Full-view
and Comparison links preserve scope, and Back restores the panel. Locations uses
the same four scope icons as Comparison. [Details and live checks](docs/places-context.md).

New-install defaults: `RuntimeObservation=on`, `Diagnostics=Warn`, Client targeting.
Profiler is OFF at every process start; explicitly turn it on, reproduce, then off.
Routine logs are mod-relative `logs/operational-v1`; optional bounded recordings are
`logs/profiler-v1`. Never post private saves, full databases or raw recordings in a
public issue. Diagnostics failing does not disable normal journal operation.

## Boundaries and compatibility

Only admitted personal backpack, active personal/settled linked toolbar and supported
visible cursor stock are read. This is **not** arbitrary open-container or complete
linked-container inventory. Ambiguous transfers, stale scope and unsupported sources
remain unknown. No item moves, automated purchases, hidden-world enumeration or
unseen request creation. Forecasts assume unchanged operation and are not promises
about unloaded/sleep/depletion behavior.

Build labels and whole-image hashes are provenance/fast paths, not universal refusal.
Unknown images need independently validated feature/semantic contracts; compatible
metadata shifts and unique behavior-identical same-owner method renames can resolve.
Changed/ambiguous semantics fail closed per capability. This cannot guarantee every
future build; optional failure must not disable independently valid stored-data UI.

## Build, test and contribute

Windows, MSBuild with .NET Framework 4.6.1 targeting support, Python 3 and a legitimate
compatible local Empyrion installation are required. Harmony is restored from its
pinned official NuGet package and patched locally. No proprietary assemblies are in
Git. See [BUILDING](docs/BUILDING.md) and [CONTRIBUTING](CONTRIBUTING.md).

Public tests use synthetic fixtures and checked native contracts. Private development
research, real session replays, saves, logs and player databases are deliberately absent.
Offline/GDI previews are not live Unity validation. Contributions should preserve
identity, observation scope, uncertainty and bounded work, and include focused tests.

## Thanks, credits and rights

Made by **proffzulus**. Original Trade Ledger code is **MIT licensed**,
free to fork and contribute to, **as is and without warranty**; see [LICENSE](LICENSE).

A tribute and sincere thank-you to **ravien_ff, Vermillion and the RE2 contributors**:
[Reforged Eden 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812)
is the world this journal was built to remember. Their official page permits use of
configuration files such as yaml/ecf/epb and requests link credit. That is not a blanket
MIT license for the scenario, its localization/dialogue CSV, art or other assets.

Empyrion, Unity, Reforged Eden 2 and all game/third-party material remain the property
of their respective rightsholders. MIT covers our original code, not their content.
Essential structured runtime reference metadata is separately identified in
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md); no raw scenario assets, game binaries,
full dialogue/localization CSV, private observations or research outputs are bundled.
Harmony/MonoMod/Cecil/Iced notices remain separate. No upstream endorsement is implied.
