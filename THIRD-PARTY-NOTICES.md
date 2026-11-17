# Rights and bundled components

Original Trade Ledger code and original project documentation are licensed under
MIT, copyright 2026 proffzulus. This does not relicense Empyrion, Reforged Eden 2,
Unity or third-party content. Their rights remain with their respective owners.

## Game and scenario reference data

Thanks to **ravien_ff, Vermillion and contributors** for
[Reforged Eden 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812).
Their official page permits use of game configuration files such as yaml/ecf/epb
and requests a link for credit. It separately reserves copyrighted assets; this is
not an explicit blanket license for localization/dialogue CSV or an MIT grant.

Trade Ledger includes derived reference metadata, not raw game/scenario assets or
player records. These tables contain IDs, short labels, numeric relationships,
source references/hashes and recognition rules, not full dialogue prose or raw
CSV/config/prefab files. Their derived elements are outside Trade Ledger's MIT grant.

| Reference table | Origin and purpose |
| --- | --- |
| Re2RequestDefinitions.json | RE2 Build 46 dialogue definitions/localization: shown-page hashes, canonical items, quantities, alternatives and source annotations. It recognizes actually shown requests, not unseen assignments. |
| DiscoveredObjectTypes.json | Audited prefab header/payload facts: template names/IDs, structural hashes and potential trader/dialogue identifiers, not discovered instances or current contents. |
| PlaceNameFamilies.json | Effective RE2-over-base template families, localized labels and potential trader counts, not live staff/offers. |
| PlaceFactions.json | RE2 faction IDs/colors and localized names, not current player standings. |
| NativeContracts.json (embedded) | Native member signatures, relationships and one-way behavior hashes for interoperability, not game implementations. Source inputs include NativeContracts5232.json, contract-5232.json and BuildContract5232.cs.txt. |

The mod does not bundle game/SDK/Unity/SQLite-provider assemblies, native SQLite,
models, art, icons, audio, video, raw configuration, dialogue/localization CSV,
saves or player databases. A legitimate game installation supplies those runtime
libraries. No modified game assembly is distributed. Reference tables are pinned
inputs; they do not guarantee support for arbitrary future scenario content.
Credit and the project's MIT license do not themselves establish broader rights
to redistribute game/scenario-derived material.

## Harmony and incorporated libraries

The release contains a locally modified **Harmony 2.3.3** by **Andreas Pardeike**
([Harmony](https://github.com/pardeike/Harmony), MIT). Its merged code includes
MonoMod Core/Utils/Backports/ILHelpers, Mono.Cecil (Mdb/Pdb/Rocks), Iced and
System.ValueTuple 4.5.0 (assembly version 4.0.3.0). MonoMod's Linux/macOS native
exception helpers are embedded in the same DLL.

Full upstream licenses and attribution are retained. System.ValueTuple's MIT notice
credits the .NET Foundation and Contributors. Its package's broad informational
third-party notices are also retained; this does not mean every component listed
there is incorporated. MonoMod.Core's packaged LICENSE.txt is Iced's notice;
MonoMod's shared project MIT license is identified separately.

The local Harmony correction checks nongeneric field-type equality after matching
declaring type, name and member kind; generic upstream behavior is retained. This
is a modified dependency, not an unmodified upstream release or a game modification.
No upstream endorsement is implied.

## Files in the source repository

- [LICENSE](LICENSE): full MIT terms for original Trade Ledger code/documentation.
- [Harmony license](vendor/Harmony-2.3.3/LICENSE) and
  [component notices](vendor/Harmony-2.3.3/THIRD-PARTY-LICENSES.txt): full upstream texts.
- [Component provenance](docs/third-party-components-20261117.md): pinned upstream
  sources and the audit of incorporated code.

No third-party binary is tracked in this repository. The developer restore recipe
fetches pinned Harmony from official NuGet and reproduces the documented correction;
see [BUILDING](docs/BUILDING.md). Runtime reference inputs live under
src/TradeLedgerProbeB. The release places the license notices beside the mod files,
using the [package notice template](docs/release/RELEASE-NOTICES.md).
