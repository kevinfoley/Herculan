using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Es1.Input;
using Herculan.Es1.Shell.MessageBox;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The war map's screen object, class <c>WarMapMenu</c> (vtable <c>00080bc6</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/war-map.md): screens 1501, a single mission's, and 1500, a career's, the mission's briefing in a box
/// under the map, its page arrows, CANCEL, ARMING or SERVICE BAY and BEGIN MISSION >. The map, its area 1502 and its six
/// buttons are the mission map's, S5's.
/// </summary>
public sealed class Es1WarMapMenu : Es1Menu {
	/// <summary>A single mission's war map, <c>dlg\menu1501.dlg</c>.</summary>
	public const short Screen = 1501;

	/// <summary>A career's war map, <c>dlg\menu1500.dlg</c>.</summary>
	public const short CareerScreen = 1500;

	/// <summary>The keyboard's mode the constructor sets, <c>INT 15h</c>'s.</summary>
	private const int KeyboardMode = Es1Keyboard.Int15Mode;

	/// <summary>
	/// <c>WarMap_MapKeys</c> (<c>000809ac</c>), the five scan codes the constructor gives <c>KeyQueue_Subscriber</c>:
	/// <c>Space</c>, <c>,</c>, <c>.</c>, <c>=</c> and <c>-</c>, the keys that work the mission map.
	/// </summary>
	private static readonly byte[] MapKeys = [0x39, 0x33, 0x34, 0x0d, 0x0c];

	/// <summary>The controls of the twelve ids of the table at <c>00080bdc</c>.</summary>
	private const short ZoomIn = 108, ZoomOut = 109, MapUp = 1503, MapDown = 1504, MapLeft = 1505, MapRight = 1506,
		PageUp = 101, PageDown = 102, Cancel = 99, Arming = 1800, ServiceBay = 1700, BeginMission = 100;

	/// <summary>The map area, the kind-14 hotspot whose pressed byte the map's buttons set.</summary>
	private const short MapArea = 1502;

	/// <summary>The screens BEGIN MISSION > goes to: <c>UnAssignedAlert</c>, <c>UnArmedAlert</c> and the loading screen.</summary>
	private const short NoHercAlert = 3333, UnarmedAlert = 3000;

	/// <summary>The career menu, CANCEL's target in a career.</summary>
	private const short CareerMenu = 1200;

	/// <summary>The <c>.ANM</c> CANCEL puts into <c>000aacfe</c>, the string at <c>00080994</c>.</summary>
	private const string CancelAnm = "anm\\esiege2.anm";

	private readonly Es1ShellDialog _dialog;

	/// <summary>
	/// <c>WarMapMenu_Ctor</c> (<c>0002bbbd</c>)(0)'s step 1, the screen object built over <c>Menu</c>; the rest is
	/// <see cref="Load"/>'s, which screen 1500's and 1501's case runs next.
	/// </summary>
	/// <param name="dialog"><c>Shell_Dialog</c> (<c>0009afc8</c>), which the briefing is loaded into.</param>
	public Es1WarMapMenu(Es1MenuContext context, Es1ShellDialog dialog) : base(context) {
		_dialog = dialog;
	}

