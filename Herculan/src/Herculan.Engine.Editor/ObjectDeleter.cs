using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// What references a roster object, a group, a route or a trigger area, by identity in the <c>.MSN</c>'s layout, every record
/// whatever its condition: the objectives (row-17 indices), the triggers (row-10 GUIDs) and the group order slots whose order
/// references it. <see cref="Options"/> counts the options of random choices (variant alternatives, GUID <c>-1</c>) that
/// reference it, and <see cref="UnrunOrders"/> the orders referencing it that no group's slot runs.
/// </summary>
internal sealed record DeleteNamers(IReadOnlyList<int> Objectives, IReadOnlyList<short> Triggers,
		IReadOnlyList<EndsOrder> Orders, int Options, int UnrunOrders) {
	public static DeleteNamers None { get; } = new([], [], [], 0, 0);

	public bool Any => Objectives.Count > 0 || Triggers.Count > 0 || Orders.Count > 0 || Options > 0 || UnrunOrders > 0;
}

/// <summary>
/// What keeps a placed object from being deleted (<see cref="ObjectDeleter.BlockersOf"/>). <see cref="Fixed"/> is why it
/// cannot be deleted at all; the rest is what references or needs it, by identity.
/// </summary>
/// <param name="Object">What references the object.</param>
/// <param name="OwnTriggers">The row-10 GUIDs of the triggers it fires when engaged or defeated.</param>
/// <param name="RemovedGroups">The row-16 GUIDs of the groups it is the last member of, which go with it, and what references each.</param>
internal sealed record DeleteBlockers(string? Fixed, DeleteNamers Object, IReadOnlyList<short> OwnTriggers,
		IReadOnlyList<(short Group, DeleteNamers Names)> RemovedGroups) {
	/// <summary>Whether nothing does, and the object can be deleted.</summary>
	public bool None => Fixed == null && !Object.Any && OwnTriggers.Count == 0 && RemovedGroups.All(group => !group.Names.Any);

	public static DeleteBlockers Because(string reason) => new(reason, DeleteNamers.None, [], []);
}

/// <summary>
/// Deletes a placed structure, mech or flyer from the open mission, whether it was loaded with it or added since, as one
/// step of <see cref="PointMover"/>'s undo history. The delete is <see cref="RosterDeletion"/>'s record edits on the
/// mission's <c>.MSN</c>, by the roster record the object stands for, which remove the object's records and the group it
/// leaves empty; the scene is then synced with the document (<see cref="SceneSync"/>)
/// (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>What refuses a delete is <see cref="RosterDeletion"/>'s: an objective, an action or an order referencing the object
/// or the group it would leave empty, an action of its own it fires when engaged or defeated, and the player's squad.
/// The squad's machines are group 0's, which its view adds and removes. Each is named by its identity in the layout
/// (<see cref="Resolve"/>), so any record shows, one the mission as opened leaves out included.</para>
///
/// <para>A delete asked for from the Properties panel or the Delete key waits for the frame's ImGui to be rendered
/// (<see cref="Request"/>, <see cref="TakeRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ObjectDeleter {
	private const int ObjectiveRow = 17;

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private SceneObject? _requested;

	public ObjectDeleter(MissionDocument source, MissionIndex index, PointMover mover, SceneSync sync) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>What keeps <paramref name="placed"/> from being deleted; <see cref="DeleteBlockers.None"/> when nothing does.</summary>
	public DeleteBlockers BlockersOf(SceneObject placed) {
		if (_index.Planned(placed) is not { } planned) {
			return DeleteBlockers.Because("not found.");
		}

		if (planned.Squad) {
			return DeleteBlockers.Because("Squadmates are added and removed in the Player's squad view.");
		}

		var source = _source;
		var check = RosterDeletion.Check(source, planned.Key.Row, planned.Key.Guid);
		if (check.InSquad) {
			return DeleteBlockers.Because("it is in the player's squad, whose machines a practice load builds from its records.");
		}

		var own = check.FiredActions.Select(action => source.RecordAt(action)?.GUID ?? -1).Where(guid => guid != -1).Distinct().ToList();
		var groups = check.EmptiedGroups.Select(group => (group.Group, Resolve(source, group.Namers))).ToList();
		return new DeleteBlockers(null, Resolve(source, check.Namers), own, groups);
	}

	/// <summary>Asks for <paramref name="placed"/> to be deleted once the frame's ImGui is rendered.</summary>
	public void Request(SceneObject placed) => _requested = placed;

	/// <summary>The object a delete was asked for since the last call, or null.</summary>
	public SceneObject? TakeRequest() {
		var requested = _requested;
		_requested = null;
		return requested;
	}

	/// <summary>
	/// Deletes <paramref name="placed"/>, its group too when it is the last member. Answers why it could not, or null
	/// when it did.
	/// </summary>
	public string? Delete(SceneObject placed) {
		var blockers = BlockersOf(placed);
		if (!blockers.None) {
			return blockers.Fixed ?? "the mission references or needs it; its Properties view says what.";
		}

		var source = _source;
		var (row, guid) = _index.KeyOf(placed)!.Value;
		MsnEdit change;
		try {
			change = _sync.Run(() => RosterDeletion.Delete(source, row, guid));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	}

	/// <summary>
	/// The records <paramref name="namers"/> lists, by identity in the layout: an objective by its row-17 index, a trigger by
	/// its GUID, and an order through each group slot running it, as the group's base names it. A record of GUID <c>-1</c> is
	/// an option of a random choice, and an order no slot runs is counted apart.
	/// </summary>
	public DeleteNamers Resolve(MissionDocument source, MsnNamers namers) {
		int options = 0;
		List<short> Guids(IEnumerable<MsnRecordId> records) {
			var guids = new List<short>();
			foreach (var record in records) {
				if (source.RecordAt(record) is not { } found) {
					continue;
				}

				if (found.GUID == -1) {
					options++;
				} else if (!guids.Contains(found.GUID)) {
					guids.Add(found.GUID);
				}
			}

			return guids;
		}

		var objectives = namers.Objectives.Where(record => record.Row == ObjectiveRow).Select(record => record.Index).Distinct().ToList();
		var triggers = Guids(namers.Actions);
		var orders = new List<EndsOrder>();
		int unrun = 0;
		foreach (short order in Guids(namers.Orders)) {
			var running = _index.OrderSlots(order);
			if (running.Count == 0) {
				unrun++;
			}

			orders.AddRange(running.Where(slot => !orders.Contains(slot)).ToList());
		}

		return new DeleteNamers(objectives, triggers, orders, options, unrun);
	}
}
