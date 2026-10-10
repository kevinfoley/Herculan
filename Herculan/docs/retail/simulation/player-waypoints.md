# Player waypoints and the nav marker

What the machine the player is flying does about its own route, and the second point the player can put on the compass. The HUD gadget that draws both is [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#waypoint-indicators); this doc owns the simulation behind it.

**A player waypoint is the player group's mission route.** There is no separate record: the route is the same block-3 waypoint group, resolved to block-1 points, that an AI group walks — see [`ai-goals.md`](ai-goals.md#the-route-cursor-is-loaded-once) for the cursor and [`../formats/msn-mission-file.md`](../formats/msn-mission-file.md) for the file layout. The player's group holds it in `group+0x06` and its cursor in `group+0x04` like any other.

**Reaching one fires nothing.** Arrival posts a computer message and steps the cursor, and that is all it does. A mission event is a block-5 action, and the only positional route to one is a block-4 *trigger area* — a box or a radius around a block-1 coordinate, tested against a subject's position and nothing else (see [`mission-deployment.md`](mission-deployment.md#the-four-ways-an-action-activates)). Authors put those on the same coordinates their waypoints use, which is what makes arrival look like the trigger.

## The player's think — `Mech_BehaviourPlayerThink` (`0041c194`)

The think slot of behaviour states 1 `player` and 2 `player fly` ([`ai-dispatch.md`](ai-dispatch.md#the-22-states)). It decides nothing about movement — the player steers — and always returns zero, so the state never ends itself. Everything in it is gated on the machine being its group's leader, which for the player's own squad it always is.

```
if (mech != group.members[0]) return 0

next = Route_WaypointAt(group.cursor, group.cursor.index + 1)
if (next && Math_GroundDistanceBetweenPoints(mech.position, next) < 10000
         && !Route_NextWaypointClosesRoute(group.cursor)) {
    Computer_PostMessage(0x1d)                  // WAYPOINT REACHED
    Route_AdvanceCursor(group.cursor)
}
```

The arrival test is `Ai_DriveToPoint`'s exactly — 10000 units of ground range, 60 metres, measured with Z dropped ([`ai-navigation.md`](ai-navigation.md#drive-to-a-point--ai_drivetopoint-0041fac4)). Two things separate the player's arrival from an AI machine's:

- **It announces itself.** `SYSTEM.STR` message `0x1d`. No AI arrival posts anything.
- **It refuses a closed route's last leg.** `Route_NextWaypointClosesRoute` (`00423170`) answers yes when the waypoint ahead is both the route's last and the same point object as its first — the duplicate a patrol circuit closes on, and the one `Route_AdvanceCursor` would wrap straight off. So a circuit stops announcing at its last distinct waypoint instead of repeating waypoint 1 forever, and the cursor parks there.

**The cursor is shared with the group**, so the player's arrivals also drive `Group_IsOrderComplete` for the squad's movement orders, and the squad's own `travelling` members can step it too.

### The three arms this doc does not cover

Past the waypoint arm the function switches on the mission's own objective type — `script.dat`'s header at `+0x06` — into one of three more arms. They belong to the mission objective layer and are [`mission-objectives.md`](mission-objectives.md#the-player-thinks-objective-arms)'s. None of them touches the route.

## The nav marker

A point the player drops under their own feet and is steered back to. Three fields of the cockpit view, and nothing else in the image reaches them:

| Field | Meaning |
|---|---|
| `view+0x25e` | The marker's position, three ints copied wholesale from the machine's |
| `view+0x26a` | Set — a marker is down |
| `view+0x26b` | Armed — the player has since left 10000 units of it |

`NavMarker_DropAtPlayer` (`00434974`) drops one, replacing any already down and always leaving it unarmed. `NavMarker_Tick` (`004349ac`) is the whole lifecycle and runs once a frame from the cockpit's own paint (`CockpitView_PerFrameUpdate`): leaving 10000 units arms the marker, and returning inside 10000 clears it and posts the same `0x1d` the route arm posts. A marker dropped where the player stands therefore cannot clear on the frame it appeared. `NavMarker_Position` (`0043495c`) is the accessor the HUD child and the HDD command display's map read, and answers null while the marker is not set.

**It is dropped by `[Alt+D]`** — command `0x220`, set-1 scancode `0x20` with the `0x200` the cockpit adds for `[Alt]` ([`cockpit-input.md`](cockpit-input.md#keyboard-commands-are-scancodes)). `CockpitWidgets_HandleCommand` claims that code in its own switch, so it never falls through to the FlashComm panel, which is where the manual's `[Alt]`+hotkey documents `D` (DISENGAGE) going. The manual does not mention the marker at all.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The player's waypoints are their own mission record, separate from the AI's routes | They are order slot 0's route on the player's own group, read through the same `Route_WaypointAt` the AI uses |
| Reaching a waypoint fires the mission action attached to it | A waypoint carries no action ref. What fires is a block-4 trigger area the author has put on the same coordinate |
| The waypoint indicator can point at the player's selected target instead of the route | The branch exists but its gate is never set — [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#waypoint-indicators) |
| `Ai_FollowRoute` is the only thing that advances a route cursor | It is one of four callers of `Route_AdvanceCursor`, and this think is another — [`ai-navigation.md`](ai-navigation.md#follow-the-route--ai_followroute-0041fb60) lists all four |
