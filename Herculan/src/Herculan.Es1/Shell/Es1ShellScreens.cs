using Herculan.Engine.Content;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's screen state and the routines of <c>shell.cpp</c> the start-up, the frame loop and the shutdown run
/// (docs/es1/shell/menus.md#opening-and-closing-a-screen): <c>Shell_OpenFirstScreen</c>, <c>Shell_DrawScreen</c>,
/// <c>Shell_TickScreen</c> and <c>Shell_CloseScreens</c>. The menus themselves are <see cref="Es1Menu"/> and the window
/// classes under it; the main menu's buttons are P3's.
/// </summary>
public sealed class Es1ShellScreens {
	/// <summary>The main menu's screen and backdrop group, and the screen <c>Shell_PreviousScreen</c> starts from.</summary>
	public const short MainMenuScreen = 1000, QuitAlertScreen = 999, LoadingScreen = 9999;

	/// <summary>The full-screen render context's rect, (0, 0)–(319, 199), <c>RenderContext_GetFullScreen</c>'s.</summary>
	public static readonly Es1Rect FullScreen = new(0, 0, Es1VideoMemory.PageWidth - 1, Es1VideoMemory.PageHeight - 1);

	private readonly Es1ShellDevices _devices;
	private readonly Es1ShellBackdrop _backdrop;
	private readonly Es1DirtyList _dirty;
	private readonly GameContent _content;

	public Es1ShellScreens(Es1ShellDevices devices, Es1ShellBackdrop backdrop, Es1DirtyList dirty, GameContent content,
			Es1MenuGlobals globals) {
		_devices = devices;
		_backdrop = backdrop;
		_dirty = dirty;
		_content = content;
		Globals = globals;
	}

	/// <summary>The window file's globals the menus share with the input.</summary>
	public Es1MenuGlobals Globals { get; }

	/// <summary><c>Shell_CurrentScreen</c> (<c>000817c0</c>), −1 in the image.</summary>
	public short CurrentScreen { get; private set; } = -1;

	/// <summary><c>Shell_PreviousScreen</c> (<c>000817c2</c>), −1 in the image.</summary>
	public short PreviousScreen { get; private set; } = -1;

	/// <summary><c>Shell_BackdropId</c> (<c>000817c4</c>), −1 in the image.</summary>
	public short BackdropId { get; private set; } = -1;

	/// <summary><c>Shell_CurrentMenu</c> (<c>000817c8</c>), the open screen object; none in the image.</summary>
	public Es1Menu? CurrentMenu { get; private set; }

	/// <summary>
	/// <c>Shell_ReturnScreen</c> (<c>0007fce8</c>), 1000 in the image: the screen the options menu's RETURN button goes
	/// back to, which names that button (docs/es1/shell/main-menu.md#the-return-screen). The main menu's buttons set it.
	/// </summary>
	public short ReturnScreen { get; set; } = MainMenuScreen;

	/// <summary>The byte <c>000817c6</c>, 1 in the image: <see cref="DrawScreen"/> draws the backdrop.</summary>
	public bool DrawsBackdrop { get; } = true;

	/// <summary>The byte <c>000817bc</c>: an intro script was cut short, so <c>lead_in.ttm</c> is skipped.</summary>
	public bool IntroCutShort { get; private set; }

	/// <summary>The <c>.ANM</c> name at <c>000aacfe</c>.</summary>
	public string? AnmName { get; private set; }

	/// <summary>The byte <c>0007fcd7</c>: 2 plays <see cref="AnmName"/> before the backdrop is drawn, 1 after the screen.</summary>
	public byte AnmMode { get; set; }

	/// <summary>The byte <c>0007fcd6</c>, which makes the next frame wait 0.1 s on backdrop 1000.</summary>
	public bool WaitAfterDraw { get; set; }

	/// <summary>
	/// The byte <c>0008444e</c>, 0 while <c>VGA8_ShowPage</c> runs and 1 after: set by the first frame shown, which the
	/// start-up's step 10 shows before <see cref="DrawScreen"/> first runs.
	/// </summary>
	public bool PageShown { get; set; }

	/// <summary>The bundle the menus read through, made by <see cref="OpenFirstScreen"/> once the shared data is loaded.</summary>
	private Es1MenuContext? _menuContext;

