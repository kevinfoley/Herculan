using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps a group from being deleted whole (<see cref="GroupDeleter.BlockersOf"/>). <see cref="Fixed"/> is why it cannot
/// be deleted at all; the rest is what references or needs it, by identity.
/// </summary>
/// <param name="Group">What references the group.</param>
/// <param name="Members">Each member going with it that something references or needs, with what does and the triggers it fires.</param>
/// <param name="Going">The members that go with it.</param>
/// <param name="Kept">The members another group places too, which stay.</param>
internal sealed record GroupDeleteBlockers(string? Fixed, DeleteNamers Group,
		IReadOnlyList<(ObjectKey Member, DeleteNamers Names, IReadOnlyList<short> OwnTriggers)> Members,
		IReadOnlyList<ObjectKey> Going, IReadOnlyList<ObjectKey> Kept) {
	/// <summary>Whether nothing does, and the group can be deleted.</summary>
	public bool None => Fixed == null && !Group.Any && Members.Count == 0;

	public static GroupDeleteBlockers Because(string reason) => new(reason, DeleteNamers.None, [], [], []);
}

/// <summary>
/// Deletes a group whole from the open mission, its members with it, as one step of <see cref="PointMover"/>'s undo history:
/// <see cref="RosterDeletion.DeleteGroup"/>'s record edits, refused as it refuses them, then the scene synced with the
/// document (<see cref="SceneSync"/>), which takes a base's pad away with its group. What blocks it is named by identity in
/// the layout, as <see cref="ObjectDeleter.Resolve"/> names it.
///
/// <para>A delete asked for from the Properties panel or the Delete key waits for the frame's ImGui to be rendered
/// (<see cref="Request"/>, <see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class GroupDeleter {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly ObjectDeleter _objects;
	private short? _requested;

	public GroupDeleter(MissionDocument source, MissionIndex index, PointMover mover, SceneSync sync, ObjectDeleter objects) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_objects = objects;
	}

	/// <summary>Why the last delete run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>What keeps row-16 GUID <paramref name="group"/> from being deleted; <see cref="GroupDeleteBlockers.None"/> when nothing does.</summary>
	public GroupDeleteBlockers BlockersOf(short group) {
		if (_index.Group(group) == null || _source.Layout.Entry(16, group) == null) {
			return GroupDeleteBlockers.Because(EditorToolbox.GroupGone);
		}

		var source = _source;
		var check = RosterDeletion.CheckGroup(source, group);
		if (check.IsSquad) {
			return GroupDeleteBlockers.Because("it is the player's squad, which every mission has.");
		}

		var needed = check.Members
			.Where(member => member.Check.Namers.Any || member.Check.FiredActions.Count > 0)
			.Select(member => (new ObjectKey(check.Row, member.Guid), _objects.Resolve(source, member.Check.Namers),
				(IReadOnlyList<short>)member.Check.FiredActions.Select(action => source.RecordAt(action)?.GUID ?? -1)
					.Where(guid => guid != -1).Distinct().ToList()))
			.ToList();
		return new GroupDeleteBlockers(null, _objects.Resolve(source, check.Namers), needed,
			check.Members.Select(member => new ObjectKey(check.Row, member.Guid)).ToList(),
			check.Kept.Select(member => new ObjectKey(check.Row, member)).ToList());
	}

	/// <summary>Asks for row-16 GUID <paramref name="group"/> to be deleted once the frame's ImGui is rendered.</summary>
	public void Request(short group) => _requested = group;

	/// <summary>Makes the delete asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } group) {
			_requested = null;
			Failure = Delete(group);
		}
	}

	// Deletes <group> and the members that go with it. Answers why it could not, or null when it did.
	private string? Delete(short group) {
		var blockers = BlockersOf(group);
		if (!blockers.None) {
			return blockers.Fixed ?? "the mission references or needs it or a member; its Properties view says what.";
		}

		var source = _source;
		MsnEdit change;
		try {
			change = _sync.Run(() => RosterDeletion.DeleteGroup(source, group));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	}
}
