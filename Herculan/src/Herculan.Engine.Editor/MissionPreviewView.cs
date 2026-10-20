using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Mission view's Preview (<see cref="MissionPreview"/>): what the preview is, each campaign flag the mission tests by
/// its label first, as a typed value field applied when it lets go, with links to the conditions testing it; what the random
/// draws show, Simulate a mission load and Back to first options, and how many variant tables show a chosen alternative.
/// Drawn for a mission held as its <c>.MSN</c> alone. The UI calls a flag a campaign variable.
/// </summary>
internal sealed class MissionPreviewView {
	private readonly MissionPreview _preview;
	private readonly PanelLinks _links;

	// A variable being typed and its value so far, until its field lets go.
	private int? _draftFlag;
	private int _draft;

	public MissionPreviewView(MissionPreview preview, PanelLinks links) {
		_preview = preview;
		_links = links;
	}

	public void Draw() {
		if (!_preview.Shown) {
			return;
		}

		ImGui.Separator();
		ImGui.Text("Preview");
		EditorText.Note("The editor shows one version of this mission. In play, campaign variables come from earlier missions "
			+ "and random choices are picked at every load.");
		EditorText.Note(_preview.Training
			? "Loaded as practice or INSTANT ACTION: every campaign variable starts at 0."
			: "Loaded at its place in the campaign, as at a career with no history: a campaign variable an earlier mission sets "
				+ "reads 0 until set here.");
		EditorText.Note("Not saved with the mission, and not undone.");

		var flags = _preview.TestedFlags;
		if (flags.Count == 0) {
			EditorText.Note("The mission tests no campaign variable.");
		} else {
			ImGui.PushID("variables");
			foreach (int flag in flags) {
				DrawFlag(flag);
			}

			ImGui.PopID();
		}

		if (_preview.TestsDrawnFlag) {
			EditorText.Note("It also tests campaign variables 4 to 6, random numbers every load picks from 0 to 11, each 0 on "
				+ "first options.");
		}

		ImGui.Spacing();
		ImGui.TextWrapped(_preview.Simulated
			? "Random choices: as one simulated mission load picked them."
			: "Random choices: each on its first option.");
		string? why = _preview.WhyNoReopen;
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Simulate a mission load")) {
			_preview.RequestSimulate();
		}

		ImGui.SameLine();
		ImGui.BeginDisabled(!_preview.Simulated && _preview.ChosenTables == 0);
		if (ImGui.Button("Back to first options")) {
			_preview.RequestFirstOptions();
		}

		ImGui.EndDisabled();
		ImGui.EndDisabled();
		int chosen = _preview.ChosenTables;
		if (chosen > 0) {
			ImGui.SameLine();
			EditorText.Note(chosen == 1
				? "1 random choice shows the option picked in its own view."
				: $"{chosen} random choices show the option picked in their own view.");
		}

		EditorText.Reason(why);
		EditorText.Note("Simulate a mission load picks every random choice as the game does at each load. Either button keeps "
			+ "the campaign variables set here.");
		EditorText.Reason(_preview.Failure);
	}

	// One variable: its label, then its field and the conditions testing it, as links.
	private void DrawFlag(int flag) {
		// Another variable's draft is dropped once nothing is being typed.
		if (_draftFlag != null && _draftFlag != flag && !ImGui.IsAnyItemActive()) {
			_draftFlag = null;
		}

		ImGui.PushID(flag);
		ImGui.Spacing();
		ImGui.TextWrapped(MissionPreview.Label(flag) is { } label ? $"{label} (variable {flag})" : $"Campaign variable {flag}");
		int value = _draftFlag == flag ? _draft : _preview.Flag(flag);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(90f));
		if (ImGui.InputInt("##value", ref value, 0, 0)) {
			_draftFlag = flag;
			_draft = Math.Clamp(value, short.MinValue, short.MaxValue);
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFlag == flag) {
			_draftFlag = null;
			_preview.RequestFlag(flag, (short)_draft);
		}

		ImGui.SameLine();
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Tested by:");
		float right = ImGui.GetWindowContentRegionMax().X;
		foreach (var test in _preview.TestsOf(flag)) {
			string op = ConditionEditing.OperatorText(test.OperatorOrRangeUpperOrResult) ?? $"code {test.OperatorOrRangeUpperOrResult}";
			string link = $"Condition {test.GUID} ({op} {test.ComparisonOperand})";
			ImGui.SameLine();
			if (ImGui.GetCursorPosX() + ImGui.CalcTextSize(link).X > right) {
				ImGui.NewLine();
			}

			ImGui.PushID(test.GUID);
			_links.Link(link, new ConditionSelection(test.GUID));
			ImGui.PopID();
		}

		ImGui.PopID();
	}
}
