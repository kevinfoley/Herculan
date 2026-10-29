using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The countdowns a trigger's Then list starts (<see cref="TriggerThenList"/>). A countdown with a trigger of its own
/// (<see cref="DelayedActions.Of"/>) is one block, open by default: its delay, and under "when it runs out" that trigger's Then
/// list, which the list draws, so a countdown in it nests the same way, two levels deep; past that, and for a trigger already
/// open further up, it falls back to the other form. Any other countdown is its link, its delay and the triggers it fires, as
/// links. Remove deletes the countdown, with its own trigger and what that trigger wires
/// (<see cref="ActionEditor.RequestRemoveCountdown"/>), after a confirmation saying what changes when that trigger does
/// anything. docs/herculan/editor-design.md#triggers-and-countdowns.
/// </summary>
internal sealed class TriggerThenCountdowns {
	// In pixels on a 100% display (ScaledImGui.Scaled): a countdown's delay field.
	private const float DelayWidth = 70f;

	private const string ConfirmTitle = "Remove the countdown?";

	// How many blocks deep countdowns nest before the next falls back to the link form: what the Properties panel's width
	// holds (PropertiesPanelLayoutTests); the retail training missions chain countdowns up to eight deep.
	private const int OpenLevels = 2;

	private readonly MissionIndex _index;
	private readonly ActionEditor _editor;
	private readonly PanelLinks _links;
	private readonly Action<short, bool, HashSet<short>> _drawRunsOut;
	private readonly Action<short, HashSet<short>> _summaryRunsOut;
	private readonly CountdownDelayField _delay = new();

	/// <param name="drawRunsOut">Draws a countdown's own trigger's Then list under "when it runs out": the trigger, whether locked, and the triggers open up the chain.</param>
	/// <param name="summaryRunsOut">The same read-only.</param>
	public TriggerThenCountdowns(MissionIndex index, ActionEditor editor, PanelLinks links,
			Action<short, bool, HashSet<short>> drawRunsOut, Action<short, HashSet<short>> summaryRunsOut) {
		_index = index;
		_editor = editor;
		_links = links;
		_drawRunsOut = drawRunsOut;
		_summaryRunsOut = summaryRunsOut;
	}

	private MissionDocument Source => _editor.Source;

	private MissionNames Names => _links.Names;

	/// <summary>
	/// Row-10 GUID <paramref name="guid"/>'s <paramref name="countdowns"/>, every control disabled when <paramref name="locked"/>;
	/// <paramref name="chain"/> holds the triggers whose lists are open around this one. Whether there was any.
	/// </summary>
	public bool Draw(short guid, IReadOnlyList<StartsCountdown> countdowns, bool locked, HashSet<short> chain) {
		if (countdowns.Count == 0) {
			return false;
		}

		ImGui.Text("Starts these countdowns:");
		ImGui.Indent();
		foreach (var starts in countdowns) {
			if (_index.Layout.Entry<ActionTimer30>(starts.Countdown)?.Fields is not { } countdown) {
				continue;
			}

			ImGui.PushID($"starts{starts.Countdown}");
			short own = DelayedActions.Of(Source, starts.Countdown);
			if (own != -1 && !chain.Contains(own) && chain.Count <= OpenLevels) {
				DrawBlock(guid, starts.Countdown, countdown.Delay, own, locked, chain);
			} else {
				DrawLinked(guid, starts.Countdown, countdown.Delay, locked);
			}

			ImGui.PopID();
		}

		ImGui.Unindent();
		return true;
	}

	/// <summary>
	/// Row-11 GUID <paramref name="countdown"/> as a read-only Then list shows it: its link, and its own trigger's list under it
	/// as deep as the editable list nests, unless <paramref name="chain"/> holds that trigger.
	/// </summary>
	public void DrawSummary(short countdown, HashSet<short> chain) {
		_links.Countdown(countdown, EditorText.AfterRoom(" starts"));
		EditorText.After(" starts");
		short own = DelayedActions.Of(Source, countdown);
		if (own == -1 || chain.Contains(own) || chain.Count > OpenLevels) {
			return;
		}

		ImGui.Indent();
		ImGui.Text("when it runs out:");
		ImGui.Indent();
		chain.Add(own);
		_summaryRunsOut(own, chain);
		chain.Remove(own);
		ImGui.Unindent();
		ImGui.Unindent();
	}

