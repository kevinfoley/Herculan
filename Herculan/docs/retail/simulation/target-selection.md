# Target selection

The player's target selection: where it lives, the keys that make it, what can be selected and when a selection is dropped. What makes an object *known*, and so selectable, is [`sensors.md`](sensors.md); the point a selected target is shot at is [`aim-point.md`](aim-point.md), or one of its components with a Targeting Pod, [`targeting-pod.md`](targeting-pod.md). How an AI machine acquires its target is [`ai-targeting.md`](ai-targeting.md).

## Where the selection lives

`+0x1a4` is the selected target every homing weapon and most of the AI reads. It is a field of the **shared base**, not of the HERC class: an aircraft and an armed structure keep theirs at the same offset, and the two places that read another object's selection — `Ai_SelectTarget`'s scoring and `Mission_IsClearOfThreats` — read it off whatever object they are holding without asking what class it is. **For the player's machine nothing in the simulation writes it.** The selection is made in the cockpit widget tree at `CockpitViewInstance+0x210` and copied onto the machine once a frame by `Player_PerFrameCockpitUpdate` (`0041b130`). AI machines get theirs from a separate family, decoded in [`ai-targeting.md`](ai-targeting.md).

Every writer of `+0x1a4` also maintains `target+0x1a2`, a count of how many objects hold that one, and raises `+0x9d` ("target changed"), which suppresses lock for one tick. The armed and transport structure ticks raise `+0x9d` too, as shared boilerplate; **nothing reads a structure's copy** — the two readers (`Mech_PerTickSystemsUpdate` and `Mech_LockTonePlay`) are both mech-only.

| Key | Scancode | Function | What it does |
|---|---|---|---|
| `Enter` | `0x1c` | `TargetSelect_Cycle` (`0043349c`) | Cycle — rebuild the shortlist, take its head, or step |
| `'` | `0x28` | `TargetSelect_Nearest` (`004333c8`) | Nearest HERC or flyer — target class 0 or 2 ([object classification](sim-object-layout.md#object-classification)) — ignoring facing |
| `;` | `0x27` | `TargetSelect_SetObject(view, 0)` (`004332dc`) | Clear. Undocumented in the manual |
| `Tab` | `0x0f` | `TargetingPod_CycleComponent` | Step the Targeting Pod's component lock, if one is fitted — [`targeting-pod.md`](targeting-pod.md) |

`TargetSelect_SetObject(view, obj)` is the entry point for `;` and for a gunsight click (`Gunsight_UpdateAndPaint`, which takes the object from a pixel pick whose arming function has no reference found — [`../rendering/terrain-drawing.md`](../rendering/terrain-drawing.md#the-pixel-pick-and-the-occlusion-probe)). The F4 scanner's TARGET button is not a caller: it runs `TargetSelect_Cycle`, exactly as `Enter` does ([`mfd-scanner.md`](mfd-scanner.md#buttons)), and the joystick's target and nearest-target actions reach `TargetSelect_Cycle` and `TargetSelect_Nearest` from `Sim_PollPlayerInput`. `TargetSelect_SetObject` *walks* `+0x210` through the object list from a stored cursor until it lands on the object asked for, so a request for something unselectable ends with the selection back where it started.

### Cycle's shortlist — `TargetSelect_Cycle` (`0043349c`)

Everything selectable and inside the ±8999 cone is filed into one of four buckets by bearing error (`|err| >> 10`, clamped to 3), sorted by range within its bucket, keeping four. Flattening the buckets in order gives the shortlist: **nearest the crosshair wins, range only breaks ties inside a band**. A repeat press whose rebuilt head is unchanged steps to the shortlist entry after the current selection.

`TargetSelect_InForwardCone` (`00433250`) is the cone test: bearing less heading **plus** turret twist, within ±8999. The twist is added, not subtracted, and `Mech_PerTickSystemsUpdate` and the sensor sweep fold it in the same direction.

The angular-size correction the function computes from the target's range and shape radius is multiplied by a literal `PUSH 0x0` (`004335d0`) and is therefore always zero.

### Can this be targeted — `TargetSelect_CanTarget` (`00433174`)

Alive (`obj+0x99`/`+0xa4` both clear), on the other [side](sim-object-layout.md#object-classification), and **known** by either sensor route ([`sensors.md`](sensors.md#the-sensor-model--sim_detectiontick-004123ac)):

- radar-visible (`obj+0x95`) within `DAT_004d1cfc` = **200000**, the last of the scanner's three ranges (`MfdDisplay_Ctor` writes 50000/100000/200000) — read directly, not the current setting;
- or a held contact within `Detection_ContactScanRange` (`00426aec`) = **30000** on the short scan setting, **60000** otherwise.

Ranges here and in the cone test are measured from the machine (`view+0x203`), or from the watched object `DAT_004d2708` while the spectator flag `DAT_0049ef5c` is set — see [The spectator flag](external-views.md#the-spectator-flag--dat_0049ef5c).

[Radar mode](sensors.md#radar-mode) therefore sets how far the player can target. Passive, targeting depends on visual contacts and reaches the 60000-unit contact range, about 360 m; active, it reaches the 200000-unit radar range (1200 m) as far as terrain gives line of sight — a stock-mission hostile at 831 m is targetable that way. A distant enemy is usually targetable because *its own* radar is on: `Mech_AiCombatReassess` switches an AI machine to ACTIVE the moment it enters a fight and a squadmate of the player's back to PASSIVE ([`sensors.md`](sensors.md#how-an-ai-machines-radar-is-set)).

### Losing the selection — `CockpitView_PerFrameUpdate`

The cockpit's per-frame update, run from `Sim_RenderFrame` just before `Player_PerFrameCockpitUpdate` copies the selection onto the machine, ends by re-running `TargetSelect_CanTarget` on `view+0x210` and clearing it when that fails. A selection is therefore dropped the frame its target dies **or stops being known** — no longer radar-visible within 200000 and not a held contact within the scan range. The whole widget pass, this check included, is skipped while `view+0x20f` is set, which `CockpitView_ApplyViewState` does for view 4, the external view; a selection survives there until the cockpit returns.

Radar visibility does not blink the selection off: decay clears `obj+0x95` and the sweep repaints it inside the same `Sim_DetectionTick` (`004123ac`), and the check runs outside it.

## Rejected readings

| Obvious reading | Actually |
|---|---|
| The player's selection is never dropped: the death path `Mech_AiSelectBehaviour` (`0041eb34`) and `Ai_ShouldAbandonTarget` (`0041c4a8`, see [`ai-targeting.md`](ai-targeting.md#abandoning-a-target--ai_shouldabandontarget-0041c4a8)) both run only for AI machines, and a text search for writes to `+ 0x210)` finds only the three selection commands | `CockpitView_PerFrameUpdate` clears it, written by the decompiler as `param_1[0x84] = 0` — `0x84 * 4 = 0x210` — so an offset search misses it. See [Losing the selection](#losing-the-selection--cockpitview_perframeupdate) |
