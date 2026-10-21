using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A dropdown that picks a campaign variable, with a box at its top that filters it as the user types: the variables the game
/// sets, by what they hold (<see cref="CampaignVariableLabels"/>); every other variable this mission's <c>.MSN</c> touches,
/// each with what touches it (<see cref="CampaignVariableUses"/>); and any variable by its number, typed in the filter box,
/// for one another mission sets. Nothing is looked up in other missions (the user's choice,
/// docs/herculan/handoff-editor-conditions.md#present-when). One picker serves every field of a view, since only one
/// dropdown is open at a time.
/// </summary>
internal sealed class CampaignVariablePicker {
	// In pixels on a 100% display (ScaledImGui.Scaled): the open list's greatest height.
	private const float ListHeight = 420f;

	private string _filter = "";

	/// <summary>
	/// Draws the dropdown <paramref name="id"/>, the full width, showing <paramref name="current"/> by its name, or asking for
	/// one when null; answers the variable chosen this frame, or null.
	/// </summary>
	public short? Draw(string id, short? current, MissionDocument source) {
		ImGui.SetNextItemWidth(-1f);
		float width = ImGui.GetContentRegionAvail().X;
		ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(float.MaxValue, ScaledImGui.Scaled(ListHeight)));
		string shown = current is { } now ? CampaignVariableLabels.Name(now) : "Choose a campaign variable";
		if (!ImGui.BeginCombo(id, shown)) {
			return null;
		}

		if (ImGui.IsWindowAppearing()) {
			_filter = "";
			ImGui.SetKeyboardFocusHere();
		}

		short? chosen = null;
		ImGui.SetNextItemWidth(-1f);
		ImGui.InputTextWithHint("##filter", "Type a name or a number", ref _filter, 64);
		short? typed = short.TryParse(_filter.Trim(), out short number) && number is >= 0 and < ConditionEditing.Flags
			? number
			: null;

		ImGui.SeparatorText("Set by the game");
		bool any = false;
		foreach (short flag in CampaignVariableLabels.Set) {
			string label = CampaignVariableLabels.Label(flag)!;
			if (Matches(label) && ImGui.Selectable(label, flag == current)) {
				chosen = flag;
			}

			any |= Matches(label);
		}

		None(any);
		ImGui.SeparatorText("Used in this mission");
		any = false;
		foreach (var use in CampaignVariableUses.Of(source.Mission).Where(use => CampaignVariableLabels.Label(use.Variable) == null)) {
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
		ImGui.SeparatorText("Any campaign variable");
		if (typed is { } variable) {
			if (ImGui.Selectable($"Campaign variable {variable}", variable == current)) {
				chosen = variable;
			}
		} else {
			EditorText.Note($"Type its number, 0 to {ConditionEditing.Flags - 1}, in the box above: for one another mission sets.");
		}

		ImGui.EndCombo();
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
		VariableUse.ObjectiveSubtracts => ("objective takes from it", "objectives take from it"),
		VariableUse.ObjectiveNames => ("objective references it", "objectives reference it"),
		VariableUse.OutOfFight => ("record writes it when out of the fight", "records write it when out of the fight"),
		VariableUse.HeaderClears => ("mission setting clears it as the mission loads", "mission settings clear it as the mission loads"),
		_ => ("record references it", "records reference it"),
	};
}
