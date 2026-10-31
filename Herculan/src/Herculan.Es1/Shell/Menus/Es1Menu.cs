using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// What a menu and its window draw and read with: the shell's display, its archives, its shared data, the window
/// globals, the dirty list, the screen state, the mission the shell loads, the options <c>Prefs_Options</c>
/// (<c>000aa24e</c>) and the picture tiles' and CREW's bitmap arrays. This engine's bundle of retail's globals, passed
/// where retail reads them.
/// </summary>
public sealed record Es1MenuContext(Es1VideoMemory Display, GameContent Content, Es1ShellData Data, Es1MenuGlobals Globals,
	Es1DirtyList Dirty, Es1ShellScreens Screens, Es1ShellMission Mission, Es1Preferences Preferences) {
	/// <summary>The bitmap arrays the picture tiles and CREW's hotspots draw from, retail's statics.</summary>
	public Es1MenuBanks Banks { get; } = new(Content);
}

/// <summary>
/// A screen object, class <c>Menu</c> (vtable <c>00081bd8</c>, <c>0x26</c> bytes) (docs/es1/shell/menus.md#the-screen-object):
/// the menu's number, the background saved under its window, the window, and the region of the window's rect. A
/// screen's class is a subclass whose slot <c>+8</c> handles its messages.
/// </summary>
public class Es1Menu {
	/// <summary>The menu numbers whose window <see cref="Close"/> destroys with <c>DUIWindow_Dtor</c>, the six at <c>00081b90</c>.</summary>
	private static readonly short[] PlainWindowMenus = [1501, 1500, 1700, 1710, 1510, 1520];

	/// <summary>The key <see cref="Open"/> puts the focus on first, <c>Space</c>.</summary>
	private const byte FirstFocusKey = 0x39;

	/// <summary>The career button the main menu makes inactive with no careers in the log.</summary>
	private const short ResumeButton = 1700;

	public Es1Menu(Es1MenuContext context) {
		Context = context;
	}

	public Es1MenuContext Context { get; }

	/// <summary><c>+0</c>, the menu's number.</summary>
	public short Number { get; private set; }

	/// <summary><c>+2</c>, the background saved under the window: <c>Region_PixelCount</c> cells of the region; none before <see cref="Open"/> and after <see cref="Close"/>.</summary>
	public ushort[]? SavedBackground { get; private set; }

	/// <summary><c>+6</c>, the window, the <c>.DLG</c>'s kind-0 record.</summary>
	public Es1DUIWindow? Window { get; set; }

	/// <summary><c>+0xa</c>, the <c>GLRectangleRegion</c>: its type-0 region at <c>+0xe</c> with the window's rect at <c>+0x12</c>–<c>+0x21</c>.</summary>
	public Es1Rect Region { get; set; }

	/// <summary>
	/// Slot <c>+8</c>, the menu's message handler, given the address of <see cref="Es1ShellScreens.HandleMessage"/>'s copy
	/// of the message, which it may change: <c>Menu_OnMessage</c> (<c>0003b764</c>), nothing.
	/// </summary>
	public virtual void OnMessage(ref Es1MenuMessage message) {
	}

	/// <summary>Slot <c>+4</c>, <c>Menu_Dtor</c> (<c>0003b6d1</c>)(menu, flags): the vtables stored back and the menu freed; the window and the background are <see cref="Close"/>'s.</summary>
	public virtual void Dtor() {
	}

