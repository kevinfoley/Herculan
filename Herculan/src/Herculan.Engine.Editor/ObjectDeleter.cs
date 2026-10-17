using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps a placed object from being deleted (<see cref="ObjectDeleter.BlockersOf"/>). <see cref="Fixed"/> is why it
/// has no roster record to unlink; the lists are what names it or needs it, by block index.
/// </summary>
/// <param name="Objectives">The objectives whose subject it is.</param>
/// <param name="Actions">The actions whose target it is.</param>
/// <param name="OwnActions">The actions it fires when engaged or defeated.</param>
/// <param name="Orders">The orders, of any group, whose subject it is.</param>
/// <param name="LastMemberOf">The groups it is the last member of.</param>
internal sealed record DeleteBlockers(string? Fixed, IReadOnlyList<int> Objectives, IReadOnlyList<int> Actions,
		IReadOnlyList<int> OwnActions, IReadOnlyList<OrderSlot> Orders, IReadOnlyList<int> LastMemberOf) {
	/// <summary>Whether nothing does, and the object can be deleted.</summary>
	public bool None => Fixed == null && Objectives.Count == 0 && Actions.Count == 0 && OwnActions.Count == 0
		&& Orders.Count == 0 && LastMemberOf.Count == 0;

	public static DeleteBlockers Because(string reason) => new(reason, [], [], [], [], []);
}

/// <summary>
/// Deletes a placed structure, mech or flyer from the open mission, whether it was loaded with it or added since, as one
/// step of <see cref="PointMover"/>'s undo history.
///
/// <para>It refuses every delete for now (<see cref="NotPorted"/>). A mission is held as its <c>.MSN</c>, with
/// <c>script.dat</c> generated from it (<see cref="HercWorks.Core.Data.File.Msn.MissionDocument"/>), and a delete written
/// into the generated file would be lost at the next generation; one written into the <c>.MSN</c> removes the object's
/// records, which renumbers every later record of its block, and the editor does not yet rebuild its index and the
/// scene's placements from the generated file
/// (docs/herculan/handoff-editor-mission-editing.md#holding-the-msn-as-the-document, "Regenerating on each edit").
/// What follows is the delete as it ran on a <c>script.dat</c> document, which the port replaces.</para>
///
/// <para>The delete unlinks the object rather than removing its record: every
/// block-11 member ref naming its roster slot is set to <c>-1</c>, and a roster slot no block-11 record names never spawns
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 1). Its block-7/8/9 record stays, so no record after
/// it is renumbered and no ref to one changes, and the roster counts DBSIM's slot caps bound
/// (<see cref="ScriptDatLint"/>) stay as they were.
///
/// <para>The briefing map reads block 9 whole and shows a structure no structure group names by that structure's own first
/// out-of-action operation (docs/retail/shell/mission-map.md#what-it-reads), so an unlinked structure's is set to 0,
/// which hides it; DBSIM reads the operation only for a record it spawns.</para>
///
/// <para>What the editor refuses to delete, its own choices (<see cref="BlockersOf"/>): the player's squad, which has no
/// roster record (rule 6); anything the mission names, an objective's subject, an action's target or an order's subject,
/// or that fires an action of its own when engaged or defeated, since those refs would be left on a slot that spawns
/// nothing and those actions would never fire from it; and the last member of a group, which DBSIM does not run safely
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 1). Deleting a group's last member would mean
/// removing its block-11 record and renumbering every group ref past it. A group's own objectives, actions and orders
/// are no bar while it keeps other members.</para>
///
/// <para>The one last member that can go is that of a group the editor added (<see cref="StructurePlacer"/>) which is
/// still block 11's last record and which nothing names: the delete then removes the group's record too, which
/// renumbers nothing.</para>
/// </summary>
internal sealed class ObjectDeleter {
	private readonly ScriptDat _document;
	private readonly MissionIndex _index;
	private readonly ScenePicker _picker;
	private readonly PointMover _mover;
	private readonly SelectionState _selection;

	public ObjectDeleter(ScriptDat document, MissionIndex index, ScenePicker picker, PointMover mover,
			SelectionState selection) {
		_document = document;
		_index = index;
		_picker = picker;
		_mover = mover;
		_selection = selection;
	}

	/// <summary>Raised with each object an undo puts back in the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Added;

	/// <summary>Raised with each object a delete or a redo takes out of the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Removed;

	private Mission Mission => _index.Mission;

