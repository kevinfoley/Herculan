# Handoff — outstanding vtable and class naming work

> **This file is a scratchpad, not a status record.** It lists what the DBSIM vtable naming pass left undone. It is never the authority on what is or is not done: `tools/scripts/es2_naming.py vtables DBSIM --unnamed` is, and the topic docs own every finding. Anything here that gets done should be deleted from this file.

Every slot of all 217 DBSIM vtables is named. What follows is around them.

## Ghidra database

- **One late start.** Ghidra starts a function at `00442956`, six bytes inside `WeaponSliderGadget_Ctor` (`00442950`, whose `55 8B EC 83 C4 CC` prologue it leaves outside). `es2_late_entries.py` reports it as INVERSE. Repair with `es2_naming.py fixentry DBSIM 00442950`, dry run first.
- **The `analysis_out` dumps are stale.** Some 260 functions were named or renamed in the database, 55 of them newly created, since they were made, so `FUN_` labels there are out of date. Regenerate with `tools/scripts/ghidra_full_decomp.py`.

## Functions not yet named

- **Destructors named only by a class record**, for classes with no vtable, so outside the slot pass. Each record's `+0x28` gives the address (`es2_classes.scan()`'s `dtor`):
  - Pools: `ObjPool<DEBRIS>` `00408f54`, `ObjPool<EXPLOSION>` `004083ae`, `ObjPool<FIRE>` `0046bb11`, `ObjPool<FLYER>` `00422069`, `ObjPool<FlatObj>` `004099ec`, `ObjPool<LightSource>` `00407a3e`, `ObjPool<MECH>` `0041c0c5`, `ObjPool<METEOR>` `0040a030`, `ObjPool<SMOKE>` `004095d5`, `ObjPool<SMOKE_BALL>` `00409604`, `LoosePool<BASE>` `00406aec`, `LoosePool<PROJECTILE>` `0040a209`.
  - Arrays and stacks: `ARRAY<GUN_STATE *>` `0041c00f`, `ARRAY<TSPartBase * *>` `00420a37`, `ARRAY<short>` `0041bf94`, `STACK<short>` `0040dd1e`.
  - Others: `AnimDisplay` `00466955`, `Assert` `00479b14`, `CONNECT_INFO` `004209ab`, `DVJoystick` `00477524`, `DVKeyboard` `00477904`, `IMKeyboard` `0045c03c`, `LightManager` `00407acd`, `MECH_TYPE_DATA` `00420867`, `MGlobs` `0045cc53`, `TSTransformState` `00476c1a`, `VolumeGroup` `00467ae4`.
- **Vtable installers that are not class functions:** `0042db40` and `0044041c`, static initialisers building global `GLRectangleRegion`s; `00453d10`, which writes a palette file (`PAL:` and `VGA:` chunks) through a `FileRWStream`; and `0043034c`, the palette module's initialiser, reached through a stored pointer at `00430e93`.
- **Callees the pass cited but did not read**, so left unnamed under the stop rule: `00439a0c` (the vertical LED bar's lit-run fill), `00439c48` (the bitmap LED bar's span draw), `00452144` (the whole of `PanelAmbience_Paint`), `00447014` and `00447048` (FLASH COMM's `,` and `.` keys, per [`../formats/mfd.md`](../formats/mfd.md)).

## Slot roles not established

Each named for its body only:

- `SimObject_TickNoOp` returns 1; what the `+0x14` tick's return means to its callers.
- `TexPoly_Slot28NoOp` (`TexPoly` `+0x28`) and `CTLWindow_Slot00NoOp` (`CTLWindow` `+0x00`).
- The flag `HddDamageScreen_Repaint` (0) and `HddDamageScreen_Tick` (1) pass to `HddDamageScreen_Update`.

## `known_vtables.json` shapes still missing for DBSIM

Slot meanings for these families live only in the per-function entries: the TS part family (`TSPartBase`, `TSPartList`, `TSGroup`, `TSBSPGroup`, `TSBSPPart`, `TSDetailPart`, `TSCellAnimPart`, `TSBitmapPart`, `TSShape`, `ANShape`, `GridShape`, `hzline`, `CONFIG_PART`), the `TSBase` and poly family, the AN sequences, the GL and stream classes (VSHELL has shapes for all three; DBSIM's tables are unlabelled), the owning cockpit displays (`PanelGauge` and below, `HUDGauge` and below, `MFDisplay`, `HDDisplay`), the message ports, the bar graphs and the alert panels.

## Tooling

- `es2_naming.py apply` runs only `ES2ApplySymbolNames`, so a batch with `?` slot targets needs a hand-run headless session with `ES2DefineFunctionAt` first. A `--define` option would fold that in.
- `es2_naming.py body` cannot disassemble bytes Ghidra has no function for; the pass used a scratch capstone script.
- Nothing lists class records with their bases, `+0x28` destructor and `+0x14` operator delete beside their `known_symbols` names; the pass used a scratch script over `es2_classes.scan()`.
- `es2_naming.py vtables`' "installed by" credits a vtable store in undefined code to the preceding Ghidra function (DRAWABLE's destructor shows up as `004785c6`).

## Engine changes to check in play

The pass changed five behaviours to match retail. Each needs a look in the running engine:

- An AI machine left with only pods and empty magazines now counts as disarmed and fires its defeat action (`MechObject.ChooseWeapon`, `WeaponMount.IsSpent`).
- Combat rating drops as ammunition runs low (`WeaponMount.CountsInCombatRating`).
- A damaged energy weapon recharges more slowly (`WeaponMount.ConditionChanged`).
- A missile tower's rockets home on its target (`SimWorld.FireRocket`).
- Structures detect all round, with no sensor arc (`Detection.InSensorArc`).
