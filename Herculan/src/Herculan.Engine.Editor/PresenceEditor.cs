using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// What loses its reference to a record in the states it is absent from (<see cref="PresenceEditor.LosersOf"/>), by block
/// index: what the delete checks list for its kind (<see cref="Names"/>), and the groups whose member slots reference a
/// roster object (<see cref="MemberOf"/>).
/// </summary>
internal sealed record PresenceLosers(DeleteNamers Names, IReadOnlyList<int> MemberOf) {
	public bool Any => Names.Any || MemberOf.Count > 0;
}

/// <summary>
/// Sets a record's Present when, which campaign states it is in, each set one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="RecordConditions"/>'s record edits on the mission's <c>.MSN</c>
/// (docs/herculan/handoff-editor-conditions.md#present-when). Every set builds the editor's state again
/// (<see cref="MissionRebuild"/>), as it conditions the record in or out; when the record is not shown in the editor
/// afterwards, the condition it now carries is selected in its place, whose Used by lists it (an object among the records
/// not shown).
///
/// <para>A herc, flyer, structure or ground vehicle, a group, an order slot and an objective have a target
/// (<see cref="ObjectTarget"/> and the rest); the player's squad, group 0, its orders and its machines, has none, as the
/// squad build reads group 0's members, and the editor leaves the squad's presence alone as it leaves its wiring (the
/// editor's choice). A route has none: no retail route a GUID other than <c>-1</c> carries is conditioned
/// (<c>HercWorks.Query values --row 8 --field GUID --where ConditionRef!=-1</c>), so a route is present in every state (the
/// user's choice).</para>
///
/// <para>What loses its reference to an absent record is what the delete checks list for its kind: an object's
/// <see cref="RosterDeletion.Check"/> namers and the groups its member slots are in, a group's
/// <see cref="RosterDeletion.GroupNamers"/>, and the group slots running an order. Nothing references an objective.</para>
///
/// <para>The sets wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class PresenceEditor {
	private const int GroupBlock = 11;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;
	private PresenceTarget? _asked;

	public PresenceEditor(MissionDocument source, PointMover mover, MissionRebuild rebuild, SelectionState selection,
			ObjectDeleter deleter) {
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last set run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>The target the last set run was for.</summary>
	public PresenceTarget? FailureFor { get; private set; }

	/// <summary>Why <paramref name="target"/>'s presence cannot be set, or null when it can.</summary>
	public string? WhyFixed(PresenceTarget target) => RecordConditions.WhyFixed(_source, target);

	/// <summary>A roster object's target; null for one of the player's squad.</summary>
	public PresenceTarget? ObjectTarget(SceneObject placed) {
		var placement = placed.Placement;
		if (placement.IsPlayerLance) {
			return null;
		}

		int block = placement.Kind switch { MissionUnitKind.Mech => 7, MissionUnitKind.Flyer => 8, _ => 9 };
		short guid = _source.GuidOf(block, placement.SlotIndex);
		return guid == -1 ? null : PresenceTarget.Of(block + 5, guid);
	}

	/// <summary>Block-11 group <paramref name="group"/>'s target; null for the player's squad.</summary>
	public PresenceTarget? GroupTarget(int group) =>
		group != 0 && _source.GuidOf(GroupBlock, group) is not -1 and var guid
			? PresenceTarget.Of(GroupRow, guid)
			: null;

	/// <summary>The target of the order in block-11 group <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public PresenceTarget? OrderTarget(int group, int slot) =>
		_source.GuidOf(GroupBlock, group) is not -1 and var guid ? PresenceTarget.OrderSlot(guid, slot) : null;

	/// <summary>Block-12 objective <paramref name="objective"/>'s target, its row-17 record.</summary>
	public PresenceTarget? ObjectiveTarget(int objective) =>
		ObjectiveEditing.RecordOf(_source, objective) is { } record ? PresenceTarget.Objective(record) : null;

	/// <summary>Asks for <paramref name="target"/> to be present only while row-1 GUID <paramref name="condition"/> holds, or always for -1.</summary>
	public void Request(PresenceTarget target, short condition) =>
		Ask(target, (MissionDocument source, out short set, out short written) => {
			set = condition;
			return RecordConditions.SetPresence(source, target, condition, out written);
		});

	/// <summary>Asks for a new flag test, and for <paramref name="target"/> to be present only while it holds.</summary>
	public void RequestNewFlagTest(PresenceTarget target) =>
		Ask(target, (MissionDocument source, out short set, out short written) =>
			RecordConditions.SetPresenceToNewFlagTest(source, target, out set, out written));

	/// <summary>Asks for a new draw and a range over it, and for <paramref name="target"/> to be present only while the range holds.</summary>
	public void RequestNewDrawRange(PresenceTarget target) =>
		Ask(target, (MissionDocument source, out short set, out short written) =>
			RecordConditions.SetPresenceToNewDrawRange(source, target, out set, out written));

	/// <summary>Makes the set asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			FailureFor = _asked;
			Failure = edit();
		}
	}

	/// <summary>What loses its reference to <paramref name="target"/> where it is absent; null for an objective, which nothing references.</summary>
	public PresenceLosers? LosersOf(PresenceTarget target) {
		var source = _source;
		short guid = RecordConditions.GuidOf(source, target);
		if (guid == -1) {
			return null;
		}

		switch (target.Row) {
			case OrderRow:
				var records = RecordConditions.Records(source, target).Select(record => record.Record).ToList();
				return new PresenceLosers(_deleter.Resolve(source, new MsnNamers([], [], records)), []);
			case GroupRow:
				return new PresenceLosers(_deleter.Resolve(source, RosterDeletion.GroupNamers(source, guid)), []);
			case >= 12 and <= 14:
				var check = RosterDeletion.Check(source, target.Row, guid);
				var groups = check.MemberOf.Select(group => source.ExportedIndex(GroupRow, group)).OfType<int>().ToList();
				return new PresenceLosers(_deleter.Resolve(source, check.Namers), groups);
			default:
				return null;
		}
	}

	private delegate MsnEdit Setting(MissionDocument source, out short condition, out short written);

	// Asks for <set>, made once the frame is drawn: rebuilds, records one step of the history, and selects the condition the
	// record carries when the record is absent in the state shown afterwards.
	private void Ask(PresenceTarget target, Setting set) {
		_asked = target;
		_requested = () => {
			if (WhyFixed(target) is { } reason) {
				return reason;
			}

			var source = _source;
			short condition = -1, written = -1;
			MsnEdit change;
			try {
				change = _rebuild.Run(() => set(source, out condition, out written));
			} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
				return ex.Message;
			}

			_mover.Record(new RecordsEdit(_rebuild, change));
			bool present = target.Row == ObjectiveRow
				? ObjectiveEditing.ExportedIndex(source, target.Index) != null
				: written != -1 && source.ExportedIndex(target.Row, written) != null;
			if (!present && condition != RecordConditions.Always) {
				_selection.Current = new ConditionSelection(condition);
			}

			return null;
		};
	}
}
