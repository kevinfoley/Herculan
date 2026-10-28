# Mission editor manual

A reference for mission designers. It describes how the game behaves, using the names the editor shows. Distances are in meters, as the editor shows them.

## Groups

Every unit in a mission belongs to a group. A group holds one kind of unit: HERCs, flyers, or structures and ground vehicles.

The group decides several things for all of its members:

- **Team.** Every member is on the group's team, human or Cybrid. A unit's own view shows its team but cannot change it.
- **Orders.** Orders belong to the group, not to its members, and every member carries them out together. A group works through up to ten orders in turn. It moves on to the next order when the current one is done, or when that order's "Ends when" trigger fires, and it stays on its last order. The group walks only its first order's route, whatever routes its later orders name.
- **Arrival.** A group with a trigger under "Waits on" is not in the mission until that trigger fires. It then arrives as the trigger's "Arrives" choice says. A group with nothing under "Waits on" is in the mission from the start.

A group's **leader** is its first member. Under "Patrol" and "Search and destroy", only the leader walks the route and looks for targets, and the other members keep formation on it ([What each order attacks](#what-each-order-attacks)). No other member takes over when the leader is destroyed. The rest of the group then stops following the route and stops looking for targets, though its members still fight back when shot.

**The player's squad** is the first group in every mission. It holds the player and the player's squadmates, and it is always in the mission from the start.

### Where members stand

A group has one **position**. Under "Starts at", it is either a position of its own or the first waypoint of its first order's route.

Each member has a **place** in the group, numbered from 1, and each place has its own spot in the group's **formation**:

- Place 1 stands on the group's position.
- Each later place stands on its own spot around the group's position, turned with the group's heading.
- A structure group's formation has a fixed number of places. Members past that number stand on the group's position.

A member can instead stand at a **position of its own**, set by moving it. "Return to formation" puts it back in its default place. A structure stays at its own position for the whole mission. For a HERC or flyer other than the leader, its own position only sets where it starts. Some orders make the members follow the leader in formation: "Patrol" and "Search and destroy" for HERCs, and "Patrol", "Search and destroy" and "Travel" for flyers. Under those orders, a member with its own position leaves it as soon as the mission starts and moves to its place in the formation around the leader.

A structure group can also lay a **base pad**: a concrete pad painted over the ground tile under the group, with the ground under it levelled. Its structures then stand at a fixed spot in that tile.

## Budgets

The bottom-left corner of the viewport shows how much of each of the game's limits the mission uses, as the amount used over the limit. Every version of the mission counts at once: a unit or position that is only present under some condition counts as if it were always there.

