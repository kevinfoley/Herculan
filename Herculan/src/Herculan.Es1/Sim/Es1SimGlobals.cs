using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The simulator's globals a machine's tick reads and writes: the clock, the generator, the dwell jitter, the turret's
/// servo latches, the live object list, the structures and the zone's shapes. Retail keeps each at a fixed address; the world owns one of these.
/// </summary>
public sealed class Es1SimGlobals {
	/// <summary>The tick length and the helpers that scale by it (<see cref="Es1SimClock"/>).</summary>
	public Es1SimClock Clock { get; } = new();

	/// <summary>
	/// The simulation's generator at <c>000b5bd4</c>, which <c>Main_StaticInit</c> (<c>00057414</c>) seeds with
	/// <c>Math_RandomSeed</c> (<c>00063aa4</c>) from ES2's table and cursors, so it starts in the state ES2's port starts in
	/// (docs/es1/simulation/fixed-point-math.md#helpers-that-match, docs/es1/simulation/main-loop.md#functions).
	/// </summary>
	public SimRandom Random { get; } = new();

	/// <summary><c>Behaviour_DwellJitter</c> (<c>000bc426</c>).</summary>
	public Es1DwellJitter DwellJitter { get; } = new();

	/// <summary>The servo-sound helpers' latches and last angles (<see cref="Es1TurretServos"/>).</summary>
	public Es1TurretServos TurretServos { get; } = new();

	/// <summary><c>LiveObjectList</c>, the objects <c>ObjectList_Add</c> has added, in the order added.</summary>
	public List<IEs1SimObject> LiveObjects { get; } = new();

	/// <summary>
	/// <c>g_BasePool</c> (<c>000aed70</c>)'s structures in build order. PLACEHOLDER: empty: blocks 7 and 8 are not spawned
	/// (the base constructor <c>00003cbc</c> is unread, docs/es1/formats/script-dat.md#open); the plan's flyers-and-bases
	/// step fills it.
	/// </summary>
	public List<Es1Structure> Structures { get; } = new();

	/// <summary><c>Zone_ShapeCount</c> and <c>Zone_ShapeObjects</c>, the zone's placed shapes; empty until the zone is loaded.</summary>
	public Es1ZoneShapes ZoneShapes { get; set; } = Es1ZoneShapes.Empty;
}

/// <summary>
/// <c>Behaviour_DwellJitter</c> (<c>000bc426</c>), the <c>int16</c> <c>Behaviour_SetState</c> steps by 13 a state change
/// (docs/es1/simulation/shields-and-behaviour-states.md#the-behaviour-block). It lies past the image's file bytes
/// (<c>0xadcd8</c> of <c>0xbdd40</c>), so it starts at 0, as ES2's does.
/// </summary>
public sealed class Es1DwellJitter {
	public short Value { get; private set; }

	/// <summary>The step <c>Behaviour_SetState</c> takes: 13 added, then the low four bits.</summary>
	public short Next() {
		Value += 0xd;
		return (short)(Value & 0xf);
	}
}

/// <summary>
/// What <c>Mech_CollisionTest</c> (<c>00014174</c>) reads of another live object: its position, its group, its target
/// class and the two vtable slots it calls (docs/es1/simulation/mech-locomotion.md#collision--mech_collisiontest-00014174).
/// </summary>
public interface IEs1SimObject {
	/// <summary><c>+0x20</c>, the position.</summary>
	Vec3i Position { get; }

	/// <summary><c>+0x3f</c>, the group.</summary>
	Es1MissionGroup? Group { get; }

	/// <summary><c>+0x162</c>, the target class, 0 for a HERC.</summary>
	short TargetClass { get; }

	/// <summary>Slot <c>+0x60</c>, the body radius; an object whose radius is 0 blocks nothing.</summary>
	short BodyRadius { get; }

	/// <summary>Slot <c>+0x6c</c>: something ran into it.</summary>
	void RunInto();
}
