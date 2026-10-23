using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The trigger view's Then list: what happens when the trigger fires, every entry editable and an Add menu over them. A group it
/// brings in, with how every such group arrives; a group it moves on from an order; a countdown it starts, with its delay; the
/// campaign variables it sets (<see cref="CampaignVariableWrites"/>); and the message it sends, by its text. Each entry is
/// written in the record that holds the link (<see cref="TriggerFollowers"/>): the group's, the order slot's or the
/// countdown's, so removing one clears that link alone; the arrival, the writes and the message are the trigger's own. What
/// each does in play is docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
/// </summary>
internal sealed class TriggerThenList {
	// In pixels on a 100% display (ScaledImGui.Scaled): a countdown's delay field.
	private const float DelayWidth = 70f;

	private readonly MissionIndex _index;
	private readonly ActionEditor _editor;
	private readonly PanelLinks _links;
	private readonly TriggerMessages _messages;
	private readonly CampaignVariableWrites _writes = new();
	private readonly CountdownDelayField _delay = new();

	public TriggerThenList(MissionIndex index, ActionEditor editor, PanelLinks links, TriggerMessages messages) {
		_index = index;
		_editor = editor;
		_links = links;
		_messages = messages;
	}

	private MissionDocument Source => _editor.Source;

	private MissionLayout Layout => _index.Layout;

	/// <summary>Row-10 GUID <paramref name="guid"/>'s Then list, of base <paramref name="fields"/>; every control disabled when <paramref name="locked"/>.</summary>
	public void Draw(short guid, MissionAction82 fields, bool locked) {
		var follows = _index.Links(guid).Follows;
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Then:");
		ImGui.SameLine();
		DrawAddMenu(guid, follows, locked);
		ImGui.Indent();
		bool any = DrawGroups(guid, fields, follows.OfType<BringsInGroup>().ToList(), locked);
		any |= DrawOrders(guid, follows.OfType<EndsOrder>().ToList(), locked);
		any |= DrawCountdowns(guid, follows.OfType<StartsCountdown>().ToList(), locked);
		any |= DrawWrites(guid, locked);
		any |= DrawMessage(guid, locked);
		if (!any) {
			EditorText.Note("Nothing happens when it fires yet: Add... lists what can.");
		}

		ImGui.Unindent();
	}

	/// <summary>Row-10 GUID <paramref name="guid"/>'s Then list, read-only, each record a link, as a trigger area's view lists it.</summary>
	public void DrawSummary(short guid, MissionAction82 fields) {
		var follows = _index.Links(guid).Follows;
		ImGui.Text("Then:");
		ImGui.Indent();
		bool any = false;
		foreach (var follower in follows) {
			ImGui.PushID(follower.GetHashCode());
			switch (follower) {
				case BringsInGroup bring:
					_links.ShortLayoutGroup(bring.Group);
					ImGui.SameLine(0f, 0f);
					ImGui.TextUnformatted($" arrives {MissionText.ArrivalKind(fields.Verb)}");
					break;
				case EndsOrder ends:
					_links.ShortLayoutGroup(ends.Group);
					ImGui.SameLine(0f, 0f);
					ImGui.TextUnformatted($" moves on from its {MissionText.Ordinal(ends.Slot + 1)} order");
					break;
				case StartsCountdown starts:
					_links.Countdown(starts.Countdown);
					ImGui.SameLine(0f, 0f);
					ImGui.TextUnformatted(" starts");
					break;
			}

			ImGui.PopID();
			any = true;
		}

		any |= CampaignVariableWrites.DrawLines(WriteLayer.Action, Writes(guid));
		short message = ActionEditing.Message(Source, guid);
		if (message >= 0) {
			int training = TriggerMessages.TrainingNumber(Layout);
			EditorText.Wrapped($"Sends: {_messages.Text(training, message) ?? NotInFile(training)}");
			if (Posts(guid) == 0) {
				EditorText.Note("never sent: it sets no campaign variable");
			}

			any = true;
		}

		if (!any) {
			EditorText.Note("nothing happens when it fires");
		}

		ImGui.Unindent();
	}

