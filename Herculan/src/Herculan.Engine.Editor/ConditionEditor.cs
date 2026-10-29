using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One record a condition's Used by list shows: a link when <see cref="Target"/> has a view, plain text otherwise.</summary>
internal sealed record ConditionUse(string Label, EditorSelection? Target);

/// <summary>
/// What references a condition (<see cref="ConditionEditor.UsesOf"/>): each record by its identity, one per line, and how many
/// templates, which no state places, records copying from them.
/// </summary>
internal sealed record ConditionUses(IReadOnlyList<ConditionUse> Shown, int Templates) {
	public bool Any => Shown.Count > 0 || Templates > 0;
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
	private readonly MissionIndex _index;
	private readonly MissionNames _names;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	/// <param name="names">What the Used by list names the records it lists by.</param>
	public ConditionEditor(MissionDocument source, MissionIndex index, MissionNames names, PointMover mover, SceneSync sync,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_names = names;
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
	/// its identity from the layout, a link where it has a view: a route, trigger area, action, timer, group or objective
	/// (<see cref="Selectable"/>), a herc, flyer, structure or ground vehicle when the scene has its object
	/// (<see cref="Object"/>); a point, a heading and the rows the editor has no view for named by what they do.
	/// </summary>
	public ConditionUses UsesOf(short guid) {
		var source = _source;
		var namers = ConditionEditing.Namers(source, guid);
		var shown = new List<ConditionUse>();
		var orders = new List<MsnRecordId>();
		int templates = 0;

		// The records with no view, counted by row.
		var unseen = new Dictionary<int, int>();
		void Unseen(int row) => unseen[row] = unseen.GetValueOrDefault(row) + 1;

		foreach (var record in namers.Records) {
			if (record.Row is >= 6 and <= 16 && source.RecordAt(record) is { GUID: -1 }) {
				templates++;
				continue;
			}

			switch (record.Row) {
				case 1:
					short condition = ConditionEditing.Records(source)[record.Index].GUID;
					shown.Add(new ConditionUse($"Condition {condition}", new ConditionSelection(condition)));
					continue;
				case 4 when record.Index == MissionTextEditing.PackageRecord(source):
					// The text package the Mission Settings window's lines edit, which is not a selection.
					shown.Add(new ConditionUse("The mission's objective, briefing and intelligence lines (Edit > Mission Settings)", null));
					continue;
				case 2 or 3 or 4 or 5 or 6 or 7:
					Unseen(record.Row);
					continue;
				case 15:
					orders.Add(record);
					continue;
				case 12 or 13 or 14 when source.RecordAt(record) is { } roster:
					shown.Add(Object(record.Row, roster.GUID));
					continue;
			}

			if (Selectable(source, record) is { } use) {
				shown.Add(use);
			}
		}

		foreach (int row in unseen.Keys.Order()) {
			shown.Add(NoView(row, unseen[row]));
		}

		var resolved = _deleter.Resolve(source, new MsnNamers([], [], orders));
		foreach (var order in resolved.Orders) {
			var group = _names.GroupBrief(order.Group);
			shown.Add(new ConditionUse($"{MissionText.Ordinal(order.Slot + 1)} order of {group.Full}", new GroupSelection(order.Group)));
		}

		if (resolved.Templates > 0) {
			shown.Add(new ConditionUse(resolved.Templates == 1 ? "1 template of a random table"
				: $"{resolved.Templates} templates of random tables", null));
		}

		if (resolved.UnrunOrders > 0) {
			shown.Add(new ConditionUse(resolved.UnrunOrders == 1 ? "1 order no group runs"
				: $"{resolved.UnrunOrders} orders no group runs", null));
		}

		var strings = source.Text?.Strings ?? [];
		foreach (short id in namers.Text.Select(record => strings[record].Guid).Distinct()) {
			shown.Add(new ConditionUse($"Text line {id}", null));
		}

		return new ConditionUses(shown.DistinctBy(use => (use.Label, use.Target)).ToList(), templates);
	}

	/// <summary>
	/// <paramref name="count"/> records of row <paramref name="row"/>, one the editor has no view for, by what they do in the
	/// mission (docs/retail/formats/msn-mission-file.md): a header patch sets the mission's settings, its zone among them, and
	/// clears the campaign variables it lists as the mission loads; a row-3 value is a briefing's or a debrief's movie; a text
	/// package other than the one the Mission Settings window edits is another version of the mission's lines and briefing movie; a
	/// row-5 record a version of the debrief; then a point and a heading.
	/// </summary>
	private static ConditionUse NoView(int row, int count) {
		string what = (row, count) switch {
			(2, 1) => "Mission settings, such as the zone",
			(2, _) => $"{count} sets of mission settings, such as the zone",
			(3, 1) => "A briefing or debrief movie",
			(3, _) => $"{count} briefing or debrief movies",
			(4, 1) => "Another version of the mission's lines and briefing movie",
			(4, _) => $"{count} other versions of the mission's lines and briefing movie",
			(5, 1) => "The debrief: its lines and movie",
			(5, _) => $"{count} versions of the debrief: lines and movie",
			(6, 1) => "A position",
			(6, _) => $"{count} positions",
			(7, 1) => "A heading",
			_ => $"{count} headings",
		};

		return new ConditionUse($"{what} (not shown in the editor)", null);
	}

	// A route, trigger area, action, timer, group or objective <record> as a link selecting it by its GUID or row-17 index,
	// named from the layout (MissionNames). Null for a record of any other row.
	private ConditionUse? Selectable(MissionDocument source, MsnRecordId record) {
		if (record.Row == 17) {
			return new ConditionUse(_names.Objective(record.Index).Full, new ObjectiveSelection(record.Index));
		}

		if (record.Row is not (8 or 9 or 10 or 11 or 16) || source.RecordAt(record) is not { } found) {
			return null;
		}

		short guid = found.GUID;
		return record.Row switch {
			8 => new ConditionUse(_names.Route(guid).Full, new RouteSelection(guid)),
			9 => new ConditionUse(_names.Area(guid).Full, new AreaSelection(guid)),
			10 => new ConditionUse(_names.Trigger(guid).Full, new ActionSelection(guid)),
			11 => new ConditionUse(_names.Countdown(guid).Full, new TimerSelection(guid)),
			_ => new ConditionUse(_names.GroupBrief(guid).Full, new GroupSelection(guid)),
		};
	}

	// The row-<row> roster record of GUID <guid> by its name (MissionNames.Object), a link when the scene has its object.
	private ConditionUse Object(int row, short guid) {
		var key = new ObjectKey(row, guid);
		var target = _index.ObjectOf(key) != null ? new ObjectSelection(row, guid) : null;
		return new ConditionUse(_names.Object(key).Full, target);
	}

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
