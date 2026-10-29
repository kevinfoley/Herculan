using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The trigger view's Then list: what happens when the trigger activates, every entry editable and an Add menu over them. A group it
/// brings in, with how every such group arrives; a group it moves on from an order; a countdown it starts, with its delay; the
/// campaign variables it sets (<see cref="CampaignVariableWrites"/>); and the message it sends, by its text. Each entry is
/// written in the record that holds the link (<see cref="TriggerFollowers"/>): the group's, the order slot's or the
/// countdown's, so removing a group or an order clears that link alone; the arrival, the writes and the message are the
/// trigger's own. A countdown is drawn, and removed, by <see cref="TriggerThenCountdowns"/>, which draws the Then list of a
/// countdown's own trigger through this one. What each does in play is
/// docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
/// </summary>
internal sealed class TriggerThenList {
	private readonly MissionIndex _index;
	private readonly ActionEditor _editor;
	private readonly PanelLinks _links;
	private readonly TriggerMessages _messages;
	private readonly CampaignVariableWrites _writes = new();
	private readonly TriggerThenCountdowns _countdowns;

	public TriggerThenList(MissionIndex index, ActionEditor editor, PanelLinks links, TriggerMessages messages) {
		_index = index;
		_editor = editor;
		_links = links;
		_messages = messages;
		_countdowns = new TriggerThenCountdowns(index, editor, links, DrawRunsOut, SummaryRunsOut);
	}

	private MissionDocument Source => _editor.Source;

	private MissionLayout Layout => _index.Layout;

	private MissionNames Names => _links.Names;

	// The widest a menu entry is drawn, in pixels: the Properties panel's width.
	private static float MenuWidth => ScaledImGui.Scaled(PropertiesPanel.PanelWidth);

	/// <summary>Row-10 GUID <paramref name="guid"/>'s Then list, of base <paramref name="fields"/>; every control disabled when <paramref name="locked"/>.</summary>
	public void Draw(short guid, MissionAction82 fields, bool locked) =>
		DrawList(guid, fields, locked, "Then:", "Nothing happens when it fires yet: Add... lists what can.", [guid]);

	/// <summary>Row-10 GUID <paramref name="guid"/>'s Then list, read-only, each record a link, as a trigger area's view lists it.</summary>
	public void DrawSummary(short guid, MissionAction82 fields) {
		ImGui.Text("Then:");
		ImGui.Indent();
		if (!DrawSummaryEntries(guid, fields, [guid])) {
			EditorText.Note("nothing happens when it fires");
		}

		ImGui.Unindent();
	}

	// The list under <heading>, with Add... beside it and <none> when it is empty; <chain> holds the triggers whose lists are
	// open around it, this one included.
	private void DrawList(short guid, MissionAction82 fields, bool locked, string heading, string none, HashSet<short> chain) {
		var follows = _index.Links(guid).Follows;
		ImGui.AlignTextToFramePadding();
		ImGui.Text(heading);
		ImGui.SameLine();
		DrawAddMenu(guid, follows, locked);
		ImGui.Indent();
		bool any = DrawGroups(guid, fields, follows.OfType<BringsInGroup>().ToList(), locked);
		any |= DrawOrders(guid, follows.OfType<EndsOrder>().ToList(), locked);
		any |= _countdowns.Draw(guid, follows.OfType<StartsCountdown>().ToList(), locked, chain);
		any |= DrawWrites(guid, locked);
		any |= DrawMessage(guid, locked);
		if (!any) {
			EditorText.Note(none);
		}

		ImGui.Unindent();
	}

	// The list of a countdown's own trigger <trigger>, under its countdown (TriggerThenCountdowns).
	private void DrawRunsOut(short trigger, bool locked, HashSet<short> chain) {
		if (Layout.Entry<MissionAction82>(trigger)?.Fields is { } fields) {
			DrawList(trigger, fields, locked || _editor.WhyFixed(trigger) != null, "when it runs out:",
				"Nothing happens when it runs out yet: Add... lists what can.", chain);
		}
	}

	// The same read-only.
	private void SummaryRunsOut(short trigger, HashSet<short> chain) {
		if (Layout.Entry<MissionAction82>(trigger)?.Fields is { } fields && !DrawSummaryEntries(trigger, fields, chain)) {
			EditorText.Note("nothing happens when it runs out");
		}
	}

