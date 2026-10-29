using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="FlyerGroups"/> over small synthetic missions whose group 0 is the player's squad: a new flyer group stands on
/// its one-waypoint route at Z 0 and hunts the squad, and a join stops at the flyer formation table's places.
/// </summary>
public class FlyerGroupsTests {
	private const short Hercs = 0;
	private const short Squad = 9;

	/// <summary>
	/// Catches the flyer written to the herc roster or its group given the herc member kind, a waypoint given a height of
	/// its own rather than Z 0 (which DBSIM reads as the flyer's starting altitude), an order hunting anything but group 0,
	/// and the add made as separate edits, whose undo leaves records behind.
	/// </summary>
	[Fact]
	public void NewGroupIsAFlyerGroupOnAZeroHeightRouteAsOneChange() {
		var document = Open(Mission());
		byte[] opened = Write(document);
		int hercs = document.Script.Mechs.Length;

		var change = FlyerGroups.Create(document, FlyerGroups.Machine(0), side: 1, x: 5000, y: 7000, out short flyer, out short group);

		var script = document.Script;
		Assert.Equal(hercs, script.Mechs.Length);
		var made = script.Groups[Exported(document, 16, group)!.Value];
		Assert.Equal(1, made.MemberKind);
		Assert.Equal(flyer, GuidAt(document, 8, made.MemberRefs[0]));
		Assert.Equal(-1, made.PositionRef);
		var order = script.Orders[made.OrderRefs[0]];
		Assert.Equal(HercGroups.SearchDestroy, order.Verb);
		Assert.Equal(HercGroups.GroupSubject, order.SubjectKind);
		Assert.Equal(0, order.SubjectRef);
		var start = script.Coordinates[script.WaypointGroups[order.RouteRef].Waypoints[0]];
		Assert.Equal((5000, 7000, 0), (start.X, start.Y, start.Z));
		Assert.Empty(ScriptDatLint.Check(script, squadSize: 1));

		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a join limit past the three places <c>FFORMS.DAT</c> spreads beside the leader (the herc limit of seven),
	/// and a flyer joining a herc group or the player's squad.
	/// </summary>
	[Fact]
	public void JoinStopsAtTheFlyerFormationsPlaces() {
		var document = Open(Mission().Add(16, Group(10, Hercs, position: -1, condition: -1, 1)).Add(12, Mech(1, type: 9)));
		FlyerGroups.Create(document, FlyerGroups.Machine(0), side: 1, x: 5000, y: 7000, out _, out short group);

		// FFORMS.DAT's three places beside the leader, member slots 1 to 3.
		for (int slot = 1; slot <= 3; slot++) {
			FlyerGroups.Join(document, group, FlyerGroups.Machine(0), out short joined);
			var record = document.Script.Groups[Exported(document, 16, group)!.Value];
			Assert.Equal(joined, GuidAt(document, 8, record.MemberRefs[slot]));
		}

		Assert.NotNull(FlyerGroups.WhyNoJoin(document, group));
		Assert.NotNull(FlyerGroups.WhyNoJoin(document, 10));
		Assert.NotNull(FlyerGroups.WhyNoJoin(document, Squad));
		byte[] before = Write(document);
		Assert.Throws<InvalidOperationException>(() => FlyerGroups.Join(document, 10, FlyerGroups.Machine(0), out _));
		Assert.Equal(before, Write(document));
	}

	/// <summary>Catches the flyer slot cap checked against another roster's cap, or off by one.</summary>
	[Fact]
	public void AddPastTheFlyerSlotsIsRefused() {
		var msn = Mission();
		for (short guid = 0; guid < ScriptDatLint.FlyerSlotCap - 1; guid++) {
			msn.Add(13, Flyer(guid));
		}

		var document = Open(msn);
		Assert.Null(FlyerGroups.WhyNoAdd(document));
		FlyerGroups.Create(document, FlyerGroups.Machine(0), side: 1, x: 5000, y: 7000, out _, out _);
		Assert.Equal(ScriptDatLint.FlyerSlotCap, document.Script.Flyers.Length);
		Assert.NotNull(FlyerGroups.WhyNoAdd(document));
	}

	// Group 0, the squad: the player's machine, facing a heading of its own.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
