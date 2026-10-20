using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One record a condition's Used by list shows: a link when <see cref="Target"/> has a view, plain text otherwise.</summary>
internal sealed record ConditionUse(string Label, EditorSelection? Target);

/// <summary>
/// What references a condition (<see cref="ConditionEditor.UsesOf"/>): the records the editor's state holds, one per line, how
/// many more the state leaves out, by kind, and how many variant alternatives, which no state places, records copying from them.
/// </summary>
internal sealed record ConditionUses(IReadOnlyList<ConditionUse> Shown, IReadOnlyList<(string Kind, int Count)> LeftOut,
		int Alternatives) {
	public bool Any => Shown.Count > 0 || LeftOut.Count > 0 || Alternatives > 0;
}

/// <summary>
/// Creates, edits and deletes the open mission's conditions, row 1, each edit one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="ConditionEditing"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's
/// state again (<see cref="MissionRebuild"/>), as a condition conditions records in and out of the generation, and the
/// selection keeps a condition by its row-1 GUID, which is its identity in the editor: row 1 reaches no <c>script.dat</c>
/// block.
///
/// <para>The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>). A mission opened from a plain
/// <c>script.dat</c> has no conditions to show (<see cref="Listed"/>) and edits nothing.</para>
/// </summary>
internal sealed class ConditionEditor {
	private readonly MissionDocument? _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public ConditionEditor(MissionDocument? source, MissionIndex index, PointMover mover, MissionRebuild rebuild,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose row 1 the views read; null for a plain <c>script.dat</c>.</summary>
	public MissionDocument? Source => _source;

	/// <summary>Row 1's records in file order, or null for a plain <c>script.dat</c>, which has none.</summary>
	public IReadOnlyList<MissionCondition14>? Listed => _source == null ? null : ConditionEditing.Records(_source);

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why no condition can be created at all, or null when one can.</summary>
	public string? WhyReadOnly => _mover.WhyReadOnly;

	/// <summary>Why <paramref name="guid"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short guid) => WhyReadOnly ?? ConditionEditing.WhyFixed(_source!, guid);

	/// <summary>Asks for a new flag test, which is then selected.</summary>
	public void RequestCreateFlagTest() => Create((MissionDocument source, out short guid) => ConditionEditing.CreateFlagTest(source, out guid));

	/// <summary>Asks for a new draw, which is then selected.</summary>
	public void RequestCreateDraw() => Create((MissionDocument source, out short guid) => ConditionEditing.CreateDraw(source, out guid));

	/// <summary>Asks for a new range over draw <paramref name="draw"/>, which is then selected.</summary>
	public void RequestCreateRange(short draw) =>
		Create((MissionDocument source, out short guid) => ConditionEditing.CreateRange(source, draw, out guid));

	/// <summary>Asks for flag test <paramref name="guid"/> to read flag <paramref name="flag"/>.</summary>
	public void RequestFlag(short guid, short flag) => Ask(guid, source => ConditionEditing.SetFlag(source, guid, flag));

	/// <summary>Asks for flag test <paramref name="guid"/> to compare by <paramref name="op"/>.</summary>
	public void RequestOperator(short guid, short op) => Ask(guid, source => ConditionEditing.SetOperator(source, guid, op));

	/// <summary>Asks for flag test <paramref name="guid"/> to compare its flag with <paramref name="value"/>.</summary>
	public void RequestValue(short guid, short value) => Ask(guid, source => ConditionEditing.SetValue(source, guid, value));

	/// <summary>Asks for draw <paramref name="guid"/> to draw below <paramref name="bound"/>.</summary>
	public void RequestBound(short guid, short bound) => Ask(guid, source => ConditionEditing.SetBound(source, guid, bound));

	/// <summary>Asks for range <paramref name="guid"/> to run from <paramref name="low"/> to <paramref name="high"/>.</summary>
	public void RequestEnds(short guid, short low, short high) =>
		Ask(guid, source => ConditionEditing.SetEnds(source, guid, low, high));

	/// <summary>Asks for <paramref name="guid"/> to require <paramref name="required"/>, or nothing for <c>-1</c>; for a range, to be over that draw.</summary>
	public void RequestRequirement(short guid, short required) =>
		Ask(guid, source => ConditionEditing.SetRequirement(source, guid, required));

	/// <summary>Asks for <paramref name="guid"/> to be deleted.</summary>
	public void RequestDelete(short guid) => Ask(guid, source => ConditionEditing.Delete(source, guid));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	/// <summary>
	/// What references <paramref name="guid"/>, whatever its condition (<see cref="ConditionEditing.Namers"/>): each record the
	/// editor's state holds by its identity, a link where it has a view, and the rest counted by kind.
	/// </summary>
	public ConditionUses UsesOf(short guid) {
		if (_source is not { } source) {
			return new ConditionUses([], [], 0);
		}

		var namers = ConditionEditing.Namers(source, guid);
		var shown = new List<ConditionUse>();
		var leftOut = new Dictionary<string, int>();
		void Leave(string kind) => leftOut[kind] = leftOut.GetValueOrDefault(kind) + 1;

		var orders = new List<MsnRecordId>();
		int alternatives = 0;
		foreach (var record in namers.Records) {
			if (record.Row is >= 6 and <= 16 && source.RecordAt(record) is { GUID: -1 }) {
				alternatives++;
				continue;
			}

			switch (record.Row) {
				case 1:
					short condition = ConditionEditing.Records(source)[record.Index].GUID;
					shown.Add(new ConditionUse($"Condition {condition}", new ConditionSelection(condition)));
					continue;
				case 2:
					shown.Add(new ConditionUse($"Header patch {record.Index}", null));
					continue;
				case 3:
					shown.Add(new ConditionUse($"Movie value {source.RecordAt(record)?.GUID}", null));
					continue;
				case 4:
					shown.Add(new ConditionUse($"Text package {record.Index}", null));
					continue;
				case 5:
					shown.Add(new ConditionUse($"Debrief {record.Index}", null));
					continue;
				case 15:
					orders.Add(record);
					continue;
			}

			if (source.Provenance.ExportedSlot(record) is not { } slot) {
				Leave(KindOf(record.Row));
				continue;
			}

			shown.Add(Identity(record.Row, slot.Index));
		}

		var resolved = _deleter.Resolve(source, new MsnNamers([], [], orders));
		foreach (var order in resolved.Orders) {
			shown.Add(new ConditionUse($"{PanelLinks.ShortGroupName(order.Group)}, order {order.Slot}", new GroupSelection(order.Group)));
		}

		for (int i = 0; i < resolved.Elsewhere; i++) {
			Leave("orders");
		}

		var strings = source.Text?.Strings ?? [];
		foreach (short id in namers.Text.Select(record => strings[record].Guid).Distinct()) {
			shown.Add(new ConditionUse($"Text line {id}", null));
		}

		return new ConditionUses(shown.DistinctBy(use => use.Label).ToList(),
			leftOut.Select(pair => (pair.Key, pair.Value)).ToList(), alternatives);
	}

	/// <summary>A record the editor's state exports at <paramref name="index"/> of the block row <paramref name="row"/> is written to, by its identity.</summary>
	public ConditionUse Identity(int row, int index) => row switch {
		6 => new ConditionUse($"Point {index}", null),
		7 => new ConditionUse($"Heading {index}", null),
		8 => new ConditionUse($"Route {index}", new RouteSelection(index)),
		9 => new ConditionUse($"Area {index}", new AreaSelection(index)),
		10 => new ConditionUse($"Action {index}", new ActionSelection(index)),
		11 => new ConditionUse($"Timer {index}", new TimerSelection(index)),
		12 or 13 or 14 => Object(row, index),
		16 => new ConditionUse(PanelLinks.ShortGroupName(index), new GroupSelection(index)),
		_ => new ConditionUse($"Objective {index}", index < _index.Mission.Objectives.Count ? new ObjectiveSelection(index) : null),
	};

	private ConditionUse Object(int row, int slot) {
		var kind = row switch { 12 => MissionUnitKind.Mech, 13 => MissionUnitKind.Flyer, _ => MissionUnitKind.Base };
		string noun = row switch { 12 => "Herc", 13 => "Flyer", _ => "Structure" };
		if (_index.ObjectAt(kind, slot) is not { } placed) {
			return new ConditionUse($"{noun} slot {slot} (not placed)", null);
		}

		if (placed.Object is Sim.BaseObject { Type.IsVehicle: true }) {
			noun = "Ground vehicle";
		}

		return new ConditionUse($"{noun} slot {slot}", new ObjectSelection(placed));
	}

	// What a record of <row> is, in the plural, for the count of those the editor's state leaves out.
	private static string KindOf(int row) => row switch {
		6 => "points",
		7 => "headings",
		8 => "routes",
		9 => "trigger areas",
		10 => "actions",
		11 => "timers",
		12 => "hercs",
		13 => "flyers",
		14 => "structures",
		16 => "groups",
		_ => "objectives",
	};

	private delegate MsnEdit Creating(MissionDocument source, out short guid);

	private void Create(Creating create) {
		_requested = () => {
			if (WhyReadOnly is { } readOnly) {
				return readOnly;
			}

			short created = -1;
			return Commit(() => create(_source!, out created), () => created);
		};
	}

	private void Ask(short guid, Func<MissionDocument, MsnEdit> edit) =>
		_requested = () => WhyFixed(guid) ?? Commit(() => edit(_source!), () => guid);

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the condition of row-1 GUID
	// <selected> while row 1 still carries it.
	private string? Commit(Func<MsnEdit> edit, Func<short> selected) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		short guid = selected();
		_selection.Current = guid != -1 && ConditionEditing.IndexOf(_source!, guid) != null ? new ConditionSelection(guid) : null;
		return null;
	}
}
