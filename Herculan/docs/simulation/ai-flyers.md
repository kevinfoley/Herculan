# Flyer AI — the `Flyer` class' own behaviour layer

The Cybrid aircraft. `Flyer` is a class of its own, not a HERC: it has its own behaviour state table, its own thinks, its own control law, and its own move. What it shares with a machine is the *dispatch* — see [`ai-dispatch.md`](ai-dispatch.md), whose model applies unchanged — and the flight model, which is the player RAZOR's ([`razor-flight.md`](razor-flight.md)).

Retail ships one flyer chassis with data: `SKIMMER` ("Landskimmer"). `nam\FLYERS.NAM` also lists `HOVTANK` and `DROPSHIP`, and neither has a `.DAT`, `.COL`, `.DMG` or `.FM`, so neither can fly or be shot.

## The seven states

`FlyerBehaviourStateTable` (`00499cf8`) holds seven `0x3c`-byte descriptors, built at startup by `Flyer_BuildStateTable` (`00414c65`) from `FlyerBehaviourSlotBlocks` (`00499e9c`) and `FlyerBehaviourStateNames` (`00499f98`). The descriptor layout is the mech one minus its trailing `+0x3c` string index — an aircraft never appears on the [F7] comm page — which is what makes the stride `0x3c` where the mech table's is `0x3e`.

| # | Name | Think | Move | Reassess | Dwell | Flags |
|---|---|---|---|---|---|---|
| 0 | `deciding` | — | — | `00422d00` | 0 | `0x00` |
| 1 | `attacking` | `00422ca8` | `004218c4` | `00422d00` | 50000 | `0x01` |
| 2 | `patrolling` | `00422b34` | `004218c4` | `00422d00` | 5000 | `0x00` |
| 3 | `search and destroy` | `00422a80` | `004218c4` | `00422d00` | 5000 | `0x00` |
| 4 | `sleeping` | — | — | — | 500 | `0x01` |
| 5 | `scouting` | `00422bdc` | `004218c4` | `00422d00` | 5000 | `0x00` |
| 6 | `dead` | — | — | — | 500 | `0x01` |

The dwell is in the simulation's timer unit, not milliseconds ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)): 5000 is about 2.4 seconds and 50000 about 24. Only flag bit 0 is ever set, and it means what it means for a machine: `Mech_AiTick` skips the dwell countdown. `attacking`, `sleeping` and `dead` therefore never time out. `attacking` reaches its reassess when its think reports finished or when an order advance zeroes the countdown; `sleeping` and `dead` have no reassess to reach.

**Five states reassess, all through `Flyer_AiSelectBehaviour`.** There is no combat form: an aircraft picks its target inside its think and changes state from there. `sleeping` and `dead` carry a null reassess triple, so `Flyer_DispatchReassess` returns without calling anything — a sleeping aircraft stays asleep whatever its group's next order says, because an order advance only zeroes the countdown that would let a reassess run.

Four of `Behaviour_SetState`'s 30 call sites install a flyer state: `Flyer_Constructor` (`deciding`), `Flyer_AiSelectBehaviour`, `Flyer_ComponentDamageWrite` (`dead`) and `Flyer_EngageWithFlight` (`attacking`). The slots through which a machine reacts to being shot (`+0x50`) or to a squad order (`+0x28`) hold `Mech_ShareContact` (`00411aec`) and a refusal stub in the flyer's vtable, and neither changes a state. So an aircraft leaves `sleeping` only by being destroyed or through `Flyer_EngageWithFlight`, which only the flight leader's `patrolling` or `search and destroy` think calls — and a sleeping flight's leader is asleep too, because an order advance zeroes every member's countdown and the whole flight reassesses onto the same order in the same pass.

### Dispatch

The flyer's vtable `+0x14`/`+0x18`/`+0x1c` are three descriptor dispatchers — `Flyer_DispatchMove` (`004217fc`), `Flyer_DispatchThink` (`0042184c`), `Flyer_DispatchReassess` (`00421888`) — reading the same `obj+0x4d` behaviour block a machine has. `Mech_AiTick` (`00411cec`) therefore drives an aircraft exactly as it drives a HERC, through `Group_OrderTick`, and a flyer that is not a live member of a mission group never thinks.

## Orders — `Flyer_AiSelectBehaviour` (`00422d00`)

The reassess maps the group's current order verb onto a state. The verb set is [`ai-goals.md`](ai-goals.md)'s.

