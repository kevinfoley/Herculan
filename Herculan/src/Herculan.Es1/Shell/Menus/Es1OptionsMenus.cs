using Herculan.Es1.Input;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The options menu's screen object, class <c>OptionsMenu</c> (vtable <c>00081f7e</c>)
/// (docs/es1/shell/main-menu.md#the-return-screen): screen 1300, PREFERENCES, CONTROLS, LOAD BATTLESET, VIEW DEMO, CREDITS
/// and RETURN, whose text <c>Menu_BuildFromDlg</c> picks by <see cref="Es1ShellScreens.ReturnScreen"/>
/// (<see cref="Es1MenuBuild"/>). PREFERENCES (1310), CONTROLS (1320) and LOAD BATTLESET (1330) only change the screen.
/// </summary>
public sealed class Es1OptionsMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1300.dlg</c>.</summary>
	public const short Screen = 1300;

	/// <summary>The buttons the handler changes: VIEW DEMO, CREDITS and RETURN.</summary>
	private const short ViewDemo = 1301, Credits = 1302, Return = 100;

	/// <summary>The value of <see cref="Es1ShellScreens.ScriptsToPlay"/> that plays the credits on the next change of screen.</summary>
	private const byte CreditsScripts = 2;

	/// <summary>The <c>.ANM</c> CREDITS puts into <c>000aacfe</c>, the string at <c>00080994</c>.</summary>
	private const string CreditsAnm = "anm\\esiege2.anm";

	public Es1OptionsMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>OptionsMenu_OnMessage</c> (<c>0002b79d</c>) (docs/es1/shell/main-menu.md#the-return-screen): VIEW
	/// DEMO becomes 9999 with <see cref="Es1ShellScreens.DemoRequested"/> set, which that screen turns into exit code 5;
	/// CREDITS becomes 1300 with <see cref="Es1ShellScreens.ScriptsToPlay"/> 2, which plays the credits as the menu opens
	/// again, and <c>anm\esiege2.anm</c> to play before the backdrop; RETURN becomes
	/// <see cref="Es1ShellScreens.ReturnScreen"/>, or 1200 when that is 1710, the service bay.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		switch (message.Target) {
			case ViewDemo:
				message = message with { Target = Es1ShellScreens.LoadingScreen };
				screens.DemoRequested = true;
				break;
			case Credits:
				message = message with { Target = Screen };
				screens.ScriptsToPlay = CreditsScripts;
				screens.AnmName = CreditsAnm;
				screens.AnmMode = 2;
				break;
			case Return:
				message = message with {
					Target = screens.ReturnScreen == Es1ServiceBayMenu.AfterScreen ? Es1CareerMenu.Screen : screens.ReturnScreen,
				};
				break;
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The battle-set picker's screen object, class <c>BattlesetAlert</c> (vtable <c>00081fa2</c>)
/// (docs/es1/formats/bst-battle-sets.md#which-battle-set): screen 1330, BATTLESET, the list 1331 of the battle sets,
/// CANCEL (1300) and ACCEPT. The list's arrows, 101 and 102, reach a handler that does nothing for them.
/// </summary>
public sealed class Es1BattlesetAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1330.dlg</c>.</summary>
	public const short Screen = 1330;

	/// <summary>The list of the battle sets.</summary>
	public const short BattleSetList = 1331;

	/// <summary>ACCEPT's id.</summary>
	private const short Accept = 100;

	public Es1BattlesetAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>BattleSetDialog_OnMessage</c> (<c>0002bb29</c>): for ACCEPT, the list's selected line (its slot
	/// <c>+0x2c</c>, <see cref="Es1MTStrListBox.GetValue"/>) stored in the roster's battle set (<see cref="Es1CareerRoster.BattleSet"/>,
	/// its word <c>+5</c>) and in <see cref="Es1ShellData.NewCareerBattleSet"/>, the word <c>0007fcec</c> a new career
	/// takes; that battle set loaded (<see cref="Es1ShellData.LoadBattleSet"/>); and the target 1300, the options menu.
	/// CANCEL's 1300 changes only the screen. The list is found with <c>DUIWindow_FindEnabledControlById</c>
	/// (<see cref="Es1Menu.FindList"/>); with none, where retail reads the control the index −1 gives, the selection is
	/// passed over, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target != Accept) {
			return;
		}

		if (FindList(BattleSetList) is { } list) {
			short battleSet = list.GetValue();
			Context.Mission.Roster.BattleSet = battleSet;
			Context.Data.NewCareerBattleSet = battleSet;
			Context.Data.LoadBattleSet(Context.Content, battleSet);
		}

		message = message with { Target = Es1OptionsMenu.Screen };
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
