# Handoff — the HERCULAN mission editor

The editor, `Herculan.Engine.Editor`, run on its own by `Herculan.Engine.Host.Editor` and from the game's Editor menu item, opens a mission as its `.MSN` and `.ENG`, shows it through the `script.dat` generated from them, edits it, and saves it into the install, where the game loads it in place of the archive's.

| Doc | Covers |
|---|---|
| [handoff-editor-document.md](handoff-editor-document.md) | how the `.MSN` is held, which record each edit writes, the edit tools, regeneration and rebuild, saving and export |
| [handoff-editor-viewing.md](handoff-editor-viewing.md) | opening, New mission, theater and time of day, selection, camera, picking, overlays, groups, waiting groups, the mission check |
| [handoff-editor-placement.md](handoff-editor-placement.md) | moving objects, waypoints, groups and area corners; routes; trigger areas |
| [handoff-editor-roster.md](handoff-editor-roster.md) | adding hercs, flyers, ground vehicles and structures; type and fit; sides; deleting; the player's squad |
| [handoff-editor-mission-logic.md](handoff-editor-mission-logic.md) | orders, wiring, actions, timers, objectives, the mission's text |
| [handoff-editor-conditions.md](handoff-editor-conditions.md) | what the editor shows, conditions, variant tables, Present when |
| [handoff-editor-ux-plan.md](handoff-editor-ux-plan.md) | the planned UI redesign, by phase; its evidence is [handoff-editor-ux-review.md](handoff-editor-ux-review.md) |
| [handoff-editor-msn-scene.md](handoff-editor-msn-scene.md) | the planned rewrite that shows and selects the `.MSN` directly, every record included, in place of the generated `script.dat` |

## What every edit does

These hold for every edit in the docs above unless the doc says otherwise:

- An edit is one step of the undo history (Edit > Undo and Redo, Ctrl+Z and Ctrl+Y), runs once the frame's panels are drawn, and builds the editor's state again from the generated file ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)). A move, a side change and a theater or time-of-day change update the scene in place instead.
- A typed field applies when it loses the focus.
- The Delete key deletes the selection, as the view's Delete does, unless a text field has the keyboard or a drag is under way.
- A delete or removal is refused while another record references what it removes, in any record and whatever its condition; the view lists each referrer as a link, one per line.
- An edit writes a field in the record that supplies it ([Which record an edit writes](handoff-editor-document.md#which-record-an-edit-writes)).
- "The editor's choice" marks behaviour this engine chose where retail gives no answer; "the user's choice" one the user decided.
- Retail counts are over the 62 `.MSN` files with campaign conditions not applied, from `HercWorks.Query`.

## Writing the editor's UI

What the user has corrected in the editor's panels, which every new view follows:

- **Vocabulary.** The player's group is the squad, never a "lance"; its other members are squadmates, never "wingmen". The player *pilots* a HERC; "fly" is for flyers. The subject of a sentence is the player, the mission or the HERC, never "the game". A group's side is its **Team**. A structure or ground vehicle is titled by its type and the name the MFD's F5 screen prints (`Base type 40: <name>`). The editor says "references", never "names it".
- **Plain words, short.** A note says what the user sees or can do, in one short sentence, not how the records produce it: "No heading of its own: it faces along its route's first leg", not "names none: the bearing of its route's first leg". Labels are the user's words ("Related objectives", not "Objectives asked of it").
- **No note for what a disabled control already shows.** A field the user cannot change is drawn disabled; a note is added only when the reason is not obvious.
- **Notes go through `EditorText`.** `EditorText.Note` draws every aside wrapped, in a grey lighter than ImGui's disabled text (unwrapped, the 340-pixel Properties panel cuts it off). `EditorText.Emphasis` draws, in bold, the one note per view the user must not miss.
- **Latin-1 only in UI strings.** The ImGui font atlas holds Basic Latin and Latin-1, so an em dash or a curly quote draws as `?`. Use parentheses, colons and straight quotes; `°` is in range.
- **Identity first.** An object's view opens with its name and then its group.
- **Errors point at the fix.** A refused input shows its reason in a strong red and tints the field to change.

## What is left, by row

Beyond the gaps each doc lists:

| Row (block) | Not edited |
|---|---|
| 1, conditions | a variant table's requirement; route tables, squad tables and tables under a shared key; a condition's place in the row |
| 2, header | the objective type (offset 6, which picks the player's objective arm) and the training mission number; the zone after New mission. The difficulty and the two training cheats are overwritten at every shell load |
| 3, movies; 5, debrief | not shown |
| 4 (13), text package | the briefing movie (`0x8E`); the records after the first; a line's trailing newline |
| 7 (2), headings | a heading for an object or a group |
| 8 (3), routes | naming one from a group's own route field (row 16) |
| 10 (5), actions | the text line (`0x44`) |
| 12 (7), mechs | a Cybrid's weapon fit and ammunition types; AI radar and cruise speed, starting condition, out-of-action counter writes |
| 13 (8), flyers | the out-of-action counter writes |
| 14 (9), structures and ground vehicles | the starting condition (0 in 29% of retail records), out-of-action counter writes (698 retail records write one) |
| 15 (10), orders | the formation and the point |
| 16 (11), groups | the formation id, paints-ground flag, heading, own route, out-of-action counter writes and map-shown flag; deleting one other than with its last member |
| 17 (12), objectives | the condition ref, which the campaign layer authors; their order |

**Counters.** Actions, objectives and out-of-action reports write counters, which persist into the campaign flags the next mission's row 1 tests ([`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store)). Nothing lists which counters a mission writes, so nothing helps choose a free one or wire it to a condition.

## Scope

| Slice | Size |
|---|---|
| **Next:** the rest of the campaign layer, the debrief | — |
| Mission text: debrief lines, action lines, the briefing and debrief movies | small–medium |
| Edit roster and group fields | medium |
| The objective type and the training mission number | small |
| Counter overview | small |
| A line-length check against the mission screen's wrap, and a line's trailing newline | small |
| Ordering objectives; deleting a group directly; clearing a reference that refuses a delete | small each |
| Heading entry and a turn handle | small |
| Mission check re-run after each move and side change | small |
| The mission list showing each mission's zone and theater | small |
| Empty formation slots, briefing-only order point | small each |
| Tint every area of a selected action; tint ground planes too | small |
| Waiting groups as translucent ghosts | small–medium |
| Orthographic top-down view | small–medium |
| Action graph | medium–large |