| Verb | State | Also |
|---|---|---|
| 0 `search/destroy` | `search and destroy` | |
| 3 `patrol` | `patrolling` | |
| 4 `sleep` | `sleeping` | latches `flyer+0xa5` |
| 5 `travel` | `scouting` | latches `flyer+0xa5` |

**Verbs 1, 2 and 6, and an empty slot, install the function's own code as a descriptor.** The switch has no default, and the descriptor it hands `Behaviour_SetState` lives in `ECX`, which nothing on that path writes. That is the same shape of defect `Mech_AiSelectBehaviour` has for an empty order slot ([`ai-goals.md`](ai-goals.md#a-group-with-no-order-at-all)), but the register does not hold zero here. `Flyer_DispatchReassess` decides whether to call by OR-ing the reassess triple's three words into `ECX`, and the callee inherits the result: the triple is `{00422d00, 0, 0}` in every state that has one, so `ECX` is `00422d00`, `Flyer_AiSelectBehaviour`'s own address. The "descriptor" the aircraft is put in is that function's opcode bytes. Its flag byte at `+0x08` is nonzero, so no countdown runs, and its move triple at `+0x24` is `{83c0bf0f, 4d7705f8, …}`, so the same tick's move dispatch calls a wild address. No retail mission reaches this: the 76 flyer groups in the 62 `.MSN` missions all have an order in their first slot, and the verbs they use are `search/destroy` (0), `patrol` (3) and `sleep` (4). Seven of them have a second order, always `search/destroy` after a `search/destroy`.

