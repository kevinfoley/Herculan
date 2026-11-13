using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dat.Shell;
using Herculan.Engine.Settings;
using Herculan.Es1.Settings;
using Herculan.Es1.Shell.Hangar;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// SYSTEM REPAIR's screen object, class <c>SystemRepairMenu</c> (vtable <c>00080e5c</c>, <c>0x72</c> bytes)
/// (docs/es1/shell/repair-and-build.md#system-repair--screen-1920): screen 1920, the window that repairs the selected
/// HERC one system, one item and one level at a time, and undoes its changes on CANCEL.
/// </summary>
public sealed class Es1SystemRepairMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1920.dlg</c>.</summary>
	public const short Screen = 1920;

	/// <summary>The item list, the STATUS list, the ALLOCATED list and the cost of one level up.</summary>
	public const short ItemList = 1921, StatusList = 1922, AllocatedList = 1923, StepCostList = 1927;

	/// <summary>INT, EXT and WPN, whose ids are the status kinds they show.</summary>
	public const short InternalButton = Es1HercDiagrams.Internal, ExternalButton = Es1HercDiagrams.External,
		WeaponsButton = Es1HercDiagrams.MountKind;

	/// <summary>The other ids of <c>SystemRepairMenu_CaseTable</c> (<c>00080e6c</c>): +, −, the arrows and CANCEL; PROCEED is REPAIR's screen.</summary>
	private const short Plus = 106, Minus = 107, PrevItem = 101, NextItem = 102, Cancel = 99;

	/// <summary>The condition at which − refunds from the opening level, a full repair's.</summary>
	private const short FullCondition = 100;

	/// <summary>
	/// Whether PROCEED keeps the levels + and − set, setting no status before it shows the bay again; off, PROCEED sets the
	/// item to <see cref="Es1RepairCosts.TargetForLevel"/> of the level STATUS shows, which after a + is one level above
	/// what + paid for (docs/es1/shell/repair-and-build.md#cancel-and-proceed). From
	/// <see cref="Es1TweakSettingDefinitions.ProceedKeepsLevels"/>.
	/// </summary>
	public static bool ProceedKeepsLevels => TweakSettings.Current.GetSettingValue(Es1TweakSettingDefinitions.ProceedKeepsLevels);

	/// <summary>
	/// <c>SystemRepairMenu_Ctor</c> (<c>0002f1aa</c>)(0) (docs/es1/shell/repair-and-build.md#system-repair--screen-1920):
	/// the block made with <c>HercStatus_Ctor</c> (<c>0001a29c</c>), every word 100 but internal word 9, which keeps what
	/// the allocation left, 0 here; <see cref="ItemLines"/> none; <see cref="OpenedSalvage"/> <c>Career_Salvage</c>; the
	/// system internal and the item 0; the selected HERC's block copied over the window's
	/// (<see cref="CopySelectedStatus"/>); <see cref="Es1RepairCosts.StepCost"/>
	/// <see cref="Es1RepairCosts.UnitValueCost"/>(up, the item's condition, 0 for no HERC, the item, the system, the
	/// chassis, −1 for no HERC); and <see cref="Allocated"/> 0.
	/// </summary>
	public Es1SystemRepairMenu(Es1MenuContext context) : base(context) {
		for (int word = 0; word < OpenedStatus.Length; word++) {
			OpenedStatus[word] = word == Es1HercRecord.PilotConditionWord ? (short)0 : Es1HercRecord.FullStatus;
		}

		ItemLines = null;
		OpenedSalvage = context.Mission.Roster.Salvage;
		SystemShown = Es1HercDiagrams.Internal;
		Item = 0;
		CopySelectedStatus();
		context.Data.RepairCosts.StepCost = (short)context.Data.RepairCosts.UnitValueCost(true, ItemCondition(), Item, SystemShown,
			SelectedHerc?.Chassis ?? -1);
		Allocated = 0;
	}

	/// <summary><c>+0x26</c>, the system shown: <c>0x784</c> internal, <c>0x785</c> external, <c>0x786</c> weapons.</summary>
	public short SystemShown { get; private set; }

	/// <summary><c>+0x28</c>, the selected item, the line of 1921.</summary>
	public short Item { get; private set; }

	/// <summary><c>+0x2a</c>, <c>Career_Salvage</c> when the window opened.</summary>
	public int OpenedSalvage { get; }

	/// <summary><c>+0x2e</c>, the selected HERC's 66-byte status block when the window opened.</summary>
	public short[] OpenedStatus { get; } = new short[Es1HercRecord.StatusLength];

	/// <summary><c>+0x70</c>, an <c>int16</c>: the salvage allocated in the window.</summary>
	public short Allocated { get; private set; }

	/// <summary>
	/// <c>SystemRepair_ItemLines</c> (<c>00080e10</c>): the system's lines INT, EXT or WPN last gave 1921, or the lines +
	/// fetched; none (0) from the constructor until then. <c>es2_xref.py --decode</c> finds its accesses only in the
	/// constructor and <see cref="OnMessage"/>, so it is kept on the window here.
	/// </summary>
	private string?[]? ItemLines { get; set; }

	private Es1HangarBay? Bay => Context.Screens.Bay;

	/// <summary>
	/// <c>Hangar_CopySelectedStatus</c> (<c>0002f3e9</c>) as the constructor uses it: the selected HERC's block copied over
	/// <see cref="OpenedStatus"/>. With no HERC retail copies 66 bytes from the address its answer, the slot number, names
	/// (repair-and-build.md's Open); nothing is copied here and the block stays <c>HercStatus_Ctor</c>'s, an engine guard.
	/// </summary>
	private void CopySelectedStatus() {
		if (SelectedHerc is { } herc) {
			Array.Copy(herc.Status, OpenedStatus, Math.Min(herc.Status.Length, OpenedStatus.Length));
		}
	}

	/// <summary>
	/// <c>Menu_Open</c>'s change for 1920, before the focus (<c>000397d6</c>), which <see cref="Refresh"/> repeats: +, found
	/// in any state (<c>DUIWindow_FindControlById</c>), made inactive when <paramref name="stepCost"/>, sign-extended, is
	/// above <paramref name="salvage"/> compared unsigned, and active otherwise, <i>changed</i> set either way. Retail
	/// indexes the controls with the −1 of a missing +; nothing is changed here then, an engine guard.
	/// </summary>
	public static void SetPlusState(Es1DUIWindow window, short stepCost, int salvage) {
		short index = window.FindControlById(Plus);
		if (index < 0) {
			return;
		}

		window.Controls[index].State = (uint)stepCost > (uint)salvage ? Es1DUIControl.Inactive : Es1DUIControl.Up;
		window.Controls[index].Changed = true;
	}

	/// <summary>
	/// <c>Hangar_SelectedComponentLines</c> (<c>000377cd</c>)(<paramref name="kind"/>), <c>Menu_ListLines</c>' case for
	/// 1921 and 1924 to 1926 (<c>0003752e</c>), which passes the id as the kind:
	/// <see cref="Es1HercDiagrams.BuildStatusLines"/>(the selected HERC, kind). With no HERC retail answers the selected
	/// slot as the lines' address; here no lines, an engine guard.
	/// </summary>
	public static string?[] SelectedComponentLines(Es1MenuContext context, short kind) {
		var hangar = context.Mission.Hangar;
		return hangar?.HercIn(hangar.SelectedSlot) is { } herc
			? Es1HercDiagrams.BuildStatusLines(herc, kind, context.Data.Text)
			: [null];
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>SystemRepairMenu_OnMessage</c> (<c>0002f8d4</c>)
	/// (docs/es1/shell/repair-and-build.md#system-repair--screen-1920): 1921's selected line read first, then the target
	/// looked up among the ten ids of <c>SystemRepairMenu_CaseTable</c> (<c>00080e6c</c>): 1921 (<c>0002f94b</c>)
	/// <see cref="SelectItem"/>; 1924 to 1926 (<c>0002f958</c>) the system the id, <see cref="ItemLines"/>
	/// <see cref="Es1MenuBuild.ListLines"/>(the id) given to 1921 with line 0 selected, then <see cref="SelectItem"/>; 106
	/// and 107 <see cref="Raise"/> and <see cref="Lower"/>; 101 and 102 (<c>00030224</c>, <c>00030270</c>) 1921's previous
	/// or next line, then <see cref="SelectItem"/>; 1900 <see cref="Proceed"/>; 99 <see cref="CancelChanges"/>. Retail
	/// reads a missing list's slot through the −1 index; a missing list is skipped here, and the line read first is the
	/// item, engine guards. 1900's case, which waits, is <see cref="OnMessageWaiting"/>'s.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var items = FindList(ItemList);
		short line = items?.GetValue() ?? Item;
		switch (message.Target) {
			case ItemList:
				SelectItem();
				break;
			case InternalButton:
			case ExternalButton:
			case WeaponsButton:
				SystemShown = message.Target;
				ItemLines = Es1MenuBuild.ListLines(Context, message.Target);
				items?.SetLines(ItemLines);
				items?.SelectLine(0);
				SelectItem();
				break;
			case Plus:
				Raise(line);
				break;
			case Minus:
				Lower(line);
				break;
			case PrevItem:
				items?.SelectPrev();
				SelectItem();
				break;
			case NextItem:
				items?.SelectNext();
				SelectItem();
				break;
			case Cancel:
				CancelChanges(ref message);
				break;
		}
	}

	/// <summary>
	/// <c>SystemRepairMenu_SelectItem</c> (<c>0002f517</c>)(menu): the item 1921's selected line; STATUS selects
	/// <see cref="Es1RepairCosts.LevelForCondition"/> of the item's condition, 0 for no HERC; ALLOCATED's lines
	/// <see cref="Es1MenuBuild.NumberLines"/>(<see cref="Allocated"/>, 1923) with line 0 selected; then
	/// <see cref="Refresh"/>. With no 1921 the item stays, an engine guard.
	/// </summary>
	private void SelectItem() {
		if (FindList(ItemList) is { } items) {
			Item = items.GetValue();
		}

		FindList(StatusList)?.SelectLine(Es1RepairCosts.LevelForCondition(ItemCondition()));
		ShowAllocatedLines();
		Refresh();
	}

	/// <summary>
	/// <c>SystemRepairMenu_Refresh</c> (<c>0002f680</c>)(menu): 1907's lines <see cref="Es1MenuBuild.NumberLines"/>(<c>Career_Salvage</c>,
	/// 1907) with line 0 selected; <see cref="Es1RepairCosts.StepCost"/> the cost of one level up of the item
	/// (<see cref="Es1RepairCosts.UnitValueCost"/>, as the constructor prices it); 1927's lines that cost with line 0
	/// selected; and + set by <see cref="SetPlusState"/>. Retail keeps 1907's array at <c>00080e18</c> and 1927's at
	/// <c>00080e1c</c>; here the lists hold them.
	/// </summary>
	private void Refresh() {
		var data = Context.Data;
		var costs = data.RepairCosts;
		int salvage = Context.Mission.Roster.Salvage;
		var salvageList = FindList(Es1RepairMenu.SalvageList);
		salvageList?.SetLines(Es1MenuBuild.NumberLines(data, salvage, Es1RepairMenu.SalvageList));
		salvageList?.SelectLine(0);
		costs.StepCost = (short)costs.UnitValueCost(true, ItemCondition(), Item, SystemShown, SelectedHerc?.Chassis ?? -1);
		var stepList = FindList(StepCostList);
		stepList?.SetLines(Es1MenuBuild.NumberLines(data, costs.StepCost, StepCostList));
		stepList?.SelectLine(0);
		if (Window is { } window) {
			SetPlusState(window, costs.StepCost, salvage);
		}
	}

	/// <summary>
	/// The + case (<c>0002fa17</c>) (docs/es1/shell/repair-and-build.md#and), with <i>L</i> the level STATUS shows:
	/// <see cref="Allocated"/> raised by <see cref="Es1RepairCosts.StepCost"/> and shown; the item set to
	/// <see cref="Es1RepairCosts.TargetForLevel"/>(<i>L</i>) (<see cref="Es1HercDiagrams.SetStatus"/>); with no
	/// <see cref="ItemLines"/>, the system's lines fetched into it and given to 1921; <paramref name="line"/>'s line
	/// rewritten (<see cref="FormatSelectedStatusLine"/>) and 1921, found in any state, marked changed; <i>L</i> − 1 above
	/// 0 selected in STATUS; the cost taken from <c>Career_Salvage</c> and from the current pilot's share; then
	/// <see cref="Refresh"/>.
	/// </summary>
	private void Raise(short line) {
		var costs = Context.Data.RepairCosts;
		var status = FindList(StatusList);
		short level = status?.GetValue() ?? 0;
		Allocated = (short)(Allocated + costs.StepCost);
		ShowAllocatedLines();
		if (SelectedHerc is { } herc) {
			Es1HercDiagrams.SetStatus(herc, Es1RepairCosts.TargetForLevel(level), Item, SystemShown);
		}

		if (ItemLines == null) {
			ItemLines = Es1MenuBuild.ListLines(Context, SystemShown);
			FindList(ItemList)?.SetLines(ItemLines);
		}

		FormatSelectedStatusLine(line);
		if (level > 0) {
			level--;
		}

		status?.SelectLine(level);
		Context.Mission.Roster.Salvage -= costs.StepCost;
		AddToShare(-costs.StepCost);
		Refresh();
	}

	/// <summary>
	/// The − case (<c>0002fd49</c>) (docs/es1/shell/repair-and-build.md#and), with <i>o</i> the item's condition in
	/// <see cref="OpenedStatus"/> (<see cref="OpenedCondition"/>) and <i>c</i> its condition on the HERC, 0 for no HERC:
	/// nothing unless <i>c</i> is above <i>o</i>. Otherwise, with <i>L</i> the level STATUS shows and <i>L</i>ₒ the level of
	/// <i>o</i>, <see cref="Es1RepairCosts.StepCost"/> the refund: one level up from <i>o</i> when <i>L</i> is <i>L</i>ₒ, or
	/// <i>L</i> + 1 is <i>L</i>ₒ and <i>c</i> is below 100, and one level down from <i>c</i> otherwise; the refund taken
	/// from <see cref="Allocated"/> and added to <c>Career_Salvage</c> and the current pilot's share; ALLOCATED shown;
	/// <i>L</i> + 1 for <i>c</i> below 100 selected in STATUS; the item set to <i>o</i> when <i>L</i> is now <i>L</i>ₒ, and
	/// to <see cref="Es1RepairCosts.FloorForLevel"/>(<i>L</i>) otherwise; <paramref name="line"/>'s line rewritten and 1921
	/// marked changed; then <see cref="Refresh"/>.
	/// </summary>
	private void Lower(short line) {
		var costs = Context.Data.RepairCosts;
		short opened = OpenedCondition();
		short current = ItemCondition();
		if (opened >= current) {
			return;
		}

		var status = FindList(StatusList);
		short level = status?.GetValue() ?? 0;
		short openedLevel = Es1RepairCosts.LevelForCondition(opened);
		short chassis = SelectedHerc?.Chassis ?? -1;
		costs.StepCost = level == openedLevel || (level + 1 == openedLevel && current < FullCondition)
			? (short)costs.UnitValueCost(true, opened, Item, SystemShown, chassis)
			: (short)costs.UnitValueCost(false, current, Item, SystemShown, chassis);
		Allocated = (short)(Allocated - costs.StepCost);
		Context.Mission.Roster.Salvage += costs.StepCost;
		AddToShare(costs.StepCost);
		ShowAllocatedLines();
		if (current < FullCondition) {
			level++;
		}

		status?.SelectLine(level);
		if (SelectedHerc is { } herc) {
			Es1HercDiagrams.SetStatus(herc, level == openedLevel ? opened : Es1RepairCosts.FloorForLevel(level), Item, SystemShown);
		}

		FormatSelectedStatusLine(line);
		Refresh();
	}

	/// <summary>
	/// PROCEED's case (<c>000302bc</c>) (docs/es1/shell/repair-and-build.md#cancel-and-proceed): nothing when
	/// <c>Career_Salvage</c> equals <see cref="OpenedSalvage"/>; otherwise the item set to
	/// <see cref="Es1RepairCosts.TargetForLevel"/> of the level STATUS shows, unless <see cref="ProceedKeepsLevels"/> is
	/// set, and the bay shown again (<see cref="Es1HangarBay.ShowSlot"/>(<c>Hangar_SelectedSlot</c>)), whose waits are its
	/// scripts'. The target stays 1900, REPAIR. With no STATUS no status is set, and with no bay or hangar nothing is
	/// shown, engine guards.
	/// </summary>
	private IEnumerable<Es1ShellWait> Proceed() {
		var mission = Context.Mission;
		if (mission.Roster.Salvage == OpenedSalvage) {
			yield break;
		}

		if (!ProceedKeepsLevels && FindList(StatusList) is { } status && SelectedHerc is { } herc) {
			Es1HercDiagrams.SetStatus(herc, Es1RepairCosts.TargetForLevel(status.GetValue()), Item, SystemShown);
		}

		if (Bay is { } bay && mission.Hangar is { } hangar) {
			foreach (var wait in bay.ShowSlot(hangar.SelectedSlot)) {
				yield return wait;
			}
		}
	}

	/// <summary>1900's case, <see cref="Proceed"/>, which waits; any other <see cref="OnMessage"/>.</summary>
	public override IEnumerable<Es1ShellWait> OnMessageWaiting(StrongBox<Es1MenuMessage> message) =>
		message.Value.Target == Es1RepairMenu.Screen ? Proceed() : base.OnMessageWaiting(message);

	/// <summary>
	/// CANCEL's case (<c>00030377</c>) (docs/es1/shell/repair-and-build.md#cancel-and-proceed): the current pilot's share
	/// raised by <see cref="OpenedSalvage"/> − <c>Career_Salvage</c>, <c>Career_Salvage</c> put back to
	/// <see cref="OpenedSalvage"/>, <see cref="OpenedStatus"/> copied over the selected HERC's block, and the target 1900,
	/// back to REPAIR.
	/// </summary>
	private void CancelChanges(ref Es1MenuMessage message) {
		var roster = Context.Mission.Roster;
		AddToShare(OpenedSalvage - roster.Salvage);
		roster.Salvage = OpenedSalvage;
		if (SelectedHerc is { } herc) {
			Array.Copy(OpenedStatus, herc.Status, Math.Min(herc.Status.Length, OpenedStatus.Length));
		}

		message = message with { Target = Es1RepairMenu.Screen };
	}

	/// <summary>
	/// <c>Hangar_FormatSelectedStatusLine</c> (<c>00030478</c>)(<paramref name="line"/>, <see cref="ItemLines"/>[line],
	/// the system): for a selected HERC, <see cref="Es1HercDiagrams.FormatStatusLine"/> of the line, which retail writes in
	/// the line's own buffer; the new line is stored in <see cref="ItemLines"/>, which 1921 holds, and 1921, found in any
	/// state (<c>DUIWindow_FindControlById</c>), is marked changed. Retail reads <see cref="ItemLines"/> with no test, even
	/// when the − case finds it 0, and the line past its end; such a line is left here, an engine guard.
	/// </summary>
	private void FormatSelectedStatusLine(short line) {
		if (SelectedHerc is { } herc && ItemLines is { } lines && line >= 0 && line < lines.Length && lines[line] is { } text) {
			lines[line] = Es1HercDiagrams.FormatStatusLine(herc, line, text, SystemShown);
		}

		if (Window is { } window && window.FindControlById(ItemList) is var index and >= 0) {
			window.Controls[index].Changed = true;
		}
	}

	/// <summary>ALLOCATED's lines <see cref="Es1MenuBuild.NumberLines"/>(<see cref="Allocated"/>, 1923) with line 0 selected; retail keeps the array at <c>00080e14</c>.</summary>
	private void ShowAllocatedLines() {
		var allocated = FindList(AllocatedList);
		allocated?.SetLines(Es1MenuBuild.NumberLines(Context.Data, Allocated, AllocatedList));
		allocated?.SelectLine(0);
	}

	/// <summary>
	/// <paramref name="amount"/> added to the pay share <c>+0x21</c> of <c>CareerRecord_Current</c>'s current pilot
	/// <c>+0x12</c>, the HERC's pilot since <see cref="Es1RepairMenu.RefreshHerc"/> selected it. Retail writes through entry
	/// −1, over the record's <c>+0x10</c>, when there is no current pilot; nothing is written here then, an engine guard.
	/// </summary>
	private void AddToShare(int amount) {
		if (Context.Mission.CurrentRecord is { } record && record.CurrentPilot is >= 0 and < Es1CareerRecord.PilotCount) {
			record.Pilots[record.CurrentPilot].PayShare += amount;
		}
	}

	/// <summary><c>Hangar_SelectedSlot</c>'s HERC, none for slot −1, an empty slot or no hangar.</summary>
	private Es1HercRecord? SelectedHerc => Context.Mission.Hangar is { } hangar ? hangar.HercIn(hangar.SelectedSlot) : null;

	/// <summary><c>Herc_GetStatus</c>(the selected HERC, the item, the system), 0 for no HERC.</summary>
	private short ItemCondition() => SelectedHerc is { } herc ? Es1HercDiagrams.GetStatus(herc, Item, SystemShown) : (short)0;

	/// <summary>
	/// <c>HercStatus_GetMenuHandlersCopy</c> (<c>000304c6</c>)(<see cref="OpenedStatus"/>, the item, the system), byte for
	/// byte <c>HercStatus_Get</c> (<c>00015896</c>): the item's condition when the window opened, read as
	/// <see cref="Es1HercDiagrams.GetStatus"/> reads a HERC's block.
	/// </summary>
	private short OpenedCondition() => Es1HercDiagrams.GetStatus(new Es1HercRecord { Status = OpenedStatus }, Item, SystemShown);

	/// <summary>Slot <c>+4</c>, <c>SystemRepairMenu_Dtor</c> (<c>0002f460</c>): its vtable, then <c>Menu</c>'s and the region's, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
