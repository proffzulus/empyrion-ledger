# Places contextual navigation, 17 November 2026

Trade Ledger 0.3.12.0; Empyrion 1.20.1 build 5232 reference profile. This bounded UI change preserves Production, Item Browser, observation/marker rules and existing orange Unfriendly.

## Delivered behavior

- Locations uses the same four runtime-atlas scope buttons as Comparison: POI, Body, Sector, System, with selected underline and hover labels.
- Places keeps its navigator/list/detail arrangement. Clicking a navigator node inspects that observed geographic scope; clicking a POI inspects its exact identity. No same-name/proximity join.
- Row clicks move only the detail cursor. The full checkbox cell toggles checks once; other checked rows survive browsing. Hide/Favorite act only on checks when any exist, otherwise on the visible cursor row. The toolbar shows the target mode. Filter changes clear stale checks; no invisible checked member adds an unchecked cursor.
- Hide/Unhide and Favorite/Unfavorite replace the bulk membership menu. Hidden Unhide and Favorites Unfavorite return to Unsorted. The other action moves to its named list. The active list/filter stays put, numerical scroll is retained/clamped, and Undo restores exact previous dispositions. Commands remain transactional and scoped to visible selections. Only the local projection is invalidated after commit; no forced full database reload.
- Relation choices are compact colored bullets, with separate orange Unfriendly, gray Unknown and slash N/A. Honored remains Friendly. Four trader choices use all-arrows, +, minus and ? with hover labels; unknown potential never means zero. The all-arrows symbol is drawn from owned vector strokes, not copied game artwork. Known runtime atlas scope icons are still borrowed at runtime only.
- The right panel contains Overview / Market / Requests. Market shows separate observed Buy/Sell rows, quantities and prices, with existing freshness colors/hover context. Distinct vendors/prices are not averaged or summed. Requests has Pending/Done, search, observed requirements/reward and readiness status. Full Requests retains complete progress, seller hints, dialogue and actions.
- Full Market/Requests and Compare → Buy A / Sell B retain the selected scope. Back restores the panel tab, query and scroll. The scope payload contains exact already-known playfields and current world identity, or the exact POI identity. Comparison swap preserves that payload. Choosing another level/filter explicitly replaces it.
- Redundant Overview membership and Mark controls are removed. Explicit Visit remains in the list toolbar and uses the existing safe one-shot target path.

## Scope, performance and safety

No new observations, persistence schema, thresholds, automatic goals or stock calculations. Existing source records remain intact. The shared location index includes actually captured request cards (including informational conversation sources), rather than requiring an existing market/persisted recognized-request row to establish scoping. This corrects an empty exact-context edge case without generating requests from definitions.

Location indexing is cached once per snapshot/geography revision. Context offers are filtered only when snapshot/scope/geography/search changes; requests reuse the revision/inventory-aware projection. Rendering virtualizes visible rows. Focus membership uses a cached key set, avoiding a linear full-list lookup per paint. No database read, parsing or native entity enumeration is added to paint or gameplay updates.

## Escape-menu audit

The prior code required capture `WorldReady` (native application State Running) to retain the UI world and show the entry. A paused menu could therefore withdraw that entry; this is a source-demonstrated gate incompatibility, not a measured reproduction of the owner's exact Unity failure. The exact registered top `InGameMenu` remains mandatory. Its owned button can now be presented with independently valid current context/store while observation readiness is paused. A click requests normal native close, retains only a 2.5-second save-scoped intent and waits for ordinary Running/focus/typing/modal/fade gates. Capture readiness and save publication barriers are unchanged. Save change, timeout and close cancel intent. No foreign window is closed opportunistically.

RightBracket `]` remains the supported keyboard entry; Escape / Close dismiss. Native key-map/member checks and offline lifecycle fixtures verify the implementation. Actual Escape-menu rendering/click and keyboard-layout behavior remain user-only live checks; README no longer calls the menu entry live verified.

## Verification and evidence

The development full suites/native-contract checks and the public synthetic suite cover this update. Synthetic regressions include exact same-name POIs, moon-only body/sector/system aggregation, distinct same-name systems, separate prices, observed zero, unsupported direction, empty search, wrong-world negatives, request Pending/Done, repeated cached queries, membership transitions and deferred menu lifecycle. Private session fixtures and evidence are deliberately excluded from this repository.

GDI previews at 1280, 1920, 2560 and 3840 widths exercise the same three-column geometry and representative long lists with all three panel tabs. They are offline layout approximations, not Unity screenshots. Playtest filter screenshots informed the compact controls; GDI output remains a layout approximation.

Next launch: repeat navigator/POI selection, relation/trader filters, checkbox bulk Hide/Unhide/Favorite/Undo without list switching; compare scoped Market/Requests to full views and Back; try same-name POIs, missing observations and long names; test Escape-menu entry while paused and `]` / native-window switching. No live acceptance is claimed by offline tests.

## Final playtest polish

Rows and checkboxes now share one bulk-selection toggle path; row clicks also focus details, and do not clear other selected rows. Their hit regions do not overlap; bulk commands and Undo are unchanged.

Snapshot publication no longer clears player geography before the next 500 ms position poll. One guarded read settles before the immutable point is replaced. World/physical-save/playfield/player mismatch invalidates before reading, with scope checked again afterwards. Completed missing/failed coordinate evidence remains Unknown; no target-distance cache, name join or repaint read is introduced.

Contextual Market rows shrink from 64 to 32 logical pixels and omit the repeated freshness badge. Existing colours, background, distinct offers/prices, full-row hover and item navigation remain. Long names clip to their cell with full hover text. Synthetic tests cover selection/toggle/bulk identity, refresh pending/settled/unknown/changed-scope cases and compact virtualization; GDI previews remain offline approximations, not Unity acceptance.
