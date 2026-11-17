# Trade Ledger

[Download on Nexus Mods](https://www.nexusmods.com/empyriongalacticsurvival/mods/199)

An in-game memory of observed markets, NPC requests, discovered places and deposits,
and personal production devices. It does not trade or move items for you.

**Version:** 1.0 (package 1.0.0). Reference setup: Empyrion 1.20.1 build 5232,
Reforged Eden 2 Build 46, Windows, single player.

## Install or update

1. Close Empyrion completely.
2. Copy the **TradeLedger** folder into **Content/Mods** in the game folder.
3. For an update, copy into the existing folder and replace the supplied files.

Keep the **runtime** subfolder and license notices together with the loader.
No PowerShell script is included or required. Do not run a legacy TradeLedgerProbeB
loader alongside TradeLedger.

The supplied **TradeLedger.settings** enables observation and Warn diagnostics.
It is required, but contains no routes, UI preferences or journal records. Normally
you can replace it with the defaults. Keep your old copy only if you deliberately
changed those service options. Copy updates into the existing folder instead of
deleting it if you want to retain logs.

## Open and use

Press **] (RightBracket)** to open the journal; Escape or Close dismisses it.
Open vendors and devices normally to record their shown information. Use Market
and Requests to find what you need, Production for remembered devices/deposits,
Destinations for visit markers/routes, and Places to browse discovered locations.

Historical stock and prices are not guaranteed current. Unknown is not zero.
Inventory progress covers the personal backpack, admitted active personal/linked
toolbar and supported visible cursor, not all linked or open-container contents.
NPC hand-in readiness uses backpack stock only. Multiplayer is not certified;
later game updates may leave incompatible features unavailable.

## Journal data and troubleshooting

Your records are in **<current save>/Mods/TradeLedger/TradeLedger.sqlite**, with
related Ledger metadata in the same directory. Replacing mod files does not remove
this data. Keep the complete save-local Mods/TradeLedger directory when copying a save.
Leave the runtime-managed **Saves/TradeLedgerCache** alone.

If the journal does not open, check the folder layout, the settings file and the
absence of a second old Ledger loader. Logs are under **TradeLedger/logs/operational-v1**;
an unwritable mod folder may use **%LOCALAPPDATA%/TradeLedger/logs** instead.
For a performance problem, turn Profile on in the journal, reproduce it, then turn
it off. Recordings are under **logs/profiler-v1**. Profiling starts off each session.
Do not post private saves, databases or unreviewed recordings publicly.

## Credits and rights

Made by **proffzulus**. Original code/documentation are MIT licensed. Full terms and
third-party rights are in [RELEASE-NOTICES.md](RELEASE-NOTICES.md),
`Harmony-LICENSE.txt` and `THIRD-PARTY-LICENSES.txt` alongside the installed files.

Thanks to **ravien_ff, Vermillion and contributors** for
[Reforged Eden 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812).
Game/scenario content is not relicensed by the mod's MIT license.
[Source and contributor documentation](https://github.com/proffzulus/empyrion-ledger/tree/dev).