	/// <summary>
	/// <c>Menu_Open</c> (<c>0003969e</c>)(menu, <paramref name="number"/>)
	/// (docs/es1/shell/menus.md#opening-a-menu--menu_open-0003969e): the number kept, <see cref="Es1MenuGlobals.SyncPointer"/>
	/// set and the window built (<see cref="Es1MenuBuild.BuildFromDlg"/>); the window made the active one; the number's
	/// control changes (1100's, 1000's, 1200's, 1610's <see cref="Es1CrewAssignMenu.SetOpeningControls"/> and, in <c>Shell_Mode</c> 1, 1700's <see cref="Es1ServiceBayMenu.SetButtons"/>); unless the number is 9999, the focus and the pointer on the first control whose key is <c>Space</c>,
	/// or on control 0; the saved background allocated, zeroed; and <see cref="Es1MenuGlobals.InputReady"/> set.
	/// </summary>
	public void Open(short number) {
		var globals = Context.Globals;
		Number = number;
		globals.SyncPointer = true;
		Es1MenuBuild.BuildFromDlg(this, number);
		if (Window is not { } window) {
			// Retail goes on with whatever +6 held; with no window there is nothing to open, an engine guard.
			Console.Error.WriteLine($"Menu {number} built no window: dlg\\menu{number}.dlg is missing, unreadable or has no window record.");
			return;
		}

		window.SetActive(globals);
		if (number == Es1MissionMenu.Screen) {
			// The list selects Mission_Kind's line with its slot +0x24 (docs/es1/shell/single-mission.md#the-single-mission-menu).
			FindList(Es1MissionMenu.KindList)?.SelectLine(Context.Mission.MissionKind);
		}

		if (number == Es1ShellScreens.MainMenuScreen && (Context.Data.CareerLog?.Entries.Length ?? 0) == 0) {
			MakeInactive(window, window.FindControlById(ResumeButton));
		}

		if (number == Es1CareerMenu.Screen) {
			Es1CareerMenu.SetButtons(window, Context.Data, MakeInactive);
		}

		if (number == Es1CrewAssignMenu.Screen) {
			Es1CrewAssignMenu.SetOpeningControls(window, MakeInactive);
		}

		if (number == Es1ServiceBayMenu.Screen && Context.Mission.Mode == 1) {
			Es1ServiceBayMenu.SetButtons(window, Context.Mission.Roster.CurrentRecord, MakeInactive);
		}

		// PLACEHOLDER: Menu_Open's control changes for menu 1310 are the preferences and controls module's, after S5; for
		// 1920, 2000, 1800, 1820 and 1900 (the last after the focus below) S4's. No ported screen opens them.

		// Retail points at control 0 of a window with none, reading an empty slot; here such a window keeps the pointer, an
		// engine guard.
		if (number != Es1ShellScreens.LoadingScreen && window.Controls.Count > 0) {
			short focus = window.FindControlByKey(FirstFocusKey);
			if (focus == -1) {
				focus = 0;
			}

			window.PointAtControl(globals, focus);
			globals.FocusIndex = focus;
		}

		SavedBackground = new ushort[Math.Max(0, Region.X1 - Region.X0 + 1) * Math.Max(0, Region.Y1 - Region.Y0 + 1)];
		globals.InputReady = true;
	}

	/// <summary>
	/// <c>Region_Save</c> of the region into <see cref="SavedBackground"/>, as <c>Shell_DrawScreen</c> saves the background
	/// under the window with the source page set to page 2, in the full-screen render context it finds installed
	/// (<see cref="Es1ShellScreens.FullScreen"/>).
	/// </summary>
	public void SaveBackground() {
		if (SavedBackground == null) {
			return;
		}

		var cells = new Es1PanelCanvas(Context.Display, Es1ShellScreens.FullScreen).SaveRegion(Region);
		Array.Copy(cells, SavedBackground, Math.Min(cells.Length, SavedBackground.Length));
	}

	/// <summary>
	/// <c>DUIWindow_FindEnabledControlById</c> (<c>0001bda4</c>)(the window <c>+6</c>, <paramref name="id"/>) and the
	/// control at the index it gives, whose list-box slots the handlers then call. Retail indexes the controls with the −1
	/// of an id it does not find and calls the slot of whatever control the index holds; here neither gives a list and the
	/// call is skipped, an engine guard.
	/// </summary>
	protected Es1MTStrListBox? FindList(short id) =>
		Window is { } window && window.FindEnabledControlById(id) is var index and >= 0 && window.Controls[index] is Es1MTStrListBox list
			? list
			: null;

	/// <summary>
	/// A control made inactive as <c>Menu_Open</c> does it: the state 2 and <i>changed</i> written, no slot called. Retail
	/// indexes the controls with a −1 it is not given here; an index of −1 changes nothing, an engine guard.
	/// </summary>
	private static void MakeInactive(Es1DUIWindow window, short index) {
		if (index < 0) {
			return;
		}

		window.Controls[index].State = Es1DUIControl.Inactive;
		window.Controls[index].Changed = true;
	}

