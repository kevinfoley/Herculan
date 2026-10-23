using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps a trigger area from being deleted (<see cref="AreaEditor.BlockersOf"/>). <see cref="Fixed"/> is why it cannot be
/// deleted at all; <see cref="Names"/> the actions referencing it.
/// </summary>
internal sealed record AreaDeleteBlockers(string? Fixed, DeleteNamers Names) {
	/// <summary>Whether nothing does, and the area can be deleted.</summary>
	public bool None => Fixed == null && !Names.Any;

	public static AreaDeleteBlockers Because(string reason) => new(reason, DeleteNamers.None);
}

/// <summary>
/// Creates, reshapes and deletes the open mission's trigger areas, each edit one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="TriggerAreaEditing"/>'s record edits on the mission's <c>.MSN</c>; the scene and the indices are then
/// synced with the document (<see cref="SceneSync"/>), as a new area's records are exported last and a shape change or a
/// delete adds or removes points, which moves every later point of block 1
/// (docs/herculan/handoff-editor-document.md#the-tools). A corner, centre or radius
/// move is <see cref="PointMover"/>'s.
///
/// <para>Areas are taken by their row-9 GUID, as the selections name them, and an area not shown in the editor is not
/// edited. The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>), since the rebuild
/// changes what the panels list; <see cref="AreaPlacing"/> calls <see cref="CreateBox"/> and <see cref="CreateCircle"/> at
/// the same point of the frame.</para>
/// </summary>
internal sealed class AreaEditor {
	private readonly MissionDocument _source;
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public AreaEditor(MissionDocument source, GeneratedIndex index, PointMover mover, SceneSync sync,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-9 GUID <paramref name="area"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short area) =>
		_index.Records.Area(area) != null ? _mover.WhyFixedArea(area) : GeneratedRecords.NotShown;

	/// <summary>What keeps <paramref name="area"/> from being deleted; <see cref="AreaDeleteBlockers.None"/> when nothing does.</summary>
	public AreaDeleteBlockers BlockersOf(short area) {
		if (WhyFixed(area) is { } reason) {
			return AreaDeleteBlockers.Because(reason);
		}

		var source = _source;
		var namers = TriggerAreaEditing.Namers(source, area);
		return new AreaDeleteBlockers(null, _deleter.Resolve(source, new MsnNamers([], namers, [])));
	}

	/// <summary>Asks for <paramref name="area"/> to be a box (<paramref name="circle"/> false) or a circle.</summary>
	public void RequestShape(short area, bool circle) =>
		_requested = () => WhyFixed(area)
			?? Commit(() => TriggerAreaEditing.SetShape(_source, area, circle ? TriggerAreaEditing.Circle : TriggerAreaEditing.Box), area);

	/// <summary>Asks for <paramref name="area"/> to be deleted.</summary>
	public void RequestDelete(short area) {
		_requested = () => {
			var blockers = BlockersOf(area);
			if (!blockers.None) {
				return blockers.Fixed ?? "A trigger references it; its view says which.";
			}

			return Commit(() => TriggerAreaEditing.Delete(_source, area), -1);
		};
	}

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	/// <summary>
	/// Adds a box from <paramref name="corner"/> to <paramref name="opposite"/> and selects it; one with no width or no depth
	/// adds nothing. Answers why it could not, or null.
	/// </summary>
	public string? CreateBox(Vec3i corner, Vec3i opposite) {
		if (corner.X == opposite.X || corner.Y == opposite.Y) {
			return null;
		}

		short area = -1;
		return Commit(() => TriggerAreaEditing.CreateBox(_source, (corner.X, corner.Y), (opposite.X, opposite.Y), out area), () => area);
	}

	/// <summary>
	/// Adds a circle of <paramref name="radius"/> world units about <paramref name="centre"/> and selects it; a radius of 0
	/// adds nothing. Answers why it could not, or null.
	/// </summary>
	public string? CreateCircle(Vec3i centre, int radius) {
		if (radius <= 0) {
			return null;
		}

		short area = -1;
		return Commit(() => TriggerAreaEditing.CreateCircle(_source, (centre.X, centre.Y), radius, out area), () => area);
	}

	private string? Commit(Func<MsnEdit> edit, short area) => Commit(edit, () => area);

	// Makes the change, rebuilding from it, records it as one step of the history and selects the area of row-9 GUID
	// <area>; an area that is gone (-1) leaves the selection to the rebuild.
	private string? Commit(Func<MsnEdit> edit, Func<short> area) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (area() is not -1 and var guid) {
			_selection.Current = new AreaSelection(guid);
		}

		return null;
	}
}
