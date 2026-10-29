# Component targeting — the Targeting Pod

A **Targeting Pod** (catalog id 29, `mech+0x30b` — [`equipment-pods.md`](equipment-pods.md)) lets the player aim at one part of the selected machine instead of at its [aim node](aim-point.md). The manual: select with `Enter`, then "use [Tab] to cycle through that target's components", and Automatic Turret Tracking follows the part rather than centre mass — which is how a pilot cripples a Cybrid's legs and salvages the rest of it. How the selection itself is made is [`target-selection.md`](target-selection.md).

Three functions drive it, each a plain direct call rather than a slot:

| | Called by | |
|---|---|---|
| `TargetingPod_ResetComponentLock` (`0040e484`) | `Player_PerFrameCockpitUpdate+0x26b` | The selection changed: restart the rotation, or switch component targeting off |
| `TargetingPod_CycleComponent` (`0040e4ac`) | `CockpitWidgets_HandleCommand+0x321`, and the resolver below | `Tab` — step to the next component the target still has |
| `TargetingPod_ResolveAimPoint` (`0040e4dc`) | `Player_ResolveTargetAimPoint+0x51` | Where to aim, and which component that is |

`Tab` is scancode `0x0f`, dispatched like every other cockpit command — see [`cockpit-input.md`](cockpit-input.md#keyboard-commands-are-scancodes). It only reaches the pod while the heads-down display is down: in view mode 1 the same case hands `0x0f` to the HDD's own command slot instead, which is the manual's `Zoom Map In/Out`.

`TargetingPod_ResolveAimPoint`'s fourth parameter is dead. `Player_ResolveTargetAimPoint` passes the target's occupancy-array pointer `mech+0x20e`, and `[EBP+0x14]` is untouched in the whole body — while the two out-parameters either side of it, `[EBP+0x18]` and `[EBP+0x1c]`, are read. The pod reaches the same array through the target's own slots instead.

## The lock is two fields, and they are not interchangeable

| Field | |
|---|---|
| `+0x7d` | `short` — the **cursor**, a position in the seven-entry rotation `TargetingPodComponentRotation` (`0049a060`). `-1` is no component lock |
| `+0x86` | `int` — the **component id** that cursor last resolved to. This, and never the cursor, is what the target is asked about |
| `+0x81` | `LongCountdownTimer` — the decay a damaged pod runs, counter at `+0x82` |
| `+0x7f` | `short` — the pod's own component damage, 0–256, cached by `TargetingPod_ConditionChanged` (`0040ef6c`) |

**A reset moves the cursor and not the id.** `TargetingPod_ResetComponentLock` writes `+0x7d` alone, so restarting the rotation on a new selection leaves `+0x86` holding the component the *previous* target was locked to, and the resolver goes on aiming at it — the machine's own part of that name, if it still has one. Selecting a new machine therefore does not put the aim back on centre mass; only pressing `Tab` moves the id. On the first selection of a mission the id is the zero the mount's block was allocated with, which names component 0.

Otherwise the two move together and only together, because the vtable slot that advances the lock writes both: it returns the new cursor and writes the component id through an out-parameter, and answers `-1` in both when the rotation has nothing left. `+0x86` is the pod's last field — `TargetingPod_Ctor` asks for `0x8a` bytes against `0x7d`–`0x85` for the other four.

## The rotation belongs to the target — vtable `+0x80` and `+0x84`

Two `SimObjectVtable` slots exist for this and nothing else; the pod is the only caller of either.

- **`+0x80` `(this, cursor, int *outComponentId)` — advance.** `Mech_NextTargetableComponent` (`00415558`) steps the cursor over `TargetingPodComponentRotation` = `{0, 4, 5, 7, 8, 9, 10}`, skipping any slot the machine has lost (its occupancy array at `+0x20e`) and wrapping at 7. `Base_NextTargetableComponent` (`00403624`) is the structure's, over its type's whole component list. `SimObject_NextTargetableComponent_None` (`00411b1c`) answers `-1`, so a flyer has no parts to single out.
- **`+0x84` `(this, componentId)` — still there?** `Mech_ComponentPresent` (`00415540`) reads that one occupancy entry. The resolver asks before using a lock, so a component shot off between presses moves the lock on rather than aiming at nothing.

**The walk steps before it looks, and the slot it started on is never tested.** A machine whose only remaining rotation slot is the one the cursor already sits on answers `-1` to both, and a cursor of `-1` is clamped to position 0 before the first step — so the first `Tab` after no lock gives rotation entry 1, component 4, and never component 0.

Seven of a machine's twenty-nine slots, straddling both the chassis band (0, 4, 5) and the systems band (7–10) of [`ai-targeting.md`](ai-targeting.md#which-component-the-shot-is-aimed-at--mech_aiselectaimcomponent-0041ce08)'s table — the manual's "target areas".

Only a HERC has them: `TargetingPod_ResetComponentLock` writes `-1` for every target whose target class (`obj+0x1a8`, [`sim-object-layout.md`](sim-object-layout.md#object-classification)) is not 0, which is the same fence that keeps a structure's and a flyer's `+0x58` out of the aim-point path ([`damage-system.md`](damage-system.md#where-a-component-stands--the-0x58-slot)). A structure's `+0x80` and `+0x84` are therefore installed but never reached.

## A damaged pod degrades in four steps

Every reader of the pod is a threshold on the cached reading at `+0x7f`, and the four do not agree on a cutoff:

| `+0x7f` | What stops | Where |
|---|---|---|
| `≥ 0x33` | The odds that the target's ECM spoofs the player's missile lock go back up: `Mech_PerTickSystemsUpdate`'s re-roll weight returns to `0x14` from the `5` a healthy pod buys — [`missile-lock.md`](missile-lock.md#ecm) | `Mech_PerTickSystemsUpdate` |
| `> 0x68` | The lock stops holding: the resolver runs the countdown at `+0x81` on every frame it answers, and each expiry reloads 5000 — about 2.4 s in that counter's unit — and drops the cursor back to `-1`. So past 40% damage the player keeps the component for a couple of seconds at a time and has to press `Tab` again. The counter is never initialised, so the first frame a damaged pod resolves on expires at once | `TargetingPod_ResolveAimPoint` |
| `≥ 0x9b` | Component targeting stops entirely — the resolver takes the target's vtable `+0x24` aim node and reports no component, exactly as a machine with no pod does | `TargetingPod_ResolveAimPoint` |
| `> 0xa9` | An **AI** machine carrying one stops preferring the systems band when it picks a component to shoot at | [`ai-targeting.md`](ai-targeting.md#which-component-the-shot-is-aimed-at--mech_aiselectaimcomponent-0041ce08) |

The Targeting Pod is the only pod that caches its damage, and a pristine pod's cache reads zero — [`equipment-pods.md`](equipment-pods.md#where-a-pod-reads-its-damage). The pod's block is zero-allocated on every path, so the lock's other three fields start at zero too: cursor 0, component 0, expired decay.
