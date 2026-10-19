using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The mission view's campaign state (<see cref="CampaignState"/>): each campaign flag the mission tests as a typed value
/// field, applied when it lets go, labelled where the retail docs say what it holds, and Draw again. Drawn for a mission
/// held as its <c>.MSN</c> alone.
/// </summary>
internal sealed class CampaignStateView {
	private readonly CampaignState _state;

	// A flag being typed and its value so far, until its field lets go.
	private int? _draftFlag;
	private int _draft;

	public CampaignStateView(CampaignState state) => _state = state;

	public void Draw() {
		if (!_state.Shown) {
			return;
		}

		ImGui.Separator();
		ImGui.Text("Campaign state");
		EditorText.Note(_state.Training
			? "Loaded as practice or INSTANT ACTION: every flag starts at 0 but flags 3 to 6."
			: "Shown as at a career with no history: every flag an earlier mission writes reads 0 until set here.");
		EditorText.Note("Not saved with the mission, and not undone.");

		var flags = _state.TestedFlags;
		if (flags.Count == 0) {
			EditorText.Note("The mission tests no campaign flag.");
		} else {
			ImGui.PushID("flags");
			foreach (int flag in flags) {
				DrawFlag(flag);
			}

			ImGui.PopID();
		}

		string? why = _state.WhyNoDraw;
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Draw again")) {
			_state.RequestDrawAgain();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Note("Draw again loads the mission again with new random draws, flags 4 to 6 among them. The other flags keep what is set here.");
		if (_state.ChosenTables > 0) {
			EditorText.Note($"{_state.ChosenTables} variant table(s) show a chosen alternative in place of their draw, until Draw again.");
		}

		EditorText.Reason(_state.Failure);
	}

	// One flag's field, labelled with what it holds when that is known.
	private void DrawFlag(int flag) {
		// Another flag's draft is dropped once nothing is being typed.
		if (_draftFlag != null && _draftFlag != flag && !ImGui.IsAnyItemActive()) {
			_draftFlag = null;
		}

		int value = _draftFlag == flag ? _draft : _state.Flag(flag);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(90f));
		if (ImGui.InputInt($"Flag {flag}##{flag}", ref value, 0, 0)) {
			_draftFlag = flag;
			_draft = Math.Clamp(value, short.MinValue, short.MaxValue);
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFlag == flag) {
			_draftFlag = null;
			_state.RequestFlag(flag, (short)_draft);
		}

		if (CampaignState.Label(flag) is { } label) {
			ImGui.SameLine();
			EditorText.Note(label);
		}
	}
}