	/// <summary>
	/// <c>WarMapMenu_Ctor</c>'s steps 2 to 5 (docs/es1/shell/war-map.md#opening-it): the briefing, block 0 of <c>BRF:</c>
	/// of <see cref="Es1ShellMission.TextPath"/>, loaded into the text box (<see cref="Es1ShellDialog.LoadConditional"/>)
	/// and its page turned on as many times as <see cref="Es1ShellScreens.BriefingPage"/>, compared signed;
	/// <see cref="Es1ShellScreens.MapRefresh"/> set to 3; unless <c>Shell_DebugMode</c>, with a keyboard, its mode kept in
	/// <see cref="Es1ShellScreens.SavedKeyboardMode"/>, the mode <c>0x20000</c> set and <see cref="MapKeys"/> given to
	/// <c>KeyQueue_Subscriber</c>; and <see cref="Es1ShellScreens.MapMouseFlag"/> set. The waits are those of the box the
	/// load deletes.
	/// </summary>
	public IEnumerable<Es1ShellWait> Load() {
		var screens = Context.Screens;
		var mission = Context.Mission;
		foreach (var wait in _dialog.LoadConditional(0, Es1MissionText.BriefingTag, mission.TextPath, mission.ReadFlag)) {
			yield return wait;
		}

		for (int i = 0; i < screens.BriefingPage; i++) {
			_dialog.NextPage();
		}

		screens.MapRefresh = 3;
		var globals = Context.Globals;
		if (!globals.DebugMode && globals.Keyboard is { } keyboard) {
			screens.SavedKeyboardMode = keyboard.Mode;
			keyboard.SetMode(KeyboardMode);
			keyboard.AddSubscriberCodes(keyboard.QueueSubscriber, MapKeys);
		}

		screens.MapMouseFlag = 1;
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>WarMapMenu_OnMessage</c> (<c>0002c057</c>) (docs/es1/shell/war-map.md#the-buttons): by the
	/// message's target, the case of the table at <c>00080bdc</c> (cases at <c>00080c0c</c>); nothing for any other id,
	/// 1502 among them.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var mission = Context.Mission;
		switch (message.Target) {
			case ZoomIn:
			case ZoomOut:
			case MapUp:
			case MapDown:
			case MapLeft:
			case MapRight:
				// 0002c07d to 0002c22b: while Shell_MissionMap is set, the slot of its table +0x4a by the button (108 +8, 109
				// +0xc, 1503 +0x10, 1504 +0x14, 1505 +0x18, 1506 +0x1c) with the map. PLACEHOLDER: the mission map's, S5's.
				screens.MapRefresh = 3;
				PressMapArea();
				break;
			case PageUp:
				// 0002c281.
				_dialog.PrevPage();
				screens.BriefingPage--;
				break;
			case PageDown:
				// 0002c297.
				_dialog.NextPage();
				screens.BriefingPage++;
				break;
			case Cancel:
				// 0002c2ad.
				if (mission.Mode == 1) {
					// PLACEHOLDER: the current career record's byte +1 cleared, its word +4 set to 1500 and the roster saved
					// with 000168b0(0009b218, the roster's word +7); the careers are S3's.
					message = message with { Target = CareerMenu };
				} else {
					message = message with { Target = Es1MissionMenu.Screen };
				}

				screens.AnmName = CancelAnm;
				screens.AnmMode = 2;
				screens.DeleteMissionMap();
				screens.MapRefresh = 0;
				break;
			case Arming:
				// 0002c367: 1820 while the byte Shell_LowMemory (0007fd09) is set, which this engine never sets
				// (Es1ShellHost's step 6), so the target stays 1800.
				break;
			case ServiceBay:
				// 0002c382: PLACEHOLDER: 0000a9c8(Shell_MissionMap), with no test that the map is set, the mission map's,
				// S5's; and in Shell_Mode 1 the current career record's byte +1 set to 1, S3's. The target stays 1700.
				break;
			case BeginMission:
				BeginMissionPressed(ref message);
				break;
		}
	}

	/// <summary>
	/// BEGIN MISSION >, <c>0002c3c9</c> (docs/es1/shell/war-map.md#begin-mission): 3333 when the player has no HERC
	/// (<see cref="Es1ShellMission.PlayerHasNoHerc"/>), else 3000 when any pilot's HERC is unarmed
	/// (<see cref="Es1ShellMission.AnyPilotUnarmed"/>), else <c>data\player.mec</c> written again
	/// (<see cref="Es1ShellMission.WritePlayerMec"/>), 9999, the mission map deleted and
	/// <see cref="Es1ShellScreens.MapRefresh"/> cleared.
	/// </summary>
	private void BeginMissionPressed(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var mission = Context.Mission;
		if (mission.Mode != 0) {
			// PLACEHOLDER: in Shell_Mode 1 the tests and PlayerMec_Write run on the roster's current career record, S3's;
			// the target stays 100, which names no screen.
			return;
		}

		if (mission.PlayerHasNoHerc()) {
			message = message with { Target = NoHercAlert };
		} else if (mission.AnyPilotUnarmed()) {
			message = message with { Target = UnarmedAlert };
		} else {
			mission.WritePlayerMec();
			message = message with { Target = Es1ShellScreens.LoadingScreen };
			screens.DeleteMissionMap();
			screens.MapRefresh = 0;
		}
	}

	/// <summary>
	/// The map's buttons' last step: the pressed byte <c>+0</c> of the hotspot 1502 set, the window <c>+6</c>'s hotspot
	/// <c>DUIWindow_FindHotspotById</c>(window, 1502) gives, so the next <see cref="Es1DUIWindow.Update"/> calls its
	/// handler and clears it. Retail writes at index −1 for a window with no such hotspot; here nothing is set, an engine
	/// guard.
	/// </summary>
	private void PressMapArea() {
		if (Window is { } window && window.FindHotspotById(MapArea) is var index and >= 0) {
			window.Hotspots[index].Pressed = true;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>WarMapMenu_Dtor</c> (<c>0002bf15</c>) (docs/es1/shell/war-map.md#leaving-it): while
	/// <see cref="Es1ShellScreens.MapDrawCount"/> is 0, the briefing's box deleted and
	/// <see cref="Es1ShellScreens.BriefingPage"/> set to 0; <see cref="Es1ShellScreens.MapRefresh"/> cleared; unless
	/// <c>Shell_DebugMode</c>, with a keyboard, <see cref="MapKeys"/> taken back from <c>KeyQueue_Subscriber</c> and the
	/// mode put back from <see cref="Es1ShellScreens.SavedKeyboardMode"/>; and <see cref="Es1ShellScreens.MapMouseFlag"/>
	/// cleared. The box the text box holds is the briefing's, whose destructor waits for nothing, so its deletion is run
	/// through here.
	/// </summary>
	public override void Dtor() {
		var screens = Context.Screens;
		if (screens.MapDrawCount == 0) {
			foreach (var _ in _dialog.DeleteBox()) {
			}

			screens.BriefingPage = 0;
		}

		screens.MapRefresh = 0;
		var globals = Context.Globals;
		if (!globals.DebugMode && globals.Keyboard is { } keyboard) {
			keyboard.RemoveSubscriberCodes(keyboard.QueueSubscriber, MapKeys);
			keyboard.SetMode(screens.SavedKeyboardMode);
		}

		screens.MapMouseFlag = 0;
	}
}

/// <summary>
/// The unarmed-HERC alert's screen object, class <c>UnArmedAlert</c> (vtable <c>00081da0</c>)
/// (docs/es1/shell/war-map.md#begin-mission): screen 3000, over the war map's backdrop, WARNING! with CANCEL and
/// PROCEED. CANCEL (99) passes to <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>, to the war map.
/// </summary>
public sealed class Es1UnArmedAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu3000.dlg</c>.</summary>
	public const short Screen = 3000;

	/// <summary>PROCEED's id.</summary>
	private const short Proceed = 100;

	public Es1UnArmedAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>UnArmedAlert_OnMessage</c> (<c>0002a650</c>): for PROCEED, the target 9999 in
	/// <c>Shell_Mode</c> 0, the mission map deleted and <see cref="Es1ShellScreens.MapRefresh"/> cleared. It writes no
	/// <c>data\player.mec</c>: the load's stands.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target != Proceed) {
			return;
		}

		var screens = Context.Screens;
		if (Context.Mission.Mode == 1) {
			// PLACEHOLDER: in Shell_Mode 1 the target 1700 and MessageBox_SetKind(Shell_MessageBox, 6), the career's, S3's;
			// the target stays 100, which names no screen.
		} else {
			message = message with { Target = Es1ShellScreens.LoadingScreen };
		}

		screens.DeleteMissionMap();
		screens.MapRefresh = 0;
	}

	/// <summary>Slot <c>+4</c>, <c>UnArmedAlert_Dtor</c> (<c>0003f97a</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The no-HERC alert's screen object, class <c>UnAssignedAlert</c> (vtable <c>00081dc2</c>)
/// (docs/es1/shell/war-map.md#begin-mission): screen 3333, over the war map's backdrop, WARNING! with OKAY (99), which
/// passes to <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>, to the war map. Its slot <c>+8</c>,
/// <c>UnAssignedAlert_OnMessage</c> (<c>0003fae8</c>), is <c>Menu_OnMessage</c>'s code, nothing.
/// </summary>
public sealed class Es1UnAssignedAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu3333.dlg</c>.</summary>
	public const short Screen = 3333;

	public Es1UnAssignedAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>Slot <c>+4</c>, <c>UnAssignedAlert_Dtor</c> (<c>0003fa31</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
