using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>The <c>.MSN</c> records that reference a row-10 action, by kind, whatever their condition.</summary>
/// <param name="Timers">Row-11 timers it starts or that fire it (<see cref="ActionTimer30.PrimaryActionRef"/>, <see cref="ActionTimer30.SequenceRefs"/>).</param>
/// <param name="Groups">Row-16 groups waiting on it (<see cref="MissionGroup164.DeploymentActionRef"/>).</param>
/// <param name="Objects">Row-12, 13 and 14 records that fire it when engaged or defeated, variant alternatives included.</param>
/// <param name="Orders">Row-15 orders it ends (<see cref="MissionOrder22.ActionRef"/>), variant alternatives included.</param>
public sealed record ActionNamers(IReadOnlyList<MsnRecordId> Timers, IReadOnlyList<MsnRecordId> Groups,
		IReadOnlyList<MsnRecordId> Objects, IReadOnlyList<MsnRecordId> Orders) {
	/// <summary>Whether any record references it.</summary>
	public bool Any => Timers.Count > 0 || Groups.Count > 0 || Objects.Count > 0 || Orders.Count > 0;
}

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s mission actions, row 10, as record edits. The record's
/// layout is docs/retail/formats/msn-mission-file.md#row-10-field-decode--the-action-record-dat_00470660-82-bytesrecord,
/// and what each field does docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
///
/// <list type="bullet">
/// <item>A repeated row-10 GUID overlays field by field (<c>Msn_MergeRow10</c> (<c>00416521</c>)), so a field is written
/// through the record that supplies it (<see cref="MissionDocument.SetField"/>), and an unset value into every record of
/// the GUID. The fields an edit starts from are the base's (<see cref="MissionDocument.Layout"/>), whose records it writes.
/// Row 10 draws no variant.</item>
/// <item>The area slots are the first four, the only ones retail fills; slots 4 to 7 stay <c>-1</c>. They are kept filled
/// from slot 0: the load counts an action's areas up to the first <c>-1</c>, so an area after a gap is never tested
/// (docs/retail/formats/script-dat.md#block-5-in-memory--58-bytes-0x3a). A remove moves every later area up a slot.</item>
/// <item>The counter writes are kept filled from the first pair, and each record's pair count <c>0x1A</c>, which the
/// generation neither exports nor merges, equal to how many of its own pairs are filled. The operations offered are the two
/// <c>Action_Activate</c> (<c>00423430</c>) has a case for, 5 (clear) and 6 (+1).</item>
/// <item>The message is stored plus one, 0 for none; DBSIM subtracts the one at load.</item>
/// <item>Types 7 to 10 read the target <c>0x50</c> as a herc, a flyer, a structure or a group; any other type keeps it as
/// authored, and DBSIM zeroes it. A type change to 7 to 10 gives the action as its target the first entry of that kind's
/// row in the layout (12 hercs, 13 flyers, 14 structures, 16 groups), in the order of each GUID's first record, whatever its
/// condition, and one to 0 to 6 unsets it, as 334 of the 338 retail records leave it (<c>HercWorks.Query values
/// --row 10 --field TargetRef</c>); both the editor's choice.</item>
/// <item>An action is deleted only while nothing references it (<see cref="Namers"/>): a ref to a GUID with no record
/// resolves to <c>-1</c>, so a timer, a waiting group, an object or an order would lose it.</item>
/// </list>
/// </summary>
public static class ActionEditing {
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int OrderRow = 15;
	private const int GroupRow = 16;

	private const string NoArea = "That trigger area is not in the mission.";

	/// <summary>The area slots offered: the first four, the only ones retail fills.</summary>
	public const int AreaSlots = 4;

	/// <summary>The counter pairs a record carries.</summary>
	public const int Pairs = 10;

	/// <summary>The types <c>Actions_EvaluateTriggers</c> (<c>00426b70</c>) has a case for: 0 to 10.</summary>
	public const short LastType = 10;

	/// <summary>The first type whose areas test the action's own target.</summary>
	public const short FirstTargetType = 7;

	/// <summary>The verbs offered: 0 to 5, every row of <c>Group_DeploymentCheck</c> (<c>004236c4</c>)'s arrival table.</summary>
	public const short LastVerb = 5;

