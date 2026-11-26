using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Es2.Scene;

namespace Herculan.Es2.Editor;

/// <summary>
/// What loses its reference to a record in the states it is absent from (<see cref="PresenceEditor.LosersOf"/>), by identity
/// in the layout: what the delete checks list for its kind (<see cref="Names"/>), and the row-16 GUIDs of the groups whose
/// member slots reference a roster object (<see cref="MemberOf"/>).
/// </summary>
internal sealed record PresenceLosers(DeleteNamers Names, IReadOnlyList<short> MemberOf) {
	public bool Any => Names.Any || MemberOf.Count > 0;
}

/// <summary>
/// Sets a record's Present when, which campaign states it is in, each set one step of the <see cref="EditHistory">undo
/// history</see>: as clauses through <see cref="PresenceRules"/>, or, under Advanced, to one of the mission's conditions through
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
	private readonly EditHistory _history;
	private readonly SceneSync _sync;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;
	private PresenceTarget? _asked;

	/// <param name="campaign">Whether the mission is played as a campaign mission, the one kind whose campaign variables vary.</param>
	public PresenceEditor(MissionDocument source, MissionIndex scene, EditHistory history, SceneSync sync, ObjectDeleter deleter,
			Func<bool> campaign) {
		_source = source;
		_scene = scene;
		_history = history;
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

	/// <summary>Why a target's presence cannot be set at all, or null when it can.</summary>
	/// <param name="target">The target.</param>
	public string? WhyFixed(PresenceTarget target) => RecordConditions.WhyFixed(_source, target);

	/// <summary>The target of the roster record an object stands for; null for one of the player's squad.</summary>
	/// <param name="placed">The object.</param>
	public PresenceTarget? ObjectTarget(SceneObject placed) =>
		_scene.Planned(placed) is { Squad: false } planned ? PresenceTarget.Of(planned.Key.Row, planned.Key.Guid) : null;

	/// <summary>A row-16 group's target; null for the player's squad, the layout's first group.</summary>
	/// <param name="group">The group's row-16 GUID.</param>
	public PresenceTarget? GroupTarget(short group) =>
		_scene.Groups.Count > 0 && _scene.Groups[0].Guid == group ? null : PresenceTarget.Of(GroupRow, group);

	/// <summary>The target of the order in a row-16 group's slot.</summary>
	/// <param name="group">The group's row-16 GUID.</param>
	/// <param name="slot">The order slot.</param>
	public PresenceTarget OrderTarget(short group, int slot) => PresenceTarget.OrderSlot(group, slot);

	/// <summary>A row-17 record's target.</summary>
	/// <param name="record">The record's index in row 17.</param>
	public PresenceTarget ObjectiveTarget(int record) => PresenceTarget.Objective(record);

	/// <summary>Asks for a target to be present by a rule.</summary>
	/// <param name="target">The target.</param>
	/// <param name="rule">The rule.</param>
	public void RequestRule(PresenceTarget target, PresenceRule rule) =>
		Ask(target, () => PresenceRules.SetRule(_source, target, rule, out _));

	/// <summary>Asks for a target to be present only while a row-1 condition holds, or always for -1.</summary>
	/// <param name="target">The target.</param>
	/// <param name="condition">The row-1 GUID, or -1 for always.</param>
	public void Request(PresenceTarget target, short condition) =>
		Ask(target, () => RecordConditions.SetPresence(_source, target, condition, out _));

	/// <summary>Why a roster object's presence cannot be moved onto a row-16 group (<see cref="RecordConditions.WhyNoMoveToGroup"/>).</summary>
	/// <param name="member">The roster object.</param>
	/// <param name="group">The group's row-16 GUID.</param>
	public string? WhyNoMoveToGroup(PresenceTarget member, short group) => RecordConditions.WhyNoMoveToGroup(_source, member, group);

	/// <summary>Asks for a row-16 group to be present when a roster object is, and the object always (<see cref="RecordConditions.MoveToGroup"/>).</summary>
	/// <param name="member">The roster object.</param>
	/// <param name="group">The group's row-16 GUID.</param>
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

	/// <summary>What loses its reference to a target where it is absent; null for an objective, which nothing references.</summary>
	/// <param name="target">The target.</param>
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

			_history.Record(new RecordsEdit(_sync, change));
			return null;
		};
	}
}
