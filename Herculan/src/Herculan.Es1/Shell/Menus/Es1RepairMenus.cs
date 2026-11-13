using System.Globalization;
using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dat.Shell;
using Herculan.Es1.Shell.Hangar;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// REPAIR's screen object, class <c>RepairMenu</c> (vtable <c>00082006</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/repair-and-build.md#repair--screen-1900): screen 1900, a window down the right of the hangar's picture
/// of the selected HERC with its type, pilot, condition, the salvage a full repair needs and the salvage available, and
/// the buttons that step to another HERC, repair it, scrap it or open SYSTEM REPAIR and PREFERENCES.
/// </summary>
public sealed class Es1RepairMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1900.dlg</c>.</summary>
	public const short Screen = 1900;

	/// <summary>The CONDITION, SALVAGE REQUIRED and SALVAGE AVAILABLE readouts; HERC TYPE and PILOT are ARMING's ids.</summary>
	public const short LevelList = 1905, CostList = 1906, SalvageList = 1907;

	/// <summary>The five ids of <c>RepairMenu_CaseTable</c> (<c>00080e20</c>): HERCS' two arrows, REPAIR ALL, SCRAP and PROCEED.</summary>
	private const short NextHerc = 2004, PrevHerc = 2003, RepairAll = 1902, Scrap = 1901, Proceed = 1710;

	/// <summary>The target and the condition a full repair reaches.</summary>
	private const short FullRepair = 100;

	/// <summary>
	/// The word list <c>00081238</c> CONDITION's lines are numbered by: <c>eng\watext.bin</c> lines <c>0x7b</c> to
	/// <c>0x7f</c>, NOMINAL to DESTROYED, the five repair levels.
	/// </summary>
	public static readonly short[] LevelLines = [0x7b, 0x7c, 0x7d, 0x7e, 0x7f, -1];

	public Es1RepairMenu(Es1MenuContext context) : base(context) {
	}

	private Es1HangarBay? Bay => Context.Screens.Bay;

	/// <summary>
	/// <c>Menu_Open</c>'s change for 1900, after the focus (docs/es1/shell/menus.md#opening-a-menu--menu_open-0003969e,
	/// <c>0003a12f</c>): SCRAP, found in any state (<c>DUIWindow_FindControlById</c>), made inactive when
	/// <c>Hangar_HercCount</c> (<c>00080892</c>), less 1 when <see cref="Es1Hangar.HasMarkedHerc"/>, is below 2, the count
	/// taken as a word. With no hangar the count is 0 here, an engine guard.
	/// </summary>
	public static void SetOpeningControls(Es1DUIWindow window, Es1Hangar? hangar, Action<Es1DUIWindow, short> makeInactive) {
		short count = hangar?.HercCount ?? 0;
		if (hangar?.HasMarkedHerc() == true) {
			count--;
		}

		if (count < 2) {
			makeInactive(window, window.FindControlById(Scrap));
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>RepairMenu_OnMessage</c> (<c>0002f009</c>) (docs/es1/shell/repair-and-build.md#repair--screen-1900):
	/// the target looked up among the five ids of <c>RepairMenu_CaseTable</c> (<c>00080e20</c>): 2004 and 2003 (<c>0002f02f</c>)
	/// <c>Hangar_SelectNext</c>(0) for 2004 and <c>Hangar_SelectPrev</c>(0) otherwise, then <see cref="RefreshHerc"/>;
	/// 1902 (<c>0002f05b</c>) <see cref="RepairAllSelected"/>; 1901 (<c>0002f0fa</c>) the target 4444; 1710
	/// (<c>0002f105</c>) <c>CareerRecord_SaveCurrent</c> (<see cref="Es1ShellMission.SaveCurrentRecord"/>). REPAIR (1920)
	/// and PREFS (1910) pass to <see cref="Es1ShellScreens.HandleMessage"/> as screens. The HERCS cases, which wait, are
	/// <see cref="OnMessageWaiting"/>'s, as is REPAIR ALL's.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		switch (message.Target) {
			case Scrap:
				message = message with { Target = Es1ScrapAlert.Screen };
				break;
			case Proceed:
				Context.Mission.SaveCurrentRecord();
				break;
		}
	}

	/// <summary>The HERCS cases (<c>0002f02f</c>), <see cref="StepHerc"/>, and REPAIR ALL's, <see cref="RepairAllSelected"/>, which wait; any other <see cref="OnMessage"/>.</summary>
	public override IEnumerable<Es1ShellWait> OnMessageWaiting(StrongBox<Es1MenuMessage> message) => message.Value.Target switch {
		NextHerc or PrevHerc => StepHerc(message.Value.Target == NextHerc),
		RepairAll => RepairAllSelected(),
		_ => base.OnMessageWaiting(message),
	};

	/// <summary>
	/// The HERCS cases (<c>0002f02f</c>): <c>Hangar_SelectNext</c>(0) for 2004 and <c>Hangar_SelectPrev</c>(0) otherwise,
	/// then <see cref="RefreshHerc"/>. The waits are the wipes'. With no bay the selection is not stepped, an engine guard.
	/// </summary>
	private IEnumerable<Es1ShellWait> StepHerc(bool next) {
		if (Bay is { } bay) {
			foreach (var wait in next ? bay.SelectNext(0) : bay.SelectPrev(0)) {
				yield return wait;
			}
		}

		RefreshHerc();
	}

	/// <summary>
	/// REPAIR ALL's case (<c>0002f05b</c>) (docs/es1/shell/repair-and-build.md#repair-all): <c>Repair_AutoSelected</c>
	/// (<c>00029bdd</c>), which is <c>Repair_Auto</c>(<c>Career_Salvage</c>, <c>Hangar_SelectedSlot</c>)
	/// (<see cref="Es1CareerAfterMission.RepairAuto"/>); <c>Hangar_ShowSlot</c>(<c>Hangar_SelectedSlot</c>)
	/// (<see cref="Es1HangarBay.ShowSlot"/>), whose waits are its scripts'; CONDITION's line <see cref="Es1RepairCosts.LevelForCondition"/>
	/// of the HERC's overall condition, 0 for no HERC (slot <c>+0x24</c>); <see cref="RefreshSalvage"/>; and
	/// <c>Roster_ForEachPilotNoOp</c> (<see cref="Es1CareerRoster.ForEachPilotNoOp"/>). With no hangar nothing is repaired
	/// or shown, an engine guard.
	/// </summary>
	private IEnumerable<Es1ShellWait> RepairAllSelected() {
		var mission = Context.Mission;
		if (mission.Hangar is { } hangar) {
			new Es1CareerAfterMission(mission, Context.Data).RepairAuto((uint)mission.Roster.Salvage, hangar.SelectedSlot, hangar);
			if (Bay is { } bay) {
				foreach (var wait in bay.ShowSlot(hangar.SelectedSlot)) {
					yield return wait;
				}
			}
		}

		FindList(LevelList)?.SelectLine(SelectedLevel());
		RefreshSalvage();
		Es1CareerRoster.ForEachPilotNoOp();
	}

	/// <summary>
	/// <c>RepairMenu_RefreshHerc</c> (<c>0002ee70</c>)(menu): the pilot of the selected slot's HERC made
	/// <c>CareerRecord_Current</c>'s current pilot (<see cref="Es1CareerRecord.SelectPilot"/> of
	/// <see cref="Es1CareerRecord.FindPilotBySlot"/>); PILOT selects <see cref="Es1ArmingMenu.PilotLine"/>(the roster's
	/// current record, that entry), HERC TYPE the HERC's chassis, −1 for none, and CONDITION
	/// <see cref="Es1RepairCosts.LevelForCondition"/> of its overall condition, 0 for none, each with slot <c>+0x24</c>;
	/// then <see cref="RefreshSalvage"/>. With no record or no hangar the pilot is not selected, engine guards.
	/// </summary>
	public void RefreshHerc() {
		var mission = Context.Mission;
		var hangar = mission.Hangar;
		short slot = hangar?.SelectedSlot ?? -1;
		short entry = -1;
		if (mission.CurrentRecord is { } record && hangar != null) {
			entry = record.FindPilotBySlot(slot);
			record.SelectPilot(entry, hangar);
		}

		FindList(Es1ArmingMenu.PilotList)?.SelectLine(Es1ArmingMenu.PilotLine(mission.Roster.CurrentRecord, entry));
		FindList(Es1ArmingMenu.ChassisList)?.SelectLine(hangar?.HercIn(slot)?.Chassis ?? -1);
		FindList(LevelList)?.SelectLine(SelectedLevel());
		RefreshSalvage();
	}

	/// <summary>
	/// <c>RepairMenu_RefreshSalvage</c> (<c>0002ecc0</c>)(menu): SALVAGE AVAILABLE's lines set (slot <c>+0x44</c>) to
	/// <see cref="Es1MenuBuild.NumberLines"/>(<c>Career_Salvage</c>, 1907) and line 0 selected; SALVAGE REQUIRED's to
	/// <see cref="Es1MenuBuild.NumberLines"/>(<see cref="Es1RepairCosts.HercCost"/>(100, the HERC), 1906) while the HERC's
	/// overall condition is below 100, and of 0 otherwise or for no HERC, and line 0 selected. Retail keeps the two arrays
	/// at <c>00080e18</c> and <c>00080e1c</c>; here the lists hold them.
	/// </summary>
	public void RefreshSalvage() {
		var data = Context.Data;
		var salvage = FindList(SalvageList);
		salvage?.SetLines(Es1MenuBuild.NumberLines(data, Context.Mission.Roster.Salvage, SalvageList));
		salvage?.SelectLine(0);
		var herc = SelectedHerc;
		int cost = herc != null && herc.OverallCondition() < FullRepair ? data.RepairCosts.HercCost(FullRepair, herc) : 0;
		var required = FindList(CostList);
		required?.SetLines(Es1MenuBuild.NumberLines(data, cost, CostList));
		required?.SelectLine(0);
	}

	/// <summary><c>Hangar_SelectedSlot</c>'s HERC, none for slot −1, an empty slot or no hangar.</summary>
	private Es1HercRecord? SelectedHerc => Context.Mission.Hangar is { } hangar ? hangar.HercIn(hangar.SelectedSlot) : null;

	/// <summary><see cref="Es1RepairCosts.LevelForCondition"/> of the selected HERC's overall condition, of 0 for none.</summary>
	private short SelectedLevel() => Es1RepairCosts.LevelForCondition(SelectedHerc?.OverallCondition() ?? 0);

	/// <summary>Slot <c>+4</c>, <c>RepairMenu_Dtor</c> (<c>000406a7</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// PREFERENCES' screen object, class <c>RepairPrefsAlert</c> (vtable <c>0008202c</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/repair-and-build.md#preferences--screen-1910): screen 1910, the choice of which HERCs the shell
/// repairs on its own after a mission, a check tile beside each of its three buttons.
/// </summary>
public sealed class Es1RepairPrefsAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1910.dlg</c>.</summary>
	public const short Screen = 1910;

	/// <summary>The buttons REPAIR ALL, REPAIR MINE and AUTO REPAIR, which choose the modes 0, 1 and 2.</summary>
	private const short FirstButton = 1911, LastButton = 1913;

	/// <summary>The kind-16 tiles beside the buttons, one per mode in the same order.</summary>
	public const short FirstTile = 1914, LastTile = 1916;

	public Es1RepairPrefsAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// The word <c>Menu_BuildFromDlg</c> gives tile <paramref name="id"/> (<c>0003ad45</c>, <c>0003ad65</c>,
	/// <c>0003ad87</c>, for 1914 to 1916): 1 when <paramref name="mode"/>, <c>Career_AutoRepairMode</c> (<c>0007fcf8</c>),
	/// is the tile's mode, and 0 otherwise.
	/// </summary>
	public static short TileWord(short id, short mode) => mode == id - FirstTile ? (short)1 : (short)0;

	/// <summary>
	/// Slot <c>+8</c>, <c>RepairPrefsAlert_OnMessage</c> (<c>0003052f</c>): for 1911, 1912 or 1913,
	/// <c>Career_AutoRepairMode</c> (<see cref="Es1CareerRoster.AutoRepairMode"/>) set to 0, 1 or 2; the tiles 1914, 1915
	/// and 1916, each found with <c>DUIWindow_FindEnabledControlById</c>, given 1 for the button's and 0 for the others
	/// with slot <c>+0x24</c> (<see cref="Es1MTGfxListBox.SetFrame"/>); and the target 1900, back to REPAIR. Any other
	/// target does nothing. Retail calls the slot of whatever control index −1 reads when a tile is missing; here that
	/// tile is skipped, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target is < FirstButton or > LastButton) {
			return;
		}

		short mode = (short)(message.Target - FirstButton);
		Context.Mission.Roster.AutoRepairMode = mode;
		for (short id = FirstTile; id <= LastTile; id++) {
			if (Window is { } window && window.FindEnabledControlById(id) is var index and >= 0
					&& window.Controls[index] is Es1MTGfxListBox tile) {
				tile.SetFrame(TileWord(id, mode));
			}
		}

		message = message with { Target = Es1RepairMenu.Screen };
	}

	/// <summary>Slot <c>+4</c>, <c>RepairPrefsAlert_Dtor</c> (<c>0004075e</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// SCRAP's screen object, class <c>ScrapAlert</c> (vtable <c>00081e06</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/repair-and-build.md#scrap--screen-4444): screen 4444, the warning that gives the salvage the selected
