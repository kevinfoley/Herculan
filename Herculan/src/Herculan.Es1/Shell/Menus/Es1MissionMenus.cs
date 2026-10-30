namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The SINGLE MISSION menu's screen object, class <c>MissionMenu</c> (vtable <c>00081c3a</c>)
/// (docs/es1/shell/single-mission.md#the-single-mission-menu): screen 1100, the list of the seven mission types and
/// BRIEFING >.
/// </summary>
public sealed class Es1MissionMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1100.dlg</c>.</summary>
	public const short Screen = 1100;

	/// <summary>The list of mission types, whose line is <see cref="Es1ShellMission.MissionKind"/>.</summary>
	public const short KindList = 1101;

	/// <summary>The controls of the four ids of the table at <c>00080a54</c>, beside <see cref="KindList"/>.</summary>
	private const short Briefing = 1501, UpArrow = 101, DownArrow = 102;

	/// <summary>The screen BRIEFING > goes to, the message before the war map.</summary>
	private const short MessageScreen = 1499;

	/// <summary>The <c>.ANM</c> BRIEFING > puts into <c>000aacfe</c>, the string at <c>00080984</c>.</summary>
	private const string MissionAnm = "anm\\esiege3.anm";

	public Es1MissionMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MissionMenu_OnMessage</c> (<c>0002b375</c>) (docs/es1/shell/single-mission.md#the-buttons): by
	/// the message's target, the case of the table at <c>00080a54</c> (cases at <c>00080a64</c>); nothing for any other
	/// target, so MISSION PARAMS, OPTIONS and RETURN TO MAIN only change the screen.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var mission = Context.Mission;
		switch (message.Target) {
			case KindList:
				// 0002b39b: the selected line the release posts.
				mission.MissionKind = message.Value;
				break;
			case Briefing:
				// 0002b3ad: the list's line, a mission of that kind loaded, its objectives written, the .ANM played after the
				// next screen, and the message screen.
				if (FindList(KindList) is { } list) {
					mission.MissionKind = list.GetValue();
				}

				mission.StartSingleMission();
				mission.Prepare(Context.Screens);
				Context.Screens.AnmName = MissionAnm;
				Context.Screens.AnmMode = 1;
				message = message with { Target = MessageScreen };
				break;
			case UpArrow:
				// 0002b419: the list's slot +0x1c, then its value.
				if (FindList(KindList) is { } up) {
					up.SelectPrev();
					mission.MissionKind = up.GetValue();
				}

				break;
			case DownArrow:
				// 0002b489: the list's slot +0x20, then its value.
				if (FindList(KindList) is { } down) {
					down.SelectNext();
					mission.MissionKind = down.GetValue();
				}

				break;
		}
	}

	/// <summary>Slot <c>+4</c>, <c>MissionMenu_Dtor</c> (<c>0003bfca</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The MISSION PARAMS menu's screen object, class <c>MissionOptionsAlert</c> (vtable <c>00081f42</c>, <c>0x30</c> bytes)
/// (docs/es1/shell/single-mission.md#the-mission-params-menu): screen 1110, five buttons that step the mission
/// parameters (<see cref="Es1ShellData.MissionParams"/>), CANCEL and ACCEPT.
/// </summary>
public sealed class Es1MissionOptionsAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1110.dlg</c>.</summary>
	public const short Screen = 1110;

	/// <summary>The option of the first mission parameter, AMMO; the five are options 12 to 16.</summary>
	public const int FirstOption = 12;

	/// <summary>The buttons AMMO to TIME OF DAY are 1111 to 1115, and the lists showing their values 1116 to 1120.</summary>
	private const short FirstButton = 1111, FirstList = 1116;

	/// <summary>CANCEL's and ACCEPT's ids, the last two of the table at <c>00080a74</c>.</summary>
	private const short Cancel = 99, Accept = 100;

	/// <summary>How many values each parameter steps through, AMMO to TIME OF DAY.</summary>
	private static readonly short[] ValueCounts = [2, 2, 4, 2, 3];

	public Es1MissionOptionsAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// <c>+0x26</c>, <c>+0x28</c>, <c>+0x2a</c>, <c>+0x2c</c> and <c>+0x2e</c>: the five parameters as the menu opened, which
	/// screen 1110's case stores and CANCEL puts back.
	/// </summary>
	public short[] OpenedParams { get; } = new short[ValueCounts.Length];

	/// <summary>
	/// Slot <c>+8</c>, <c>MissionParams_OnMessage</c> (<c>0002b4fe</c>) (docs/es1/shell/single-mission.md#the-mission-params-menu):
	/// by the message's target, the case of the table at <c>00080a74</c> (cases at <c>00080a90</c>); nothing for any other
	/// target. AMMO to TIME OF DAY (<c>0002b527</c>, <c>0002b595</c>, <c>0002b603</c>, <c>0002b66f</c>, <c>0002b6dd</c>)
	/// each add 1 to their word as an <c>int16</c>, take the signed remainder by its value count, select it in their list
	/// with its slot <c>+0x24</c> and store it, as a byte, as their option with <c>Prefs_SetOption</c>(1, value, option).
	/// CANCEL (<c>0002b746</c>) puts <see cref="OpenedParams"/> back into the words and every option back with
	/// <c>Prefs_RevertAll</c>(1), then runs on into ACCEPT (<c>0002b781</c>): <c>Prefs_CommitAll</c>(1),
	/// <c>Prefs_Save</c>, and the target 1100.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var words = Context.Data.MissionParams;
		var preferences = Context.Preferences;
		int step = message.Target - FirstButton;
		if (step >= 0 && step < ValueCounts.Length) {
			words[step] = (short)((short)(words[step] + 1) % ValueCounts[step]);
			FindList((short)(FirstList + step))?.SelectLine(words[step]);
			preferences.ShellSetOption(true, (byte)words[step], FirstOption + step);
			return;
		}

		switch (message.Target) {
			case Cancel:
				Array.Copy(OpenedParams, words, OpenedParams.Length);
				preferences.ShellRevertAll(true);
				goto case Accept;
			case Accept:
				preferences.ShellCommitAll(true);
				preferences.ShellSave();
				message = message with { Target = Es1MissionMenu.Screen };
				break;
		}
	}

	/// <summary>Slot <c>+4</c>, <c>MissionOptionsAlert_Dtor</c> (<c>0004021b</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
