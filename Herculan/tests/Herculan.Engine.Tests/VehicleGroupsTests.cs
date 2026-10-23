using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Sim;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="VehicleGroups"/> over small synthetic missions whose group 0 is the player's squad: a new convoy is a
/// structure group travelling its one-waypoint route, and a join into a convoy stops at the four members the leader pick
/// reaches, while a base group is no convoy.
/// </summary>
public class VehicleGroupsTests {
	private const short Hercs = 0;
	private const short Structures = 2;
	private const short Squad = 9;

	// A supply transport, a ground vehicle (docs/retail/simulation/ground-vehicles.md).
	private const short Transport = 0x2d;

	/// <summary>
	/// Catches the vehicle written to another roster or its group given another member kind, the order given a subject or
	/// a verb other than travel (the herc and flyer groups' search/destroy on the squad), the group's own route field not
	/// naming the order's route, the convoy painting its ground (which snaps its point into a tile) or the herc and flyer
	/// groups losing theirs, a convoy whose last order can be removed (DBSIM faults on a group with neither a heading nor a
	/// slot-0 order), and the add made as separate edits, whose undo leaves records behind.
	/// </summary>
	[Fact]
	public void NewConvoyTravelsItsOneWaypointRouteAsOneChange() {
		var document = Open(Mission());
		byte[] opened = Write(document);

		var change = VehicleGroups.Create(document, VehicleGroups.Machine(Transport), side: 0, x: 5000, y: 7000, out short vehicle,
			out short group);

		var script = document.Script;
		var made = script.Groups[Exported(document, 16, group)!.Value];
		Assert.Equal(Structures, made.MemberKind);
		Assert.Equal(vehicle, document.GuidOf(9, made.MemberRefs[0]));
		Assert.Equal(Transport, script.Bases[made.MemberRefs[0]].TypeIndex);
		Assert.Equal((-1, -1), (made.PositionRef, made.HeadingRef));
		var order = script.Orders[made.OrderRefs[0]];
		Assert.Equal(VehicleGroups.Travel, order.Verb);
		Assert.Equal(-1, order.SubjectKind);
		Assert.Equal(order.RouteRef, made.RouteRef);
		var route = script.WaypointGroups[order.RouteRef].Waypoints;
		Assert.Single(route);
		var start = script.Coordinates[route[0]];
		Assert.Equal((5000, 7000, 0), (start.X, start.Y, start.Z));
		Assert.True(VehicleGroups.IsConvoy(document, group, BaseObject.IsGroundVehicle));
		Assert.NotNull(GroupOrders.WhyNoRemove(document, group, 0));
		Assert.Empty(ScriptDatLint.Check(script, squadSize: 1));

		document.Undo(change);
		Assert.Equal(opened, Write(document));

		var hercDocument = Open(Mission());
		HercGroups.Create(hercDocument, HercGroups.Machine(9, Unset(10), Unset(10)), side: 1, x: 0, y: 0,
			out _, out short hercs);
		Assert.Equal(0, made.PaintsGround);
		Assert.Equal(1, hercDocument.Script.Groups[Exported(hercDocument, 16, hercs)!.Value].PaintsGround);
	}

	/// <summary>
	/// Catches a convoy join limit past the four members <c>0046a4b8</c>'s leader pick reaches (the flyer limit of three or
	/// the herc limit of seven), a convoy told by its member kind alone, so that a base group led by a building caps a
	/// vehicle at four and gives it no point of its own, and a vehicle joining a herc group or the player's squad.
	/// </summary>
	[Fact]
	public void JoinStopsAtTheConvoysFourthVehicleAndABaseGroupIsNoConvoy() {
		var vehicleRecord = Base(2);
		vehicleRecord[BaseRosterEntry62.TypeIndexWord] = Transport;
		var document = Open(Mission()
			.Add(14, Base(1))
			.Add(14, vehicleRecord)
			.Add(16, Group(20, Structures, position: -1, condition: -1, 1, 2))
			.Add(16, Group(21, Hercs, position: -1, condition: -1, 0)));
		VehicleGroups.Create(document, VehicleGroups.Machine(Transport), side: 0, x: 5000, y: 7000, out _, out short convoy);

		// The leader pick's four members, slots 1 to 3 after the vehicle in slot 0.
		for (int slot = 1; slot <= 3; slot++) {
			VehicleGroups.Join(document, convoy, VehicleGroups.Machine(Transport), BaseObject.IsGroundVehicle, out short joined);
			var record = document.Script.Groups[Exported(document, 16, convoy)!.Value];
			Assert.Equal(joined, document.GuidOf(9, record.MemberRefs[slot]));
			Assert.Equal(-1, document.Script.Bases[record.MemberRefs[slot]].PositionRef);
		}

		Assert.NotNull(VehicleGroups.WhyNoJoin(document, convoy, BaseObject.IsGroundVehicle));
		Assert.False(VehicleGroups.IsConvoy(document, 20, BaseObject.IsGroundVehicle));
		Assert.NotNull(VehicleGroups.WhyNoJoin(document, 20, BaseObject.IsGroundVehicle));
		Assert.NotNull(VehicleGroups.WhyNoJoin(document, 21, BaseObject.IsGroundVehicle));
		Assert.NotNull(VehicleGroups.WhyNoJoin(document, Squad, BaseObject.IsGroundVehicle));
		byte[] before = Write(document);
		Assert.Throws<InvalidOperationException>(() =>
			VehicleGroups.Join(document, convoy, VehicleGroups.Machine(Transport), BaseObject.IsGroundVehicle, out _));
		Assert.Equal(before, Write(document));
	}

	/// <summary>Catches the structure slot cap, which ground vehicles share, checked against another roster's cap, or off by one.</summary>
	[Fact]
	public void AddPastTheStructureSlotsIsRefused() {
		var msn = Mission();
		for (short guid = 0; guid < ScriptDatLint.BaseSlotCap - 1; guid++) {
			msn.Add(14, Base(guid));
		}

		var document = Open(msn);
		Assert.Null(VehicleGroups.WhyNoAdd(document));
		VehicleGroups.Create(document, VehicleGroups.Machine(Transport), side: 0, x: 5000, y: 7000, out _, out _);
		Assert.Equal(ScriptDatLint.BaseSlotCap, document.Script.Bases.Length);
		Assert.NotNull(VehicleGroups.WhyNoAdd(document));
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

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
