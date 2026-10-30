using Herculan.Es1.Input;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The service bay's screen object, class <c>ServiceBayMenu</c> (vtable <c>00081fc6</c>)
/// (docs/es1/shell/between-missions.md#the-service-bays-bar): screens 1700, the bay before a mission, and 1710, the bay
/// after one, each the strip of five buttons along the bottom of the hangar's picture. The bay's own display and the
/// screens its buttons open, ARMING, CREW, REPAIR and BUILD, are the hangar's, S4's.
/// </summary>
public sealed class Es1ServiceBayMenu : Es1Menu {
	/// <summary>The bay before a mission, <c>dlg\menu1700.dlg</c>.</summary>
	public const short Screen = 1700;

	/// <summary>The bay after a mission, <c>dlg\menu1710.dlg</c>.</summary>
	public const short AfterScreen = Es1CareerMenu.ServiceBay;

	/// <summary>The controls of the seven ids of the table at <c>00080d28</c>.</summary>
	private const short Arming = 1800, MainMenu = 1000, Career = Es1CareerMenu.Screen, NextMission = 1499, Briefing = 1500, Build = 2000,
		BeginMission = 100;

	/// <summary>The arming screen ARMING opens while <c>Shell_LowMemory</c> is set.</summary>
	private const short LowMemoryArming = 1820;

	/// <summary>The warnings: the player's HERC disabled, a squadmate's disabled, and the service bays filled.</summary>
	public const short PlayerDisabledAlert = 7770, AnyDisabledAlert = 7771, BaysFullAlert = 7772;

	/// <summary>The screens BEGIN MISSION > goes to: <c>UnAssignedAlert</c> and <c>UnArmedAlert</c>.</summary>
	private const short NoHercAlert = Es1UnAssignedAlert.Screen, UnarmedAlert = Es1UnArmedAlert.Screen;

	/// <summary>The hangar's HERC count, less the marked one, at which BUILD warns that the bays are full.</summary>
	private const short FullHangar = 8;

	/// <summary>The message kinds the bar puts up: a mission's talking head, a new campaign's, and the last word before a mission.</summary>
	public const short BriefingKind = 1, CampaignKind = 5, LaunchKind = 6;

	/// <summary>The <c>.ANM</c> MAIN MENU and CAREER put into <c>000aacfe</c>, the string at <c>00080994</c>.</summary>
	private const string MenuAnm = "anm\\esiege2.anm";

	/// <summary>The lines of <c>eng\watext.bin</c> <see cref="SetButtons"/> gives BEGIN MISSION > and BRIEFING.</summary>
	private const short BeginLine = 0xc0, BriefingMoreLine = 0xc3, BeginMoreLine = 0xc1, BriefingLine = 0xc2;