	/// <summary>
	/// <c>Menu_Close</c> (<c>0003b138</c>)(menu) (docs/es1/shell/menus.md#closing-a-menu--menu_close-0003b138):
	/// <see cref="Es1MenuGlobals.InputReady"/> cleared; while <see cref="Es1ShellScreens.PageShown"/> is set, the saved
	/// background put back at the window's corner with <c>Region_Restore</c>, first on the draw page the display holds and
	/// then on page 2, and the window's box (its right − left by its bottom − top) added as dirty with delay 1; the
	/// background freed; the active window cleared; the window destroyed, with <c>DUIWindow_Dtor</c> for the six menus at
	/// <c>00081b90</c> and <c>MTWindow_Dtor</c> for the rest; and the full-screen render context made again for the scripts.
	/// </summary>
	public void Close() {
		var globals = Context.Globals;
		globals.InputReady = false;
		if (Context.Screens.PageShown && Window is { } shown && SavedBackground is { } saved) {
			var display = Context.Display;
			var canvas = new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
			int keptDraw = display.DrawPage;
			canvas.RestoreRegion(Region, shown.Rect.X0, shown.Rect.Y0, saved);
			display.DrawPage = Es1ShellBackdrop.BackgroundPage;
			canvas.RestoreRegion(Region, shown.Rect.X0, shown.Rect.Y0, saved);
			display.DrawPage = keptDraw;
			Context.Dirty.Add(shown.Rect.X0, shown.Rect.Y0, shown.Rect.X1 - shown.Rect.X0, shown.Rect.Y1 - shown.Rect.Y0, 1);
		}

		SavedBackground = null;
		Es1DUIWindow.ClearActive(globals);
		if (Window is { } window) {
			if (Array.IndexOf(PlainWindowMenus, Number) < 0 && window is Es1MTWindow menuWindow) {
				menuWindow.Dtor();
			} else {
				// DUIWindow_Dtor; for the six menus it is called on a menu window too, skipping MTWindow_Dtor's frees, which
				// hold nothing here. Retail calls MTWindow_Dtor on any other menu's window, a plain one included.
				window.Dtor();
			}
		}

		Window = null;
		// RenderContext_GetFullScreen into 0007fcd8, which puts the scripts' default render context back to the whole screen.
		Context.Screens.GetFullScreen();
	}
}

/// <summary>
/// The main menu's screen object, class <c>MainMenu</c> (vtable <c>00081bf4</c>) (docs/es1/shell/main-menu.md#the-main-menu):
/// screen 1000.
/// </summary>
public sealed class Es1MainMenu : Es1Menu {
	/// <summary>The buttons' ids, the six of the table at <c>000809b8</c>.</summary>
	private const short InstantAction = 1499, Resume = 1700, SingleMission = 1100, Career = 1200, Options = 1300, ExitToDos = 1003;

	/// <summary>The <c>.ANM</c> INSTANT ACTION and RESUME put into <c>000aacfe</c>, the string at <c>00080984</c>.</summary>
	private const string MissionAnm = "anm\\esiege3.anm";

	public Es1MainMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MainMenu_OnMessage</c> (<c>0002a70a</c>) (docs/es1/shell/main-menu.md#the-buttons): by the
	/// message's target, the case of the table at <c>000809b8</c> (cases at <c>000809d0</c>); nothing for any other target.
	/// Each case but EXIT TO DOS sets <see cref="Es1ShellScreens.ReturnScreen"/>.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var mission = Context.Mission;
		switch (message.Target) {
			case InstantAction:
				// 0002a730: a random single mission loaded, its objectives written, and the .ANM played after the next screen.
				mission.Mode = 0;
				screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
				mission.MissionKind = -1;
				mission.StartSingleMission();
				mission.Prepare(screens);
				screens.AnmName = MissionAnm;
				screens.AnmMode = 1;
				break;
			case Resume:
				// 0002a773.
				mission.Mode = 1;
				screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
				mission.MissionKind = -1;
				ResumeCareer(ref message);
				break;
			case SingleMission:
				// 0002a963.
				mission.Mode = 0;
				screens.ReturnScreen = SingleMission;
				break;
			case Career:
				// 0002a977.
				mission.Mode = 1;
				screens.ReturnScreen = Career;
				break;
			case Options:
				// 0002a98b.
				screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
				break;
			case ExitToDos:
				// 0002a996.
				screens.ExitCode = 0;
				screens.QuitFlag = true;
				break;
		}
	}

