# Contributing

Fork the repository, make a focused change and open a pull request. Describe the
behavior it changes and how you checked it. [BUILDING](docs/BUILDING.md) covers the
local dependencies, build, synthetic tests and packaging.

## Keep the journal's meaning

Trade Ledger remembers observations; it does not play for the user. Preserve current
save/player isolation, exact place/request identities, unknown versus observed zero,
freshness, manual decisions and raw evidence. Do not broaden a passive observation
into unseen-world enumeration or an inventory action. Keep expensive work off the
gameplay thread and use bounded/revision-driven updates. The short
[architecture brief](PROJECT_BRIEF.md) points to the relevant implementation areas.

Add synthetic regression cases for changed behavior. Keep offline checks separate
from live-game validation in the pull request; do not launch or control the game as
a test side effect. UI changes should include representative narrow/large layouts.

## Keep private and third-party material out

Do not commit saves, databases, personal observations, logs, credentials, machine
paths, blueprint exports, proprietary assemblies, raw scenario assets or private
research. Use synthetic fixtures. Game libraries are supplied locally; dependency
restore/build outputs stay ignored. Check your committed files, not only .gitignore.

Before opening a pull request:

```powershell
python scripts/audit-source.py
python scripts/audit-source.py --history HEAD
```

The second command checks every reachable commit tree; deleting a secret in a later
commit does not remove it from history. If an audit finds private data, stop uploading
that branch and resolve it before sharing. Do not rewrite other people's work.

Original contributions use MIT. Keep upstream attribution and all complete license
texts; do not submit material you cannot license. Game/RE2-derived reference tables
have separate provenance, not an MIT ownership claim. Changes to those tables need
review against legitimate local inputs and the [rights notice](THIRD-PARTY-NOTICES.md).
