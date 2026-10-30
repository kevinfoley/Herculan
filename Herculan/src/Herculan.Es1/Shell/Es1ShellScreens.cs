using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's screen state and the routines of <c>shell.cpp</c> the start-up and the shutdown run
/// (docs/es1/shell/menus.md#opening-and-closing-a-screen): <c>Shell_OpenFirstScreen</c>, <c>Shell_DrawScreen</c> and
/// <c>Shell_CloseScreens</c>. The menus themselves, <c>Menu_Open</c> and the <c>.DLG</c> window, are P2's and the main
/// menu P3's, so no menu is ever current here and the paths that need one are not taken.
/// </summary>
public sealed class Es1ShellScreens {
	/// <summary>The main menu's screen and backdrop group, and the screen <c>Shell_PreviousScreen</c> starts from.</summary>
	public const short MainMenuScreen = 1000, QuitAlertScreen = 999, LoadingScreen = 9999;

	private readonly Es1ShellDevices _devices;
	private readonly Es1ShellBackdrop _backdrop;
	private readonly Es1DirtyList _dirty;

	public Es1ShellScreens(Es1ShellDevices devices, Es1ShellBackdrop backdrop, Es1DirtyList dirty) {
		_devices = devices;
		_backdrop = backdrop;
		_dirty = dirty;
	}

	/// <summary><c>Shell_CurrentScreen</c> (<c>000817c0</c>), −1 in the image.</summary>
	public short CurrentScreen { get; private set; } = -1;

	/// <summary><c>Shell_PreviousScreen</c> (<c>000817c2</c>), −1 in the image.</summary>
	public short PreviousScreen { get; private set; } = -1;

	/// <summary><c>Shell_BackdropId</c> (<c>000817c4</c>), −1 in the image.</summary>
	public short BackdropId { get; private set; } = -1;

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

	/// <summary>
	/// <c>Shell_OpenFirstScreen</c> (<c>0003ba47</c>)(<paramref name="exitCode"/>), the start-up's step 15
	/// (docs/es1/shell/menus.md#the-first-screen--shell_openfirstscreen-0003ba47): the scripts set up, then by the code
	/// the intro or a mission's results, then the main menu over the backdrop 1000.
	/// </summary>
	/// <param name="playIntro"><c>Shell_PlayIntro</c>, which <c>-A</c> clears.</param>
	public void OpenFirstScreen(short exitCode, bool playIntro) {
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
		// PLACEHOLDER: a MainMenu (vtable 00081bf4) made the current menu Shell_CurrentMenu and opened with
		// Menu_Open(menu, 1000), P2's .DLG engine and P3's main menu; no menu is current until they land.
		CurrentScreen = MainMenuScreen;
		PreviousScreen = QuitAlertScreen;
		BackdropId = MainMenuScreen;
	}

	/// <summary>
	/// <c>Shell_DrawScreen</c> (<c>0003c1b7</c>) (docs/es1/shell/menus.md#drawing-a-screen) with no menu current, while
	/// <see cref="PageShown"/> is set: the cursor taken off; with <see cref="DrawsBackdrop"/> set, the <c>.ANM</c> when
	/// <see cref="AnmMode"/> is 2, then the backdrop copied onto the draw page (<see cref="Es1ShellBackdrop.Draw"/>); the
	/// draw page put back; the source page put back, the whole screen added as a dirty box with delay 0, and the cursor
	/// drawn unless the screen is 9999.
	/// </summary>
	public void DrawScreen() {
		if (!PageShown) {
			return;
		}

		var display = _devices.Display;
		// DUIWindow_InputReady cleared, P2's; the raster slot 0008662c, VGA_8's slot 31, takes the cursor off.
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

		// PLACEHOLDER: DigitalDialog_ShowMessage(Shell_MessageBox) and Ttm_RunAll, the message box and the scripts, P2's
		// and the shell's unported interpreter. With no menu current, the menu's draw (render contexts, Region_Save,
		// DUIWindow_Draw, the dirty box of backdrop 1000 and WaitAfterDraw) is not reached; the draw page is put back.
		display.DrawPage = keptDraw;

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

		// DUIWindow_InputReady set, P2's.
	}

	/// <summary>
	/// <c>Shell_CloseScreens</c> (<c>0003c081</c>), the shutdown's step 3: <c>0004199c</c>, <c>Ttm_Shutdown</c>, the
	/// current menu destroyed and the render contexts <c>0007fcd8</c> and <c>0007fcdc</c> destroyed. None of them is held
	/// here: no menu is current, the scripts are not ported, and this engine keeps no render context for them.
	/// </summary>
	public void CloseScreens() {
	}
}
