# Building Trade Ledger

These commands apply to this public source checkout. Players can install the
[prebuilt Nexus download](https://www.nexusmods.com/empyriongalacticsurvival/mods/199)
without build tools or PowerShell.

## Requirements

- Windows PowerShell 5.1, Python 3, and Visual Studio/MSBuild with C# 7.3 and the
  .NET Framework 4.6.1 targeting pack.
- A legitimate local Empyrion installation. The reference profile is Empyrion
  1.20.1 build 5232, RE2 Build 46, single player.
- HTTPS access to NuGet for the pinned Harmony dependency.

Game API, Unity and SQLite libraries are local build/runtime inputs. They are not
redistributed. The tracked reference JSON supplies recognition/type metadata;
private research, saved games, databases and session logs are not build inputs.

## Build and test

From the repository root, substitute your own game folder and MSBuild executable:

```powershell
./scripts/restore-dependencies.ps1 -GameRoot '<game folder>'
./scripts/build.ps1 -GameRoot '<game folder>' -MSBuildPath '<MSBuild.exe>'
./scripts/test.ps1 -GameRoot '<game folder>' -MSBuildPath '<MSBuild.exe>'
```

Restore checks the pinned NuGet hash and reproduces the local Harmony field-type
correction. Build validates game dependencies and native feature contracts, compiles
the two public assemblies, and writes a stage manifest. Known-image detail checks
run on the reference image; another image needs independently validated contracts.
Build labels alone are not a compatibility veto. A required contract failure blocks
certification; optional capability failures remain explicit.

Tests compile/run the public synthetic regressions, native-contract fixtures and
isolated deployment fixtures. They also create and check the release ZIP. Outputs
stay in ignored artifacts/vendor build directories: no game launch, real installation
or live save edit is a build/test side effect. Offline fixtures and GDI previews
cannot establish native UI rendering, game timing or live save/load behavior.

## Outputs and packaging

- Compiled assemblies: `artifacts/poc-b/build/`.
- Complete mod folder after packaging: `artifacts/release/stage/TradeLedger/`.
- ZIP: `artifacts/release/TradeLedger-1.0.0-empyrion5232-re2.zip` for the reference build.

To refresh packaging after a successful build without recompiling:

```powershell
python scripts/prepare-trade-ledger-release.py
python scripts/prepare-trade-ledger-release.py --check
```

The packager uses the existing stage, adds the release README/notices, recalculates
the manifest and creates a deterministic **10-entry ZIP with no PowerShell scripts**.
It checks CRC, allowed contents, hashes, personal paths and byte reproducibility.
Changing release templates does not update an existing ZIP until packaging is run.
Do not silently overwrite an already published archive during documentation work.

Original game/scenario files, player data and build-only dependencies do not belong
in the package. Keep the complete notices with Harmony and preserve the rights
boundaries in [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md).

## Optional developer deployment

The user ZIP installs manually; the source tree also has a guarded developer updater.
With actual game/client/dedicated processes closed, run it separately if needed:

```powershell
./scripts/deploy-trade-ledger.ps1 -GameRoot '<game folder>' `
  -Stage artifacts/release/stage/TradeLedger -Install -Confirm:$false
```

It validates package/native contracts, exclusive file access and any legacy transition,
preserves installed settings and prevents duplicate loaders. Unknown files, locks or
invalid ownership block replacement. Do not force files or edit saves to pass a check.
The updater does not launch or terminate the game.

See [CONTRIBUTING](../CONTRIBUTING.md) for source/privacy checks and
[PROJECT_BRIEF](../PROJECT_BRIEF.md) for the code's main invariants.
