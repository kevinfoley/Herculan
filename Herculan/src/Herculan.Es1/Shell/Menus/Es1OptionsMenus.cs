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
/// PREFERENCES' screen object, class <c>PrefsMenu</c> (vtable <c>00081f60</c>, <c>0x2c</c> bytes)
/// (docs/es1/shell/options.md#preferences--screen-1310): screen 1310, MUSIC, SOUND, DETAIL and SPEECH, each with the box
/// that shows its setting, CANCEL and ACCEPT. Screen 1310's case (<c>0003e7eb</c>) makes it and copies the four
/// settings into it, which CANCEL puts back.
/// </summary>
public sealed class Es1PrefsMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1310.dlg</c>.</summary>
	public const short Screen = 1310;

	/// <summary>The four settings' buttons.</summary>
	private const short MusicButton = 1311, SoundButton = 1312, DetailButton = 1313, SpeechButton = 1317;

	/// <summary>The boxes that show the settings, list boxes of style 1.</summary>
	public const short MusicList = 1314, SoundList = 1315, DetailList = 1316, SpeechList = 1318;

	/// <summary>CANCEL's and ACCEPT's ids.</summary>
	private const short Cancel = 99, Accept = 100;

	/// <summary>
	/// <c>PrefsMenu_OffOnLines</c> (<c>000811a8</c>), MUSIC's and SOUND's lines: OFF and ON, <c>eng\watext.bin</c>'s lines
	/// 2 and 3.
	/// </summary>
	public static readonly short[] OffOnLines = [2, 3, -1];

	/// <summary><c>PrefsMenu_DetailLines</c> (<c>000811b0</c>), DETAIL's lines: LOW, MEDIUM, HIGH and MAX, lines 4 to 7.</summary>
	public static readonly short[] DetailLines = [4, 5, 6, 7, -1];

	/// <summary>
	/// <c>PrefsMenu_SpeechLines</c> (<c>000811bc</c>), SPEECH's lines: TEXT ONLY, VOICE ONLY and TEXT AND VOICE, lines
	/// <c>0x10</c> to <c>0x12</c>.
	/// </summary>
	public static readonly short[] SpeechLines = [0x10, 0x11, 0x12, -1];

	/// <summary>The settings as the menu opened, <c>+0x26</c> to <c>+0x2a</c>.</summary>
	private readonly byte _music, _sound;

	/// <inheritdoc cref="_music"/>
	private readonly short _detail, _radio;

	/// <summary>
	/// Screen 1310's case: the menu, with <see cref="Es1ShellData.MusicOption"/> at <c>+0x26</c>,
	/// <see cref="Es1ShellData.SoundOption"/> at <c>+0x27</c>, <see cref="Es1ShellData.DetailOption"/> at <c>+0x28</c>
	/// and <see cref="Es1ShellData.RadioOption"/> at <c>+0x2a</c>.
	/// </summary>
	public Es1PrefsMenu(Es1MenuContext context) : base(context) {
		var data = context.Data;
		_music = data.MusicOption;
		_sound = data.SoundOption;
		_detail = data.DetailOption;
		_radio = data.RadioOption;
	}

	/// <summary>
	/// <c>Menu_Open</c>'s change for menu 1310: with no sound manager (<see cref="Es1ShellDevices.SoundManagerPresent"/>,
	/// the dword <c>00082768</c>), MUSIC, SOUND and SPEECH, each found in any state with <c>DUIWindow_FindControlById</c>,
	/// made inactive.
	/// </summary>
	public static void SetOpeningControls(Es1DUIWindow window, Action<Es1DUIWindow, short> makeInactive) {
		if (Es1ShellDevices.SoundManagerPresent) {
			return;
		}

		makeInactive(window, window.FindControlById(MusicButton));
		makeInactive(window, window.FindControlById(SoundButton));
		makeInactive(window, window.FindControlById(SpeechButton));
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>PrefsMenu_OnMessage</c> (<c>0002b82b</c>) (docs/es1/shell/options.md#the-buttons--prefsmenu_onmessage-0002b82b):
	/// by the message's target, the six ids at <c>00080aac</c>; nothing for any other. A setting's button steps the
	/// setting, selects its line in its box (<see cref="Es1Menu.FindList"/>, slot <c>+0x24</c>) and stores it as its
	/// option with the shell's <c>Prefs_SetOption</c>(1, value, option) (<see cref="Es1Preferences.ShellSetOption"/>);
	/// the window stays. CANCEL puts the four settings back, plays or stops the music as MUSIC does, and runs
	/// <c>Prefs_RevertAll</c>(1) (<see cref="Es1Preferences.ShellRevertAll"/>), then ACCEPT's code: <c>Prefs_CommitAll</c>(1)
	/// (<see cref="Es1Preferences.ShellCommitAll"/>), <c>Prefs_Save</c> (<see cref="Es1Preferences.ShellSave"/>) and the
	/// target 1300, OPTIONS.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var data = Context.Data;
		var preferences = Context.Preferences;
		switch (message.Target) {
			case SoundButton:
				// 0002b854.
				data.SoundOption ^= 1;
				FindList(SoundList)?.SelectLine(data.SoundOption);
				preferences.ShellSetOption(true, data.SoundOption, Es1Preferences.SoundsOption);
				break;
			case MusicButton:
				// 0002b8b3.
				data.MusicOption ^= 1;
				FindList(MusicList)?.SelectLine(data.MusicOption);
				PlayOrStopMusic(data.MusicOption);
				preferences.ShellSetOption(true, data.MusicOption, Es1Preferences.MusicOption);
				break;
			case DetailButton:
				// 0002b961: options 4 to 6 the setting × 64 + 0x3f, then option 3 0 for LOW and 1 otherwise.
				data.DetailOption = (short)((data.DetailOption + 1) % 4);
				FindList(DetailList)?.SelectLine(data.DetailOption);
				byte level = (byte)(data.DetailOption * 64 + 0x3f);
				preferences.ShellSetOption(true, level, Es1Preferences.SkyOption);
				preferences.ShellSetOption(true, level, Es1Preferences.GroundOption);
				preferences.ShellSetOption(true, level, Es1Preferences.DetailOption);
				preferences.ShellSetOption(true, data.DetailOption == 0 ? (byte)0 : (byte)1, Es1Preferences.HorizonOption);
				break;
			case SpeechButton:
				// 0002ba18.
				if (preferences.ShellSpeechArchiveFound) {
					data.RadioOption = (short)((data.RadioOption + 1) % 3);
					FindList(SpeechList)?.SelectLine(data.RadioOption);
					preferences.ShellSetOption(true, (byte)data.RadioOption, Es1Preferences.RadioOption);
				}

				break;
			case Cancel:
				// 0002ba91, which runs on into ACCEPT's code.
				data.SoundOption = _sound;
				data.MusicOption = _music;
				data.DetailOption = _detail;
				data.RadioOption = _radio;
				PlayOrStopMusic(data.MusicOption);
				preferences.ShellRevertAll(true);
				Save(ref message);
				break;
			case Accept:
				// 0002bb0d.
				Save(ref message);
				break;
		}
	}

	/// <summary>ACCEPT's code: every option committed and the 17 written to <c>data\prefs.cfg</c>, and the target 1300.</summary>
	private void Save(ref Es1MenuMessage message) {
		Context.Preferences.ShellCommitAll(true);
		Context.Preferences.ShellSave();
		message = message with { Target = Es1OptionsMenu.Screen };
	}

	/// <summary>
	/// With the backdrop's music <c>ShellMusic_Object</c> (<c>0007fcfc</c>) set, its slot <c>+0x10</c> (play) for a
	/// setting of 1 and <c>+0x14</c> (stop) for 0, through its vtable at <c>+0x3b</c>. PLACEHOLDER: the object is the
	/// shell's sound manager's, which is not ported (<see cref="Es1ShellDevices.Init"/>'s <c>ShellSound_Init</c>), so
	/// there is none and nothing plays or stops, as in retail with the object not set.
	/// </summary>
	private static void PlayOrStopMusic(byte setting) {
		_ = setting;
	}

	/// <summary>Slot <c>+4</c>, <c>PrefsMenu_Dtor</c> (<c>000402d2</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
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