	/// <summary>Why no object of a mission held as its <c>.MSN</c> is deleted yet: see the class summary.</summary>
	public const string NotPorted = "deleting is not yet ported to the .MSN the mission is held as: a delete renumbers the "
		+ "generated records, and the editor does not yet rebuild its index from them.";

	/// <summary>What keeps <paramref name="placed"/> from being deleted; <see cref="DeleteBlockers.None"/> when nothing does.</summary>
	public DeleteBlockers BlockersOf(SceneObject placed) {
		var placement = placed.Placement;
		if (_mover.WhyReadOnly is { } readOnly) {
			return DeleteBlockers.Because(readOnly);
		}

		if (placement.IsPlayerLance) {
			return DeleteBlockers.Because("the player's squad is placed from group 0 and player.mec, with no roster record.");
		}

		var kind = placement.Kind;
		int slot = placement.SlotIndex;
		if (RosterLength(kind) is not { } length || slot < 0 || slot >= length) {
			return DeleteBlockers.Because($"its {kind.ToString().ToLowerInvariant()} roster slot {slot} is not in the file.");
		}

		var links = Links(kind, slot);
		if (links.Count == 0) {
			return DeleteBlockers.Because("no group in the file places it.");
		}

		var actions = new List<int>();
		for (int i = 0; i < Mission.Actions.Count; i++) {
			if (TargetKind(Mission.Actions[i].Type) == kind && Mission.Actions[i].TargetRef == slot) {
				actions.Add(i);
			}
		}

		var orders = new List<OrderSlot>();
		for (int group = 0; group < Mission.GroupOrders.Count; group++) {
			var groupOrders = Mission.GroupOrders[group];
			for (int i = 0; i < groupOrders.Count; i++) {
				if (groupOrders[i] is { } order && MissionIndex.UnitKindOf(order.SubjectKind) == kind
						&& order.SubjectRef == slot) {
					orders.Add(new OrderSlot(group, i, order));
				}
			}
		}

		var own = new[] { placement.EngagementActionRef, placement.DefeatActionRef }
			.Where(action => action >= 0).Distinct().ToList();
		var lastMemberOf = links.Select(link => link.Group).Distinct()
			.Where(group => IsLastMember(group, slot) && !Removable(group))
			.ToList();
		return new DeleteBlockers(NotPorted, _index.ObjectivesOfObject(placed).ToList(), actions, own, orders, lastMemberOf);
	}

	/// <summary>Deletes <paramref name="placed"/> and clears the selection. Answers why it could not, or null when it did.</summary>
	public string? Delete(SceneObject placed) {
		var blockers = BlockersOf(placed);
		if (!blockers.None) {
			return blockers.Fixed ?? "the mission names it or needs it; its Properties view says what.";
		}

		var placement = placed.Placement;
		short? operation = placement.Kind == MissionUnitKind.Base
				&& _document.Bases[placement.SlotIndex].CounterOps is { Length: > 0 } operations
			? operations[0]
			: null;
		var links = Links(placement.Kind, placement.SlotIndex);
		int lone = links.Select(link => link.Group).Distinct()
			.Where(group => IsLastMember(group, placement.SlotIndex) && Removable(group))
			.DefaultIfEmpty(-1).First();
		var removed = lone >= 0 && _index.SetupOf(lone) is { } setup
			? new RemovedGroup(lone, _document.Groups[lone], _index.KindOf(lone) ?? placement.Kind,
				_index.SideOf(lone) ?? placement.Side, setup)
			: null;
		var edit = new DeleteEdit(this, placed, links, operation, removed);
		edit.Redo();
		_mover.Record(edit);
		_selection.Current = null;
		return null;
	}

	// Every block-11 member ref naming a roster slot, as (group, position among its twenty). Record 0's are the player's
	// squad (rule 6) and are left alone.
	private List<(int Group, int Position)> Links(MissionUnitKind kind, int slot) {
		short roster = kind switch {
			MissionUnitKind.Mech => 0,
			MissionUnitKind.Flyer => 1,
			_ => 2
		};

		var links = new List<(int, int)>();
		for (int group = 1; group < _document.Groups.Length; group++) {
			var record = _document.Groups[group];
			if (record.MemberKind != roster) {
				continue;
			}

			for (int position = 0; position < record.MemberRefs.Length; position++) {
				if (record.MemberRefs[position] == slot) {
					links.Add((group, position));
				}
			}
		}

		return links;
	}

