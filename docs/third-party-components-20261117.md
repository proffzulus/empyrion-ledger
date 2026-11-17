# Bundled third-party component notice audit (2026-11-17)

Trade Ledger 1.0 includes a locally modified Harmony 2.3.3 net452 assembly.
Its ILRepack manifest and types identify MonoMod Core 1.1.0, Utils 25.0.4,
Backports 1.1.0, ILHelpers 1.0.1, Mono.Cecil 0.11.5 (including Mdb/Pdb/Rocks),
MonoMod.Iced 1.17.0.0, and System.ValueTuple assembly 4.0.3.0.
These are incorporated code, not merely build dependencies.

License/provenance sources:

- [Harmony at its package source commit](https://github.com/pardeike/Harmony/blob/395749ff507ab4e2cd4f84cf99085ec18f6870bc/LICENSE): MIT.
- [MonoMod at the packages' repository commit](https://github.com/MonoMod/MonoMod/blob/263ee720150836cbe8eeb3cb165ceebd5e2896eb/LICENSE): MIT. This also covers its embedded Linux/macOS exception helpers; both embedded helper byte hashes match this commit's binaries. NuGet metadata additionally credits 2024 0x0ade, DaNike.
- [Mono.Cecil 0.11.5](https://github.com/jbevain/cecil/blob/0.11.5/LICENSE.txt): MIT notice, including its contributor attribution.
- [Iced at MonoMod's pinned submodule](https://github.com/icedland/iced/blob/425462f47e0752f143b4f3826b875d5c33b5baf1/LICENSE.txt): MIT. MonoMod.Core's NuGet LICENSE.txt contains this Iced notice; it is not the MonoMod project license.
- [System.ValueTuple 4.5.0 official NuGet package](https://api.nuget.org/v3-flatcontainer/system.valuetuple/4.5.0/system.valuetuple.4.5.0.nupkg): LICENSE.TXT is MIT, copyright .NET Foundation and Contributors. Package SHA256: `9E21FA9767D4E76BC0CEE065C1D40CC34384A114BFEC4D70E6C981168A926802`. Its version.txt identifies commit `30ab651fcb4354552bd4891619a0bdd81e0ebdbf`. All 147 method instruction bodies across nine merged ValueTuple types match the net461 asset. Package version 4.5.0 and assembly version 4.0.3.0 are different identifiers, not a conflict.

Full license texts and the supplied System.ValueTuple informational third-party
notices are in vendor/Harmony-2.3.3/THIRD-PARTY-LICENSES.txt and copied into the
release by the dependency/package recipes. That broad upstream informational
notice does not mean all of its listed .NET Core components are incorporated.
Do not infer the release contents from another target framework's dependencies.
Game/Unity/SQLite-provider assemblies remain external dependencies, not ZIP payload.

The notice-only correction does not recompile or change any binary. Archive CRC,
all manifest payload hashes, deterministic ZIP generation, source-boundary checks
and unchanged-binary comparisons are checked separately. These establish technical
evidence, not blanket legal clearance. Original Trade Ledger MIT terms do not
relicense game/RE2-derived reference metadata or third-party code.
