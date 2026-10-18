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
/// history, through <see cref="TriggerAreaEditing"/>'s record edits on the mission's <c>.MSN</c>; the editor's state is then
/// built again from the generated file (<see cref="MissionRebuild"/>), since a new area's records are exported last and a
/// shape change or a delete adds or removes points, which moves every later point of block 1
/// (docs/herculan/handoff-editor-authoring.md#holding-the-msn-as-the-document, "The tools"). A corner, centre or radius
/// move is <see cref="PointMover"/>'s, which rebuilds nothing.
///
/// <para>Areas are taken by their block-4 index, as the panels list them, and written as the row-9 GUID of the record that
/// created the slot. The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>), since the rebuild
/// changes what the panels list; <see cref="AreaPlacing"/> calls <see cref="CreateBox"/> and <see cref="CreateCircle"/> at
/// the same point of the frame. A mission opened from a plain <c>script.dat</c> edits nothing
/// (<see cref="PointMover.WhyReadOnly"/>).</para>
/// </summary>
internal sealed class AreaEditor {
	private const int AreaBlock = 4;
	private const int AreaRow = 9;

	private readonly MissionDocument? _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public AreaEditor(MissionDocument? source, MissionIndex index, PointMover mover, MissionRebuild rebuild,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why no area can be created or edited at all, or null when one can.</summary>
	public string? WhyReadOnly => _mover.WhyReadOnly;

	/// <summary>Why area <paramref name="area"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int area) => _mover.WhyFixedArea(area);

	/// <summary>What keeps <paramref name="area"/> from being deleted; <see cref="AreaDeleteBlockers.None"/> when nothing does.</summary>
	public AreaDeleteBlockers BlockersOf(int area) {
		if (WhyFixed(area) is { } reason) {
			return AreaDeleteBlockers.Because(reason);
		}

		var source = _source!;
		var namers = TriggerAreaEditing.Namers(source, GuidOf(area));
		return new AreaDeleteBlockers(null, _deleter.Resolve(source, new MsnNamers([], namers, [])));
	}

	/// <summary>Asks for <paramref name="area"/> to be a box (<paramref name="circle"/> false) or a circle.</summary>
	public void RequestShape(int area, bool circle) {
		short guid = GuidOf(area);
		_requested = () => WhyFixed(area)
			?? Commit(() => TriggerAreaEditing.SetShape(_source!, guid, circle ? TriggerAreaEditing.Circle : TriggerAreaEditing.Box), guid);
	}

	/// <summary>Asks for <paramref name="area"/> to be deleted.</summary>
	public void RequestDelete(int area) {
		short guid = GuidOf(area);
		_requested = () => {
			var blockers = BlockersOf(area);
			if (!blockers.None) {
				return blockers.Fixed ?? "An action references it; its view says which.";
			}

			return Commit(() => TriggerAreaEditing.Delete(_source!, guid), -1);
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
		if (WhyReadOnly is { } readOnly) {
			return readOnly;
		}

		if (corner.X == opposite.X || corner.Y == opposite.Y) {
			return null;
		}

		short area = -1;
		return Commit(() => TriggerAreaEditing.CreateBox(_source!, (corner.X, corner.Y), (opposite.X, opposite.Y), out area), () => area);
	}

	/// <summary>
	/// Adds a circle of <paramref name="radius"/> world units about <paramref name="centre"/> and selects it; a radius of 0
	/// adds nothing. Answers why it could not, or null.
	/// </summary>
	public string? CreateCircle(Vec3i centre, int radius) {
		if (WhyReadOnly is { } readOnly) {
			return readOnly;
		}

		if (radius <= 0) {
			return null;
		}

		short area = -1;
		return Commit(() => TriggerAreaEditing.CreateCircle(_source!, (centre.X, centre.Y), radius, out area), () => area);
	}

	// The row-9 GUID of block-4 area <area>, or -1.
	private short GuidOf(int area) => _source?.GuidOf(AreaBlock, area) ?? -1;

	private string? Commit(Func<MsnEdit> edit, short area) => Commit(edit, () => area);

	// Makes the change, rebuilding from it, records it as one step of the history and selects the area of row-9 GUID
	// <area> where it is exported now; an area that is gone leaves the selection to the rebuild.
	private string? Commit(Func<MsnEdit> edit, Func<short> area) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		if (area() is not -1 and var guid && _source!.ExportedIndex(AreaRow, guid) is { } exported && _index.Area(exported) != null) {
			_selection.Current = new AreaSelection(exported);
		}

		return null;
	}
}
