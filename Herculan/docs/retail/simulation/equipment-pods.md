# DBSIM.EXE equipment pods

The five non-firing pods a HERC can carry — ECM, TARG, SHLD, TURB, ENRG. This doc owns the pod as an **object**: how the five classes are built, what each one overrides, how their cockpit rows behave, when they tick, and the damage curve they share. What each pod's contribution then *does* belongs to the system it feeds, and stays there:

| Pod | Effect owned by |
|---|---|
| ECM | the jammer flag and the spoof roll in [`missile-lock.md`](missile-lock.md#ecm) |
| TARG | [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod) |
| SHLD | [`damage-system.md`](damage-system.md#the-shield-system) |
| TURB | the speed term in [`mech-locomotion.md`](mech-locomotion.md#damage-effects-on-movement) |
| ENRG | [`reactor-energy-pool.md`](reactor-energy-pool.md#reactor-output-rate--mech_computereactorrate-00417d08) |

Pods are ordinary weapon mounts on ordinary hardpoints. At the end of `Mech_ConfigureLoadout`, `MechLoadout_FileEquipmentPods` (`0040fb2c`) walks the finished mount list and files five weapon ids into a five-pointer array. The switch keys on the mount template's `+0x56`, which `Weapons_LoadResourceTables` (`0040fc8c`) writes as the record's own table index — so it is the `SHELL0.VOL` `gam\WEAPONS.DAT` catalog id.

| Slot | Offset | Id | Name | Effect |
|---|---|---|---|---|
| 0 | `+0x307` | 18 | ECM | on/off from its own gauge, `EcmPod_Tick` (`0040f184`), which posts the jamming messages `0x2a`/`0x2b` |
| 1 | `+0x30b` | 29 | TARG | component targeting, [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod) |
| 2 | `+0x30f` | 30 | SHLD | shield capacity, `Mech_ComputeShieldCapacity` (`00417bec`) |
| 3 | `+0x313` | 32 | ENRG | reactor rate, `Mech_ComputeReactorRate` (`00417d08`) |
| 4 | `+0x317` | 31 | TURB | speed while engaged, `TurboPod_Tick` (`0040f1f0`) and [mech-locomotion.md](mech-locomotion.md) |

Slot order is not id order (`0x1f`→[4], `0x20`→[3]). The switch assigns rather than accumulates, so a second copy of a pod fills the same slot and contributes nothing — the last mount in hardpoint order wins.

Each pod is its own class: `Pod_CtorBase` (`0040e234`) installs the shared table `00498cdc` after `WeaponMount_CtorBase`, and one of `EcmPod_Ctor`, `TargetingPod_Ctor`, `ShieldPod_Ctor`, `EnergyPod_Ctor` and `TurboPod_Ctor` (`0040e274`, `e308`, `e344`, `e380`, `e2bc`) then replaces it with its own and writes its catalog id to `+0x77`. **A pod's layout diverges from a weapon mount's at that offset**: `+0x77` is the catalog id and `+0x79` the cockpit gauge handle, where an ammunition mount keeps the gauge handle at `+0x77` itself; and what `+0x7d` and `+0x7f` hold differs again per pod, so an offset read off one of these classes says nothing about the others.

## What each class actually overrides

Against the shared table `00498cdc`, the five reach for only four slots between them — `+0x18` and `+0x6c` aside, which every class differs in because they are its destructor and its class-descriptor pointer rather than behaviour. A dash is the base row inherited unchanged:

| Pod | `+0x34` pool turn | `+0x50` tick | `+0x64` gauge | `+0x68` condition changed |
|---|---|---|---|---|
| base | `WeaponMount_RefireTick` (`0040ef94`) | `Pod_TickBase` (`0040f144`) | pure virtual, `Rtl_PureVirtualCalled` (`00496684`) | `WeaponMount_ConditionChangedBase` (`0040ee0c`) |
| ECM | — | `EcmPod_Tick` (`0040f184`) | `EcmPod_CreateGauge` | — |
| TARG | — | — | `TargetingPod_CreateGauge` | `TargetingPod_ConditionChanged` (`0040ef6c`) |
| SHLD | — | — | `ShieldPod_CreateGauge` | — |
| ENRG | — | — | `EnergyPod_CreateGauge` | — |
| TURB | `TurboPod_ChargeTick` (`0040f0d0`) | `TurboPod_Tick` (`0040f1f0`) | `TurboPod_CreateGauge` | — |

Inheriting `WeaponMount_RefireTick` in the pool slot is what makes a pod free to run: it counts the mount's refire timer down and returns the budget untouched. **The Turbo Pod is the one pod that draws on the pool** — `TurboPod_ChargeTick` spends 35 of its own charge per tick while engaged, then — if the mount is not destroyed — buys back `min(20, budget, 2000 - charge)` out of the budget the weapons left, toward the 2000 its constructor starts it at. What that does to the weapons' share is in [`reactor-energy-pool.md`](reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4).

**The Shield and Energy pods are inert objects.** Neither overrides anything but its own gauge constructor: no tick of its own, no notification, no state. That is the pod *object*, not the number it feeds: each is reached by name from the one routine that wants it, and `Mech_ComputeShieldCapacity` re-runs on every `Mech_ComponentDamageWrite`, so a Shield Pod's contribution really does fall away as the pod is shot. `Mech_ComputeReactorRate` has no second caller, so an Energy Pod's does not. `es2_fieldscan.py` over `0x307`–`0x317` finds nothing else — `Mech_ComputeShieldCapacity` is the only access to `+0x30f` it reports and `Mech_ComputeReactorRate` the only access to `+0x313`. That scan resolves the rebase idiom (it recovers `AlertPanel_Enter`'s writes through an `EBX+0x300` alias, on an unrelated object), so a slot reached as `equipmentPods[n]` off the array base would show; and no caller indexes the array in any case, since the two `+0x307` readers at `0041aa10` and `0041aa44` dereference slot 0 directly. It remains a null result.

**The `+0x50` tick runs on the player's machine only.** Its one call site is `WeaponMounts_PerFrameUpdate` (`00410b40`), whose one caller is `Player_PerFrameCockpitUpdate` — so an AI machine's pods are never ticked at all. What that does and does not cost such a machine, both checkable in play:

- **An AI machine engages its Turbo Pod without the tick.** The flag at `+0x81` has three writers, not one: `TurboPod_Tick`, the pool turn `TurboPod_ChargeTick` that drops it when the tank empties, and `TurboPod_Engage` (`0040f09c`), which `Mech_BehaviourFleeThink` and `Ai_DriveToPoint` call directly. So a fleeing or sprinting AI machine does get the speed bonus, and gets it silently, because the engage tone is gated on the pod having a cockpit gauge and no AI machine's has one — but it has no button, so nothing engages one anywhere else, and it holds the pod on for exactly as long as the behaviour keeps asking. The two call sites and their gates are in [`ai-navigation.md`](ai-navigation.md#drive-to-a-point--ai_drivetopoint-0041fac4) and [`ai-combat-states.md`](ai-combat-states.md).
- **An AI machine's ECM pod never sets `+0x7f`**, which `EcmPod_Ctor` clears and `EcmPod_Tick` alone writes. `Mech_GetEcmPodSwitch` (`0041aa44`) reads that field, and `Mech_PerTickSystemsUpdate` raises the per-tick engagement-action latch `obj+0xa2` on the jamming machine's own selected target when it is set ([`mission-deployment.md`](mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6)), so that half of ECM is the player's alone. The other half, the jammer flag at `+0xa1`, does work for an AI machine: it is derived from the radar mode instead of from the pod, as [`missile-lock.md`](missile-lock.md#ecm) describes.

A player-only tick also explains why `EcmPod_Ctor`'s null gauge handle at `+0x79` is not a crash: `EcmPod_Tick` dereferences it unconditionally, and only ever sees the real handle `EcmPod_CreateGauge` gave the player's own pod.

`Pod_TickBase` is a display refresh and nothing else: it takes the gauge's 8-byte state block through `PodGauge_GetStateBlock`, replaces byte 1 with the mount's destroyed flag at `pod+0x49`, leaves byte 0 — the button — and the other six bytes as they were, and pushes the block back through `PodGauge_SetStateBlock`. It touches no pod field. `EcmPod_Tick` and `TurboPod_Tick` end in the same two calls; what makes them ticks is the button handling in front.

## Only two pods have a button

`CockpitView_CreatePodGauge` (`004321d4`) switches on the catalog id at `pod+0x77` and builds one of three gauge classes: `TogglePodGauge_Ctor` (`00441998`, vtable `0049c8a4`) for ECM, `TurboPodGauge_Ctor` (`00441a34`, vtable `0049c878`, which derives from it and adds an LED bar over the pod's charge — [`../formats/cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#weapon-hardpoint-rows)) for TURB, and the plain `PodGauge_Ctor` (`00441524`, vtable `0049c8d0`) for TARG, SHLD and ENRG. **The split is the tick table again** — the two classes with a tick of their own are the two with a gauge class of their own.

Every pod row is clickable and sounds on a click: `PodGauge_Ctor` registers the row's `WeaponSelectGadget` child with `Widget_RegisterClickable`, and that child sounds `Widget_ClickSound` and forwards to its owner's slot 0 ([`../formats/cockpit-input.md`](../formats/cockpit-input.md)). What the slot does is where the classes part:

- `TogglePodGauge_OnClick` (`004419fc`) XORs the button byte at `gauge+0xc2` — structurally the same child-table search as `ShieldsGauge_OnClick`. `EcmPod_Tick` and `TurboPod_Tick` read that byte back out of the state block on the next tick. This is the whole of a pod's on/off toggle; the paint that shows it is in [`../formats/cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#weapon-hardpoint-rows).
- `PodGauge_OnClick` (`00441988`) sets the repaint byte and returns.

So a Shield, Targeting or Energy pod row clicks and sounds but has no on/off state to change, and `Pod_TickBase` would ignore it if it did. Clicking is not how a Targeting Pod is used either — that is `[Tab]`, through `CockpitWidgets_HandleCommand` ([`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod)).

**A pod row does not care which mouse button pressed it.** Every other weapon row's handler branches on the button bit its `GetValue` slot returned — left arms, right chains — but `TogglePodGauge_OnClick` takes only the gauge and the child, so a right click toggles a pod exactly as a left one does and neither arms nor chains anything. The number keys reach it too: `CockpitWidgets_HandleCommand` answers codes `0x02`-`0x0b` by calling `WeaponMounts_SelectByGauge` on the row's gauge *and* pressing its select gadget, so the bare key runs the arm (which a pod refuses) and then the toggle. `[Alt]` and a number is the other command bank, `0x202`-`0x20b`, which the weapon manager answers itself and which therefore never reaches a pod.

### What the two ticks do with the button

`EcmPod_Tick` (`0040f184`) is the simpler: it posts `JAMMING ENGAGED` (`0x2a`) or `JAMMING DISABLED` (`0x2b`) whenever the button differs from what it copied last, then mirrors it into both `+0x7d` and `+0x7f`, and pushes the block back with the destroyed byte refreshed. It **posts rather than replaces**, unlike the radar and auto-track toggles, so a run of quick presses reads the whole sequence out.

`TurboPod_Tick` (`0040f1f0`) is a state machine over the pod's charge at `+0x7d` and its engaged flag at `+0x81`:

- The button off drops the flag, full stop.
- The button on and the pod idle engages it — through `TurboPod_Engage` (`0040f09c`), which demands more than 600 of charge and sounds `0x2c`, the same servo the throttle lever uses. **So a pod that has just run itself dry cannot be switched straight back on.**
- The button on and the pod engaged cuts out the moment the charge reaches zero.
- Then **the pod writes the button back from the flag**, so a row pressed with too little charge lights for one frame and goes out again by itself. Nothing else in the cockpit clears a button it did not set.

The charge itself is the Turbo Pod's `+0x34` pool turn, `TurboPod_ChargeTick` (`0040f0d0`), described [above](#what-each-class-actually-overrides). An engaged pod spends its charge whether or not the mount is destroyed, and only a live mount buys any back, so shooting the hardpoint a pod sits on leaves the pilot whatever is in the tank and no more.

## Where a pod reads its damage

**The Shield and Energy pods read their damage live** — `Component_ReadDamagePercent` against the mount's own component, `.GL +0x17` + 19, each time their bonus is recomputed. **The Targeting Pod is the only pod that caches its damage**: its vtable `+0x68` override, `TargetingPod_ConditionChanged` (`0040ef6c`), fills `+0x7f` from the reading `Mech_ComponentDamageWrite` hands every mount after a write. Same quantity, two mechanisms reached by different code, so a search for one offset does not find the other. What the cached reading gates is in [`target-selection.md`](target-selection.md#a-damaged-pod-degrades-in-four-steps).

**A pristine pod's cache reads zero**, so the two mechanisms agree at spawn. Neither `TargetingPod_Ctor` nor `Mech_ConfigureLoadout` writes `+0x7f` — the constructor writes the gauge handle and the catalog id and stops, and the loadout pass ends at `MechLoadout_FileEquipmentPods`, `Mech_ComputeShieldCapacity`, `Shield_RefillToBalance` and `Mech_ComputeReactorRate` without touching any mount's condition slot. What settles it is that every mount block starts zeroed ([`weapon-mounts.md`](weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8)), so a pod block is zero on every path.

Because `Mech_ComponentDamageWrite` then hands **every** mount its component's reading on every write anywhere on the machine, the cache tracks the live figure from there on. The two are still not interchangeable: the cache is only as current as the last write, and a change to a component reading that bypassed `Mech_ComponentDamageWrite` would part them ([Open](#open)).

## The damage curve both bonuses share

Gated off entirely at 225/256 damage: `scale = 1024 - 204 * (damage / 51)`, Q10 — five steps from 1024 (pristine) down to 208, then nothing. A pristine pod is worth `Q10(1024, base) = base`: it **doubles** the stat it feeds.

Three pods use it, each against its own base — see [`damage-system.md`](damage-system.md#the-shield-system) and [`reactor-energy-pool.md`](reactor-energy-pool.md#reactor-output-rate--mech_computereactorrate-00417d08) for what "doubles" amounts to for the Shield and Energy pods, and for the manual's claim about the Energy Pod that the pool's own literals disprove. The Turbo Pod is the exception to the doubling: its base is a literal 1000, so a pristine one is worth a shade under top speed rather than a second one ([`mech-locomotion.md`](mech-locomotion.md#damage-effects-on-movement)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The `+0x50` tick runs on the player's machine alone, so an AI machine never engages its Turbo Pod | The engaged flag at `+0x81` is not the tick's to write: `TurboPod_Engage` (`0040f09c`) is called directly by the AI's behaviours, as the AI bullet above lists. The tick is how the *player* engages one, not how anybody does |
| A pod row's click handler branches on the mouse button like every other weapon row's | `TogglePodGauge_OnClick` takes no button value, so a right click toggles the pod rather than chaining it. Only the energy and ammunition row classes read the button bit |

## Open

- **Open:** whether any writer of a component damage reading bypasses `Mech_ComponentDamageWrite`, which is the only path that refreshes the Targeting Pod's cache.
