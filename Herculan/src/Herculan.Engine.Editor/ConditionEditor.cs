using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One record a condition's Used by list shows: a link when <see cref="Target"/> has a view, plain text otherwise.</summary>
internal sealed record ConditionUse(string Label, EditorSelection? Target);

/// <summary>
/// What references a condition (<see cref="ConditionEditor.UsesOf"/>): the records the editor can select, one per line, how
/// many more the editor's state leaves out, by kind, and how many variant alternatives, which no state places, records
/// copying from them.
/// </summary>
internal sealed record ConditionUses(IReadOnlyList<ConditionUse> Shown, IReadOnlyList<(string Kind, int Count)> LeftOut,
		int Alternatives) {
	public bool Any => Shown.Count > 0 || LeftOut.Count > 0 || Alternatives > 0;
}

/// <summary>
/// Creates, edits and deletes the open mission's conditions, row 1, each edit one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="ConditionEditing"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's
/// state again (<see cref="SceneSync"/>), as a condition conditions records in and out of the generation.
///
/// <para>The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class ConditionEditor {
	private readonly MissionDocument _source;
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public ConditionEditor(MissionDocument source, GeneratedIndex index, PointMover mover, SceneSync sync,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose row 1 the views read.</summary>
	public MissionDocument Source => _source;

	/// <summary>Row 1's records in file order.</summary>
	public IReadOnlyList<MissionCondition14> Listed => ConditionEditing.Records(_source);

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="guid"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short guid) => ConditionEditing.WhyFixed(_source, guid);

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
	/// What references <paramref name="guid"/>, whatever its condition (<see cref="ConditionEditing.Namers"/>): each record by
	/// its identity, a link where it has a view, a route, trigger area, action, timer, group or objective whether or not the
	/// editor's state holds it (<see cref="Selectable"/>), and the rest the state does not hold counted by kind.
	/// </summary>
	public ConditionUses UsesOf(short guid) {
		var source = _source;
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

			if (Selectable(source, record) is { } use) {
				shown.Add(use);
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
			shown.Add(new ConditionUse($"{PanelLinks.ShortGroupName(order.Group)}, order {order.Slot}", Records.GroupAt(order.Group)));
		}

		for (int i = 0; i < resolved.Elsewhere; i++) {
			Leave("orders");
		}

		var strings = source.Text?.Strings ?? [];
		foreach (short id in namers.Text.Select(record => strings[record].Guid).Distinct()) {
			shown.Add(new ConditionUse($"Text line {id}", null));
		}

		return new ConditionUses(shown.DistinctBy(use => (use.Label, use.Target)).ToList(),
			leftOut.Select(pair => (pair.Key, pair.Value)).ToList(), alternatives);
	}

	/// <summary>A record the editor's state exports at <paramref name="index"/> of the block row <paramref name="row"/> is written to, by its identity.</summary>
	public ConditionUse Identity(int row, int index) => row switch {
		6 => new ConditionUse($"Point {index}", null),
		7 => new ConditionUse($"Heading {index}", null),
		8 => new ConditionUse($"Route {index}", Records.RouteAt(index)),
		9 => new ConditionUse($"Area {index}", Records.AreaAt(index)),
		10 => new ConditionUse($"Action {index}", Records.ActionAt(index)),
		11 => new ConditionUse($"Timer {index}", Records.TimerAt(index)),
		12 or 13 or 14 => Object(row, index),
		16 => new ConditionUse(PanelLinks.ShortGroupName(index), Records.GroupAt(index)),
		_ => new ConditionUse($"Objective {index}", index < _index.Mission.Objectives.Count ? Records.ObjectiveAt(index) : null),
	};

	private GeneratedRecords Records => _index.Records;

	// A route, trigger area, action, timer, group or objective <record> as a link selecting it by its GUID or row-17 index:
	// by its generated identity where the editor's state holds it, else by its kind, its view then saying it is not shown in
	// the editor. Null for a record of any other row.
	private ConditionUse? Selectable(MissionDocument source, MsnRecordId record) {
		if (record.Row == 17) {
			var objective = new ObjectiveSelection(record.Index);
			return Records.Objective(record.Index) is { } shown
				? new ConditionUse($"Objective {shown}", objective)
				: new ConditionUse("Objective (not shown in the editor)", objective);
		}

		if (record.Row is not (8 or 9 or 10 or 11 or 16) || source.RecordAt(record) is not { } found) {
			return null;
		}

		short guid = found.GUID;
		if (source.ExportedIndex(record.Row, guid) is { } index) {
			return Identity(record.Row, index);
		}

		EditorSelection target = record.Row switch {
			8 => new RouteSelection(guid),
			9 => new AreaSelection(guid),
			10 => new ActionSelection(guid),
			11 => new TimerSelection(guid),
			_ => new GroupSelection(guid),
		};
		string noun = record.Row switch { 8 => "Route", 9 => "Area", 10 => "Action", 11 => "Timer", _ => "Group" };
		return new ConditionUse(record.Row == 16 ? "Group (absent from the mission as opened)" : $"{noun} (not shown in the editor)", target);
	}

	private ConditionUse Object(int row, int slot) {
		var kind = row switch { 12 => MissionUnitKind.Mech, 13 => MissionUnitKind.Flyer, _ => MissionUnitKind.Base };
		string noun = row switch { 12 => "Herc", 13 => "Flyer", _ => "Structure" };
		if (_index.ObjectAt(kind, slot) is not { } placed) {
			return new ConditionUse($"{noun} slot {slot} (not placed)", null);
		}

		if (placed.Object is Sim.BaseObject { Type.IsVehicle: true }) {
			noun = "Ground vehicle";
		}

		return new ConditionUse($"{noun} slot {slot}", _index.Scene.SelectionOf(placed));
	}

	// What a record of <row> is, in the plural, for the count of those the editor's state leaves out.
	private static string KindOf(int row) => row switch {
		6 => "points",
		7 => "headings",
		12 => "hercs",
		13 => "flyers",
		_ => "structures",
	};

	private delegate MsnEdit Creating(MissionDocument source, out short guid);

	private void Create(Creating create) {
		_requested = () => {
			short created = -1;
			return Commit(() => create(_source, out created), () => created);
		};
	}

	private void Ask(short guid, Func<MissionDocument, MsnEdit> edit) =>
		_requested = () => WhyFixed(guid) ?? Commit(() => edit(_source), () => guid);

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the condition of row-1 GUID
	// <selected> while row 1 still carries it.
	private string? Commit(Func<MsnEdit> edit, Func<short> selected) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		short guid = selected();
		_selection.Current = guid != -1 && ConditionEditing.IndexOf(_source, guid) != null ? new ConditionSelection(guid) : null;
		return null;
	}
}