	// The groups it brings in, each a link with Remove, then how every one of them arrives; whether there was any.
	private bool DrawGroups(short guid, MissionAction82 fields, IReadOnlyList<BringsInGroup> groups, bool locked) {
		if (groups.Count == 0) {
			return false;
		}

		ImGui.Text("Brings in:");
		ImGui.Indent();
		foreach (var bring in groups) {
			ImGui.PushID($"bring{bring.Group}");
			_links.LayoutGroup(bring.Group);
			ImGui.SameLine();
			RemoveButton(guid, bring, locked, "Removing it puts the group in the mission from the start.");
			ImGui.PopID();
		}

		ImGui.AlignTextToFramePadding();
		ImGui.Text("Arrives:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(locked);
		string current = MissionText.ArrivalChoice(fields.Verb);
		if (ImGui.BeginCombo("##arrives", current)) {
			foreach (short verb in MissionText.Arrivals) {
				string choice = MissionText.ArrivalChoice(verb);
				if (ImGui.Selectable(choice, choice == current) && choice != current) {
					_editor.RequestVerb(guid, verb);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note(MissionText.ArrivalNote(fields.Verb));
		if (fields.Verb is < 0 or > ActionEditing.LastVerb) {
			EditorText.Note($"Stored as {fields.Verb}: the group arrives in place.");
		}

		EditorText.Note(groups.Count > 1
			? "These groups arrive the same way: the arrival is set on the trigger, for every group it brings in."
			: "The arrival is set on the trigger: every group it brings in arrives the same way.");
		ImGui.Unindent();
		return true;
	}

	// The order slots it ends, each the group's link and the order, with Remove; whether there was any.
	private bool DrawOrders(short guid, IReadOnlyList<EndsOrder> orders, bool locked) {
		if (orders.Count == 0) {
			return false;
		}

		ImGui.Text("Moves these groups on to their next order:");
		ImGui.Indent();
		foreach (var ends in orders) {
			ImGui.PushID($"ends{ends.Group}.{ends.Slot}");
			_links.ShortLayoutGroup(ends.Group);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted($", from its {OrderName(ends.Group, ends.Slot)}");
			ImGui.SameLine();
			RemoveButton(guid, ends, locked, null);
			ImGui.PopID();
		}

		ImGui.Unindent();
		return true;
	}

	// The countdowns it starts, each a link with Remove, its delay as a field and the triggers it fires; whether there was any.
	private bool DrawCountdowns(short guid, IReadOnlyList<StartsCountdown> countdowns, bool locked) {
		if (countdowns.Count == 0) {
			return false;
		}

		ImGui.Text("Starts these countdowns:");
		ImGui.Indent();
		foreach (var starts in countdowns) {
			if (Layout.Entry<ActionTimer30>(starts.Countdown)?.Fields is not { } countdown) {
				continue;
			}

			ImGui.PushID($"starts{starts.Countdown}");
			_links.ShortCountdown(starts.Countdown);
			ImGui.SameLine();
			RemoveButton(guid, starts, locked, "Removing it makes the countdown start with the mission.");
			ImGui.AlignTextToFramePadding();
			ImGui.Text("Delay (s):");
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(DelayWidth));
			ImGui.BeginDisabled(locked);
			if (_delay.Draw("##delay", starts.Countdown, countdown.Delay) is { } typed) {
				_editor.RequestCountdownDelay(guid, starts.Countdown, typed);
			}

			ImGui.EndDisabled();
			ImGui.SameLine();
			ImGui.TextUnformatted(MissionText.Duration(countdown.Delay));
			var fired = TimerEditing.Fired(Source, starts.Countdown).Where(trigger => trigger != -1).ToList();
			ImGui.AlignTextToFramePadding();
			ImGui.Text(fired.Count > 0 ? "then fires:" : "then fires nothing yet.");
			foreach (short trigger in fired) {
				ImGui.SameLine();
				ImGui.PushID(trigger);
				_links.Trigger(trigger);
				ImGui.PopID();
			}

			ImGui.PopID();
		}

		ImGui.Unindent();
		return true;
	}

	// The campaign variables it sets, shown only when it sets one, each a sentence with its operation and variable, Remove,
	// and Add after the last; whether there was any.
	private bool DrawWrites(short guid, bool locked) {
		var writes = Writes(guid);
		if (writes.Count == 0) {
			return false;
		}

		switch (_writes.Draw("Sets campaign variables", WriteLayer.Action, writes, Source, locked, _editor.WhyNoAddPair(guid))) {
			case SetWriteRequest set:
				_editor.RequestPair(guid, set.Slot, set.Variable, set.Op);
				break;
			case AddWriteRequest:
				_editor.RequestAddPair(guid);
				break;
			case RemoveWriteRequest remove:
				_editor.RequestRemovePair(guid, remove.Slot);
				break;
		}

		return true;
	}

	// The message it sends, by its text cut to fit, a choice of the file's lines with Remove, and when it is sent; whether
	// there was one.
	private bool DrawMessage(short guid, bool locked) {
		short id = ActionEditing.Message(Source, guid);
		if (id < 0) {
			return false;
		}

		int training = TriggerMessages.TrainingNumber(Layout);
		var messages = _messages.For(training);
		string text = _messages.Text(training, id) ?? NotInFile(training);
		ImGui.Spacing();
		ImGui.Text("Sends a message:");
		ImGui.Indent();
		var style = ImGui.GetStyle();
		float remove = ImGui.CalcTextSize("Remove").X + style.FramePadding.X * 2f + style.ItemSpacing.X;
		float width = ImGui.GetContentRegionAvail().X - remove;
		ImGui.SetNextItemWidth(width);
		ImGui.BeginDisabled(locked || messages == null);
		if (ImGui.BeginCombo("##message", Fit(text, width - ImGui.GetFrameHeight() - style.FramePadding.X * 2f))) {
			foreach (int line in messages?.Ids ?? []) {
				if (ImGui.Selectable($"{Fit(_messages.Text(training, line) ?? "", width)}##{line}", line == id) && line != id) {
					_editor.RequestMessage(guid, (short)line);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(locked);
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestMessage(guid, -1);
		}

		ImGui.EndDisabled();
		if (Fit(text, width) != text) {
			EditorText.Wrapped(text);
		}

		if (messages == null) {
			EditorText.Note($"The install has no str\\{TriggerMessages.FileName(training)}, so its lines cannot be listed.");
		}

		int posts = Posts(guid);
		string who = training == 0
			? "HQ sends it on the pilot and squad channel"
			: $"Training mission {training}'s instructor shows it";
		if (posts == 0) {
			EditorText.Emphasis("Never sent: a message goes out once for each campaign variable the trigger sets, and it sets none.");
		} else {
			EditorText.Note($"{who}, once for each campaign variable the trigger sets: {Times(posts)} here.");
		}

		ImGui.Unindent();
		return true;
	}

	// The Add... button and its menu: bring in a group, move a group on from an order, start a countdown (a new one or one of
	// the mission's), set a campaign variable, send a message.
	private void DrawAddMenu(short guid, IReadOnlyList<TriggerFollower> follows, bool locked) {
		ImGui.BeginDisabled(locked);
		if (ImGui.SmallButton("Add...")) {
			ImGui.OpenPopup("thenAdd");
		}

		ImGui.EndDisabled();
		if (!ImGui.BeginPopup("thenAdd")) {
			return;
		}

		if (ImGui.BeginMenu("Bring in a group")) {
			foreach (var group in _index.Groups) {
				var follower = new BringsInGroup(group.Guid);
				if (follows.Contains(follower)) {
					continue;
				}

				string? why = _editor.WhyNoFollow(follower);
				string now = group.Waiting ? $" (now waits on {MissionText.TriggerName(Layout, group.DeploymentAction)})" : "";
				if (ImGui.MenuItem($"{MissionText.GroupName(group)}{now}##{group.Guid}", string.Empty, false, why == null)) {
					_editor.RequestFollow(guid, follower);
				}

				Tooltip(why);
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("Move a group on from its order")) {
			foreach (var group in _index.Groups) {
				var slots = GroupOrders.Orders(Source, group.Guid);
				if (GroupOrders.Filled(slots) == 0 || !ImGui.BeginMenu($"{MissionText.GroupName(group)}##{group.Guid}")) {
					continue;
				}

				for (int slot = 0; slot < slots.Length; slot++) {
					var follower = new EndsOrder(group.Guid, slot);
					if (slots[slot] == -1 || follows.Contains(follower)) {
						continue;
					}

					short ending = Layout.Entry<MissionOrder22>(slots[slot])?.Fields.ActionRef ?? -1;
					string now = _index.Action(ending) != null ? $" (now ends on {MissionText.TriggerName(Layout, ending)})" : "";
					if (ImGui.MenuItem($"{OrderName(group.Guid, slot)}{now}##{slot}")) {
						_editor.RequestFollow(guid, follower);
					}
				}

				ImGui.EndMenu();
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("Start a countdown")) {
			if (ImGui.MenuItem("New countdown")) {
				_editor.RequestNewCountdown(guid);
			}

			foreach (var countdown in Layout.Entries<ActionTimer30>()) {
				var follower = new StartsCountdown(countdown.Guid);
				if (!follows.Contains(follower)
						&& ImGui.MenuItem($"{MissionText.CountdownName(Layout, countdown.Guid)} ({MissionText.CountdownSummary(_index, countdown.Fields)})##{countdown.Guid}")) {
					_editor.RequestFollow(guid, follower);
				}
			}

			ImGui.EndMenu();
		}

		string? noWrite = _editor.WhyNoAddPair(guid);
		if (ImGui.MenuItem("Set a campaign variable", string.Empty, false, noWrite == null)) {
			_editor.RequestAddPair(guid);
		}

		Tooltip(noWrite);
		int training = TriggerMessages.TrainingNumber(Layout);
		var messages = _messages.For(training);
		bool sends = ActionEditing.Message(Source, guid) >= 0;
		if (ImGui.BeginMenu("Send a message", messages != null && !sends)) {
			float width = ScaledImGui.Scaled(PropertiesPanel.PanelWidth);
			foreach (int line in messages?.Ids ?? []) {
				if (ImGui.MenuItem($"{Fit(_messages.Text(training, line) ?? "", width)}##{line}")) {
					_editor.RequestMessage(guid, (short)line);
				}
			}

			ImGui.EndMenu();
		}

		Tooltip(sends ? "It sends one already: change it under Sends a message." : null);
		ImGui.EndPopup();
	}

	// A Remove button that takes <follower> off the trigger, with <consequence> on hover.
	private void RemoveButton(short guid, TriggerFollower follower, bool locked, string? consequence) {
		ImGui.BeginDisabled(locked);
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestUnfollow(guid, follower);
		}

		ImGui.EndDisabled();
		Tooltip(consequence);
	}

	// A group's order slot as the Then list names it: "1st order (search/destroy)".
	private string OrderName(short group, int slot) {
		short order = GroupOrders.Orders(Source, group)[slot];
		string verb = Layout.Entry<MissionOrder22>(order)?.Fields is { } fields ? MissionText.OrderVerb(fields.Verb) : "missing";
		return $"{MissionText.Ordinal(slot + 1)} order ({verb})";
	}

	// The trigger's filled campaign variable writes, as its base sets them.
	private IReadOnlyList<(short Variable, short Op)> Writes(short guid) {
		var pairs = ActionEditing.CounterPairs(Source, guid);
		return pairs.Take(ActionEditing.FilledPairs(pairs)).ToList();
	}

	// How many times the message goes out: Action_Activate (00423430) queues it once per pair naming a campaign variable.
	private int Posts(short guid) => ActionEditing.CounterPairs(Source, guid).Count(pair => pair.Counter >= 0);

	private static string NotInFile(int training) => $"(a line not in str\\{TriggerMessages.FileName(training)})";

	private static string Times(int count) => count switch { 1 => "once", 2 => "twice", _ => $"{count} times" };

	private static void Tooltip(string? text) {
		if (text != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(text);
		}
	}

	/// <summary><paramref name="text"/> cut, with "...", to fit <paramref name="width"/> pixels in the current font.</summary>
	internal static string Fit(string text, float width) {
		const string more = "...";
		if (ImGui.CalcTextSize(text).X <= width) {
			return text;
		}

		int length = text.Length;
		while (length > 0 && ImGui.CalcTextSize(text[..length] + more).X > width) {
			length--;
		}

		return text[..length].TrimEnd() + more;
	}
}
