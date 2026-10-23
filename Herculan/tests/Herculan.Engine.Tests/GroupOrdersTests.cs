using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Sim;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="GroupOrders"/> over small synthetic missions: the slots stay filled from slot 0, an order another slot names
/// is copied before it is edited, and a group keeps the order DBSIM needs.
/// </summary>
public class GroupOrdersTests {
	private const short Hercs = 0;
	private const short Structures = 2;
	private const short Squad = 9;

	/// <summary>
	/// Catches a remove that empties the slot instead of moving the later orders up, which leaves them unreachable behind
	/// the gap; a remove that deletes an order another group still names; an add into a slot other than the one after the
	/// last; and a move that does not trade the two slots.
	/// </summary>
	[Fact]
	public void SlotsStayFilledFromTheFirst() {
		var document = Open(Mission()
			.Add(6, Point(1, 100))
			.Add(15, Order(20, verb: 0))
			.Add(15, Order(21, verb: 3))
			.Add(15, Order(22, verb: 5))
			.Add(16, Orders(Group(10, Hercs, position: 1, condition: -1, 0), 20, 21, 22))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 0), 21)));

		GroupOrders.Remove(document, 10, 1);

		Assert.Equal([20, 22, -1], GroupOrders.Orders(document, 10)[..3]);
		Assert.Contains(document.Mission.Orders!, record => record.GUID == 21);

		GroupOrders.Remove(document, 10, 0);

		Assert.Equal([22, -1], GroupOrders.Orders(document, 10)[..2]);
		Assert.DoesNotContain(document.Mission.Orders!, record => record.GUID == 20);

		GroupOrders.Add(document, 10, HercGroups.Order(4, -1, -1, -1), out short added);
		GroupOrders.Move(document, 10, 1, 0);

		Assert.Equal([added, 22, -1], GroupOrders.Orders(document, 10)[..3]);
	}

	/// <summary>Catches an edit written into an order another group's slot names too, which changes that group's order.</summary>
	[Fact]
	public void EditOfASharedOrderGivesTheSlotItsOwnCopy() {
		var document = Open(Mission()
			.Add(15, Order(20, verb: 0))
			.Add(16, Orders(Group(10, Hercs, position: -1, condition: -1, 0), 20))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 0), 20)));

		GroupOrders.SetVerb(document, 10, 0, 5);

		short own = GroupOrders.Orders(document, 10)[0];
		Assert.NotEqual(20, own);
		Assert.Equal(5, document.Script.Orders[Exported(document, 15, own)!.Value].Verb);
		Assert.Equal(20, GroupOrders.Orders(document, 11)[0]);
		Assert.Equal(0, document.Script.Orders[Exported(document, 15, 20)!.Value].Verb);
		Assert.False(GroupOrders.Shared(document, own));
	}

	/// <summary>
	/// Catches the last order of a herc group removed, which the mission check reports and DBSIM faults on, while a
	/// structure group with a heading of its own can lose its last.
	/// </summary>
	[Fact]
	public void AHercGroupKeepsItsLastOrder() {
		var structures = Orders(Group(11, Structures, position: -1, condition: -1, 0), 21);
		structures[MissionGroup164.HeadingRefWord] = 0;
		var document = Open(Mission()
			.Add(14, Base(0))
			.Add(15, Order(20, verb: 0))
			.Add(15, Order(21, verb: 5))
			.Add(16, Orders(Group(10, Hercs, position: -1, condition: -1, 0), 20))
			.Add(16, structures));
		byte[] opened = Write(document);

		Assert.NotNull(GroupOrders.WhyNoRemove(document, 10, 0));
		Assert.Throws<InvalidOperationException>(() => GroupOrders.Remove(document, 10, 0));
		Assert.Equal(opened, Write(document));

		GroupOrders.Remove(document, 11, 0);
		Assert.Equal(-1, GroupOrders.Orders(document, 11)[0]);
	}

	/// <summary>
	/// Catches a first order left with no route in a group with no point of its own, which stands on that route, and a
	/// search/destroy order left with no subject, whose completion test reads the subject: a verb change that keeps no
	/// subject, and a subject of None accepted for that verb. A group with a point of its own may run no route.
	/// </summary>
	[Fact]
	public void ShapesNoRetailOrderHasAreNotMade() {
		var walking = Order(20, verb: 3);
		walking[MissionOrder22.RouteRefWord] = 5;
		var document = Open(Mission()
			.Add(6, Point(1, 100))
			.Add(8, Route(5, 1))
			.Add(15, walking)
			.Add(15, Order(21, verb: 3))
			.Add(16, Orders(Group(10, Hercs, position: -1, condition: -1, 0), 20))
			.Add(16, Orders(Group(11, Hercs, position: 1, condition: -1, 0), 21)));

		Assert.NotNull(GroupOrders.WhyNoRoute(document, 10, 0, -1));
		Assert.Throws<InvalidOperationException>(() => GroupOrders.SetRoute(document, 10, 0, -1));
		Assert.Null(GroupOrders.WhyNoRoute(document, 11, 0, -1));

		GroupOrders.SetVerb(document, 10, 0, HercGroups.SearchDestroy);

		var order = document.Script.Orders[Exported(document, 15, 20)!.Value];
		Assert.Equal(HercGroups.GroupSubject, order.SubjectKind);
		Assert.Equal(0, order.SubjectRef);
		Assert.Throws<InvalidOperationException>(() => GroupOrders.SetSubject(document, 10, 0, -1, -1));
	}

	/// <summary>
	/// Catches a convoy's new order made search/destroy on the squad, as any other group's, or travel given a subject, and
	/// a structure group led by a building given travel: the convoy test read from the group's member kind alone.
	/// </summary>
	[Fact]
	public void AConvoysNewOrderTravelsItsRoute() {
		const short transport = 0x2d;
		var vehicle = Base(1);
		vehicle[BaseRosterEntry62.TypeIndexWord] = transport;
		var walking = Order(20, verb: 5);
		walking[MissionOrder22.RouteRefWord] = 5;
		var document = Open(Mission()
			.Add(6, Point(1, 100))
			.Add(8, Route(5, 1))
			.Add(14, Base(0))
			.Add(14, vehicle)
			.Add(15, walking)
			.Add(16, Orders(Group(10, Structures, position: -1, condition: -1, 1, 0), 20))
			.Add(16, Orders(Group(11, Structures, position: -1, condition: -1, 0, 1), 20)));

		var convoy = GroupOrders.NewOrder(document, 10, BaseObject.IsGroundVehicle);
		Assert.Equal((VehicleGroups.Travel, (short)-1, (short)-1), (convoy.Verb, convoy.SubjectKind, convoy.SubjectRef));
		Assert.Equal(5, convoy.RouteRef);

		var bases = GroupOrders.NewOrder(document, 11, BaseObject.IsGroundVehicle);
		Assert.Equal((HercGroups.SearchDestroy, HercGroups.GroupSubject), (bases.Verb, bases.SubjectKind));
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

	// A row-15 order of <verb> naming no route, subject or action.
	private static short[] Order(short guid, short verb) {
		var words = Enumerable.Repeat((short)-1, 11).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionOrder22.VerbWord] = verb;
		words[MissionOrder22.VerbWord + 1] = 0; // the formation
		return words;
	}

	// <group> with the row-15 GUIDs <orders> in its first order slots.
	private static short[] Orders(short[] group, params short[] orders) {
		orders.CopyTo(group, MissionGroup164.OrderRefsWord);
		return group;
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
