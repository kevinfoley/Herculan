using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of an action timer: the action that starts it, its delay and the actions it fires, each a
/// choice or field that changes it (<see cref="TimerEditor"/>), with links, and a Delete button. What a timer does is
/// docs/retail/simulation/mission-deployment.md#action-timers--actiontimer_tick-004230a4.
/// </summary>
internal sealed class TimerView {
	private readonly MissionIndex _index;
	private readonly ScriptDat _document;
	private readonly TimerEditor _editor;
	private readonly PanelLinks _links;

	// A delay being typed, and the timer it is for, until its field lets go.
	private int? _delayFor;
	private int _delay;

	/// <param name="document">The generated <c>script.dat</c>, whose block 6 holds each delay in seconds as the file stores it.</param>
	public TimerView(MissionIndex index, ScriptDat document, TimerEditor editor, PanelLinks links) {
		_index = index;
		_document = document;
		_editor = editor;
		_links = links;
	}

	private Mission Mission => _index.Mission;

	/// <summary>Row-11 GUID <paramref name="guid"/>'s view, the timer generated at block-6 index <paramref name="timer"/>.</summary>
	public void Draw(short guid, int timer) {
		if (timer < 0 || timer >= Mission.ActionTimers.Count || timer >= _document.ActionTimers.Length) {
			return;
		}

		var record = Mission.ActionTimers[timer];
		bool locked = _editor.WhyFixed(guid) != null;
		ImGui.Text($"Timer {timer}");
		ImGui.Separator();
		DrawStart(guid, record, locked);
		DrawDelay(guid, timer, _document.ActionTimers[timer].Delay, locked);
		DrawFired(guid, record, locked);

		ImGui.Separator();
		ImGui.BeginDisabled(locked);
		if (ImGui.Button("Delete")) {
			_editor.RequestDelete(guid);
		}

		ImGui.EndDisabled();
		EditorText.Reason(_editor.Failure);
	}

	// The action that starts it, or none: then it counts down from mission start.
	private void DrawStart(short timer, MissionActionTimer record, bool locked) {
		Label("Starts when");
		ImGui.BeginDisabled(locked);
		if (ActionChoice.Draw("##start", record.PrimaryActionRef, Mission.Actions.Count, _index.Records) is { } chosen) {
			_editor.RequestStart(timer, chosen);
		}

		ImGui.EndDisabled();
		if (record.PrimaryActionRef >= 0) {
			_links.Action(record.PrimaryActionRef);
			EditorText.Note("It counts down once that action fires.");
		} else {
			EditorText.Note("None: it counts down from the start of the mission.");
		}
	}

	// The delay in seconds, a field applied when it lets go, kept to what the countdown holds.
	// The delay of the timer of row-11 GUID <guid>, generated at block-6 index <timer>.
	private void DrawDelay(short guid, int timer, short seconds, bool locked) {
		if (_delayFor is { } draftFor && draftFor != timer && !ImGui.IsAnyItemActive()) {
			_delayFor = null;
		}

		int shown = _delayFor == timer ? _delay : seconds;
		Label("Delay (s)");
		ImGui.BeginDisabled(locked);
		if (ImGui.InputInt("##delay", ref shown, 0, 0)) {
			_delayFor = timer;
			_delay = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _delayFor == timer) {
			_delayFor = null;
			int draft = Math.Clamp(_delay, 0, TimerEditing.MaxDelay);
			if (draft != seconds) {
				_editor.RequestDelay(guid, draft);
			}
		}

		ImGui.EndDisabled();
		EditorText.Note($"{MissionText.Duration(seconds)}. At most {TimerEditing.MaxDelay} s (about 9 hours); 0 fires at once.");
	}

	// The actions it fires when it runs out, each a choice with Remove and a link, and Add after the last.
	private void DrawFired(short timer, MissionActionTimer record, bool locked) {
		ImGui.Spacing();
		ImGui.Text("When it runs out, fires:");
		ImGui.Indent();
		int filled = Array.FindLastIndex(record.SequenceRefs.ToArray(), action => action >= 0) + 1;
		for (int slot = 0; slot < filled; slot++) {
			short action = record.SequenceRefs[slot];
			ImGui.PushID($"fired{slot}");
			ImGui.BeginDisabled(locked);
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(150f));
			if (action >= 0) {
				if (ActionChoice.Draw("##fired", action, Mission.Actions.Count, _index.Records, offerNone: false) is { } chosen) {
					_editor.RequestFired(timer, slot, chosen);
				}
			} else {
				EditorText.Note("(empty)");
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove") && action >= 0) {
				_editor.RequestRemoveFired(timer, slot);
			}

			ImGui.EndDisabled();
			if (action >= 0) {
				_links.Action(action);
			}

			ImGui.PopID();
		}

		if (filled == 0) {
			EditorText.Note("No action yet: it fires nothing.");
		}

		string? why = _editor.WhyNoAddFired(timer);
		ImGui.BeginDisabled(why != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(150f));
		if (ImGui.BeginCombo("##addfired", "Add an action")) {
			for (int action = 0; action < Mission.Actions.Count; action++) {
				if (ImGui.Selectable($"Action {action}")) {
					_editor.RequestAddFired(timer, _index.Records.ActionGuid(action));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Note("It fires every one of them at once.");
		ImGui.Unindent();
	}

	// A field's label, with the choice after it filling the rest of the line.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(text);
		ImGui.SameLine(ScaledImGui.Scaled(90f));
		ImGui.SetNextItemWidth(-1f);
	}
}