	private bool IsLastMember(int group, int slot) =>
		_document.Groups[group].MemberRefs.All(member => member < 0 || member == slot);

	// Whether a group can go with its last member: one the editor added, still block 11's last record, so removing it
	// renumbers nothing, and named by no objective, order or action.
	private bool Removable(int group) =>
		group == _document.Groups.Length - 1 && group >= Mission.GroupKinds.Count
			&& !_index.ObjectivesOfGroup(group).Any()
			&& !Mission.GroupOrders.Any(orders => orders.Any(order =>
				order is { SubjectKind: MissionOrderSubject.Group } && order.SubjectRef == group))
			&& !Mission.Actions.Any(action => action.Type == MissionAction.SubjectTargetGroup && action.TargetRef == group);

	private int? RosterLength(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => _document.Mechs.Length,
		MissionUnitKind.Flyer => _document.Flyers.Length,
		MissionUnitKind.Base => _document.Bases.Length,
		_ => null
	};

	// The roster an action's target indexes, by its trigger subject type, or null for one with no object target.
	private static MissionUnitKind? TargetKind(short type) => type switch {
		MissionAction.SubjectTargetMech => MissionUnitKind.Mech,
		MissionAction.SubjectTargetFlyer => MissionUnitKind.Flyer,
		MissionAction.SubjectTargetBase => MissionUnitKind.Base,
		_ => null
	};

	// Unlinks the object in the document and takes it out of the index, the picker and the drawing, and the selection off
	// it; answers where it stood in the index.
	private (int Object, int Member) Hide(DeleteEdit edit) {
		var placed = edit.Placed;
		foreach (var (group, position) in edit.Links) {
			_document.Groups[group].MemberRefs[position] = -1;
		}

		if (edit.Operation != null) {
			_document.Bases[placed.Placement.SlotIndex].SetCounterOp(0, 0);
		}

		if (_selection.Current is ObjectSelection { Object: var selected } && ReferenceEquals(selected, placed)) {
			_selection.Current = null;
		}

		Removed?.Invoke(placed);
		_picker.Remove(placed);
		var at = _index.DetachObject(placed);
		if (edit.Group is { } removed) {
			if (_selection.Current is GroupSelection { Group: var group } && group == removed.Index) {
				_selection.Current = null;
			}

			_document.Groups = _document.Groups[..^1];
			_index.RemoveLastAddedGroup();
		}

		return at;
	}

	// Links it again and puts it back where it stood.
	private void Show(DeleteEdit edit, (int Object, int Member) at) {
		var placed = edit.Placed;
		short slot = (short)placed.Placement.SlotIndex;
		foreach (var (group, position) in edit.Links) {
			_document.Groups[group].MemberRefs[position] = slot;
		}

		if (edit.Operation is { } operation) {
			_document.Bases[slot].SetCounterOp(0, operation);
		}

		if (edit.Group is { } removed) {
			_document.Groups = [.. _document.Groups, removed.Record];
			_index.AddGroup(removed.Index, removed.Kind, removed.Side, removed.Setup);
		}

		_index.RestoreObject(placed, at);
		_picker.Add(placed);
		Added?.Invoke(placed);
	}

	// A group the delete removed with its last member, as it stood, to append again on undo.
	private sealed record RemovedGroup(int Index, ScriptGroup Record, MissionUnitKind Kind, MissionSide Side,
		MissionGroupSetup Setup);

	// One delete, undone and redone as a whole. The undo history is last in, first out, so everything made after the delete
	// has been undone before the delete is.
	private sealed class DeleteEdit : IEditorEdit {
		private readonly ObjectDeleter _deleter;
		private (int Object, int Member) _at;

		public DeleteEdit(ObjectDeleter deleter, SceneObject placed, List<(int Group, int Position)> links, short? operation,
				RemovedGroup? group) {
			_deleter = deleter;
			Placed = placed;
			Links = links;
			Operation = operation;
			Group = group;
		}

		public SceneObject Placed { get; }

		/// <summary>The member refs that named it.</summary>
		public List<(int Group, int Position)> Links { get; }

		/// <summary>A structure's first out-of-action operation as it stood, or null for a mech or a flyer.</summary>
		public short? Operation { get; }

		/// <summary>The group removed with it, or null when it kept its group.</summary>
		public RemovedGroup? Group { get; }

		public void Undo() => _deleter.Show(this, _at);

		public void Redo() => _at = _deleter.Hide(this);
	}
}