	/// <summary>
	/// <c>Shell_OpenFirstScreen</c> (<c>0003ba47</c>)(<paramref name="exitCode"/>), the start-up's step 15
	/// (docs/es1/shell/menus.md#the-first-screen--shell_openfirstscreen-0003ba47): the scripts set up, then by the code
	/// the intro or a mission's results, then the main menu over the backdrop 1000: a <see cref="Es1MainMenu"/> made the
	/// current menu and opened as 1000.
	/// </summary>
	/// <param name="playIntro"><c>Shell_PlayIntro</c>, which <c>-A</c> clears.</param>
	/// <param name="data">The shared data the start-up loaded, which the menus draw with.</param>
	public void OpenFirstScreen(short exitCode, bool playIntro, Es1ShellData data) {
		_menuContext = new Es1MenuContext(_devices.Display, _content, data, Globals, _dirty, this);
		// DirtyList_CreateContext is the dirty list's (Es1DirtyList). PLACEHOLDER: Ttm_Configure(page 2, the full-screen
		// context, 0x32), Ttm_AllocHandles(2), Ttm_SetPathPrefix(res32\) and TtmLists_Load set up the shell's script
		// interpreter, which is not ported (ES1's simulator interpreter differs, docs/es1/formats/ttm-scripts.md#the-shell).
		switch (exitCode) {
			case 3:
			case 4:
				// PLACEHOLDER: back from a mission, Mission_ReadRestore, Shell_ProcessMissionResults and the results screens
				// of a single mission or a career, which are S2's and S3's; the start goes on to the main menu as code 6 does.
				Console.WriteLine($"data\\exit.cfg holds {exitCode}, back from a mission: the results screens are not ported, so the main menu opens.");
				break;
			case 6:
				break;
			default:
				if (playIntro) {
					// PLACEHOLDER: ShellMusic_Play(0xc), intro\dynamix.ttm, intro\lead_in.ttm unless IntroCutShort is set,
					// ShellMusic_Play(0xd), intro\iintro.ttm and intro\earth_w.ttm, through Shell_PlayTtm; the shell's script
					// interpreter is not ported, so every start goes on as -A does.
				}

				IntroCutShort = false;
				break;
		}

		// Ttm_SetPathPrefix(res32\) is the scripts' (above); then the main menu's case.
		AnmName = "anm\\esiege2.anm";
		AnmMode = 2;
		_backdrop.Show(MainMenuScreen);
		var menu = new Es1MainMenu(_menuContext);
		CurrentMenu = menu;
		menu.Open(MainMenuScreen);
		CurrentScreen = MainMenuScreen;
		PreviousScreen = QuitAlertScreen;
		BackdropId = MainMenuScreen;
	}

	/// <summary>
	/// <c>Shell_DrawScreen</c> (<c>0003c1b7</c>) (docs/es1/shell/menus.md#drawing-a-screen), while
	/// <see cref="PageShown"/> is set: <see cref="Es1MenuGlobals.InputReady"/> cleared and the cursor taken off; with
	/// <see cref="DrawsBackdrop"/> set, the <c>.ANM</c> when <see cref="AnmMode"/> is 2, then the backdrop copied onto the
	/// draw page (<see cref="Es1ShellBackdrop.Draw"/>); with a current menu, the source and draw pages set to page 2, the
	/// background under the window saved, the window drawn on page 2 unless the message box is up, the draw page put back,
	/// and then, still unless the message box is up, on backdrop 1000 (screen 9999 aside) the whole screen added as dirty
	/// with delay 1, otherwise the window drawn again on the draw page, and <see cref="WaitAfterDraw"/> set; with none, the
	/// draw page put back; then the source page put back, the whole screen added as dirty with delay 0, the cursor drawn
	/// unless the screen is 9999, and <see cref="Es1MenuGlobals.InputReady"/> set.
	/// </summary>
	public void DrawScreen() {
		if (!PageShown) {
			return;
		}

		var display = _devices.Display;
		Globals.InputReady = false;
		// The raster slot 0008662c, VGA_8's slot 31, takes the cursor off.
		display.Cursor.Hide();
		int keptDraw = display.DrawPage, keptSource = display.SourcePage;
		if (DrawsBackdrop) {
			if (AnmMode == 0 && CurrentScreen != MainMenuScreen) {
				// The raster slot 0008663c with (the draw page, 0) and (the visible page, 0). PLACEHOLDER: the slot is
				// unread; no ported screen other than the main menu draws.
			}

			if (AnmMode == 2) {
				// PLACEHOLDER: Anm_Play(1, 0xa, AnmName); the shell has no .ANM player yet (docs/es1/formats/anm-animation.md).
				AnmMode = 0;
			}

			_backdrop.Draw();
			if (BackdropId == 4000) {
				// PLACEHOLDER: 0001240b and the career record's HERC diagram, S3's; no ported screen shows backdrop 4000.
			} else {
				// PLACEHOLDER: 00012467 is unread.
			}
		} else if (CurrentScreen is 1700 or 1710) {
			// PLACEHOLDER: the career record's HERC diagram (00029651, CareerRecord_SelectPilot), S3's.
		}

		// PLACEHOLDER: DigitalDialog_ShowMessage(Shell_MessageBox) and Ttm_RunAll, the message box (P3's) and the scripts
		// (the shell's unported interpreter).
		if (CurrentMenu is { } menu) {
			DrawMenu(menu, keptDraw);
		} else {
			display.DrawPage = keptDraw;
		}

		if (AnmMode == 1) {
			// PLACEHOLDER: the palette uploaded, then Anm_Play(1, 0xb, AnmName); no ported screen sets mode 1.
			AnmMode = 0;
		}

		display.SourcePage = keptSource;
		_dirty.Add(0, 0, Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight, 0);
		if (CurrentScreen != LoadingScreen) {
			// The raster slot 00086628, VGA_8's slot 30, draws the cursor.
			display.Cursor.Show();
		}

		Globals.InputReady = true;
	}

