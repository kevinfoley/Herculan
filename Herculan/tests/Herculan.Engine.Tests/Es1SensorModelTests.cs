using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>ES1's sensor model and its line of sight through the zone's shapes, on constructed machines and shapes.</summary>
public class Es1SensorModelTests {
	private const int Height = 1000;

	/// <summary>
	/// A human machine knows of a Cybrid in its arc within the scan range and not of one beyond it, and a Cybrid facing
	/// away does not know of it; a Cybrid facing it does, and a turret twisted half a turn takes the machine's own arc
	/// with it. Catches the other's half turn dropped (the Cybrid facing away would see), ES2's 80000 scan range (the far
	/// Cybrid would be known), the two contacts' sharing swapped, or the turret's twist left out of the bearing.
	/// </summary>
	[Fact]
	public void ContactsAreMadeInEachArcWithinTheScanRange() {
		var (globals, human, near, far) = World(nearHeading: 0);
		Es1SensorModel.Tick(globals, human);
		Assert.True(Knows(human, near));
		Assert.False(Knows(human, far));
		Assert.False(Knows(near, human));
		Assert.False(Knows(far, human));

		(globals, human, near, _) = World(nearHeading: -0x8000);
		human.Twist = -0x8000;
		Es1SensorModel.Tick(globals, human);
		Assert.False(Knows(human, near));
		Assert.True(Knows(near, human));
	}

	/// <summary>
	/// A zone shape on the line between two machines facing each other keeps either from knowing of the other, and the ray
	/// between them strikes it where its one cell begins, 10240 from the start in the shape's frame (the ray's start at
	/// x −10000, a step of 512, the first step inside the cell at x 240). Catches the cache's sense inverted, the ray built
	/// pointing away from its end, the march's origin or step wrong, or the distance taken to the wrong point.
	/// </summary>
	[Fact]
	public void AZoneShapeOnTheLineOfSightBlocksIt() {
		var shape = Shape(new Vec3i(10000, 0, 0));
		Assert.True(new Es1ZoneShapes([shape]).RaycastShapes(0, new Vec3i(20000, 0, 100), new Vec3i(0, 0, 100), out var hit));
		Assert.Equal(10240, hit.Distance);
		Assert.Equal(shape, hit.Shape);

		var (globals, human, cybrid) = Pair(new Vec3i(0, 0, 100), new Vec3i(20000, 0, 100), [shape]);
		Es1SensorModel.Tick(globals, human);
		Assert.False(Knows(human, cybrid));
		Assert.False(Knows(cybrid, human));

		(globals, human, cybrid) = Pair(new Vec3i(0, 0, 100), new Vec3i(20000, 0, 100), [Shape(new Vec3i(10000, 5000, 0))]);
		Es1SensorModel.Tick(globals, human);
		Assert.True(Knows(human, cybrid));
		Assert.True(Knows(cybrid, human));
	}

	/// <summary>
	/// The Cybrid's radar alone shows both up within 100000, beyond the scan range, and not past 100000. Catches the
	/// machine's own radar required, only one of the two lit, or ES2's 200000 reach.
	/// </summary>
	[Fact]
	public void EitherRadarShowsBothUpWithinItsRange() {
		var (globals, human, cybrid) = Pair(Vec3i.Zero, new Vec3i(0, 90000, 0), []);
		cybrid.RadarOn = true;
		Es1SensorModel.Tick(globals, human);
		Assert.True(human.RadarVisible);
		Assert.True(cybrid.RadarVisible);

		(globals, human, cybrid) = Pair(Vec3i.Zero, new Vec3i(0, 110000, 0), []);
		cybrid.RadarOn = true;
		Es1SensorModel.Tick(globals, human);
		Assert.False(human.RadarVisible);
		Assert.False(cybrid.RadarVisible);
	}

