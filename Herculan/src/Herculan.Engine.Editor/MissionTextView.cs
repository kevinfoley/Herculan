using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The mission view's text: the objective lines the player is shown, the briefing and the intelligence report (row 4),
/// each line a field that applies when it lets go, with Up, Down and Remove, and Add after the last
/// (<see cref="MissionTextEditor"/>). Where the game shows each list is docs/retail/simulation/alert-panels.md's (the
/// objectives panel) and docs/retail/shell/mission-screen.md's (the summary text box).
/// </summary>
internal sealed class MissionTextView {
	// The objectives panel builds seven labels and drops an eighth line (ObjectivesPanel_Paint (00457b58)).
	private const int PanelLines = 7;

	private readonly MissionTextEditor _editor;
	private readonly LineField _lines = new();

	public MissionTextView(MissionTextEditor editor) => _editor = editor;

	public void Draw() {
		if (_editor.LineCount is not { } count) {
			return;
		}

		ImGui.Separator();
		ImGui.Text("Text");
		EditorText.Note($"{count} of the {MissionTextEditing.LineCap} lines the simulator reads.");
		bool locked = _editor.WhyReadOnly != null;
		DrawList(MissionTextList.Objectives, "Objective lines", locked);
		DrawList(MissionTextList.Briefing, "Briefing", locked);
		DrawList(MissionTextList.Intelligence, "Intelligence report", locked);
		EditorText.Reason(_editor.Failure);
	}

	private void DrawList(MissionTextList list, string heading, bool locked) {
		if (!ImGui.CollapsingHeader($"{heading}###{list}", ImGuiTreeNodeFlags.DefaultOpen)) {
			return;
		}

		var lines = _editor.Lines(list);
		EditorText.Note(list == MissionTextList.Objectives
			? lines.Count > PanelLines ? $"The F11 panel lists the first {PanelLines}." : "The F11 panel lists them."
			: "The mission screen shows them in a campaign, joined into one text.");
		if (lines.Any(line => line.Text?.Any(c => c > 127) == true)) {
			EditorText.Note("Letters past code 127 show as spaces on version 1.0's mission screen.");
		}

		ImGui.PushID(list.ToString());
		for (int i = 0; i < lines.Count; i++) {
			var (slot, _, text) = lines[i];
			ImGui.PushID(slot);
			DrawLine(list, slot, text, i > 0 ? lines[i - 1].Slot : -1, i < lines.Count - 1 ? lines[i + 1].Slot : -1, locked);
			ImGui.PopID();
		}

		string? why = _editor.WhyNoAdd(list);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add a line")) {
			_editor.RequestAdd(list);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		ImGui.PopID();
	}

	// One line; <before> and <after> are the filled slots either side of it, -1 for none.
	private void DrawLine(MissionTextList list, int slot, string? text, int before, int after, bool locked) {
		ImGui.BeginDisabled(locked);
		if (text == null) {
			EditorText.Note("No text shown in the editor.");
		} else if (_lines.Draw("##line", $"{list}.{slot}", text) is { } typed) {
			_editor.RequestText(list, slot, typed);
		}

		ImGui.BeginDisabled(before < 0);
		if (ImGui.SmallButton("Up")) {
			_editor.RequestMove(list, slot, before);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(after < 0);
		if (ImGui.SmallButton("Down")) {
			_editor.RequestMove(list, slot, after);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestRemove(list, slot);
		}

		ImGui.EndDisabled();
		if (_editor.Shared(list, slot)) {
			EditorText.Note("Another record uses this line too: a change here gives this slot its own copy.");
		}
	}
}
