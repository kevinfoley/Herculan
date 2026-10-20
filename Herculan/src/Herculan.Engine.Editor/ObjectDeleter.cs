using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// What names a roster object or a group, by block index in the generated <c>script.dat</c>: the objectives whose
/// subject it is, the actions whose target it is, and the order slots naming it as their subject.
/// <paramref name="Elsewhere"/> counts the naming records the editor's state does not export (a record that fails its
/// condition here, or an order no group runs), which name it in another state.
/// </summary>
internal sealed record DeleteNamers(IReadOnlyList<int> Objectives, IReadOnlyList<int> Actions,
		IReadOnlyList<OrderSlot> Orders, int Elsewhere) {
	public static DeleteNamers None { get; } = new([], [], [], 0);

	public bool Any => Objectives.Count > 0 || Actions.Count > 0 || Orders.Count > 0 || Elsewhere > 0;
}

/// <summary>
/// What keeps a placed object from being deleted (<see cref="ObjectDeleter.BlockersOf"/>). <see cref="Fixed"/> is why it
/// cannot be deleted at all; the rest is what names it or needs it, by block index.
/// </summary>
/// <param name="Object">What names the object.</param>
/// <param name="OwnActions">The actions it fires when engaged or defeated, by block-5 index; -1 for one this state does not export.</param>
/// <param name="RemovedGroups">The groups it is the last member of, by block-11 index, which go with it, and what names each.</param>
internal sealed record DeleteBlockers(string? Fixed, DeleteNamers Object, IReadOnlyList<int> OwnActions,
		IReadOnlyList<(int Group, DeleteNamers Names)> RemovedGroups) {
	/// <summary>Whether nothing does, and the object can be deleted.</summary>
	public bool None => Fixed == null && !Object.Any && OwnActions.Count == 0 && RemovedGroups.All(group => !group.Names.Any);

	public static DeleteBlockers Because(string reason) => new(reason, DeleteNamers.None, [], []);
}

/// <summary>
/// Deletes a placed structure, mech or flyer from the open mission, whether it was loaded with it or added since, as one
/// step of <see cref="PointMover"/>'s undo history. The delete is <see cref="RosterDeletion"/>'s record edits on the
/// mission's <c>.MSN</c>, by the roster record the object stands for, which remove the object's records and the group it
/// leaves empty and generate <c>script.dat</c> again; the scene is then synced with the document (<see cref="SceneSync"/>)
/// (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>What refuses a delete is <see cref="RosterDeletion"/>'s: an objective, an action or an order naming the object
/// or the group it would leave empty, an action of its own it fires when engaged or defeated, and the player's squad.
/// The squad's machines are group 0's, which its view adds and removes.</para>
///
/// <para>A delete asked for from the Properties panel or the Delete key waits for the frame's ImGui to be rendered
/// (<see cref="Request"/>, <see cref="TakeRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ObjectDeleter {
	private const int GroupRow = 16;

	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private SceneObject? _requested;

	public ObjectDeleter(ScriptDat document, MissionDocument source, GeneratedIndex index, PointMover mover,
			SceneSync sync) {
		_document = document;
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>What keeps <paramref name="placed"/> from being deleted; <see cref="DeleteBlockers.None"/> when nothing does.</summary>
	public DeleteBlockers BlockersOf(SceneObject placed) {
		var placement = placed.Placement;
		if (placement.IsPlayerLance) {
			return DeleteBlockers.Because("it is in the player's squad; group 0's view adds and removes squadmates.");
		}

		var source = _source;
		if (_index.Scene.KeyOf(placed) is not { } key) {
			return DeleteBlockers.Because("its roster record is not in the file.");
		}

		var check = RosterDeletion.Check(source, key.Row, key.Guid);
		if (check.InSquad) {
			return DeleteBlockers.Because("it is in the player's squad, whose machines a practice load builds from its records.");
		}

		var own = check.FiredActions.Select(action => source.Provenance.ExportedSlot(action)?.Index ?? -1).ToList();
		var groups = check.EmptiedGroups
			.Select(group => (source.ExportedIndex(GroupRow, group.Group) ?? -1, Resolve(source, group.Namers)))
			.ToList();
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
		var (row, guid) = _index.Scene.KeyOf(placed)!.Value;
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
	/// The records naming something, as the block indices this state exports them at: an order through each group slot
	/// running it.
	/// </summary>
	public DeleteNamers Resolve(MissionDocument source, MsnNamers namers) {
		int elsewhere = 0;
		List<int> Exported(IReadOnlyList<MsnRecordId> records) {
			var indices = new List<int>();
			foreach (var record in records) {
				if (source.Provenance.ExportedSlot(record) is { } slot) {
					indices.Add(slot.Index);
				} else {
					elsewhere++;
				}
			}

			return indices;
		}

		var objectives = Exported(namers.Objectives);
		var actions = Exported(namers.Actions);
		var orders = new List<OrderSlot>();
		foreach (int order in Exported(namers.Orders).Distinct()) {
			int before = orders.Count;
			for (int group = 0; group < _document.Groups.Length && group < _index.Mission.GroupOrders.Count; group++) {
				var refs = _document.Groups[group].OrderRefs;
				for (int slot = 0; slot < refs.Length; slot++) {
					if (refs[slot] == order && _index.Mission.GroupOrders[group][slot] is { } run) {
						orders.Add(new OrderSlot(group, slot, run));
					}
				}
			}

			if (orders.Count == before) {
				elsewhere++;
			}
		}

		return new DeleteNamers(objectives, actions, orders, elsewhere);
	}
}
