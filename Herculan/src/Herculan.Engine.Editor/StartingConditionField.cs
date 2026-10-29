using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// An object's Starting condition in its view, from its GUID's base, set through <see cref="StartingConditionEditor"/> as one
/// undo step: a structure's or ground vehicle's as a percentage of every part's health
/// (docs/retail/simulation/structure-behaviour.md#starting-condition), a herc's as a choice of the bands the game damages it
/// by (docs/retail/simulation/component-damage.md#starting-condition--mech_applystartingcondition-004178e8). Then one sentence
/// for each other version of the mission that states another value, which is how the campaign carries a structure's
/// destruction into a later mission (docs/retail/campaign-continuity.md). A flyer's record has none.
/// </summary>
internal sealed class StartingConditionField {
	/// <summary>
	/// A herc band: the lowest value in it, what it writes (the editor's choice of a value inside it; 100 and 50 are the only
	/// two retail records state), its label and what it does.
	/// </summary>
	private sealed record Band(short Lowest, short Written, string Label, string Effect);

	// Highest first; a negative value reads as undamaged, as the game reads it.
	private static readonly Band[] HercBands = [
		new(80, 100, "Undamaged", "It starts undamaged."),
		new(60, 70, "Lightly damaged", "It starts with light damage spread at random over its parts."),
		new(40, 50, "Damaged", "It starts with damage spread at random over its parts."),
		new(20, 30, "Badly damaged", "It starts with its reactor destroyed and heavy damage spread at random over its parts. It "
			+ "leaves no salvage."),
		new(short.MinValue, 0, "Wreck", "It starts as a wreck: down with a leg destroyed, it can't move or be targeted. It leaves "
			+ "no salvage."),
	];

	private readonly StartingConditionEditor _editor;

	// A percentage being typed, and the GUID of the structure it is for, until the field lets go.
	private short? _draftFor;
	private int _draft;

	public StartingConditionField(StartingConditionEditor editor) {
		_editor = editor;
	}

	/// <summary>Draws <paramref name="key"/>'s starting condition; nothing for a flyer or a record the layout no longer has.</summary>
	public void Draw(ObjectKey key) {
		var source = _editor.Source;
		if (!StartingConditions.Carries(key.Row) || source.Layout.Entry(key.Row, key.Guid) == null) {
			return;
		}

		short current = StartingConditions.Current(source, key.Row, key.Guid);
		string? why = _editor.WhyFixed(key);
		ImGui.PushID("startingCondition");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Starting condition:");
		ImGui.SameLine();
		ImGui.BeginDisabled(why != null);
		if (key.Row == 12) {
			DrawHerc(key, current);
		} else {
			DrawStructure(key, current);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		DrawOverrides(source, key);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	// The bands as a choice, what the chosen one does, and the record's own value when it is not the one the band writes.
	private void DrawHerc(ObjectKey key, short current) {
		var band = BandOf(current);
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##band", band.Label)) {
			foreach (var option in HercBands) {
				if (ImGui.Selectable(option.Label, option == band) && option != band) {
					_editor.Request(key, option.Written);
				}
			}

			ImGui.EndCombo();
		}

		EditorText.Note(current == band.Written ? band.Effect : $"{band.Effect} Its record states {current}%.");
	}

	// The percentage, typed, applied when the field lets go, and what it does: on the field's hover when undamaged, else as a note.
	private void DrawStructure(ObjectKey key, short current) {
		if (_draftFor != null && _draftFor != key.Guid && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == key.Guid ? _draft : current;
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(56f));
		if (ImGui.InputInt("##percent", ref shown, 0, 0)) {
			_draftFor = key.Guid;
			_draft = shown;
		}

		bool undamaged = current is < 0 or >= 100;
		EditorText.Tooltip(undamaged ? "It starts undamaged. 0% places it already collapsed." : null);
		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == key.Guid) {
			_draftFor = null;
			short value = (short)Math.Clamp(_draft, 0, 100);
			if (value != current) {
				_editor.Request(key, value);
			}
		}

		ImGui.SameLine();
		ImGui.Text("%");
		if (undamaged) {
			return;
		}

		EditorText.Note(current switch {
			0 => "It starts already collapsed, showing its wreck where its type has one. Starting that way fires none of its "
				+ "triggers and sets no campaign variables.",
			_ => $"Each of its parts starts {100 - current}% damaged.",
		});
	}

	// One sentence per other version that states another value, from the records of the GUID under other conditions.
	private static void DrawOverrides(MissionDocument source, ObjectKey key) {
		var overrides = StartingConditions.Overrides(source, key.Row, key.Guid);
		if (overrides.Count == 0) {
			return;
		}

		foreach (var set in overrides) {
			var rule = set.Condition == RecordConditions.Always ? PresenceRule.Always : PresenceRules.ReadCondition(source, set.Condition).Rule;
			EditorText.Wrapped($"It starts {Describe(key.Row, set.Value)} {PresenceSentences.Occasion(rule)}.");
		}

		EditorText.Note("A change here does not change it in those versions.");
	}

	// "collapsed (0%)", "at 60%", or for a herc its band: "\"Damaged\" (50%)".
	private static string Describe(int row, short value) => row == 12
		? $"\"{BandOf(value).Label}\" ({value}%)"
		: value switch {
			0 => "collapsed (0%)",
			< 0 or >= 100 => $"undamaged ({value}%)",
			_ => $"at {value}%",
		};

	private static Band BandOf(short value) => value < 0 ? HercBands[0] : HercBands.First(band => value >= band.Lowest);
}