	// A countdown with a trigger of its own: a tree node whose line is the link, the delay and Remove, over that trigger's list.
	private void DrawBlock(short guid, short countdown, short seconds, short own, bool locked, HashSet<short> chain) {
		ImGui.AlignTextToFramePadding();
		bool open = ImGui.TreeNodeEx("##block", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
		ImGui.SameLine();

		// The duration only when it says more than the field: from a minute up.
		string? duration = seconds >= 60 ? MissionText.Duration(seconds) : null;
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		float after = ScaledImGui.Scaled(DelayWidth) + spacing + EditorText.TextRoom("s") + spacing
			+ (duration != null ? EditorText.TextRoom(duration) + spacing : 0f) + EditorText.ButtonRoom("Remove");
		_links.Link("Countdown", new TimerSelection(countdown), after);
		ImGui.SameLine();
		DrawDelay(guid, countdown, seconds, locked);
		ImGui.SameLine();
		ImGui.TextUnformatted("s");
		if (duration != null) {
			ImGui.SameLine();
			ImGui.TextUnformatted(duration);
		}

		ImGui.SameLine();
		DrawRemove(guid, countdown, own, locked);
		if (!open) {
			return;
		}

		chain.Add(own);
		_drawRunsOut(own, locked, chain);
		chain.Remove(own);
		ImGui.TreePop();
	}

	// Any other countdown: its link with Remove, its delay as a field and the triggers it fires, as links. Past the open
	// levels it may still have a trigger of its own, which Remove deletes with it.
	private void DrawLinked(short guid, short countdown, short seconds, bool locked) {
		_links.Countdown(countdown, EditorText.ButtonRoom("Remove"));
		ImGui.SameLine();
		DrawRemove(guid, countdown, DelayedActions.Of(Source, countdown), locked);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Delay (s):");
		ImGui.SameLine();
		DrawDelay(guid, countdown, seconds, locked);
		ImGui.SameLine();
		ImGui.TextUnformatted(MissionText.Duration(seconds));
		var fired = TimerEditing.Fired(Source, countdown).Where(trigger => trigger != -1).ToList();
		ImGui.AlignTextToFramePadding();
		ImGui.Text(fired.Count > 0 ? "then fires:" : "then fires nothing yet.");

		// The first beside "then fires:", the rest one per line: a second on the same line would have no room left.
		for (int i = 0; i < fired.Count; i++) {
			short trigger = fired[i];
			if (i == 0) {
				ImGui.SameLine();
			}

			ImGui.PushID(trigger);
			_links.Trigger(trigger);
			ImGui.PopID();
		}
	}

	private void DrawDelay(short guid, short countdown, short seconds, bool locked) {
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(DelayWidth));
		ImGui.BeginDisabled(locked);
		if (_delay.Draw("##delay", countdown, seconds) is { } typed) {
			_editor.RequestCountdownDelay(guid, countdown, typed);
		}

		ImGui.EndDisabled();
	}

	// Remove, deleting the countdown; with <own>, its own trigger, after the confirmation when that trigger does anything. A
	// removal Core refuses is asked for anyway, so its reason shows under the view.
	private void DrawRemove(short guid, short countdown, short own, bool locked) {
		ImGui.BeginDisabled(locked);
		if (ImGui.SmallButton("Remove")) {
			var removal = DelayedActions.Removal(Source, countdown);
			if (removal.Refusal == null && Changes(removal)) {
				ImGui.OpenPopup(ConfirmTitle);
			} else {
				_editor.RequestRemoveCountdown(guid, countdown);
			}
		}

		ImGui.EndDisabled();
		EditorText.Tooltip(own != -1 ? "Deletes the countdown and what happens when it runs out."
			: TimerEditing.Fired(Source, countdown).Any(trigger => trigger != -1)
				? "Deletes the countdown. The triggers it fires stay in the mission, but it no longer fires them."
				: "Deletes the countdown.");
		DrawConfirm(guid, countdown);
	}

	// Whether removing does more than delete a countdown and a trigger that does nothing.
	private bool Changes(TimerRemoval removal) =>
		removal.Groups.Count > 0 || removal.Orders.Count > 0 || removal.Timers.Count > 1 || removal.Actions.Any(SetsOrSends);

	// Whether row-10 GUID <action> sets a campaign variable or sends a message.
	private bool SetsOrSends(short action) =>
		ActionEditing.FilledPairs(ActionEditing.CounterPairs(Source, action)) > 0 || ActionEditing.Message(Source, action) >= 0;

	// The confirmation Remove opens: what changes, each record by its name, then Remove and Cancel.
	private void DrawConfirm(short guid, short countdown) {
		if (!ImGui.IsPopupOpen(ConfirmTitle)) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PropertiesPanel.PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(ConfirmTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			return;
		}

		if (_index.Layout.Entry<ActionTimer30>(countdown) == null) {
			ImGui.CloseCurrentPopup();
			ImGui.EndPopup();
			return;
		}

		var removal = DelayedActions.Removal(Source, countdown);
		EditorText.Wrapped("This deletes the countdown and what happens when it runs out.");
		Listed("These groups will be in the mission from the start:", removal.Groups.Select(Names.GroupBrief));
		Listed("These orders will no longer end on a trigger:", removal.Orders.SelectMany(_index.OrderSlots).Select(OrderName));
		Listed("These countdowns inside it are deleted too:", removal.Timers.Skip(1).Select(Names.Countdown));
		if (removal.Actions.Any(SetsOrSends)) {
			ImGui.Spacing();
			EditorText.Wrapped("The campaign variables it sets and the messages it sends go with it.");
		}

		if (removal.Refusal is { } reason) {
			EditorText.Reason(reason);
		}

		ImGui.Separator();
		ImGui.BeginDisabled(removal.Refusal != null);
		if (ImGui.Button("Remove")) {
			_editor.RequestRemoveCountdown(guid, countdown);
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		if (ImGui.Button("Cancel")) {
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	// <heading> and a line per name under it; nothing for none.
	private static void Listed(string heading, IEnumerable<RecordName> names) {
		var shown = names.ToList();
		if (shown.Count == 0) {
			return;
		}

		ImGui.Spacing();
		EditorText.Wrapped(heading);
		ImGui.Indent();
		foreach (var name in shown) {
			NameText.Draw(name);
		}

		ImGui.Unindent();
	}

	// An order slot by its place and group: "2nd order of Cybrid hercs (group 4)".
	private RecordName OrderName(EndsOrder order) {
		var group = Names.GroupBrief(order.Group);
		return group with { Text = $"{MissionText.Ordinal(order.Slot + 1)} order of {group.Text}" };
	}
}