`flyer+0xa5` is the third byte of the out-of-the-fight triple ([`component-damage.md`](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5)), and no write that clears it is known ([Open](#open)). A flight ordered to travel or to sleep stops counting as something the other side has to contest, which is the point.

## The thinks

Three of the four open with the same movement step, `Flyer_FollowStep` (`00422a50`): the group's **first member** flies the route and everybody else keeps station on it. It also zeroes `flyer+0x96`, so a flight not fighting holds its radar passive.

The leader's half is `Flyer_LeadRouteStep` (`004224c4`). It steers at the waypoint one past the group's route cursor, as a walking machine does ([`ai-goals.md`](ai-goals.md#the-route-cursor-is-loaded-once)), and steps the cursor once that waypoint is inside 15000 ground units; with no waypoint past the cursor it keeps steering at the cursor's own, so a flight at the end of an open route never stops flying at its last point. Altitude is held at 30000 and the steering command is `Q16(bearing error, 28000)`. The wingmen's half is [station keeping](#station-keeping).

- **`scouting`** (`00422bdc`) is that step and nothing else.
- **`patrolling`** (`00422b34`) adds a target sweep for the leader alone, gated on the `flyer+0x5b` countdown: `Ai_SelectTarget` with mask `0x10` (reject own class), so a flight on patrol takes any machine or structure but not another aircraft. A sweep reloads the countdown with 10000, about five seconds ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)), but the countdown lies in the `0x28` bytes from `flyer+0x5a` that `Behaviour_SetState` zeroes, and the state's own reassess reinstalls it at the end of every 5000-count dwell. So the leader sweeps on the state's first tick and again after every reassess, about every 2.4 seconds, and the 10000 reload never runs out.
- **`search and destroy`** (`00422a80`) is the same with mask `0x11`, and what it finds must also pass `Group_IsOrderTarget` — a flight under this order engages only what the mission named.
- **`attacking`** (`00422ca8`) reports **finished** as soon as the target is immobilised or destroyed, which zeroes the dwell and sends the aircraft back through the reassess. Otherwise the leader flies the attack run and the rest hold station.

`Flyer_EngageWithFlight` (`00422bf0`) is what a successful sweep calls: the leader enters `attacking`, then hands every surviving member the same target and the same state. **A flight commits as one.** The function is recursive and terminates because only the leader takes the loop.

## Station keeping

`Flyer_FormationStep` (`00422598`) puts a wingman at a point `0x2000` ahead of its leader along the leader's heading, plus its own slot of `dat\FFORMS.DAT` ([layout](../formats/script-dat.md#the-flyer-formation-table)). The anchor is therefore a spot the leader is flying *into*, not the leader itself, which is what stops a wingman chasing a machine that keeps turning under it.

A wingman that is **ahead** of its station — the station bearing outside ±90° — while still aligned with the leader and inside 10000 units has its steering command *mirrored* (`-0x8000 - err`) rather than reversed, so it eases back from in front instead of hauling round through a half turn.

The slot's offset comes through the flyer's vtable `+0x78`, `Flyer_ApplyFormationOffset` (`00421e98`), and `Flyer_FormationStep` asks for it afresh every tick it holds station. It carries a Z, where a machine's or a structure's does not, so a wingman sits behind *and above* its leader. The file, and how the offset is rotated onto the leader, are [`script-dat.md`](../formats/script-dat.md#the-flyer-formation-table)'s.

## The attack run — `Flyer_AttackRun` (`004226a0`)

The only place a Cybrid flyer shoots, and a **two-phase circuit rather than a pursuit**. The phase is `flyer+0x1fe`.

- **Phase 0, extending.** The steering command is reversed — the aircraft flies *away* — while it holds the target's altitude plus 10000. Past 90000 units of range it turns in.
- **Phase 1, running in.** Outside 65000 it is still closing at that same height; inside it, the nose goes onto the target. The run ends and the phase returns to 0 the moment the bearing leaves ±45°, the height over the target drops under 3000, or the range closes inside 10000.

So a flight keeps coming round rather than trying to stay on something that can turn inside it.

**The aim is led.** The target's position is offset along its own heading by `(range>>3) * (targetSpeed*8) / shotSpeed`, with the range capped at 200000; `shotSpeed` is the aircraft's own travel speed until the run is close enough to shoot, and `PROJ.DAT` row 2's speed from then on.

Firing needs bearing and pitch both within ±1000, `flyer+0x1f4` within ±10, and the countdown handed at `flyer+0x21e` expired. **The first shot of each pass is a missile** — a flag at `flyer+0x5a`, cleared on every tick spent in phase 0 and by `Behaviour_SetState` when the aircraft enters `attacking`, lets one `Rocket_Fire(0)` off the `(500, 200, -100)` muzzle inside 30000 units — and every shot after it on that pass is a pair of `Bullet_Fire(2)` rounds from that point and its mirror. The countdown reloads with 1500, about 0.7 seconds ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)).

The flyer pool is zero-filled when it is built and `Flyer_Constructor` does not write `flyer+0x1f4`, so the gate passes unless something else writes that field; no writer is known ([Open](#open)). The flag is a `short` at `+0x5a`, so its second byte is the low byte of the `+0x5b` count the target sweep steps. The overlap changes nothing: the attack run and the sweep belong to different states, and `Behaviour_SetState` zeroes `+0x5a` through `+0x81` on every state change, so neither value carries into the other's state.

A flyer's missiles always have a lock: the gate is the launcher's vtable `+0x6c`, and the `Flyer` class' slot is a `return 1` stub. See [`rockets.md`](rockets.md).

## The control law

Every movement step — the leader's route step, station keeping and the attack run — ends in `Flyer_SteerAndFly` (`004222fc`), which turns a heading error into a bank and runs the flight model. The bank command is a 16-bit accumulator:

```
bank = Q16(-turn, 2500) + Q16(bankTurnRate, 32000) + Q16(rollRate, -5000)
```

where `bankTurnRate` is `flyer+0x287` — the heading rate the current bank is already producing, which is the term that stops a turn once it is actually coming round. Three arms follow:

- Past the chassis' bank limit (type record `+0x0e`, 14000 on `SKIMMER`) less a 1500 hysteresis band, and only in the direction that would take it further over, the aircraft is **held at the limit**.
- A steering command **under 2000** is answered with rudder (`Q16(-turn, 4000)`) and level wings rather than a bank at all — and the rudder is refused while the roll is 800 or more, so the wings come level first. The test is signed: a bank of any size to the negative side does not block the rudder.
- Anything larger is flown as a bank.

The pitch channel is `Flyer_PitchToAltitude` (`00422108`) into `Flyer_PitchCommand` (`00422098`): a height error becomes a pitch demand against a **fixed 10000-unit horizontal run**, so the angle asked for depends on the error alone; the demand is then scaled, resolved through the current bank, and damped by the pitch rate. Cruise altitude is a flat 30000 world units. `Flyer_PitchToAltitude`'s other arm, gated on `flyer+0xae` and reading `flyer+0x23c`, belongs to the walking machine's obstacle avoidance and leg placement ([`ai-navigation.md`](ai-navigation.md), [`mech-locomotion.md`](mech-locomotion.md)); the flyer pool is zero-filled, so the arm is live for an aircraft only if something writes `flyer+0xae`, and no flyer path that does is known ([Open](#open)). The leader term in the bank command is scaled by the leader's `flyer+0x1f8`, which is zero on the same terms: no writer is known.

`Flyer_ApplyFlightCommand` (`004221a8`) hands the result to `FlightModel_Step` (`00466a54`) — see [`razor-flight.md`](razor-flight.md#control-law-flightmodel_step) for the model itself. Two things it does on the way:

- **The two stick axes are squared**, sign kept (`(v*v)>>8`), before the ±0x100 clamp. Small commands are softened quadratically and only a large one reaches full deflection, which is what keeps an AI aircraft from sawing its controls.
- **The wing-damage argument is a stack array of four zeros.** A `Flyer` has one component, not a RAZOR's wings and nacelles, so it never takes the model's lost-wing or lost-nacelle arms.

### A flyer cannot change speed

The command array's throttle element is **zero on every call**: `Flyer_SteerAndFly` zeroes it before any of its three arms and none of them writes it, so the flight model's rate branch never steps the setting. The setting starts at `Flyer_Constructor`'s `0x200` on the model's ±`0x400` scale, and with `SKIMMER.FM`'s 500–1000 airspeed range that asks for 875, biased only by the aircraft's own pitch attitude.

The model's other throttle branch is chosen by the player's controls, not by the aircraft. `FlightModel_Step` (`00466a54`) reads the throttle element as a position, `axis << 3` ([`razor-flight.md`](razor-flight.md#throttle)), when the joystick capability block's `+4` reports a throttle and `004d1fd6` is 2 — option 26, the RAZOR block's THROTTLE row ([`preferences.md`](preferences.md#the-bindings-are-twelve-bytes-of-the-same-file)). It tests both whatever it is flying, so under that binding every Cybrid flyer's setting is written to 0 on each step and a `SKIMMER` cruises at 750.

`Flyer_FormationThrottle` (`00422260`) does work a throttle figure out of the station error and the leader's speed: it adds the correction to the previous value, clamps the sum to `[0x8c, 0x100]` and stores it in `flyer+0x21c`. That figure is not the command array's throttle element, and no other reader of `flyer+0x21c` is known ([Open](#open)), so it acts as a private accumulator. The route step and the attack run reload it with `0xb4`, and `Flyer_FormationStep` with `0x100` while the station is more than 15000 away. `FUN_00422a3c` (`00422a3c`) zeroes it and returns 0; what calls it is [Open](#open).

## The move — `Flyer_MovementTick` (`004218c4`)

The `+0x24` slot of the four states that have one.

**The world velocity is added to the position raw**, three plain `ADD`s at `0042190a`, with no `Math_IntegrateRateOverTick`. That is what makes a flyer fast: its world velocity is a **per-tick step** where `Razor_MovementTick`'s is a rate. Integrating it leaves a `SKIMMER` moving at a fraction of its speed.

Then four terrain probes, in the airframe's own frame:

| Probe | Point | Reaction |
| --- | --- | --- |
| Right wingtip | `(1000, -420, -300)` | Roll away, `Q10(4000, depth)` |
| Left wingtip | `(-1000, -420, -300)` | as above, opposite sign |
| Nose | `(0, 1000, 0)` | Pitch up, `Q10(2000, depth)` |
| Look-ahead | `(0, 15000, -1500)` | Pitch up, `Q10(20, depth)` |

Each kicks the rate and applies it to the attitude in the same tick. **None of them does any damage and none sweeps against objects** — a Cybrid flyer that scrapes a hillside is rolled or pitched off it and flies on, where a RAZOR loses the wing. The two pitch probes clear a negative pitch rate first, so a climb the terrain orders is not fighting a dive the control law asked for.

The frame the probes are placed through is captured after the position moves and is **not** rebuilt as they go, even though each contact marks the attitude stale: the original holds one matrix pointer across all four.

Finally the origin is clamped to `terrainHeight + 500`, and the flyby loop (sound `0x31`) is started or released at 30000 units from the view object.

## Death

`Flyer_ComponentDamageWrite` (`00421bb4`) loses the aircraft on component 0: the health record is one component with one dependent ([`component-damage.md`](component-damage.md#the-component-damage-system)), so destroying it sets `obj+0x99`, runs the aircraft's out-of-action report and defeat action ([`mission-deployment.md`](mission-deployment.md#the-out-of-action-report)) and credits the kill to the attacker through the attacker's vtable `+0x60`. It also stops the flyby loop, which the `dead` state's missing move can no longer do. Past the flag it installs the `dead` descriptor, which has neither think nor move, and **writes -100000 into `flyer+0x2e`**, the object's world Z. The wreck drops straight out of the world, and its own move cannot clamp it back to the ground, because the state it is now in has no move slot.

For the length of that call the debris carrier global `004a96e4` points at the aircraft's own world velocity, so wreckage the component cascade sheds keeps the speed it was doing — see [`destruction-effects.md`](destruction-effects.md). The hit test's own throw happens after the clear and gets nothing.

## Drawing

A flyer's shape is textured from **`ENEMY.DBA`**, the Cybrid mechs' bank, for every flyer type — [`../formats/dts-texture-binding.md`](../formats/dts-texture-binding.md#the-flyers-bank).

An aircraft is drawn by **cell** rather than by node — it loses components like a machine but has nothing that animates — and with its full attitude, bank and pitch included, where a structure has only a heading.

## Rejected readings

| Reading | Why it is wrong |
| --- | --- |
| A flyer is driven by its own tick, separate from the mech AI | It is the same `Mech_AiTick`. Only the three dispatchers and the descriptor table differ |
| The flyer behaviour table is the mech's, indexed differently | Two tables, different addresses, different strides, different names. `00499cf8` and `004993a4` share only their shape |
| `Flyer_MovementTick` integrates its velocity like `Razor_MovementTick` | It adds it raw. The two functions look alike, and integrating would leave a `SKIMMER` at a fraction of its speed |
| `flyer+0x2e`'s `-100000` on death is a fall rate | It is the Z position. There is no fall: the aircraft is simply put below the world |
| `Flyer_FormationThrottle`'s output controls the flight | The model's throttle input is the command array's fourth element, which `Flyer_SteerAndFly` zeroes on every call; `flyer+0x21c` is not handed to the model |
| A flight leader sweeps for targets every 10000 counts, the figure the sweep reloads | The countdown is in the bytes `Behaviour_SetState` zeroes, and the state's reassess reinstalls it every 5000-count dwell, before the reload can run out. The leader sweeps at every reassess |
| A flyer's own radar is what paints it | It zeroes `flyer+0x96` every non-combat tick. A flight is painted by the other side's scanner or not at all |
| All seven states share one reassess, so a sleeping aircraft wakes when its group's order changes | `sleeping` and `dead` carry a null reassess triple; the dispatcher returns without calling. The other five carry `00422d00` |
| An empty order slot, or verb 1, 2 or 6, leaves the aircraft in the state it had | The descriptor argument is `ECX`, which holds `00422d00` on that path, and the aircraft is put in a "state" made of that function's code |

## Open

- **Open:** no write that clears `flyer+0xa5` is known. `es2_fieldscan.py a5 --writes-only` finds stores of 1 on sim objects (`Flyer_AiSelectBehaviour`, `Ai_ChooseWeapon`, `Base_Construct`, `Base_TripleTurretThinkTick`) and stores of 0 only to the command screen's own `+0xa5`; a grep of the decompile for the offset finds no other.
- **Open:** no writer of `flyer+0x1f4` (the attack run's fire gate) or `flyer+0x1f8` (the leader-term gain) is known. `es2_fieldscan.py` over the whole image finds each field's one read and no write, a grep of the decompile for both offsets and their `int`-indexed forms finds nothing more, and `Flyer_Constructor`, 402 undefined bytes in the disassembly, writes neither in the decompile.
- **Open:** no flyer path that writes `flyer+0xae` or `flyer+0x23c` is known. `es2_fieldscan.py` finds their writes in `Mech_LocomotionTick`, `Mech_MovementTick`, `Mech_Constructor` and `Mech_ComponentDamageWrite` alone.
- **Open:** no reader of `flyer+0x21c` but `Flyer_FormationThrottle` is known. `es2_fieldscan.py` finds that read and the five writes named above; the field's other hits are other classes' `+0x21c`.
- **Open:** what calls `FUN_00422a3c`, which zeroes `flyer+0x21c` and returns 0 — the shape of a think. `es2_xref.py` finds no branch, stored pointer or vtable slot holding its address, and its `PUSH EBP` follows `Flyer_AttackRun`'s `RET` directly, so its entry is not late.