	/// <summary><c>Action_Activate</c>'s two counter operations.</summary>
	public const short CounterClear = 5;
	public const short CounterIncrement = 6;

	/// <summary>How many mission counters there are, the 1,000 of docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4.</summary>
	public const int Counters = MissionGenerator.CampaignFlagCount;

	/// <summary>
	/// A new action's type and verb: type 1 (any machine of the player's group), 170 of the 338 retail records, and verb 1
	/// (in place), 172 of them (<c>HercWorks.Query values --row 10 --field Type</c>, <c>--field Verb</c>).
	/// </summary>
	public const short NewType = 1;
	public const short NewVerb = 1;

	/// <summary>
	/// A new pair's counter and operation: counter 0 incremented (6), what all 66 retail records that write a counter carry in
	/// their first pair (docs/retail/formats/msn-mission-file.md#row-10-field-decode--the-action-record-dat_00470660-82-bytesrecord).
	/// </summary>
	public const short NewPairCounter = 0;
	public const short NewPairOp = CounterIncrement;

	/// <summary>
	/// Appends a new action, answering its GUID in <paramref name="guid"/>, testing row-9 area <paramref name="area"/> in slot
	/// 0, or none for <c>-1</c>. The record takes each field's most common retail value (<c>HercWorks.Query values --row 10</c>):
	/// unconditioned and its dead word <c>0x04</c> <c>-1</c> (all 338), <see cref="NewType"/> and <see cref="NewVerb"/>, every
	/// other area slot <c>-1</c>, no counter write and a pair count of 0 (272), no text ref (337), no message (272) and no
	/// target (334).
	/// </summary>
	public static MsnEdit Create(MissionDocument document, short area, out short guid) {
		if (area != -1 && document.Layout.Entry(AreaRow, area) == null) {
			throw new InvalidOperationException(NoArea);
		}

		var areas = Unset(8);
		areas[0] = area;
		short created = -1;
		var change = document.Change(() => document.Append(ActionRow, new MissionAction82 {
			ConditionRef = -1, Unk04 = -1, Type = NewType, Verb = NewVerb, AreaRefs = areas, Unk1A = 0, CounterPairs = Unset(20),
			TextRefs = Unset(5), MessageId = 0, TargetRef = -1,
		}, out created));
		guid = created;
		return change;
	}

	/// <summary>Row-10 GUID <paramref name="action"/>'s base fields (a copy), throwing for one no record carries.</summary>
	public static MissionAction82 Fields(MissionDocument document, short action) =>
		document.Layout.Entry<MissionAction82>(action)?.Fields
			?? throw new InvalidOperationException("That trigger is not in the mission.");

	/// <summary>
	/// The row-9 GUIDs in <paramref name="action"/>'s first <see cref="AreaSlots"/> slots as its base names them, <c>-1</c> for
	/// an empty one or one naming no area.
	/// </summary>
	public static short[] Areas(MissionDocument document, short action) =>
		Fields(document, action).AreaRefs.Take(AreaSlots)
			.Select(area => document.Layout.Entry(AreaRow, area) != null ? area : (short)-1).ToArray();

	/// <summary>How many area slots are filled before the first empty one, the areas DBSIM tests.</summary>
	public static int FilledAreas(short[] areas) => Array.IndexOf(areas, (short)-1) is >= 0 and var gap ? gap : areas.Length;

	/// <summary>Why row-9 area <paramref name="area"/> cannot be added to <paramref name="action"/>, or null when it can.</summary>
	public static string? WhyNoAddArea(MissionDocument document, short action, short area) {
		var areas = Areas(document, action);
		return FilledAreas(areas) >= AreaSlots ? $"It already tests {AreaSlots} areas, the most it can hold."
			: area != -1 && areas.Contains(area) ? "It tests that area already."
			: area != -1 && document.Layout.Entry(AreaRow, area) == null ? NoArea
			: null;
	}

