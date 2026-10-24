using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The mission view's debrief, by outcome (docs/herculan/handoff-editor-mission-logic.md#the-missions-text): each outcome
/// headed by its clauses, with the text and movie it shows, the lines it replaces editable and the rest dimmed; then
/// Otherwise, the unconditioned text and movie; then the loss line, read-only, with its cases (<see cref="DebriefText"/>).
/// Edits go through <see cref="DebriefEditor"/>. Where the mission screen shows the debrief is
/// docs/retail/shell/mission-screen.md#the-summary-text-box.
/// </summary>
internal sealed class DebriefView {
	// What a field takes in bytes of UTF-8, as LineField's.
	private const uint MaxBytes = 2048;

	private readonly DebriefEditor _editor;
	private readonly LineField _lines = new();

	public DebriefView(DebriefEditor editor) => _editor = editor;

	public void Draw() {
		ImGui.Separator();
		ImGui.Text("Debrief");
		// A training debrief never reloads the mission's text (docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line).
		EditorText.Note(_editor.Campaign()
			? "Shown on the mission screen after a campaign mission."
			: "Shown on the mission screen only after a campaign mission, so not after this one.");
		var source = _editor.Source;
		if (DebriefText.Record(source) == null) {
			EditorText.Note("This mission has no debrief.");
			return;
		}

		ImGui.PushID("debrief");
		var loss = DebriefText.LossLineOf(source);
		var lines = DebriefText.Lines(source).Where(line => line.Id != loss?.Line.Id).ToList();
		if (lines.Any(line => Texts(source, line).Any(text => text.Any(c => c > 127)))) {
			EditorText.Note("Accented letters show as spaces on the mission screen in the original 1.0 release.");
		}

		var outcomes = DebriefText.Outcomes(source);
		for (int i = 0; i < outcomes.Count; i++) {
			DrawOutcome(source, i + 1, outcomes[i], lines);
		}

		DrawOtherwise(source, outcomes.Count > 0, lines);
		DrawLosses(source, loss);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	// An outcome: its clauses, then each line, its own editable and the rest as Otherwise has them, dimmed, then its movie.
	private void DrawOutcome(MissionDocument source, int number, short condition, IReadOnlyList<DebriefLine> lines) {
		if (!ImGui.CollapsingHeader($"Outcome {number}###outcome{condition}", ImGuiTreeNodeFlags.DefaultOpen)) {
			return;
		}

		ImGui.PushID($"outcome{condition}");
		EditorText.Wrapped($"{When(source, condition)}:");
		bool dimmed = false;
		foreach (var line in lines) {
			ImGui.PushID(line.Slot);
			if (MissionTextEditing.TextUnder(source, line.Id, condition) is { } own) {
				DrawLine(line, condition, own);
			} else {
				dimmed = true;
				DrawDimmed(MissionTextEditing.TextUnder(source, line.Id, -1) ?? string.Empty);
			}

			ImGui.PopID();
		}

		if (dimmed) {
			EditorText.Note("Dimmed lines read as under Otherwise.");
		}

		DrawMovie(source, condition);
		ImGui.PopID();
	}

	// The unconditioned text and movie, headed Otherwise when the debrief has outcomes.
	private void DrawOtherwise(MissionDocument source, bool outcomes, IReadOnlyList<DebriefLine> lines) {
		if (!ImGui.CollapsingHeader(outcomes ? "Otherwise###otherwise" : "Text###otherwise", ImGuiTreeNodeFlags.DefaultOpen)) {
			return;
		}

		ImGui.PushID("otherwise");
		if (outcomes) {
			EditorText.Wrapped("When no outcome holds:");
		}

		if (lines.Count == 0) {
			EditorText.Note("No lines besides the losses line.");
		}

		foreach (var line in lines) {
			ImGui.PushID(line.Slot);
			if (MissionTextEditing.TextUnder(source, line.Id, -1) is { } text) {
				DrawLine(line, -1, text);
			} else {
				EditorText.Note("No text when no outcome holds.");
			}

			ImGui.PopID();
		}

		DrawMovie(source, -1);
		ImGui.PopID();
	}

	// The loss line as one switch, read-only, and its cases, collapsed.
	private static void DrawLosses(MissionDocument source, LossLine? loss) {
		ImGui.PushID("losses");
		bool on = loss != null;
		ImGui.BeginDisabled();
		ImGui.Checkbox("Report Hercs and squadmates lost", ref on);
		ImGui.EndDisabled();
		if (loss == null) {
			ImGui.PopID();
			return;
		}

		var all = DebriefText.Lines(source);
		if (all.Count > 1 && all[0] == loss.Line) {
			EditorText.Note("Its line comes first in the debrief.");
		}

		if (ImGui.TreeNode("What it reports")) {
			EditorText.Note("The last case that holds, and nothing when none does.");
			for (int i = 0; i < loss.Cases.Count; i++) {
				ImGui.PushID(i);
				EditorText.Wrapped($"{PresenceSentences.When(loss.Cases[i].Rule)}:");
				DrawDimmed(loss.Cases[i].Text);
				ImGui.PopID();
			}

			ImGui.TreePop();
		}

		ImGui.PopID();
	}

	// An editable line, keyed by its line and condition, applied when it lets go.
	private void DrawLine(DebriefLine line, short condition, string text) {
		if (_lines.Draw("##line", $"debrief.{condition}.{line.Slot}", text) is { } typed) {
			_editor.RequestText(line, condition, typed);
		}
	}

	// A line shown and not edited here.
	private static void DrawDimmed(string text) {
		ImGui.BeginDisabled();
		ImGui.SetNextItemWidth(-1f);
		ImGui.InputText("##same", ref text, MaxBytes, ImGuiInputTextFlags.ReadOnly);
		ImGui.EndDisabled();
	}

	// "Movie: 51", dimmed when the outcome under <condition> has none of its own; nothing when the debrief references no movie.
	private static void DrawMovie(MissionDocument source, short condition) {
		if (DebriefText.MovieRef(source) == -1) {
			return;
		}

		short? own = DebriefText.MovieUnder(source, condition);
		short? movie = own ?? DebriefText.MovieUnder(source, -1);
		string shown = movie is null or -1 ? "Movie: none" : $"Movie: {movie}";
		if (own == null && condition != -1) {
			ImGui.TextDisabled(shown);
		} else {
			ImGui.TextUnformatted(shown);
		}
	}

	// "When the mission was won and campaign variable 250 is 1", or the condition as the Conditions list names it where
	// its clauses cannot be read.
	private static string When(MissionDocument source, short condition) =>
		PresenceRules.ReadCondition(source, condition).Rule is { } rule
			? PresenceSentences.When(rule)
			: $"When condition {condition} holds ({ConditionEditing.Describe(source.Mission, condition) ?? "missing"})";

	// Every text <line> has, under any condition.
	private static IEnumerable<string> Texts(MissionDocument source, DebriefLine line) =>
		(source.Text?.Strings ?? []).Where(entry => entry.Guid == line.Id).Select(entry => entry.Val ?? string.Empty);
}
