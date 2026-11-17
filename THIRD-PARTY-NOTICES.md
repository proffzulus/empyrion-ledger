# Rights, provenance and dependencies

`LICENSE` licenses original Trade Ledger code and original project documentation under
MIT, copyright 2026 proffzulus. It does **not** relicense Empyrion,
Reforged Eden 2, Unity or third-party content. All their rights are retained.

## Empyrion and Reforged Eden 2

Empyrion game/SDK/Unity/provider DLLs, native SQLite, prefabs, models, textures/icons,
audio/video, configuration files, dialogue/localization CSV, saves and player data
are not included. A legitimate local installation supplies build/runtime dependencies.
No modified game binary is produced. Native interoperability tables contain member
identities/signatures, semantic relationships and one-way hashes, not decompiled
method bodies or copied executable IL.

RE2 credit: **ravien_ff, Vermillion and contributors**,
https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812 . As inspected on
2026-11-17, the official page permits use of game configuration files such as yaml,
ecf and epb, asks link credit, and separately reserves rights in its copyrighted
assets. This is not blanket permission or an MIT grant for localization/dialogue CSV.

The following **essential embedded runtime reference tables** are retained so this
source builds the current implementation. They are a narrow exception to excluding
private research outputs, not publication of a research catalogue/archive or live
observations. Their game/scenario-derived elements are explicitly outside our MIT
grant. They contain identifiers, short names/labels, factual numeric relationships,
source hashes/references and authored recognition rules, **no full dialogue prose or
raw CSV/config/prefab assets**. No independent asset ownership is asserted.

| Runtime resource | Provenance / use |
| --- | --- |
| `Re2RequestDefinitions.json` | RE2 Build 46 Dialogues.ecf/CSV and item localization; shown-body hashes plus canonical item IDs/names, quantities, alternatives, conflict/source annotations. Matching only an actually shown page; never preload an assignment. |
| `DiscoveredObjectTypes.json` | Selected locally audited authored template header/payload facts; names/IDs, hashes, structural type knowledge, potential trader identifiers/counts. Never spawned/discovered instances. |
| `PlaceNameFamilies.json` | Effective installed RE2-over-base template precedence, localized short names, stable template families and authored potential trader counts. No actual staff/offers. |
| `PlaceFactions.json` | RE2 Factions.ecf IDs/colours and localization short names; never imports default standings as current player relations. |
| `NativeContracts5232.json`, `contract-5232.json`, `BuildContract5232.cs.txt` | Empyrion 1.20.1/5232 interoperability identities, signatures, ABI relationships and hash evidence; no game DLL or native implementation copied. |

These tables are pinned reference inputs, not a promise of automatic regeneration
for arbitrary new scenario content. Runtime content/native guards retain unknowns or
degrade unsupported features. Changes need review against legitimately installed
files; do not copy raw third-party assets into this repository. Any broader content
redistribution or future binary/Nexus publication needs its own rights review. This
source publication does not claim those broader permissions.

## Harmony and bundled upstream components

No third-party DLL/package is tracked. `scripts/restore-dependencies.ps1` fetches the
official Lib.Harmony 2.3.3 NuGet package over HTTPS and checks pinned SHA256, then
`scripts/build-harmony-fieldtype-fix.ps1` reproduces the existing owned field-type
matching correction. The original MIT notice and component notices are in
`vendor/Harmony-2.3.3/LICENSE` and `THIRD-PARTY-LICENSES.txt` there. Keep them with
any locally built binary distribution; no upstream endorsement or warranty.

Harmony: Andreas Pardeike, https://github.com/pardeike/Harmony (MIT).
The merged package's MonoMod Core/Utils/Backports/ILHelpers, Mono.Cecil and Iced
notices are preserved verbatim in that notice file. The local patch affects only
nongeneric field-type equality after name/declaring-type/kind checks; generic
upstream behavior is retained. It modifies the owned dependency, not the game.