	/// <summary>
	/// The human machine's decay, which its first tick runs (the Cybrid's is held off, whose line-of-sight cache would
	/// answer blocked unread), keeps a contact within 120000 on the ground plane, though it lies farther in three
	/// dimensions, and drops one past it from both machines, clearing the radar-visible byte. Catches ES2's 100000, the
	/// three-dimensional distance, or only one side's contact dropped.
	/// </summary>
	[Fact]
	public void DecayDropsContactsPastTheGroundRangeFromBoth() {
		var (globals, human, cybrid) = Pair(Vec3i.Zero, new Vec3i(0, 110000, 50000), []);
		SetKnown(human, cybrid);
		cybrid.Sensors.DecayCountdown = 5000;
		Es1SensorModel.Tick(globals, human);
		Assert.True(Knows(human, cybrid));
		Assert.True(Knows(cybrid, human));

		(globals, human, cybrid) = Pair(Vec3i.Zero, new Vec3i(0, 125000, 0), []);
		SetKnown(human, cybrid);
		cybrid.Sensors.DecayCountdown = 5000;
		human.RadarVisible = true;
		Es1SensorModel.Tick(globals, human);
		Assert.False(Knows(human, cybrid));
		Assert.False(Knows(cybrid, human));
		Assert.False(human.RadarVisible);
	}

	/// <summary>A human machine at the origin facing +Y, a Cybrid 20000 ahead with the heading given and one 70000 ahead.</summary>
	private static (Es1SimGlobals Globals, Es1Mech Human, Es1Mech Near, Es1Mech Far) World(short nearHeading) {
		var globals = new Es1SimGlobals();
		var humans = new Es1MissionGroup { Side = 0 };
		var cybrids = new Es1MissionGroup { Side = 1 };
		var human = NewMech(globals, humans, new Vec3i(0, 0, 0), 0);
		var near = NewMech(globals, cybrids, new Vec3i(0, 20000, 0), nearHeading);
		var far = NewMech(globals, cybrids, new Vec3i(0, 70000, 0), 0);
		return (globals, human, near, far);
	}

	/// <summary>A human machine at <paramref name="from"/> facing the Cybrid at <paramref name="at"/>, which faces it, among the shapes given.</summary>
	private static (Es1SimGlobals Globals, Es1Mech Human, Es1Mech Cybrid) Pair(Vec3i from, Vec3i at, Es1ShapeObject[] shapes) {
		var globals = new Es1SimGlobals { ZoneShapes = new Es1ZoneShapes(shapes) };
		short heading = Es1MissionGroup.HeadingToward(from, at);
		var human = NewMech(globals, new Es1MissionGroup { Side = 0 }, from, heading);
		var cybrid = NewMech(globals, new Es1MissionGroup { Side = 1 }, at, unchecked((short)(heading - 0x8000)));
		return (globals, human, cybrid);
	}

	/// <summary>An unrotated shape at <paramref name="at"/>: one cell 512 units a side from its origin, <see cref="Height"/> tall, radius 2000.</summary>
	private static Es1ShapeObject Shape(Vec3i at) {
		var frame = Transform3.Identity;
		frame.X = at.X;
		frame.Y = at.Y;
		frame.Z = at.Z;
		var heights = new int[256];
		heights[1] = Height;
		heights[255] = Height;
		return new Es1ShapeObject(frame, 2000, new Es1GridShape(new GridShapeCollision(1, 1, 0, 0, 9, heights, [[1]])));
	}

	private static bool Knows(Es1Mech holder, Es1Mech of) => Es1SensorTables.Get(holder.Sensors.Contacts, of.ListSlot);

	private static void SetKnown(Es1Mech a, Es1Mech b) {
		Es1SensorTables.Set(a.Sensors.Contacts, b.ListSlot, true);
		Es1SensorTables.Set(b.Sensors.Contacts, a.ListSlot, true);
	}

	private static Es1Mech NewMech(Es1SimGlobals globals, Es1MissionGroup group, Vec3i position, short heading) {
		var chassis = new Es1HercSimDat {
			AnimId_StopMove = 0,
			AnimId_StopReverse = 1,
			AnimId_TorsoTwist = -1,
			AnimId_TorsoPitch = -1,
		};
		var sequences = Enumerable.Range(0, 2).Select(_ => new ANSequence {
			Frames = [new ANSequenceFrame { Duration = 100 }],
			PartIds = [0],
			TransformIndices = [0],
		}).ToArray();
		var animation = ShapeAnimation.From(new ANAnimList {
			Sequences = sequences,
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		})!;

		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter) {
			Position = position,
			Rotation = (0, 0, heading),
			Group = group,
		};
		group.Members.Add(mech);
		globals.AddLiveObject(mech);
		return mech;
	}
}