	// The read-only list's entries, one line each, a countdown's own trigger's list nested under it; whether there was any.
	private bool DrawSummaryEntries(short guid, MissionAction82 fields, HashSet<short> chain) {
		var follows = _index.Links(guid).Follows;
		bool any = false;
		foreach (var follower in follows) {
			ImGui.PushID(follower.GetHashCode());
			switch (follower) {
				case BringsInGroup bring: {
					string arrives = $" arrives {MissionText.ArrivalKind(fields.Verb)}";
					_links.ShortLayoutGroup(bring.Group, EditorText.AfterRoom(arrives));
					EditorText.After(arrives);
					break;
				}
				case EndsOrder ends: {
					string movesOn = $" moves on from its {MissionText.Ordinal(ends.Slot + 1)} order";
					_links.ShortLayoutGroup(ends.Group, EditorText.AfterRoom(movesOn));
					EditorText.After(movesOn);
					break;
				}
				case StartsCountdown starts:
					_countdowns.DrawSummary(starts.Countdown, chain);
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

		return any;
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
			_links.LayoutGroup(bring.Group, EditorText.ButtonRoom("Remove"));
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

		// What the arrival does on hover of the closed choice, not under it: open, the last item is the list's.
		if (!EditorText.BeginCombo("##arrives", current)) {
			EditorText.Tooltip(MissionText.ArrivalNote(fields.Verb) + "\n\n" + (groups.Count > 1
				? "These groups arrive the same way: the arrival is set on the trigger, for every group it brings in."
				: "The arrival is set on the trigger: every group it brings in arrives the same way."));
		} else {
			foreach (short verb in MissionText.Arrivals) {
				string choice = MissionText.ArrivalChoice(verb);
				if (ImGui.Selectable(choice, choice == current) && choice != current) {
					_editor.RequestVerb(guid, verb);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (fields.Verb is < 0 or > ActionEditing.LastVerb) {
			EditorText.Note($"Stored as {fields.Verb}: the group arrives in place.");
		}

		ImGui.Unindent();
		return true;
	}

	// The order slots it ends, each the group's link and the order, with Remove; whether there was any.
	private bool DrawOrders(short guid, IReadOnlyList<EndsOrder> orders, bool locked) {
		if (orders.Count == 0) {
			return false;
		}

		EditorText.Wrapped("Moves these groups on to their next order:");
		ImGui.Indent();
		foreach (var ends in orders) {
			ImGui.PushID($"ends{ends.Group}.{ends.Slot}");
			// The order wraps short of Remove, so the button stays inside the panel, and takes at most half what the link leaves.
			string from = $", from its {OrderName(ends.Group, ends.Slot)}";
			float remove = EditorText.ButtonRoom("Remove");
			_links.ShortLayoutGroup(ends.Group,
				remove + MathF.Min(EditorText.TextRoom(from), (ImGui.GetContentRegionAvail().X - remove) / 2f));
			ImGui.SameLine(0f, 0f);
			ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - remove);
			EditorText.Wrapped(from);
			ImGui.PopTextWrapPos();
			ImGui.SameLine();
			RemoveButton(guid, ends, locked, null);
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
		float width = ImGui.GetContentRegionAvail().X - EditorText.ButtonRoom("Remove");
		ImGui.SetNextItemWidth(width);
		ImGui.BeginDisabled(locked || messages == null);
		if (EditorText.BeginCombo("##message", text)) {
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
				string now = group.Waiting ? $" (now waits on {TriggerNumber(group.DeploymentAction)})" : "";
				if (ImGui.MenuItem($"{NameText.Fit(Names.Group(group.Guid).Full + now, MenuWidth)}##{group.Guid}", string.Empty, false, why == null)) {
					_editor.RequestFollow(guid, follower);
				}

				EditorText.Tooltip(why);
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("Move a group on from its order")) {
			foreach (var group in _index.Groups) {
				var slots = GroupOrders.Orders(Source, group.Guid);
				if (GroupOrders.Filled(slots) == 0 || !ImGui.BeginMenu($"{NameText.Fit(Names.Group(group.Guid).Full, MenuWidth)}##{group.Guid}")) {
					continue;
				}

				for (int slot = 0; slot < slots.Length; slot++) {
					var follower = new EndsOrder(group.Guid, slot);
					if (slots[slot] == -1 || follows.Contains(follower)) {
						continue;
					}

					short ending = Layout.Entry<MissionOrder22>(slots[slot])?.Fields.ActionRef ?? -1;
					string now = _index.Action(ending) != null ? $" (now ends on {TriggerNumber(ending)})" : "";
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
						&& ImGui.MenuItem($"{NameText.Fit(Names.Countdown(countdown.Guid).Full, MenuWidth)}##{countdown.Guid}")) {
					_editor.RequestFollow(guid, follower);
				}
			}

			ImGui.EndMenu();
		}

		string? noWrite = _editor.WhyNoAddPair(guid);
		if (ImGui.MenuItem("Set a campaign variable", string.Empty, false, noWrite == null)) {
			_editor.RequestAddPair(guid);
		}

		EditorText.Tooltip(noWrite);
		int training = TriggerMessages.TrainingNumber(Layout);
		var messages = _messages.For(training);
		bool sends = ActionEditing.Message(Source, guid) >= 0;
		if (ImGui.BeginMenu("Send a message", messages != null && !sends)) {
			float width = MenuWidth;
			foreach (int line in messages?.Ids ?? []) {
				if (ImGui.MenuItem($"{Fit(_messages.Text(training, line) ?? "", width)}##{line}")) {
					_editor.RequestMessage(guid, (short)line);
				}
			}

			ImGui.EndMenu();
		}

		EditorText.Tooltip(sends ? "It sends one already: change it under \"Sends a message\"." : null);
		ImGui.EndPopup();
	}

	// A Remove button that takes <follower> off the trigger, with <consequence> on hover.
	private void RemoveButton(short guid, TriggerFollower follower, bool locked, string? consequence) {
		ImGui.BeginDisabled(locked);
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestUnfollow(guid, follower);
		}

		ImGui.EndDisabled();
		EditorText.Tooltip(consequence);
	}

	// A group's order slot as the Then list names it: "1st order (Search and destroy)".
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

	// Row-10 GUID <guid> by its number alone, for a note that a link already names in full: "trigger 2".
	private string TriggerNumber(short guid) => MissionText.NumberIn(Layout, 10, guid) is { } number ? $"trigger {number}" : "a trigger";

	private static string Fit(string text, float width) => NameText.Fit(text, width);
}