	/// <summary>
	/// <c>Shell_DrawScreen</c>'s step 4 with a current menu: the render contexts <c>0007fcdc</c> (<c>0004c88a</c>, of the
	/// window's rect) and <c>0007fcd8</c> (<c>0004c53c</c>, the screen less the window, in clip mode 2) made the scripts'
	/// default, which this engine does not keep as it has no script interpreter; the source and draw pages set to page 2
	/// and the background saved (<see cref="Es1Menu.SaveBackground"/>); the window drawn on page 2 unless the message box
	/// is up; the draw page put back; and unless the message box is up, the whole screen dirty with delay 1 on backdrop
	/// 1000 (screen 9999 aside) or the window drawn again on the draw page, and <see cref="WaitAfterDraw"/> set.
	/// </summary>
	private void DrawMenu(Es1Menu menu, int keptDraw) {
		var display = _devices.Display;
		display.SourcePage = Es1ShellBackdrop.BackgroundPage;
		display.DrawPage = Es1ShellBackdrop.BackgroundPage;
		menu.SaveBackground();
		if (!Globals.MessageBoxUp) {
			// PLACEHOLDER: while the byte 000817bd counts down, Shell_MissionMap's slot +4 and the text box 00018dcb run once per
			// page first; no ported screen makes the mission map or sets the byte.
			menu.Window?.Draw();
		}

		display.DrawPage = keptDraw;
		if (Globals.MessageBoxUp) {
			return;
		}

		if (BackdropId == MainMenuScreen && CurrentScreen != LoadingScreen) {
			_dirty.Add(0, 0, Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight, 1);
		} else {
			menu.Window?.Draw();
			// PLACEHOLDER: with the byte 000809a8 set, 00009c64(Shell_MissionMap) and 000809a8 set to 3; no ported screen makes
			// the mission map.
		}

		WaitAfterDraw = true;
	}

	/// <summary>
	/// <c>Shell_TickScreen</c> (<c>0003c115</c>), the frame loop's step 5 (docs/es1/shell/menus.md#drawing-a-screen):
	/// unless the message box is up, the current menu's window updated (<see cref="Es1DUIWindow.Update"/>).
	/// </summary>
	public void TickScreen() {
		if (CurrentMenu?.Window is { } window && !Globals.MessageBoxUp) {
			window.Update(Globals, _dirty);
		}

		// PLACEHOLDER: while Shell_MissionMap (000809a4) is set, 00009cfb and the byte 000809a8's countdown (menus.md's Open);
		// no ported screen makes the mission map.
	}

	/// <summary>
	/// <c>Shell_CloseScreens</c> (<c>0003c081</c>), the shutdown's step 3: <c>0004199c</c> and <c>Ttm_Shutdown</c>, the
	/// scripts' (not ported); the current menu destroyed through its slot <c>+4</c> (<see cref="Es1Menu.Dtor"/>), which
	/// leaves its window as it stands; and the render contexts <c>0007fcd8</c> and <c>0007fcdc</c> destroyed, which this
	/// engine does not keep.
	/// </summary>
	public void CloseScreens() {
		CurrentMenu?.Dtor();
	}
}
