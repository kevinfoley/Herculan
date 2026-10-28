using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A dropdown that picks a campaign variable, with a box at its top that filters it as the user types: the variables the game
/// sets, by what they hold (<see cref="CampaignVariableLabels"/>); the variables the debrief reads after the mission, by what
/// it does with them, each with what in this mission touches it; every other variable this mission's <c>.MSN</c> touches,
/// each with what touches it (<see cref="CampaignVariableUses"/>); and any variable by its number, typed in the filter box,
/// for one another mission sets or tests. Nothing is looked up in other missions (the user's choice,
/// docs/herculan/handoff-editor-conditions.md#present-when). For a write (<see cref="CampaignVariableWrites"/>) the
/// debrief's variables come first and those the game sets last, each named by what a write to it amounts to
/// (<see cref="CampaignVariableLabels.WriteLabel"/>). One picker serves every field of a view, since only one dropdown is
/// open at a time.
/// </summary>
internal sealed class CampaignVariablePicker {
	// In pixels on a 100% display (ScaledImGui.Scaled): the open list's greatest height.
	private const float ListHeight = 420f;

	private string _filter = "";

	/// <summary>
	/// Draws the dropdown <paramref name="id"/>, <paramref name="width"/> wide as ImGui reads an item width (by default the
	/// rest of the line), showing <paramref name="current"/> by its name, or asking for one when null; answers the variable
	/// chosen this frame, or null. <paramref name="forWrite"/> lists and names the variables for a write in place of a test.
	/// </summary>
	public short? Draw(string id, short? current, MissionDocument source, bool forWrite = false, float width = -1f) {
		ImGui.SetNextItemWidth(width);
		float listWidth = ImGui.GetContentRegionAvail().X;
		ImGui.SetNextWindowSizeConstraints(new Vector2(listWidth, 0f), new Vector2(float.MaxValue, ScaledImGui.Scaled(ListHeight)));
		string shown = current is not { } now ? "Choose a campaign variable"
			: forWrite ? CampaignVariableLabels.WriteName(now)
			: CampaignVariableLabels.Name(now);
		if (!EditorText.BeginCombo(id, shown)) {
			return null;
		}

		if (ImGui.IsWindowAppearing()) {
			_filter = "";
			ImGui.SetKeyboardFocusHere();
		}

		ImGui.SetNextItemWidth(-1f);
		ImGui.InputTextWithHint("##filter", "Type a name or a number", ref _filter, 64);
		short? typed = short.TryParse(_filter.Trim(), out short number) && number is >= 0 and < ConditionEditing.Flags
			? number
			: null;

		var uses = CampaignVariableUses.Of(source.Mission);
		short? chosen;
		if (forWrite) {
			chosen = DrawReadAfter(uses, current);
			chosen = DrawUsed(uses, current) ?? chosen;
			chosen = DrawSet(current, forWrite) ?? chosen;
		} else {
			chosen = DrawSet(current, forWrite);
			chosen = DrawReadAfter(uses, current) ?? chosen;
			chosen = DrawUsed(uses, current) ?? chosen;
		}

		ImGui.SeparatorText("Any campaign variable");
		if (typed is { } variable) {
			if (ImGui.Selectable($"Campaign variable {variable}", variable == current)) {
				chosen = variable;
			}
		} else {
			EditorText.Note($"Type its number, 0 to {ConditionEditing.Flags - 1}, in the box above: for one another mission "
				+ (forWrite ? "tests." : "sets."));
		}

		ImGui.EndCombo();
		return chosen;
	}

	// The variables the game sets, by what they hold, or for a write by what a write to them amounts to.
	private short? DrawSet(short? current, bool forWrite) {
		ImGui.SeparatorText("Set by the game");
		short? chosen = null;
		bool any = false;
		foreach (short flag in CampaignVariableLabels.Set) {
			string label = (forWrite ? CampaignVariableLabels.WriteLabel(flag) : null) ?? CampaignVariableLabels.Label(flag)!;
			if (!Matches(label)) {
				continue;
			}

			any = true;
			if (ImGui.Selectable(label, flag == current)) {
				chosen = flag;
			}
		}

		None(any);
		return chosen;
	}