/// HERC will yield, whose SCRAP scraps it and whose CANCEL goes back to REPAIR.
/// </summary>
public sealed class Es1ScrapAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu4444.dlg</c>.</summary>
	public const short Screen = 4444;

	/// <summary>The SCRAP button's target; CANCEL's 1900 passes to <see cref="Es1ShellScreens.HandleMessage"/> as a screen.</summary>
	private const short ScrapButton = 100;

	/// <summary>The characters of the formatted value copied over the text's <c>#</c>.</summary>
	private const int ValueWidth = 3;

	public Es1ScrapAlert(Es1MenuContext context) : base(context) {
	}

	private Es1HangarBay? Bay => Context.Screens.Bay;

	/// <summary>
	/// <c>Hangar_FormatScrapValue</c> (<c>0002a1aa</c>)(<paramref name="text"/>), which <c>Menu_BuildFromDlg</c> applies to
	/// menu 4444's kind-1 text holding <c>#</c>: <see cref="Es1RepairCosts.ScrapValue"/> (<c>Herc_FrameValue</c>,
	/// <c>00038d6c</c>) of the selected slot's HERC, divided unsigned by 1000, formatted with <c>%3d</c> (<c>00080940</c>),
	/// and its first three characters copied over the first <c>#</c> and the two characters after it. Retail reads the
	/// selected slot's HERC with no test and writes past a text that ends sooner; with no HERC the text is kept here, and a
	/// shorter text keeps its length, engine guards.
	/// </summary>
	public static string FormatValue(string text, Es1Hangar? hangar, Es1RepairCosts costs) {
		int at = text.IndexOf('#', StringComparison.Ordinal);
		if (at < 0 || hangar?.HercIn(hangar.SelectedSlot) is not { } herc) {
			return text;
		}

		string value = ((uint)costs.ScrapValue(herc) / 1000).ToString(CultureInfo.InvariantCulture).PadLeft(ValueWidth);
		var chars = text.ToCharArray();
		for (int i = 0; i < ValueWidth && at + i < chars.Length; i++) {
			chars[at + i] = value[i];
		}

		return new string(chars);
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>ScrapAlert_OnMessage</c> (<c>0002f111</c>), for 100 only: the selected HERC scrapped
	/// (<see cref="Es1HangarBay.ScrapSelectedHerc"/>); the slot then selected kept and the pilot
	/// <see cref="Es1CareerRecord.FindPilotBySlot"/> finds for it; the scripts given their bays
	/// (<see cref="Es1HangarBay.AssignScripts"/>); the draw page wiped onto the visible one
	/// (<see cref="Es1ScreenBlinds.Copy"/>(8, the visible page, the draw page)); that pilot made the current pilot
	/// (<see cref="Es1CareerRecord.SelectPilot"/>), and the kept slot selected again when it is −1; and the target 1900,
	/// back to REPAIR. The waits are the scrap's and the wipes'. With no bay nothing is scrapped, given its bay or wiped,
	/// and with no record or hangar no pilot is selected, engine guards.
	/// </summary>
	public override IEnumerable<Es1ShellWait> OnMessageWaiting(StrongBox<Es1MenuMessage> message) {
		if (message.Value.Target != ScrapButton) {
			yield break;
		}

		var mission = Context.Mission;
		var bay = Bay;
		if (bay != null) {
			foreach (var wait in bay.ScrapSelectedHerc()) {
				yield return wait;
			}
		}

		var hangar = mission.Hangar;
		short slot = hangar?.SelectedSlot ?? -1;
		short entry = mission.CurrentRecord?.FindPilotBySlot(slot) ?? -1;
		if (bay != null) {
			var display = bay.Surface.Display;
			foreach (var wait in bay.AssignScripts().Concat(bay.Blinds.Copy(8, display.VisiblePage, display.DrawPage))) {
				yield return wait;
			}
		}

		if (hangar != null) {
			mission.CurrentRecord?.SelectPilot(entry, hangar);
			if (entry == -1) {
				hangar.SelectedSlot = slot;
			}
		}

		message.Value = message.Value with { Target = Es1RepairMenu.Screen };
	}

	/// <summary>Slot <c>+4</c>, <c>ScrapAlert_Dtor</c> (<c>0003fbac</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// SALVAGE's screen object, class <c>SalvageAllocAlert</c> (vtable <c>00082050</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/repair-and-build.md#salvage--screen-2010): screen 2010, a window with ACCEPT and CANCEL. What opens it
/// is repair-and-build.md's Open; this engine adds no way in.
/// </summary>
public sealed class Es1SalvageAllocAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu2010.dlg</c>.</summary>
	public const short Screen = 2010;

	public Es1SalvageAllocAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>SalvageAllocAlert_OnMessage</c> (<c>00030ed3</c>): it answers the message's target and changes
	/// nothing, so ACCEPT's 100 and CANCEL's 2000 pass to <see cref="Es1ShellScreens.HandleMessage"/> as they are.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
	}

	/// <summary>Slot <c>+4</c>, <c>SalvageAllocAlert_Dtor</c> (<c>00040815</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