	/// <summary>
	/// RESUME's career part from <c>0002a78e</c> (docs/es1/shell/main-menu.md#resume), on the roster and its current record:
	/// while no career is loaded, <c>Career_LoadCurrent</c> (<see cref="Es1CareerRoster.LoadCurrent"/>), then
	/// <see cref="Es1CareerRoster.PeekNextCampaign"/>'s low byte <c>0xff</c> or <c>0x63</c>, the career lost or won, ends the
	/// case with the target 1260 and <see cref="Es1ShellScreens.ReturnScreen"/> 1000; otherwise the target the record's word
	/// <c>+4</c>, the career's mission loaded unless that is 1710 or the player's pilot is lost, <c>Career_BackupLine</c>
	/// the last backup's line and <c>Career_Loaded</c> set. Then, with the player's pilot lost, the target 1250 and the
	/// return screen 1000; otherwise the target the record's <c>+4</c>, 5555 for 1710 when the record's word <c>+0xa8</c>
	/// is not −1, and <c>anm\esiege3.anm</c> to play after the next screen.
	/// </summary>
	private void ResumeCareer(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		var roster = Context.Mission.Roster;
		if (!roster.Loaded) {
			// 0002a79b.
			roster.LoadCurrent();
			if ((roster.PeekNextCampaign(roster.CurrentRecord) & 0xff) is 0xff or 0x63) {
				screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
				message = message with { Target = Es1KIAWarningAlert.CareerOverScreen };
				return;
			}

			message = message with { Target = roster.CurrentRecord.Word4 };
			if (message.Target != Es1CareerMenu.ServiceBay && Es1CareerRoster.PlayerCondition(roster.CurrentRecord) != 0) {
				roster.StartMission(0);
			}

			roster.BackupLine = (short)(roster.CountUsedRecords() - 1);
			roster.Loaded = true;
		}

		// 0002a878.
		var record = roster.CurrentRecord;
		if (Es1CareerRoster.PlayerCondition(record) == 0) {
			screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
			message = message with { Target = Es1KIAWarningAlert.KilledScreen };
			return;
		}

		message = message with { Target = record.Word4 };
		if (message.Target != Es1CareerMenu.ServiceBay) {
			Context.Mission.Prepare(screens);
		}

		if (message.Target == Es1CareerMenu.ServiceBay && record.Equipment.LastKind != -1) {
			message = message with { Target = Es1CareerMenu.TechRelease };
		}

		screens.AnmName = MissionAnm;
		screens.AnmMode = 1;
	}

	/// <summary>Slot <c>+4</c>, <c>MainMenu_Dtor</c> (<c>0003b771</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The quit question's screen object, class <c>QuitAlert</c> (vtable <c>00081e48</c>) (docs/es1/shell/main-menu.md#the-quit-question):
/// screen 999, opened as menu 888 or 999 (<see cref="Es1ShellScreens.OpenQuitAlert"/>).
/// </summary>
public sealed class Es1QuitAlert : Es1Menu {
	/// <summary>PROCEED's id.</summary>
	private const short Proceed = 100;

	public Es1QuitAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>QuitAlert_OnMessage</c> (<c>0002a554</c>): for PROCEED, <see cref="Es1ShellScreens.ExitCode"/> 0
	/// and <see cref="Es1ShellScreens.QuitFlag"/> set. CANCEL (99) passes to <see cref="Es1ShellScreens.HandleMessage"/>'s
	/// <i>back</i>.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target == Proceed) {
			Context.Screens.ExitCode = 0;
			Context.Screens.QuitFlag = true;
		}
	}

	/// <summary>Slot <c>+4</c>, <c>QuitAlert_Dtor</c> (<c>0003fd1a</c>): its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