- **HERCs, Flyers and Buildings & vehicles** count the units the mission places. The player and each squadmate take two HERC slots each. Structures and ground vehicles share one budget. The editor does not add a unit past its limit unless the mission is [playable in HERCULAN only](#playable-in-herculan-only).
- **Objects** counts every HERC, flyer, structure and ground vehicle the mission places, the squad's included. In retail, past the limit the mission can crash soon after it starts. HERCULAN runs it.
- **Positions** and **Map markers** are explained below.

Every one of these limits is retail's alone: HERCULAN has none of them.

### Positions

A **position** is a point on the map that something in the mission stands on. Positions are used by:

- a group's position, and a member's position of its own;
- each waypoint of a route;
- each trigger area: a circle uses its center, and a box uses two opposite corners.

Two things at the same point can share one position, and a shared position counts once. A position that nothing uses any more, for example after deleting the group that stood on it, still counts until it is removed. Edit > "Remove unused positions" removes them, except those a random table picks. Every position, used or unused, also sets the [mission zone](#the-mission-zone), so when removing unused positions would shrink the mission zone, the editor first asks whether to remove those too.

In retail, a mission with more positions than the limit crashes as it loads. HERCULAN loads it.

### Map markers

The heads-down map shows a marker for each of these:

- every HERC, flyer, structure and ground vehicle in the mission, including the player's squad and units that arrive later;
- each waypoint of the player's route after the first, up to nine;
- the nav marker.

In retail, a mission that needs more markers than the limit crashes as it starts. HERCULAN runs it and shows every marker.

### Playable in HERCULAN only

Edit > Mission Settings > "Playable in" chooses between "Retail and HERCULAN", the default, and "HERCULAN only". A mission playable in HERCULAN only is held to none of the limits above: the editor adds units past them, Problems does not report them, and the budgets show each count beside retail's limit. Retail ignores the setting and still loads the mission, so a mission past a limit can crash it.

Switching back to "Retail and HERCULAN" deletes nothing. Problems lists each limit the mission is over, and the editor adds no more units of a kind that is over its limit.

The setting is saved in the `.MSN`, after the last of the records retail reads. A mission file that ends partway through its win and lose conditions, as retail's `DEMO2` does, cannot carry it.

## The mission zone

The **mission zone** is the rectangle around all of the mission's [positions](#positions), used and unused. View > "Play Area" shows it in the viewport as three rectangles, each further out than the last:

| Color | Where | What it marks |
|---|---|---|
| White | The mission zone's edge | The heads-down map draws this edge as its red border. A player who crosses it is warned "Approaching mission zone boundary". |
| Blue | 360 m outside the mission zone | The edge of the heads-down map. The map shows terrain up to here, and the player cannot pan it any further. |
| Red | 660 m outside the mission zone | A player who crosses it has the mission aborted: "Rules of engagement violated. Mission aborted." |

The warning and the abort apply to the player only.

Placing or moving anything outside the mission zone grows it, and all three rectangles move out with it. Removing the positions that make up one of its edges shrinks it.

## How AI units find enemies

### Awareness

An AI unit can only attack an enemy it is **aware** of. It becomes aware of an enemy in two ways:

- **The enemy is on radar** ([below](#on-radar)). Every AI unit on the opposing side within 6,000 m of it is then aware of it, which on most maps is every unit in the mission.
- **Its side sights the enemy.** A unit sights an enemy within 480 m when the enemy is inside its field of view and the terrain does not block the line of sight. A HERC sees about 79° to either side of the direction its torso faces; a structure sees all the way round. When a unit is hit, it counts as sighting whoever shot it.

A sighting makes every unit on the sighting unit's side within 600 m of the enemy aware of it. The player's own sightings are the exception: they are not passed on to the player's squadmates. Awareness from a sighting ends once the unit and the enemy are more than 600 m apart along the ground, or the terrain blocks the line of sight between them. This is re-checked about every 5 seconds.

### On radar

A unit is **on radar** when an enemy's radar has picked it up. Being on radar is what makes the whole map aware of a unit.

For a human unit and a Cybrid unit that have a clear line of sight to each other:

| Distance between them | Result |
|---|---|
| Up to 840 m | If either has its radar on, both are on radar |
| 840 m to 1,200 m | Whichever has its radar on is on radar |
| Over 1,200 m | Neither puts the other on radar |

If neither has its radar on, neither is on radar. A unit stays on radar until about 5 seconds after this rule last applied to it.

So as soon as a Cybrid with its radar on comes within 840 m of the player and has a clear line of sight, every Cybrid on the map is aware of the player. Most orders still attack only enemies close by ([What each order attacks](#what-each-order-attacks)), but a "Guard" with no "Target" and a "Search and destroy" targeting the player's squad act on this from anywhere on the map.

### When a unit's radar is on

- **The player's HERC** starts each mission with its radar off; the player switches it.
- **The player's squadmates** follow the player's radar commands.
- **Other HERCs** follow their "Radar" setting while they carry out their order. "On" keeps the radar on. "Off outside a fight" keeps it off. Whatever the setting, a HERC's radar is on while it fights, and a HERC under "Travel" or "Follow" also switches it on while it is watching an enemy.
- **Listening posts and radar towers** always have their radar on.
- **Aircraft** have their radar off while they fly their route.

## What each order attacks

Each order decides which enemies a group looks for and how far it reaches. Reach is measured from the HERC doing the looking, except under "Guard". A group's **leader** is its first member.

| Order | Who looks for targets | What it attacks | Reach | Fights back when shot |
|---|---|---|---|---|
| Patrol | The leader | Any enemy | 600 m | Yes |
| Search and destroy | The leader | Only its "Target" | 6,000 m | Yes |
| Guard, with a "Target" | Every member | Enemy HERCs | 540 m from the post | Yes |
| Guard, no "Target" | Every member | Enemy HERCs | 5,880 m from the post | Yes |
| Travel, Follow | Every member | Any enemy, without stopping | 600 m | Yes, then resumes its order |
| Ram | Every member | Any enemy | 600 m | No |
| Sleep | Nobody | Nothing | None | No |

A HERC looks only for enemies it is aware of, and looks again about every 5 seconds.

### Once a fight starts

- When a group's leader starts a fight, every other member joins it, each choosing its own target.
- Under every order except "Sleep" and "Ram", a HERC that is shot fights back, usually against whoever shot it. A guard instead takes the enemy HERC nearest its post ([Guard](#guard)).
- A fighting HERC chooses its target again about every 24 seconds, from the enemies it is aware of within 600 m. Under "Search and destroy", it switches to its "Target" if it is aware of it within 6,000 m. The fight ends when it finds nothing. Under "Travel" and "Follow" it ends at that point anyway ([Travel and Follow](#travel-and-follow)).

### Patrol

The group walks its route. The leader attacks any enemy it is aware of within 600 m; the other members do not look for targets themselves. The order's "Target" does not change what the group attacks or how far it reaches.

### Search and destroy

The leader looks only for the order's "Target". When the "Target" is a group, any member of that group counts. The reach is 6,000 m, so the group goes after its "Target" anywhere on the map once it is aware of it. It walks past every other enemy unless one shoots at it.

### Guard

Each member holds a **post**:

- With a "Target", the post is the "Target"'s position. When the "Target" is a group, the post is the position of its first member.
- With no "Target", the post is the first waypoint of the group's route.

Guards stand between 60 m and 180 m from the post, facing outward, and walk back when they are further away. Each guard looks for the enemy HERC nearest the post, measuring along the ground from the post rather than from itself: within 540 m with a "Target", and within 5,880 m without one. It prefers HERCs that fewer others are already attacking. Guards do not go after structures, vehicles or aircraft by choice; they attack one only when it shoots them and no enemy HERC is within reach. A guard chases its target for about 24 seconds, then returns to guarding and looks again at once.

> **A "Guard" with no "Target" covers the whole map.** Once an enemy HERC is on radar, every such guard leaves its post and goes after it, wherever it is. Give the order a "Target" to keep the guards close to their post.

> **A "Guard" with a "Target" can end at the start of the mission.** The order ends once its "Target" is destroyed, or once no enemy group's current order names the same "Target". That enemy group must also still have a member that is not destroyed, immobilized or left with no working weapon. In most missions no enemy group names it, so the order ends the moment the mission starts and the group moves straight on to its next order. A "Guard" that is the group's last order is not affected: the group keeps guarding.

### Travel and Follow

Under "Travel" every member walks the group's route; under "Follow" every member walks towards the order's "Target" and stops 150 m short of it. When the "Target" is a group, they follow its first member. While moving, each member watches the most suitable enemy it is aware of within 600 m and fires at it without stopping. A member that is shot stops to fight, but it returns to its order the next time it chooses a target, about 24 seconds later.

### Ram

Each member picks the most suitable enemy it is aware of within 600 m, picks again about every 5 seconds, and charges into it. It never fires, and it ignores being shot. The order's "Target" does not change what it rams.

### Sleep

The group stands still, attacks nothing and ignores being shot. Its radar follows its "Radar" setting, so a sleeping HERC with its radar "On" still puts nearby enemies on radar.

### Aircraft

- **Patrol:** the flight's leader looks about every 2.4 seconds for any HERC, vehicle or structure it is aware of within 600 m. Aircraft do not attack other aircraft. The whole flight attacks the leader's target together.
- **Search and destroy:** as for a patrol, but only the order's "Target", within 6,000 m.
- **Travel:** the flight flies its route and attacks nothing.

Being shot does not make an aircraft fight back.

### Gun towers and missile towers

A tower picks an enemy HERC, vehicle or aircraft it is aware of within 360 m, and picks again about every 5 seconds. It fires only at targets within 240 m, in bursts of about 5 seconds separated by pauses of about 5 seconds. Being shot does not make a tower change its target.

## "When engaged, fires" triggers

A unit's "When engaged, fires" trigger fires when any of these happens:

- A unit on its side sights an enemy within 300 m of it. The sighting unit can be any unit on that side except the player.
- It is within 300 m of an enemy, and each of the two is aware of the other through a sighting.
- An enemy within 300 m of it hits any unit on its side.
- An enemy hits it while it is that enemy's selected target, from any distance.

While the player's ECM jams a unit, that unit's trigger cannot fire.