	/// <summary>Adds row-9 area <paramref name="area"/> to <paramref name="action"/> in the slot after its last filled one.</summary>
	public static MsnEdit AddArea(MissionDocument document, short action, short area) {
		if ((area == -1 ? "No area was given." : WhyNoAddArea(document, action, area)) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		int slot = FilledAreas(Areas(document, action));
		return document.Change(() => document.SetField(MsnField.ActionArea(slot), action, area));
	}

	/// <summary>Puts row-9 area <paramref name="area"/> in <paramref name="action"/>'s filled slot <paramref name="slot"/>.</summary>
	public static MsnEdit SetArea(MissionDocument document, short action, int slot, short area) {
		var areas = Areas(document, action);
		if (slot < 0 || slot >= FilledAreas(areas)) {
			throw new InvalidOperationException($"There is no area in place {slot + 1}.");
		}

		if (document.Layout.Entry(AreaRow, area) == null) {
			throw new InvalidOperationException(NoArea);
		}

		if (areas.Where((_, at) => at != slot).Contains(area)) {
			throw new InvalidOperationException("It tests that area already.");
		}

		return document.Change(() => document.SetField(MsnField.ActionArea(slot), action, area));
	}

	/// <summary>Removes the area in <paramref name="action"/>'s slot <paramref name="slot"/>, moving every later area up a slot.</summary>
	public static MsnEdit RemoveArea(MissionDocument document, short action, int slot) {
		var areas = Areas(document, action);
		int last = FilledAreas(areas) - 1;
		if (slot < 0 || slot > last) {
			throw new InvalidOperationException($"There is no area in place {slot + 1}.");
		}

		return document.Change(() => {
			for (int at = slot; at < last; at++) {
				document.SetField(MsnField.ActionArea(at), action, areas[at + 1]);
			}

			document.SetField(MsnField.ActionArea(last), action, -1);
		});
	}

	/// <summary>
	/// The script.dat block a target of type <paramref name="type"/> indexes: 7 hercs, 8 flyers, 9 structures, 11 groups for
	/// type 10; null for a type whose target is not read.
	/// </summary>
	public static int? TargetBlock(short type) => type switch { 7 => 7, 8 => 8, 9 => 9, 10 => 11, _ => null };

	/// <summary>Why <paramref name="action"/> cannot be made type <paramref name="type"/>, or null when it can.</summary>
	public static string? WhyNoType(MissionDocument document, short action, short type) {
		if (type is < 0 or > LastType) {
			return $"A type is 0 to {LastType}.";
		}

		return TargetBlock(type) is { } block && Fields(document, action).Type != type && FirstTarget(document, block) == -1
			? $"The mission has no {TargetKind(type)} to be its target."
			: null;
	}

	// The GUID of the first layout entry of the row script.dat block <block> is exported from, or -1 when the row has none.
	private static short FirstTarget(MissionDocument document, int block) =>
		document.Layout.Entries(block + 5) is { Count: > 0 } entries ? entries[0].Guid : (short)-1;

	/// <summary>What a target of type <paramref name="type"/> is, in a word.</summary>
	public static string TargetKind(short type) => type switch { 7 => "herc", 8 => "flyer", 9 => "structure", _ => "group" };

	/// <summary>
	/// Sets <paramref name="action"/>'s type, 0 to <see cref="LastType"/>. A change to 7 to 10 gives it the first layout entry of
	/// that kind as its target, and a change to 0 to 6 unsets the target (the class summary).
	/// </summary>
	public static MsnEdit SetType(MissionDocument document, short action, short type) {
		if (WhyNoType(document, action, type) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short current = Fields(document, action).Type;
		if (current == type) {
			throw new InvalidOperationException("It is that type already.");
		}

		short target = TargetBlock(type) is { } block ? FirstTarget(document, block) : (short)-1;
		return document.Change(() => {
			document.SetField(MsnField.ActionType, action, type);
			document.SetField(MsnField.ActionTarget, action, target);
		});
	}

	/// <summary>
	/// The GUID of <paramref name="action"/>'s base's target, in the row its type names; <c>-1</c> for none, one naming no
	/// record, or a type that reads none.
	/// </summary>
	public static short Target(MissionDocument document, short action) {
		var fields = Fields(document, action);
		return TargetBlock(fields.Type) is { } block && document.Layout.Entry(block + 5, fields.TargetRef) != null
			? fields.TargetRef
			: (short)-1;
	}

	/// <summary>
	/// Sets <paramref name="action"/>'s target to <paramref name="target"/>, a GUID of the row its type, 7 to 10, names. Throws for
	/// any other type, and for a GUID no record of that row carries.
	/// </summary>
	public static MsnEdit SetTarget(MissionDocument document, short action, short target) {
		if (TargetBlock(Fields(document, action).Type) is not { } block) {
			throw new InvalidOperationException("Only a trigger tested on a particular herc, flyer, structure or group has a target.");
		}

		if (document.Layout.Entry(block + 5, target) == null) {
			throw new InvalidOperationException("That target is not in the mission.");
		}

		return document.Change(() => document.SetField(MsnField.ActionTarget, action, target));
	}

	/// <summary>Sets <paramref name="action"/>'s verb, 0 to <see cref="LastVerb"/>.</summary>
	public static MsnEdit SetVerb(MissionDocument document, short action, short verb) {
		if (verb is < 0 or > LastVerb) {
			throw new ArgumentOutOfRangeException(nameof(verb), verb, $"An arrival is 0 to {LastVerb}.");
		}

		Fields(document, action);
		return document.Change(() => document.SetField(MsnField.ActionVerb, action, verb));
	}

	/// <summary><paramref name="action"/>'s ten (counter, operation) pairs as its base sets them.</summary>
	public static (short Counter, short Op)[] CounterPairs(MissionDocument document, short action) {
		var pairs = Fields(document, action).CounterPairs;
		return Enumerable.Range(0, Pairs).Select(slot => (pairs[slot * 2], pairs[slot * 2 + 1])).ToArray();
	}

	/// <summary>How many pairs are filled, from the first to the last that sets either word.</summary>
	public static int FilledPairs(IReadOnlyList<(short Counter, short Op)> pairs) {
		for (int slot = pairs.Count - 1; slot >= 0; slot--) {
			if (pairs[slot].Counter != -1 || pairs[slot].Op != -1) {
				return slot + 1;
			}
		}

		return 0;
	}

	/// <summary>Why no counter write can be added to <paramref name="action"/>, or null when one can.</summary>
	public static string? WhyNoAddPair(MissionDocument document, short action) =>
		FilledPairs(CounterPairs(document, action)) >= Pairs ? $"It already lists {Pairs} campaign variable changes, the most it can hold." : null;

	/// <summary>Adds a counter write after <paramref name="action"/>'s last (<see cref="NewPairCounter"/>, <see cref="NewPairOp"/>).</summary>
	public static MsnEdit AddPair(MissionDocument document, short action) {
		if (WhyNoAddPair(document, action) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		int slot = FilledPairs(CounterPairs(document, action));
		return document.Change(() => {
			document.SetField(MsnField.ActionCounter(slot), action, NewPairCounter);
			document.SetField(MsnField.ActionCounterOp(slot), action, NewPairOp);
			CountPairs(document, action);
		});
	}

	/// <summary>
	/// Sets <paramref name="action"/>'s filled counter write <paramref name="slot"/> to counter <paramref name="counter"/>, 0 to
	/// <see cref="Counters"/> - 1, and operation <paramref name="op"/>, <see cref="CounterClear"/> or <see cref="CounterIncrement"/>.
	/// </summary>
	public static MsnEdit SetPair(MissionDocument document, short action, int slot, short counter, short op) {
		if (slot < 0 || slot >= FilledPairs(CounterPairs(document, action))) {
			throw new InvalidOperationException($"There is no campaign variable write {slot + 1}.");
		}

		if (counter is < 0 or >= Counters) {
			throw new ArgumentOutOfRangeException(nameof(counter), counter, $"A campaign variable is 0 to {Counters - 1}.");
		}

		if (op is not (CounterClear or CounterIncrement)) {
			throw new ArgumentOutOfRangeException(nameof(op), op, "An operation is 5 (clear) or 6 (+1).");
		}

		return document.Change(() => {
			document.SetField(MsnField.ActionCounter(slot), action, counter);
			document.SetField(MsnField.ActionCounterOp(slot), action, op);
		});
	}

	/// <summary>Removes <paramref name="action"/>'s counter write <paramref name="slot"/>, moving every later one up.</summary>
	public static MsnEdit RemovePair(MissionDocument document, short action, int slot) {
		var pairs = CounterPairs(document, action);
		int last = FilledPairs(pairs) - 1;
		if (slot < 0 || slot > last) {
			throw new InvalidOperationException($"There is no campaign variable write {slot + 1}.");
		}

		return document.Change(() => {
			for (int at = slot; at < last; at++) {
				document.SetField(MsnField.ActionCounter(at), action, pairs[at + 1].Counter);
				document.SetField(MsnField.ActionCounterOp(at), action, pairs[at + 1].Op);
			}

			document.SetField(MsnField.ActionCounter(last), action, -1);
			document.SetField(MsnField.ActionCounterOp(last), action, -1);
			CountPairs(document, action);
		});
	}

	/// <summary>The message <paramref name="action"/>'s base posts, its id, or <c>-1</c> for none: the stored value less one.</summary>
	public static short Message(MissionDocument document, short action) => (short)(Fields(document, action).MessageId - 1);

	/// <summary>Sets the message <paramref name="action"/> posts to id <paramref name="id"/>, stored plus one, or none for <c>-1</c>.</summary>
	public static MsnEdit SetMessage(MissionDocument document, short action, short id) {
		if (id is < -1 or >= short.MaxValue) {
			throw new ArgumentOutOfRangeException(nameof(id), id, "A message id is 0 or more, or -1 for none.");
		}

		Fields(document, action);
		return document.Change(() => document.SetField(MsnField.ActionMessage, action, (short)(id + 1)));
	}

	/// <summary>The records referencing row-10 GUID <paramref name="action"/>, every record whatever its condition.</summary>
	public static ActionNamers Namers(MissionDocument document, short action) {
		var mission = document.Mission;
		if (action == -1) {
			return new ActionNamers([], [], [], []);
		}

		static List<MsnRecordId> Where<T>(T[]? row, int number, Func<T, bool> references) {
			var found = new List<MsnRecordId>();
			for (int i = 0; i < (row ?? []).Length; i++) {
				if (row![i] is { } record && references(record)) {
					found.Add(new MsnRecordId(number, i));
				}
			}

			return found;
		}

		var timers = Where(mission.ActionTimers, TimerRow,
			timer => timer.PrimaryActionRef == action || timer.SequenceRefs.Contains(action));
		var groups = Where(mission.Groups, GroupRow, group => group.DeploymentActionRef == action);
		var objects = Where(mission.Mechs, 12, mech => mech.EngagementActionRef == action || mech.DefeatActionRef == action)
			.Concat(Where(mission.Flyers, 13, flyer => flyer.EngagementActionRef == action || flyer.DefeatActionRef == action))
			.Concat(Where(mission.Bases, 14, structure => structure.EngagementActionRef == action || structure.DefeatActionRef == action))
			.ToList();
		var orders = Where(mission.Orders, OrderRow, order => order.ActionRef == action);
		return new ActionNamers(timers, groups, objects, orders);
	}

	/// <summary>
	/// Deletes row-10 GUID <paramref name="action"/>: every record of the GUID, whatever its condition, and the <c>.ENG</c>
	/// records of each text id they referenced that no field references afterwards, as one
	/// <see cref="MissionDocument.Change"/>. Throws <see cref="InvalidOperationException"/> while <see cref="Namers"/> finds
	/// anything.
	/// </summary>
	public static MsnEdit Delete(MissionDocument document, short action) {
		if (Namers(document, action).Any) {
			throw new InvalidOperationException("That trigger is referenced by a countdown, a group, an object or an order.");
		}

		var texts = (document.Mission.Actions ?? []).Where(record => record.GUID == action)
			.SelectMany(record => record.TextRefs).Where(id => id != -1).Distinct().ToList();
		return document.Change(() => {
			document.Remove(ActionRow, action);
			MissionTextEditing.RemoveUnreferenced(document, texts);
		});
	}

	// Sets each record of <action>'s pair count to how many of its own pairs are filled, from the first.
	private static void CountPairs(MissionDocument document, short action) =>
		document.RewriteEvery<MissionAction82>(ActionRow, action, record => record.Unk1A = (short)FilledPairs(
			Enumerable.Range(0, Pairs).Select(slot => (record.CounterPairs[slot * 2], record.CounterPairs[slot * 2 + 1])).ToList()));

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();
}