	// The variables the debrief reads after the mission, by what it does with them, and what in this mission touches each.
	private short? DrawReadAfter(IReadOnlyList<CampaignVariableUse> uses, short? current) {
		ImGui.SeparatorText("Read by the game after the mission");
		short? chosen = null;
		bool any = false;
		foreach (short flag in CampaignVariableLabels.ReadAfter) {
			string label = $"Campaign variable {flag}: {CampaignVariableLabels.Effect(flag)}";
			if (uses.FirstOrDefault(use => use.Variable == flag) is { } use) {
				label += $". In this mission: {Describe(use)}";
			}

			if (!Matches(label)) {
				continue;
			}

			any = true;
			if (ImGui.Selectable(label, flag == current)) {
				chosen = flag;
			}
		}

		None(any);
		return chosen;
	}

	// Every other variable this mission touches, with what touches it.
	private short? DrawUsed(IReadOnlyList<CampaignVariableUse> uses, short? current) {
		ImGui.SeparatorText("Used in this mission");
		short? chosen = null;
		bool any = false;
		foreach (var use in uses.Where(use => CampaignVariableLabels.Label(use.Variable) == null
				&& CampaignVariableLabels.Effect(use.Variable) == null)) {
			string label = $"Campaign variable {use.Variable}: {Describe(use)}";
			if (!Matches(label)) {
				continue;
			}

			any = true;
			if (ImGui.Selectable(label, use.Variable == current)) {
				chosen = use.Variable;
			}
		}

		None(any);
		return chosen;
	}

	// Whether <label> holds what is typed in the filter, any case; a number matches "Campaign variable <number>:" only, not
	// any label with that digit in it.
	private bool Matches(string label) {
		string filter = _filter.Trim();
		return filter.Length == 0 || (short.TryParse(filter, out _)
			? label.StartsWith($"Campaign variable {filter}:", StringComparison.Ordinal)
			: label.Contains(filter, StringComparison.OrdinalIgnoreCase));
	}

	private static void None(bool any) {
		if (!any) {
			EditorText.Note("None.");
		}
	}

	/// <summary>What touches a variable, short: "2 triggers add to it, 3 records test it".</summary>
	public static string Describe(CampaignVariableUse use) =>
		string.Join(", ", use.Counts.OrderBy(count => count.Key).Select(count => count.Value == 1
			? $"1 {Words(count.Key).One}"
			: $"{count.Value} {Words(count.Key).Many}"));

	private static (string One, string Many) Words(VariableUse use) => use switch {
		VariableUse.Tested => ("record tests it", "records test it"),
		VariableUse.TriggerAdds => ("trigger adds to it", "triggers add to it"),
		VariableUse.TriggerClears => ("trigger clears it", "triggers clear it"),
		VariableUse.TriggerNames => ("trigger references it", "triggers reference it"),
		VariableUse.ObjectiveSets => ("objective sets it", "objectives set it"),
		VariableUse.ObjectiveClears => ("objective clears it", "objectives clear it"),
		VariableUse.ObjectiveAdds => ("objective adds to it", "objectives add to it"),
		VariableUse.ObjectiveSubtracts => ("objective subtracts from it", "objectives subtract from it"),
		VariableUse.ObjectiveNames => ("objective references it", "objectives reference it"),
		VariableUse.OutOfFight => ("record sets it when destroyed or immobilized", "records set it when destroyed or immobilized"),
		VariableUse.HeaderClears => ("mission setting clears it as the mission loads", "mission settings clear it as the mission loads"),
		_ => ("record references it", "records reference it"),
	};
}
