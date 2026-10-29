using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// What loses its reference to a record in the states it is absent from (<see cref="PresenceEditor.LosersOf"/>), by identity
/// in the layout: what the delete checks list for its kind (<see cref="Names"/>), and the row-16 GUIDs of the groups whose
/// member slots reference a roster object (<see cref="MemberOf"/>).
/// </summary>
internal sealed record PresenceLosers(DeleteNamers Names, IReadOnlyList<short> MemberOf) {
	public bool Any => Names.Any || MemberOf.Count > 0;
}

/// <summary>
/// Sets a record's Present when, which campaign states it is in, each set one step of <see cref="PointMover"/>'s undo
/// history: as clauses through <see cref="PresenceRules"/>, or, under Advanced, to one of the mission's conditions through
/// <see cref="RecordConditions"/>, both record edits on the mission's <c>.MSN</c>
/// (docs/herculan/editor-design.md#present-when). Every set builds the editor's state again
/// (<see cref="SceneSync"/>), as it conditions the record in or out; the selection stays on the record, whose view still
/// shows its Present when where the mission as opened leaves it out.
///
/// <para>A herc, flyer, structure or ground vehicle, a group, an order slot and an objective have a target, taken from the
/// record's identity in the <c>.MSN</c>, so a record the mission as opened leaves out has one too
/// (<see cref="ObjectTarget"/> and the rest); the player's squad, its group and its machines, has none, as the squad build
/// reads group 0's members, and the editor leaves the squad's presence alone as it leaves its wiring (the editor's choice).
/// The squad's orders have a target that <see cref="RecordConditions.WhyFixed"/> refuses. A route has none: no retail route
/// a GUID other than <c>-1</c> carries is conditioned (<c>HercWorks.Query values --row 8 --field GUID --where
/// ConditionRef!=-1</c>), so a route is present in every state (the user's choice).</para>
///
/// <para>What loses its reference to an absent record is what the delete checks list for its kind: an object's
/// <see cref="RosterDeletion.Check"/> namers and the groups its member slots are in, a group's
/// <see cref="RosterDeletion.GroupNamers"/>, and the group slots running an order. Nothing references an objective.</para>
///
/// <para>The sets wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class PresenceEditor {
	private const int OrderRow = 15;
	private const int GroupRow = 16;

	private readonly MissionDocument _source;
	private readonly MissionIndex _scene;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;
	private PresenceTarget? _asked;

	/// <param name="campaign">Whether the mission is played as a campaign mission, the one kind whose campaign variables vary.</param>
	public PresenceEditor(MissionDocument source, MissionIndex scene, PointMover mover, SceneSync sync, ObjectDeleter deleter,
			Func<bool> campaign) {
		_source = source;
		_scene = scene;
		_mover = mover;
		_sync = sync;
		_deleter = deleter;
		Campaign = campaign;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>Whether the mission is played as a campaign mission.</summary>
	public Func<bool> Campaign { get; }

	/// <summary>Why the last set run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>The target the last set run was for.</summary>
	public PresenceTarget? FailureFor { get; private set; }

	/// <summary>Why <paramref name="target"/>'s presence cannot be set at all, or null when it can.</summary>
	public string? WhyFixed(PresenceTarget target) => RecordConditions.WhyFixed(_source, target);

	/// <summary>The target of the roster record <paramref name="placed"/> stands for; null for one of the player's squad.</summary>
	public PresenceTarget? ObjectTarget(SceneObject placed) =>
		_scene.Planned(placed) is { Squad: false } planned ? PresenceTarget.Of(planned.Key.Row, planned.Key.Guid) : null;

	/// <summary>Row-16 group <paramref name="group"/>'s target; null for the player's squad, the layout's first group.</summary>
	public PresenceTarget? GroupTarget(short group) =>
		_scene.Groups.Count > 0 && _scene.Groups[0].Guid == group ? null : PresenceTarget.Of(GroupRow, group);

	/// <summary>The target of the order in row-16 group <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public PresenceTarget OrderTarget(short group, int slot) => PresenceTarget.OrderSlot(group, slot);

	/// <summary>Row-17 record <paramref name="record"/>'s target.</summary>
	public PresenceTarget ObjectiveTarget(int record) => PresenceTarget.Objective(record);

	/// <summary>Asks for <paramref name="target"/> to be present by <paramref name="rule"/>.</summary>
	public void RequestRule(PresenceTarget target, PresenceRule rule) =>
		Ask(target, () => PresenceRules.SetRule(_source, target, rule, out _));

	/// <summary>Asks for <paramref name="target"/> to be present only while row-1 GUID <paramref name="condition"/> holds, or always for -1.</summary>
	public void Request(PresenceTarget target, short condition) =>
		Ask(target, () => RecordConditions.SetPresence(_source, target, condition, out _));

	/// <summary>Why roster object <paramref name="member"/>'s presence cannot be moved onto row-16 group <paramref name="group"/> (<see cref="RecordConditions.WhyNoMoveToGroup"/>).</summary>
	public string? WhyNoMoveToGroup(PresenceTarget member, short group) => RecordConditions.WhyNoMoveToGroup(_source, member, group);

	/// <summary>Asks for row-16 group <paramref name="group"/> to be present when roster object <paramref name="member"/> is, and the object always (<see cref="RecordConditions.MoveToGroup"/>).</summary>
	public void RequestMoveToGroup(PresenceTarget member, short group) =>
		Ask(member, () => RecordConditions.MoveToGroup(_source, member, group));

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
				return new PresenceLosers(_deleter.Resolve(source, check.Namers), check.MemberOf);
			default:
				return null;
		}
	}

	// Asks for <set>, made once the frame is drawn: rebuilds and records one step of the history.
	private void Ask(PresenceTarget target, Func<MsnEdit> set) {
		_asked = target;
		_requested = () => {
			if (WhyFixed(target) is { } reason) {
				return reason;
			}

			MsnEdit change;
			try {
				change = _sync.Run(set);
			} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
				return ex.Message;
			}

			_mover.Record(new RecordsEdit(_sync, change));
			return null;
		};
	}
}
