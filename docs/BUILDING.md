# Build and offline verification

## Local prerequisites

- Windows PowerShell 5.1 or compatible PowerShell; Python 3.
- Visual Studio/MSBuild with C# 7.3 and .NET Framework 4.6.1 targeting support.
- A legitimately installed compatible Empyrion. Reference: 1.20.1 build 5232,
  RE2 Build 46, single player. The build reads `Client/Empyrion_Data/Managed` and
  the game's native SQLite path; it never copies those libraries into the package.
- HTTPS access to the pinned official Harmony NuGet package on first restore.

No private project directory, research workspace, saved game, real database or log
is a build/test input. There are no automatic installs/deployments or game launches.
Paths below are placeholders: supply your own installation and MSBuild executable.

```powershell
./scripts/restore-dependencies.ps1 -GameRoot '<your game folder>'
./scripts/build.ps1 -GameRoot '<your game folder>' -MSBuildPath '<MSBuild.exe>'
./scripts/test.ps1 -GameRoot '<your game folder>' -MSBuildPath '<MSBuild.exe>'
python scripts/audit-source.py
```

`build.ps1` validates the current feature contracts using read-only Cecil inspection.
The known reference image additionally runs all 25 detailed native verifiers. An
unknown image must satisfy audited semantic/ABI relationships; runtime-core failure
blocks certification, optional failure is explicit per capability. Labels alone are
not rejection, and no old metadata token is blindly admitted. No native game method
is executed. Contracts/bindings remain cached once per loaded module at runtime.

The two public assemblies compile deterministically with mapped source paths and
locally restored Harmony. A synthetic owned collision fixture is prepared in the
runtime assembly as in the current implementation; no game assembly is patched.
Outputs go only under ignored `artifacts/` (and ignored restored vendor binaries).
The stable historical ProbeB source namespace/test assembly identity is intentional.

`test.ps1` runs the published synthetic regression subset (including lifecycle,
scanner/HUD work, devices, requests seller evidence, route shortages, marker identity,
graph overview, distances, profiler and UI consistency), structural native fallback
and isolated installer/native-audit fixtures. It excludes private real-session replays
and the old research corpus. It is not the full private historical 7,625-check suite,
and neither suite is live Unity acceptance. It generates offline GDI previews only.

## Local package (no upload)

```powershell
python scripts/prepare-trade-ledger-release.py
python scripts/prepare-trade-ledger-release.py --check
```

This creates `artifacts/release/stage/TradeLedger` and a deterministic source-built
ZIP with the owned dependency/notices and checked installer. No game/scenario DLLs,
raw assets, databases, personal paths or logs belong there. The source release does
not itself upload a binary or certify broader content redistribution rights.

## Optional explicit local install

Only when **the actual game/client/dedicated processes are closed**, separately run:

```powershell
./scripts/deploy-trade-ledger.ps1 -GameRoot '<your game folder>' `
  -Stage artifacts/release/stage/TradeLedger -Install -Confirm:$false
```

It validates the manifest and every owned file, checks actual processes and exclusive
file access, preserves validated installed settings, prevents two loaders, and writes
only the owned mod payload. Unknown files, invalid hashes, unsafe legacy transitions
or locks stop it. Never force files, stop the game, edit saves or delete data to make
installation pass. Existing save-local `Mods/TradeLedger` is not moved or reset.

The latest UI/crop metadata changes were checked offline on the reference setup;
scrollbars, native atlas glyphs, modal timing, current-game metadata and save/load
behavior still need normal user acceptance. Do not launch the game as a build test.
