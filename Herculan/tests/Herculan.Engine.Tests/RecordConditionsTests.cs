using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RecordConditions"/> over small synthetic missions: a record's Present when is written into every record of its
/// GUID or refused, an order another slot runs is copied first, and a variant-keyed record's own condition is the one written.
/// </summary>
public class RecordConditionsTests {
	private const short Hercs = 0;
	private const short Structures = 2;
	private const short Squad = 9;

	// Condition 0 tests flag 5 = 1, which fails with every flag 0.
	private const short Fails = 0;

	/// <summary>
	/// Catches the set writing one record of a GUID whose records carry different conditions, which leaves the other creating
	/// a half-filled slot, and catches the squad's group conditioned: both are refused and the file is left as it was.
	/// </summary>
	[Fact]
	public void DifferingRecordsAndTheSquadAreRefused() {
		var document = Open(Mission()
			.Add(14, Base(1))
			.Add(14, Base(1, condition: Fails))
			.Add(16, Group(10, Structures, position: -1, condition: -1, 1)));
		byte[] opened = Write(document);

		var structure = PresenceTarget.Of(14, 1);
		Assert.Null(RecordConditions.Shared(document, structure));
		Assert.NotNull(RecordConditions.WhyNoPresence(document, structure, Fails));
		Assert.Throws<InvalidOperationException>(() => RecordConditions.SetPresence(document, structure, Fails, out _));
		Assert.Throws<InvalidOperationException>(() => RecordConditions.SetPresence(document, PresenceTarget.Of(16, Squad), Fails, out _));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches the set writing only the first (or the supplying) record of a GUID whose records agree: the other, still
	/// unconditioned, would create the slot alone, so the structure would stay in a state where it is meant to be absent.
	/// </summary>
	[Fact]
	public void AgreeingRecordsAreAllWritten() {
		var document = Open(Mission()
			.Add(14, Base(1))
			.Add(14, Base(1))
			.Add(16, Group(10, Structures, position: -1, condition: -1, 1)));
		Assert.NotNull(document.ExportedIndex(14, 1));

		RecordConditions.SetPresence(document, PresenceTarget.Of(14, 1), Fails, out short written);

		Assert.Equal(1, written);
		Assert.All(document.Mission.Bases!.Where(record => record.GUID == 1), record => Assert.Equal(Fails, record.ConditionRef));
		Assert.Null(document.ExportedIndex(14, 1));
	}

	/// <summary>
	/// Catches a herc group's first order conditioned, which leaves the group with no slot-0 order where it is absent; and an
	/// order another group's slot runs too conditioned in place, which takes it from that group as well: the slot is given
	/// its own copy, which carries the condition, and the other group keeps the order unconditioned.
	/// </summary>
	[Fact]
	public void AFirstOrderIsKeptAndASharedOrderIsCopiedBeforeItIsConditioned() {
		var document = Open(Mission()
			.Add(15, GroupOrder(20, Squad))
			.Add(15, GroupOrder(21, Squad))
			.Add(15, GroupOrder(22, Squad))
			.Add(16, Orders(Group(10, Hercs, position: -1, condition: -1, 0), 21, 20))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 0), 22, 20)));

		Assert.NotNull(RecordConditions.WhyNoPresence(document, PresenceTarget.OrderSlot(10, 0), Fails));

		RecordConditions.SetPresence(document, PresenceTarget.OrderSlot(10, 1), Fails, out short written);

		Assert.NotEqual(20, written);
		Assert.Equal(written, Assert.Single(document.Mission.Groups!, record => record.GUID == 10).OrderRefs[1]);
		Assert.All(document.Mission.Orders!.Where(record => record.GUID == written), record => Assert.Equal(Fails, record.ConditionRef));
		Assert.All(document.Mission.Orders!.Where(record => record.GUID == 20), record => Assert.Equal(-1, record.ConditionRef));
		Assert.Equal(20, GroupOrders.Orders(document, 11)[1]);
	}

	/// <summary>
	/// Catches the set written into the variant alternative a herc draws (a GUID <c>-1</c> record, shared by every record
	/// drawing it) rather than the herc's own record: the herc's condition is its own word, outside the copied span.
	/// </summary>
	[Fact]
	public void AVariantKeyedHercsOwnConditionIsWritten() {
		const short key = 7;
		var document = Open(Mission()
			.Add(1, Condition(guid: 1, condition: -1, type: 3, lower: key, upper: 0, operand: 1))
			.Add(1, Condition(guid: 2, condition: 1, type: 2, lower: 0, upper: 0, operand: 0))
			.Add(12, Mech(-1, type: 3, condition: 2))
			.Add(12, Mech(5, type: -1, variant: key))
			.Add(16, Group(10, Hercs, position: -1, condition: -1, 5)));
		Assert.Equal(3, document.Script.Mechs[document.ExportedIndex(12, 5)!.Value].TypeIndex);

		RecordConditions.SetPresence(document, PresenceTarget.Of(12, 5), Fails, out _);

		Assert.Equal(Fails, Assert.Single(document.Mission.Mechs!, record => record.GUID == 5).ConditionRef);
		Assert.Equal(2, Assert.Single(document.Mission.Mechs!, record => record.GUID == -1).ConditionRef);
		Assert.Null(document.ExportedIndex(12, 5));
	}

	// Condition 0, which fails, and group 0, the squad: the player's machine, facing a heading of its own.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(1, Condition(guid: Fails, condition: -1, type: 0, lower: 5, upper: MissionGenerator.OpEqual, operand: 1))
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
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