	public Es1ServiceBayMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// <c>Menu_Open</c>'s case for 1700 in <c>Shell_Mode</c> 1 (disassembly <c>0003969e</c>, after 1820's): with the record's
	/// byte <c>+1</c> 0, the briefing not yet seen from the war map, BEGIN MISSION > (100) takes line <c>0xc0</c> and BRIEFING
	/// (1500) line <c>0xc3</c> through their slot <c>+0x3c</c> (<see cref="Es1DUIControl.SetTextFromTable"/>), BEGIN
	/// MISSION > is made inactive and the byte is set to 0 again; otherwise BEGIN MISSION > takes line <c>0xc1</c> and
	/// BRIEFING line <c>0xc2</c>. <paramref name="record"/> is the roster's record its word <c>+7</c> names. Each control
	/// is found with <c>DUIWindow_FindControlById</c>; a missing one is passed over, an engine guard.
	/// </summary>
	public static void SetButtons(Es1DUIWindow window, HercWorks.Core.Data.File.Dat.Shell.Es1CareerRecord record,
			Action<Es1DUIWindow, short> makeInactive) {
		if (record.Byte1 == 0) {
			Es1CareerMenu.SetText(window, BeginMission, BeginLine);
			Es1CareerMenu.SetText(window, Briefing, BriefingMoreLine);
			makeInactive(window, window.FindControlById(BeginMission));
			record.Byte1 = 0;
		} else {
			Es1CareerMenu.SetText(window, BeginMission, BeginMoreLine);
			Es1CareerMenu.SetText(window, Briefing, BriefingLine);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>ServiceBayMenu_OnMessage</c> (<c>0002d327</c>) (docs/es1/shell/between-missions.md#the-bars-buttons):
	/// by the message's target, the case of the table at <c>00080d28</c> (cases at <c>00080d44</c>); nothing for any other
	/// id, CREW (1610) and REPAIR (1900) among them. Both bars share it.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var mission = Context.Mission;
		switch (message.Target) {
			case Arming:
				// 0002d34d: LowMemoryArming while the byte Shell_LowMemory (0007fd09) is set, which this engine never sets
				// (Es1ShellHost's step 6), so the target stays 1800.
				break;
			case MainMenu:
				// 0002d368.
				screens.DeleteMissionMap();
				screens.AnmName = MenuAnm;
				screens.AnmMode = 2;
				break;
			case Career:
				// 0002d38b.
				screens.DeleteMissionMap();
				screens.ReturnScreen = AfterScreen;
				screens.AnmName = MenuAnm;
				screens.AnmMode = 2;
				break;
			case NextMission:
				// 0002d3b7.
				if (mission.PlayerHercDisabled()) {
					message = message with { Target = PlayerDisabledAlert };
				} else if (mission.AnyHercDisabled()) {
					message = message with { Target = AnyDisabledAlert };
				} else {
					MoveToNextMission(Context, ref message);
				}

				break;
			case Briefing:
				// 0002d4a6.
				BriefingPressed(ref message);
				break;
			case Build:
				// 0002d52f: the hangar's HERC count 00080892, less 1 when Hangar_HasMarkedHerc answers 1. With no hangar the count
				// reads 0, an engine guard.
				if (mission.Hangar is { } hangar && hangar.HercCount - (hangar.HasMarkedHerc() ? 1 : 0) >= FullHangar) {
					message = message with { Target = BaysFullAlert };
				}

				break;
			case BeginMission:
				BeginMissionPressed(ref message);
				break;
		}
	}

	/// <summary>
	/// NEXT MISSION >'s step 3 (docs/es1/shell/between-missions.md#next-mission), which <see cref="Es1AnyDisabledAlert"/>'s
	/// PROCEED also runs (<c>0002d3f5</c>, <c>AnyDisabledAlert_OnMessage</c> (<c>0002a581</c>)):
	/// <see cref="Es1ShellScreens.ReturnScreen"/> 1200; the word <c>+4</c> of the roster's record its word <c>+7</c> names
	/// 1700; <c>Roster_AdvanceCareer</c>(roster, the dword <c>000aad58</c>, which it does not read)
	/// (<see cref="Es1CareerRoster.AdvanceCareer"/>); <c>Career_StartMission</c>(roster, 0); <c>Career_Save</c>(roster, the
	/// roster's dword <c>+7</c>); <c>Shell_PrepareMission</c>; and when the record its word <c>+7</c> then names stands at
	/// position <c>+8</c> 0, the first of a new campaign, the message box's kind 5 and the target 1710.
	/// </summary>
	public static void MoveToNextMission(Es1MenuContext context, ref Es1MenuMessage message) {
		var roster = context.Mission.Roster;
		context.Screens.ReturnScreen = Career;
		roster.CurrentRecord.Word4 = Screen;
		roster.AdvanceCareer();
		roster.StartMission(0);
		roster.Save(roster.Current);
		context.Mission.Prepare(context.Screens);
		if (roster.CurrentRecord.Position == 0) {
			context.Globals.MessageBox.SetKind(CampaignKind);
			message = message with { Target = AfterScreen };
		}
	}

	/// <summary>
	/// BRIEFING, <c>0002d4a6</c> (docs/es1/shell/between-missions.md#briefing): the word <c>+4</c> of the roster's record its
	/// word <c>+7</c> names 1700 and <c>Career_Save</c>(roster, the roster's dword <c>+7</c>); then, while that record's byte
	/// <c>+1</c> is 0, the message box's kind 1 and the target 1499, whose case puts kind 1 up again; otherwise the target
	/// stays 1500, the war map.
	/// </summary>
	private void BriefingPressed(ref Es1MenuMessage message) {
		var roster = Context.Mission.Roster;
		roster.CurrentRecord.Word4 = Screen;
		roster.Save(roster.Current);
		if (roster.CurrentRecord.Byte1 == 0) {
			Context.Globals.MessageBox.SetKind(BriefingKind);
			message = message with { Target = NextMission };
		}
	}

	/// <summary>
	/// BEGIN MISSION >, <c>0002d55a</c> (docs/es1/shell/between-missions.md#starting-the-mission-from-the-bay), on
	/// <c>CareerRecord_Current</c>: 3333 when the player has no HERC (<see cref="Es1ShellMission.PlayerHasNoHerc"/>), else
	/// 3000 when any pilot's HERC is unarmed (<see cref="Es1ShellMission.AnyPilotUnarmed"/>), else <c>data\player.mec</c>
	/// written (<see cref="Es1ShellMission.WritePlayerMec"/>), the target 1700, the message box's kind 6, whose answer posts
	/// the mission, 9999, the mission map deleted and <see cref="Es1ShellScreens.MapRefresh"/> cleared.
	/// </summary>
	private void BeginMissionPressed(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var mission = Context.Mission;
		if (mission.PlayerHasNoHerc()) {
			message = message with { Target = NoHercAlert };
		} else if (mission.AnyPilotUnarmed()) {
			message = message with { Target = UnarmedAlert };
		} else {
			mission.WritePlayerMec();
			message = message with { Target = Screen };
			Context.Globals.MessageBox.SetKind(LaunchKind);
			screens.DeleteMissionMap();
			screens.MapRefresh = 0;
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The warning that a squadmate's HERC is disabled, class <c>AnyDisabledAlert</c> (vtable <c>00081de8</c>)
/// (docs/es1/shell/between-missions.md#the-bars-buttons): screen 7771, over the bay, WARNING! with CANCEL (99), which
/// passes to <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>, and PROCEED (1499).
/// </summary>
public sealed class Es1AnyDisabledAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu7771.dlg</c>.</summary>
	public const short Screen = Es1ServiceBayMenu.AnyDisabledAlert;

	/// <summary>PROCEED's id.</summary>
	private const short Proceed = 1499;

	public Es1AnyDisabledAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>AnyDisabledAlert_OnMessage</c> (<c>0002a581</c>): for PROCEED, NEXT MISSION >'s step 3
	/// (<see cref="Es1ServiceBayMenu.MoveToNextMission"/>), so the squad goes on with the disabled HERC.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target == Proceed) {
			Es1ServiceBayMenu.MoveToNextMission(Context, ref message);
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
