using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a countdown (a row-11 action timer), from the <c>.MSN</c>'s layout by GUID: its name, its
/// delay and what starts it (<see cref="MissionNames.Countdown"/>), the trigger that starts it (none: the mission's start), its delay in seconds, the triggers it
/// fires when it runs out, each a choice that changes it (<see cref="TimerEditor"/>), with links, and a Delete button. What a
/// countdown does is docs/retail/simulation/mission-deployment.md#action-timers--actiontimer_tick-004230a4.
/// </summary>
internal sealed class CountdownView {
	private readonly MissionIndex _index;
	private readonly TimerEditor _editor;
	private readonly PanelLinks _links;
	private readonly CountdownDelayField _delay = new();

	public CountdownView(MissionIndex index, TimerEditor editor, PanelLinks links) {
		_index = index;
		_editor = editor;
		_links = links;
	}

	/// <summary>Row-11 GUID <paramref name="guid"/>'s view; nothing for a GUID the layout has no countdown for.</summary>
	public void Draw(short guid) {
		if (_index.Layout.Entry<ActionTimer30>(guid)?.Fields is not { } countdown) {
			return;
		}

		bool locked = _editor.WhyFixed(guid) != null;
		var name = _links.Names.Countdown(guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		ImGui.Separator();
		DrawStart(guid, countdown, locked);
		DrawDelay(guid, countdown.Delay, locked);
		DrawFired(guid, locked);

		ImGui.Separator();
		ImGui.BeginDisabled(locked);
		if (ImGui.Button("Delete")) {
			_editor.RequestDelete(guid);
		}

		ImGui.EndDisabled();
		EditorText.Reason(_editor.Failure);
	}

	// The trigger that starts it, or none: then it counts down from the mission's start.
	private void DrawStart(short guid, ActionTimer30 countdown, bool locked) {
		short start = TimerEditing.Start(_editor.Source, guid);
		Label("Starts when");
		ImGui.BeginDisabled(locked);
		if (ActionChoice.Draw("##start", start, _index, _links.Names, none: "The mission starts") is { } chosen) {
			_editor.RequestStart(guid, chosen);
		}

		ImGui.EndDisabled();
		if (start != -1) {
			_links.Trigger(start);
			EditorText.Note("It counts down once that trigger activates.");
		} else {
			EditorText.Note("It counts down from the start of the mission.");
		}
	}

	// The delay in seconds, a field applied when it lets go.
	private void DrawDelay(short guid, short seconds, bool locked) {
		Label("Delay (s)");
		ImGui.BeginDisabled(locked);
		if (_delay.Draw("##delay", guid, seconds) is { } typed) {
			_editor.RequestDelay(guid, typed);
		}

		ImGui.EndDisabled();
		EditorText.Note(CountdownDelayField.Note(seconds));
	}

	// The triggers it fires when it runs out, each a choice with Remove and a link, and Add after the last, a new trigger or one
	// of the mission's. A slot left empty between two filled ones is not shown: the countdown fires every filled slot.
	private void DrawFired(short guid, bool locked) {
		ImGui.Spacing();
		ImGui.Text("When it runs out, fires:");
		ImGui.Indent();
		var fired = TimerEditing.Fired(_editor.Source, guid);
		bool any = false;
		for (int slot = 0; slot < fired.Length; slot++) {
			short trigger = fired[slot];
			if (trigger == -1) {
				continue;
			}

			any = true;
			ImGui.PushID($"fired{slot}");
			ImGui.BeginDisabled(locked);
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(150f));
			if (ActionChoice.Draw("##fired", trigger, _index, _links.Names, offerNone: false) is { } chosen) {
				_editor.RequestFired(guid, slot, chosen);
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove")) {
				_editor.RequestRemoveFired(guid, slot);
			}

			ImGui.EndDisabled();
			_links.Trigger(trigger);
			ImGui.PopID();
		}

		if (!any) {
			EditorText.Note("No trigger yet: it fires nothing.");
		}

		string? why = _editor.WhyNoAddFired(guid);
		ImGui.BeginDisabled(locked || why != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(150f));
		if (NameText.BeginCombo("##addfired", new RecordName("Add a trigger"))) {
			if (ImGui.Selectable("New trigger")) {
				_editor.RequestNewFired(guid);
			}

			foreach (var trigger in _index.Actions) {
				if (NameText.Selectable(_links.Names.Trigger(trigger.Guid), $"add{trigger.Guid}", false)) {
					_editor.RequestAddFired(guid, trigger.Guid);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		if (any) {
			EditorText.Note("It fires every one of them at once.");
		}

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
