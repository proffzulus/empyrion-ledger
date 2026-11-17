# Contributing

Fork, create a focused branch, run the build/tests, and open a pull request. Keep the
change reviewable; describe behavior, validation and remaining live-game limits.
Contributions to original project code are provided under MIT; do not submit material
you cannot license. Preserve upstream attributions and metadata rights boundaries.

- Never add saves, player records, databases, logs, personal paths, credentials,
  blueprints, game binaries, raw scenario assets or private research. .gitignore is
  not a substitute for auditing every committed tree and outgoing history.
- Keep read-only observation scoped to the current local player and actually
  discovered/shown interfaces. Unknown is not zero, stale is not fresh, and identical
  names do not establish identity. Never weaken guards to make a test appear green.
- Keep work bounded/cached off gameplay hot paths where possible. Preserve native
  save barriers, marker ownership, manual state and capability-level degradation.
- Add synthetic regressions. Do not copy real session data to make fixtures.
- Game/native dependencies are local inputs. Do not commit generated binaries or
  patched game files. Builds/tests must never deploy or launch/control the game.

Run `python scripts/audit-source.py` before committing. For a prospective push, run
`python scripts/audit-source.py --history HEAD`; this inspects every reachable tree.
If a secret ever enters history, stop publication and report it; merely deleting the
working file does not remove it from Git. Do not rewrite someone else's remote history.
