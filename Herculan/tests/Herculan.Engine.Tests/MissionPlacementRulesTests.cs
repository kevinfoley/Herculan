using Herculan.Engine.Numerics;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionPlacementRules"/> over constructed groups, keyed by <c>.MSN</c>-style GUIDs rather than by roster slot,
/// on the rules no retail-data test pins down: which group claims a record and at which member index, which member a pad is
/// read from, and which point a group stands on.
/// </summary>
public sealed class MissionPlacementRulesTests {
	/// <summary>
	/// The first group in file order to name a record claims it, at its raw slot position, gaps counted; the squad's group
	/// and a group of another kind, or of none, claim nothing in the roster.
	/// </summary>
	[Fact]
	public void TheFirstGroupToNameARecordClaimsItAtItsSlot() {
		var groups = new (MissionUnitKind Kind, IReadOnlyList<short?> Members)[] {
			(MissionUnitKind.Mech, new short?[] { 101, 102 }),
			(MissionUnitKind.Mech, new short?[] { null, 201, null, 101 }),
			(MissionUnitKind.Flyer, new short?[] { 201 }),
			(MissionUnitKind.Mech, new short?[] { 201, 301 }),
			((MissionUnitKind)(-1), new short?[] { 401 }),
		};

		var hercs = MissionPlacementRules.ClaimMembers(MissionUnitKind.Mech, groups);
		Assert.Equal(new Dictionary<short, MissionMemberClaim> {
			[201] = new(1, 1),
			[101] = new(1, 3),
			[301] = new(3, 1),
		}, hercs);

		var flyers = MissionPlacementRules.ClaimMembers(MissionUnitKind.Flyer, groups);
		Assert.Equal(new Dictionary<short, MissionMemberClaim> { [201] = new(2, 0) }, flyers);
	}

	/// <summary>A pad is read from the record its group claims at the lowest member index, not the first it claims in roster order.</summary>
	[Fact]
	public void APadIsReadFromItsGroupsLowestMember() {
		var claims = new Dictionary<short, MissionMemberClaim> {
			[5] = new(2, 3),
			[7] = new(2, 1),
			[9] = new(1, 0),
		};

		Assert.Equal((short)7, MissionPlacementRules.PadLead(claims, group: 2));
		Assert.Null(MissionPlacementRules.PadLead(claims, group: 3));
	}

	/// <summary>
	/// A group stands on its own point over its route's start, on the route's start over the origin, and only a structure
	/// group that paints its ground snaps; the authored point is the one before the snap.
	/// </summary>
	[Fact]
	public void AGroupStandsOnItsOwnPointElseItsRouteStart() {
		var formations = BaseFormationTable.Parse(OnePadFormation());
		var layout = formations.LayoutFor(0)!;
		var own = new Vec3i(100000, 100000, 7);
		Vec3i[] route = [new(300000, 300000, 0), new(310000, 300000, 0)];

		var pad = MissionPlacementRules.GroupSetup(MissionUnitKind.Base, own, null, route, 0, true, formations);
		var (snappedX, snappedY) = layout.SnapAnchor(own.X, own.Y);
		Assert.Equal(new Vec3i(snappedX, snappedY, own.Z), pad.Point);
		Assert.Equal(own, pad.AuthoredPoint);
		Assert.Equal(MissionGroupPointSource.OwnPoint, pad.PointSource);

		Assert.Equal(own, MissionPlacementRules.GroupSetup(MissionUnitKind.Base, own, null, route, 0, false, formations).Point);
		Assert.Equal(own, MissionPlacementRules.GroupSetup(MissionUnitKind.Mech, own, null, route, 0, true, formations).Point);

		var walking = MissionPlacementRules.GroupSetup(MissionUnitKind.Mech, null, null, route, 0, false, formations);
		Assert.Equal(route[0], walking.Point);
		Assert.Equal(MissionGroupPointSource.RouteStart, walking.PointSource);
		Assert.Equal(MissionPlacementRules.RouteBearing(route), walking.Heading);
		Assert.True(walking.HeadingFromRoute);

		var nowhere = MissionPlacementRules.GroupSetup(MissionUnitKind.Mech, null, 1820, [], 0, false, formations);
		Assert.Equal(Vec3i.Zero, nowhere.Point);
		Assert.Equal(MissionGroupPointSource.None, nowhere.PointSource);
		Assert.Equal(1820, nowhere.Heading);
		Assert.False(nowhere.HeadingFromRoute);
	}

	/// <summary>A <c>BFORMS.DAT</c> of one formation: one follower slot, and an 8-by-8 pad anchored a quarter of the way into its tile.</summary>
	private static byte[] OnePadFormation() {
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write(1);
		writer.Write(1);
		writer.Write(1000);
		writer.Write(0);
		writer.Write((short)0);
		writer.Write(0);
		writer.Write(64);
		writer.Write(64);
		const int dimension = 8;
		writer.Write(dimension);
		for (int row = 0; row < dimension; row++) {
			writer.Write((byte)dimension);
			writer.Write(new byte[dimension]);
		}

		writer.Flush();
		return stream.ToArray();
	}
}
