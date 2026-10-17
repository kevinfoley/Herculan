using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="HercGroups"/> over small synthetic missions whose group 0 is the player's squad: a new herc group stands on
/// its one-waypoint route and hunts the squad, and a join fills the group's first free member slot.
/// </summary>
public class HercGroupsTests {
	private const short Hercs = 0;
	private const short Squad = 9;

	/// <summary>
	/// Catches a new group whose order names no route, a route that does not start at the drop point, an order hunting
	/// anything but group 0, the order left out of slot 0 (which the mission check reports, and DBSIM faults on), and the
	/// add made as separate edits, whose undo leaves records behind.
	/// </summary>
	[Fact]
	public void NewGroupStandsOnItsRouteHuntingTheSquadAsOneChange() {
		var document = Open(Mission());
		byte[] opened = Write(document);

		var change = HercGroups.Create(document, Machine(), side: 1, x: 5000, y: 7000, squadSize: 1, out short mech, out short group);

		var script = document.Script;
		var made = script.Groups[document.ExportedIndex(16, group)!.Value];
		Assert.Equal(mech, document.GuidOf(7, made.MemberRefs[0]));
		Assert.Equal(1, made.Side);
		Assert.Equal(-1, made.PositionRef);
		var order = script.Orders[made.OrderRefs[0]];
		Assert.Equal(HercGroups.SearchDestroy, order.Verb);
		Assert.Equal(HercGroups.GroupSubject, order.SubjectKind);
		Assert.Equal(0, order.SubjectRef);
		var start = script.Coordinates[script.WaypointGroups[order.RouteRef].Waypoints[0]];
		Assert.Equal((5000, 7000), (start.X, start.Y));
		Assert.Empty(ScriptDatLint.Check(script, squadSize: 1));

		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a join that takes the slot after the last member rather than the first free one, a join into the player's
	/// squad, and one past the places the mech formation table spreads.
	/// </summary>
	[Fact]
	public void JoinTakesTheFirstFreeSlotOfAHercGroupOnly() {
		var document = Open(Mission()
			.Add(12, Mech(1, type: 9))
			.Add(16, Group(10, Hercs, position: -1, condition: -1, 1, -1, 1))
			.Add(16, Group(11, Hercs, position: -1, condition: -1, 1, 1, 1, 1, 1, 1, 1, 1)));

		HercGroups.Join(document, 10, Machine(), squadSize: 1, out short mech);

		var joined = document.Script.Groups[document.ExportedIndex(16, 10)!.Value];
		Assert.Equal(mech, document.GuidOf(7, joined.MemberRefs[1]));
		Assert.NotNull(HercGroups.WhyNoJoin(document, Squad));
		Assert.NotNull(HercGroups.WhyNoJoin(document, 11));
		byte[] before = Write(document);
		Assert.Throws<InvalidOperationException>(() => HercGroups.Join(document, Squad, Machine(), squadSize: 1, out _));
		Assert.Equal(before, Write(document));
	}

	/// <summary>Catches the mech slot cap checked without the squad's machines, which share it, or off by one.</summary>
	[Fact]
	public void AddPastTheMechSlotsIsRefused() {
		var document = Open(Mission());
		int hercs = document.Script.Mechs.Length;

		Assert.Null(HercGroups.WhyNoAdd(document, ScriptDatLint.MechSlotCap - hercs - 1));
		Assert.NotNull(HercGroups.WhyNoAdd(document, ScriptDatLint.MechSlotCap - hercs));
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

	private static MechRosterEntry144 Machine() =>
		HercGroups.Machine(12, Enumerable.Repeat((short)-1, 10).ToArray(), Enumerable.Repeat((short)5, 10).ToArray());

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
