# Trade Ledger 1.0

**Version:** 1.0 (package 1.0.0; assemblies 1.0.0.0). Reference: Empyrion 1.20.1 build 5232 / RE2 Build 46. Made by **proffzulus**.

Client-side personal memory for Empyrion and Reforged Eden 2: actually observed markets, shown NPC requests, discovered places and deposits, and observed personal production devices. No autonomous trading, inventory actions or universe reveal.

## Compatibility and release status

The native profile targets **Empyrion 1.20.1 build 5232**, Windows client, RE2 Build 46, single player. This public-name package has native-contract and offline regression coverage, not a new live-game acceptance run. This is the known reference image, not a universal build-number allowlist. Later build labels and moved metadata tokens are accepted when the feature contracts still match. Unique method renames in a proven owner require unchanged ABI and full IL/operand/local/exception behavior. Renamed owners/fields, ambiguous matches or changed semantics disable the affected capability; new obfuscation schemes may still require an update. Core API/Unity/SQLite and private-library integrity checks remain. Do not disable these checks. Unsupported optional reads remain unknown rather than empty or fresh.

This is preparation for distribution, not a Nexus upload. See RELEASE-NOTICES.md for redistribution permissions still requiring resolution. No EAC workaround is provided; use the game's supported client-mod setup.

Thanks to ravien_ff, Vermillion and contributors for [Reforged Eden 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812). Original Trade Ledger code/documentation are MIT-licensed; third-party/game/scenario rights remain separate in RELEASE-NOTICES.md and the included license notices.

## Installation and update

1. Close Empyrion, including client/dedicated game processes. Do not install over loaded DLLs.
2. For a new installation, place the **TradeLedger** folder in `<Empyrion>/Content/Mods/`. Its top-level loader is `TradeLedger.dll`, paired with `TradeLedger_Info.yaml` (`ModTargets: Client`). Keep the `runtime` subfolder intact. No separate application, source checkout or developer directory is required.
3. To update an existing **TradeLedger** folder, keep your existing **TradeLedger.settings**; do not overwrite it with the ZIP defaults. Copy the other supplied files, including the runtime subfolder, into that same folder and replace matching package files.
4. **Do not delete the existing mod folder.** Preserve its logs and any other personal files. Do not modify saves or caches. A file-in-use error means stop copying until the game is fully closed; do not force replacement.

No installation script is included or required. Do not enable a legacy **TradeLedgerProbeB** loader alongside TradeLedger. Resolve a legacy prototype migration before adding another active loader; the steps above cover new installations and updates to the public TradeLedger folder.

New-install settings: passive `RuntimeObservation=on`, `Diagnostics=Warn`, Client targeting. Performance profiling is **OFF on every process start**, session-only opt-in through the journal's **Profile: Off/On** control; it also stops after its bounded duration/size budget. Press **`]` (RightBracket)** to open; Escape / Close dismiss. An Escape-menu Trade Ledger entry is also implemented, with paused-menu handoff covered offline; its actual visibility/click remains a user live check. Existing native-window switching remains supported by the guarded game bindings.

## Data and diagnostics

Journal tabs begin **Market, Requests, Production, Destinations**. In Devices, coloured resource cards show filtered stock, device counts and per-product rates; click a resource to filter and click it again to clear. Places offers sortable Distance and Max traders, coloured relation dots and a relation filter (orange Unfriendly remains separate from Neutral). Unknown is not zero; N/A denotes a proven inapplicable faction relation. Item Browser has direct Fit graph / Center selected / Clear graph controls, with recipe and native item properties alongside the graph. Comparison's four scope icons choose POI, Body, Sector or System independently on each side.

- Authoritative journal records are SQLite: `<current save>/Mods/TradeLedger/TradeLedger.sqlite`, with related Ledger metadata in that save-local directory. Updating only `Content/Mods/TradeLedger` does not remove those records. Keep the complete save-local directory when copying your own saves.
- Deleting the installed mod folder would lose its `TradeLedger.settings` and local logs/profiler recordings, even though save-local journal data remains separate. Preserve those files during updates.
- The existing game-adjacent private `Saves/TradeLedgerCache` supports safe publication/read mirrors outside native backup trees. It is runtime-managed, not a distributable database or an independent workspace.
- Routine logs: `<mod>/logs/operational-v1/` (up to eight 2 MiB sessions / 16 MiB). Optional recordings: `<mod>/logs/profiler-v1/` (bounded recordings, up to 32 MiB retained). No source directory is used. If the mod directory is unwritable, diagnostics may fall back to `%LOCALAPPDATA%/TradeLedger/logs`; failure of logging does not disable Ledger.
- To investigate a hitch: explicitly enable Profile, reproduce it, disable Profile, and retain the resulting log with the package fingerprint. Ledger timings do not prove a game-wide cause. Warn/Info/Debug diagnostics are independent; Debug is temporary troubleshooting.
- A clean install has no historic legacy recovery input. Historical developer-workspace stores are neither imported nor redistributed. Never copy another player's records into a save to make a new installation appear populated.

## Dependencies and limits

Requires the game's own ModApi/Mif/Unity/SQLite assemblies and native SQLite; these are **not included**. The package includes a reviewed modified Harmony 2.3.3 binary and its license/third-party notices. Do not substitute an arbitrary Harmony version. A conflicting loaded Harmony copy can disable optional observers safely.

Inventory coverage remains admitted personal backpack, active personal/linked toolbar and supported visible cursor; it is not complete linked-container or arbitrary open-container access. Hand-in readiness is backpack-only. Historical prices/stock remain historical. Missing observations are unknown. Multiplayer, new public-folder loader startup and game save/load rendering need live validation; no multiplayer certification is claimed.

Next-launch checks after a compatible certified install: journal open/close and native-window switching; fresh vendor/device observation; low-reserve maintenance with the journal closed; request progress/Done; one-shot and route marker reload reconciliation; new offscreen deposit discovery. Launch the game yourself. Removing installation scripts does not establish that antivirus/VirusTotal warnings are resolved.

Places also provides scoped Overview / Market / Requests in the right panel, compact relation/trader filters, direct Hide/Unhide and Favorite/Unfavorite with Undo. Select a geographic node for aggregate observations or a POI for exact-instance observations. Full-view and Comparison links preserve that scope; Back restores the panel. Locations uses the same four scope icons as Comparison.

Final 17 November polish: Places row clicks only focus details. The whole checkbox cell toggles checks once. Hide/Favorite act only on checked rows, or on the cursor row when no rows are checked; plain action labels preserve Undo and the current list. Same-context distance refresh replaces coordinates atomically, while genuinely unavailable evidence remains Unknown. Contextual Market uses single compact rows; freshness remains in colours and hover details, and distinct offers/prices stay separate.
