using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's screen state and the routines of <c>shell.cpp</c> the start-up, the frame loop and the shutdown run
/// (docs/es1/shell/menus.md#opening-and-closing-a-screen): <c>Shell_OpenFirstScreen</c>, <c>Shell_HandleMessage</c>,
/// <c>Shell_DrawScreen</c>, <c>Shell_TickScreen</c>, <c>Shell_OpenQuitAlert</c> and <c>Shell_CloseScreens</c>. The menus
/// themselves are <see cref="Es1Menu"/> and the window classes under it.
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

	/// <summary>
	/// <c>Shell_ExitCode</c> (<c>0007fcac</c>), the code the shutdown writes to <c>data\exit.cfg</c> and the shell returns:
	/// the code the start-up read until a screen sets it (docs/es1/shell/startup.md#the-ways-out).
	/// </summary>
	public short ExitCode { get; set; }

	/// <summary><c>Shell_QuitFlag</c> (<c>0007fd05</c>), which ends the frame loop at its next test.</summary>
	public bool QuitFlag { get; set; }

	/// <summary>
	/// <c>Shell_DemoRequested</c> (<c>000817ce</c>), which the options menu's VIEW DEMO sets and screen 9999 turns into exit
	/// code 5 (docs/es1/shell/main-menu.md#the-return-screen). No ported screen sets it.
	/// </summary>
	public bool DemoRequested { get; set; }

	/// <summary><c>Palette_UploadWhole</c>: the palette made the live one, the monitor's.</summary>
	public Action<HercWorks.Core.Data.File.Dyn.DynamixPalette>? UploadPalette { get; set; }

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

	/// <summary>
	/// The byte <c>000817c6</c>, 1 in the image: <see cref="DrawScreen"/> draws the backdrop. <see cref="HandleMessage"/>
	/// sets it when the new screen shows a new backdrop and clears it when it does not (disassembly <c>0003ca4d</c>,
	/// <c>0003ca6e</c>); <see cref="OpenQuitAlert"/> clears it, so the question is drawn over the picture as it stands.
	/// </summary>
	public bool DrawsBackdrop { get; private set; } = true;

	/// <summary>
	/// <c>Shell_ResumeScreen</c> (<c>000817be</c>), −1 in the image: what a <i>back</i> message (target 99) stores as the
	/// current screen, once, so that the screen it opens takes it as its previous one. <see cref="OpenQuitAlert"/> sets it
	/// to the previous screen of the screen it closes.
	/// </summary>
	public short ResumeScreen { get; set; } = -1;

	/// <summary>
	/// The object <c>Shell_MissionMap</c> (<c>000809a4</c>), the briefing's mission map, none in the image. Its class is
	/// S5's and no ported screen makes one (<see cref="Es1ShellMission.Prepare"/>); <see cref="HandleMessage"/> tests it as
	/// retail does.
	/// </summary>
	public object? MissionMap { get; set; }

	/// <summary>
	/// The byte <c>000809a8</c>, 0 in the image, which the mission map's redraws count down (<c>Shell_TickScreen</c>,
	/// <c>Shell_DrawScreen</c>, <c>Shell_HandleInput</c>; menus.md's Open). No ported screen sets it.
	/// </summary>
	public byte MapRefresh { get; set; }

	/// <summary>
	/// The byte <c>000817bd</c>, 0 in the image: how many more times <see cref="DrawScreen"/> runs the mission map's drawing
	/// first (menus.md's Open). <see cref="HandleMessage"/> sets it for the alerts 3000 and 3333 over the war map, and
	/// <see cref="OpenQuitAlert"/> for the quit question.
	/// </summary>
	public byte MapDrawCount { get; private set; }

	/// <summary>
	/// The byte <c>000809a9</c>, 0 in the image: <see cref="HandleMessage"/> plays the intro's last two scripts on the next
	/// change of screen when it is 1, the credits when it is 2. No ported screen sets it.
	/// </summary>
	public byte ScriptsToPlay { get; set; }

	/// <summary><see cref="HandleMessage"/>'s answer: 1 when it changed screens, 0 when it did not.</summary>
	public bool MessageChangedScreen { get; private set; }

	/// <summary>The byte <c>000817bc</c>: a script <see cref="PlayTtm"/> played was cut short, so the intro skips <c>lead_in.ttm</c>.</summary>
	public bool IntroCutShort { get; private set; }

	/// <summary>The <c>.ANM</c> name at <c>000aacfe</c>.</summary>
	public string? AnmName { get; set; }

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

	/// <summary>The shell's <c>.TTM</c> scripts, their handle table and globals, which <see cref="OpenFirstScreen"/> sets up; none before.</summary>
	public Es1TtmManager? Scripts { get; private set; }

	/// <summary><c>TtmLists_Load</c>'s lists, the array at <c>0007e710</c> of the count byte <c>0007e714</c>'s lists; none before it reads a count.</summary>
	public Es1NameList[]? TtmLists { get; private set; }

	/// <summary>
	/// <c>RenderContext_GetFullScreen</c> (<c>0004c716</c>): the one render context <c>00083bc4</c> it makes, set each call
	/// to the rect (0, 0)–(319, 199), the origin (0, 0), clip mode 1 and the translation flag clear, made afresh for each
	/// use; <c>Shell_Main</c> keeps it at <c>0007fcd8</c>.
	/// </summary>
	public Es1PanelCanvas FullScreenContext() => new(_devices.Display, FullScreen);

	/// <summary>The scripts' timer count, every second interrupt: 60 ticks a second.</summary>
	private const int ScriptTimerCount = 2;

	/// <summary><c>Ttm_Configure</c>'s handle count.</summary>
	private const byte ScriptHandles = 0x32;

	/// <summary>The folders <c>Ttm_SetPathPrefix</c> is given, the two <c>SHELL0</c> folders that hold scripts.</summary>
	private const string ScriptFolder = "res32\\", IntroFolder = "intro\\";

	/// <summary>
	/// <c>Shell_OpenFirstScreen</c> (<c>0003ba47</c>)(<paramref name="exitCode"/>), the start-up's step 15
	/// (docs/es1/shell/menus.md#the-first-screen--shell_openfirstscreen-0003ba47): the scripts set up
	/// (<c>Ttm_Configure</c>(page 2, the full-screen context, <c>0x32</c>), <c>Ttm_AllocHandles</c>(2) on
	/// <c>Shell_Timer</c>, the prefix <c>res32\</c> and <c>TtmLists_Load</c>), then by the code the intro or a mission's
	/// results, then the main menu over the backdrop 1000: a <see cref="Es1MainMenu"/> made the current menu and opened as
	/// 1000. The intro, any code but 3, 4 and 6 while <paramref name="playIntro"/> is set, plays <c>intro\dynamix.ttm</c>,
	/// <c>intro\lead_in.ttm</c> unless the first was cut short, <c>intro\iintro.ttm</c> and <c>intro\earth_w.ttm</c>
	/// (<see cref="PlayTtm"/>); the prefix goes back to <c>res32\</c> as the main menu's case begins.
	/// </summary>
	/// <param name="playIntro"><c>Shell_PlayIntro</c>, which <c>-A</c> clears.</param>
	/// <param name="data">The shared data the start-up loaded, which the menus draw with.</param>
	/// <param name="mission">The mission the menus load, which their handlers reach through the menus' context.</param>
	/// <param name="presentFrame"><c>Shell_PresentFrame</c>, which the intro's scripts show their frames with.</param>
	/// <param name="pollDeviceBlock"><c>Input_PollDeviceBlock</c>, which the intro's scripts read the key or button that cuts them short from.</param>
	public IEnumerable<Es1ShellWait> OpenFirstScreen(short exitCode, bool playIntro, Es1ShellData data, Es1ShellMission mission,
			Func<IEnumerable<Es1ShellWait>> presentFrame, Func<Es1LiveBlock> pollDeviceBlock) {
		// The devices load the options before the first screen; options of no file stand in when they did not, an engine guard.
		_menuContext = new Es1MenuContext(_devices.Display, _content, data, Globals, _dirty, this, mission,
			_devices.Preferences ?? Es1Preferences.Default);
		// DirtyList_CreateContext is the dirty list's (Es1DirtyList). Then the scripts. The shared data's generator exists from
		// the start-up's step 11 on; a draw of 0 without it is an engine guard.
		var random = data.Random;
		var scripts = Es1TtmManager.ForShell(_devices.Display, _content.Read, _dirty, bound => random?.Below(bound) ?? 0);
		scripts.UploadPalette = palette => UploadPalette?.Invoke(palette);
		scripts.PresentPalette = palette => {
			_backdrop.Palette = palette;
			_backdrop.PaletteDue = true;
		};
		Scripts = scripts;
		scripts.Configure(Es1ShellBackdrop.BackgroundPage, FullScreenContext, ScriptHandles);
		scripts.AllocHandles(ScriptTimerCount, _devices.Timer);
		scripts.SetPathPrefix(ScriptFolder);
		LoadTtmLists();
		switch (exitCode) {
			case 3:
			case 4:
				// PLACEHOLDER: back from a mission, Mission_ReadRestore, Shell_ProcessMissionResults and the results screens
				// of a single mission or a career, with the endings' scripts through PlayTtm, which are S2's and S3's; the
				// start goes on to the main menu as code 6 does.
				Console.WriteLine($"data\\exit.cfg holds {exitCode}, back from a mission: the results screens are not ported, so the main menu opens.");
				break;
			case 6:
				break;
			default:
				if (playIntro) {
					scripts.SetPathPrefix(IntroFolder);
					// PLACEHOLDER: ShellMusic_Play(0xc), and (0xd) before iintro.ttm; ES1's sound is not ported
					// (Es1ShellDevices.Init's ShellSound_Init).
					foreach (var wait in PlayTtm("dynamix.ttm", presentFrame, pollDeviceBlock)) {
						yield return wait;
					}

					if (!IntroCutShort) {
						foreach (var wait in PlayTtm("lead_in.ttm", presentFrame, pollDeviceBlock)) {
							yield return wait;
						}
					}

					foreach (var wait in PlayTtm("iintro.ttm", presentFrame, pollDeviceBlock)) {
						yield return wait;
					}

					foreach (var wait in PlayTtm("earth_w.ttm", presentFrame, pollDeviceBlock)) {
						yield return wait;
					}
				}

				IntroCutShort = false;
				break;
		}

		// The main menu's case.
		scripts.SetPathPrefix(ScriptFolder);
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
	/// <c>TtmLists_Load</c> (<c>00011dfc</c>) (docs/es1/formats/shell-file-lists.md#the-animation-lists):
	/// <c>cfg\tm_files.dat</c>'s lists, which replace the ones before unless the file's count is 0. A file that does not
	/// open or read leaves the lists as they are here, where retail reads its count from the failed stream.
	/// </summary>
	private void LoadTtmLists() {
		if (_content.Read("cfg", "TM_FILES.DAT") is { } bytes && new Es1TtmFileListsTransformer().Parse(bytes) is { } file
				&& file.Lists.Length != 0) {
			TtmLists = file.Lists;
		}
	}

	/// <summary>
	/// <c>Shell_PlayTtm</c> (<c>0003b8df</c>)(<paramref name="name"/>) (docs/es1/shell/menus.md#the-first-screen--shell_openfirstscreen-0003ba47):
	/// the draw page and page 2 cleared to colour 0 with <c>VGA8_ClearPage</c>, the raster's slot 35 (<c>0008663c</c>,
	/// docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps), a frame shown and the new draw page cleared; the
	/// full-screen render context made the scripts' default; the script opened playing, not looping, on that default
	/// (<c>Ttm_Open</c>(0, 0, 0, name)); then, a frame each, the script run (<c>Ttm_RunScript</c>), the input read
	/// (<c>Input_PollDeviceBlock</c>) and a frame shown, until the script's mode is not playing, the input's keyboard word
	/// is not 0 or its first mouse button is down. A script still playing, so cut short, has the pages cleared again with
	/// a frame between; a key or the button sets <see cref="IntroCutShort"/>. Then the script closed and the palette
	/// <c>0007fcd0</c> cleared. A script that does not open counts as ended after one frame here (see
	/// <see cref="Es1TtmManager.GetMode"/>), where retail reads the slot before the handle table.
	/// </summary>
	private IEnumerable<Es1ShellWait> PlayTtm(string name, Func<IEnumerable<Es1ShellWait>> presentFrame,
			Func<Es1LiveBlock> pollDeviceBlock) {
		var display = _devices.Display;
		var scripts = Scripts!;
		foreach (var wait in ClearPages()) {
			yield return wait;
		}

		scripts.SetDefaultContext(FullScreenContext);
		byte handle = scripts.Open(Es1TtmPlayer.Playing, 0, null, name);
		Es1LiveBlock block;
		while (true) {
			foreach (var wait in scripts.RunScript(handle)) {
				yield return wait;
			}

			block = pollDeviceBlock();
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			if (scripts.GetMode(handle) != Es1TtmPlayer.Playing || block.CommandWord != 0 || block.MouseButtons[0] != 0) {
				break;
			}
		}

		if (scripts.GetMode(handle) == Es1TtmPlayer.Playing) {
			foreach (var wait in ClearPages()) {
				yield return wait;
			}
		}

		if (block.CommandWord != 0 || block.MouseButtons[0] != 0) {
			IntroCutShort = true;
		}

		scripts.Close(handle);
		_backdrop.Palette = null;

		// The draw page as it is at each call: before the frame, and the new one after it.
		IEnumerable<Es1ShellWait> ClearPages() {
			display.ClearPage(display.DrawPage, 0);
			display.ClearPage(Es1ShellBackdrop.BackgroundPage, 0);
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			display.ClearPage(display.DrawPage, 0);
		}
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
				// unread; a change to a screen with a new backdrop and no .ANM reaches it, as the later modules' screens do.
			}

			if (AnmMode == 2) {
				// PLACEHOLDER: Anm_Play(1, 0xa, AnmName); the shell has no .ANM player yet (docs/es1/formats/anm-animation.md).
				AnmMode = 0;
			}

			_backdrop.Draw();
			if (BackdropId == 4000) {
				// PLACEHOLDER: 0001240b and the career record's HERC diagram, S3's; RESUME's screen 1700 shows backdrop 4000,
				// with no menu until S4.
			} else {
				// PLACEHOLDER: 00012467 is unread.
			}
		} else if (CurrentScreen is 1700 or 1710) {
			// PLACEHOLDER: the career record's HERC diagram (00029651, CareerRecord_SelectPilot), S3's.
		}

		// PLACEHOLDER: DigitalDialog_ShowMessage(Shell_MessageBox) and Ttm_RunAll (Scripts.RunAllAfterTick), the message box
		// and the scripts it draws through (P7b, docs/es1/shell/main-menu.md#instant-actions-path-to-the-mission).
		if (CurrentMenu is { } menu) {
			DrawMenu(menu, keptDraw);
		} else {
			display.DrawPage = keptDraw;
		}

		if (AnmMode == 1) {
			// Palette_UploadWhole(0007fcd0) and 0007fcd4 cleared, then Anm_Play(1, 0xb, AnmName). PLACEHOLDER: the shell has
			// no .ANM player yet (docs/es1/formats/anm-animation.md).
			if (_backdrop.Palette is { } palette) {
				UploadPalette?.Invoke(palette);
			}

			_backdrop.PaletteDue = false;
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
	/// default (PLACEHOLDER: P7b's, with <see cref="Es1TtmManager.SetDefaultContext"/>; until then the scripts keep the
	/// full-screen default, and no ported screen opens a script past the intro); the source and draw pages set to page 2
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

	/// <summary>The target a <i>back</i> message names, 99.</summary>
	private const short BackTarget = 99;

	/// <summary>
	/// The numbers a message may name, <c>Shell_ScreenIds</c> (<c>000817e0</c>), zero-ended in the image: the same 41 as
	/// the case table's at <c>00082060</c>.
	/// </summary>
	private static readonly short[] ScreenIds = [
		999, 1000, 1100, 1110, 1200, 1210, 1220, 1230, 1240, 1250, 1300, 1310, 1320, 1330, 1499, 1490, 1500, 1501, 1520, 1510,
		1610, 1700, 1710, 1800, 1820, 1900, 1910, 1920, 2000, 2010, 9999, 3000, 3333, 4444, 7770, 7771, 7772, 5555, 6666, 1260,
		1270,
	];

	/// <summary>
	/// <c>Shell_HandleMessage</c> (<c>0003c7d8</c>)(<paramref name="message"/>), the frame loop's step 4
	/// (docs/es1/shell/menus.md#changing-screens--shell_handlemessage-0003c7d8): the pending message reset; nothing more
	/// for the code −1; the current menu's handler given the message, which it may change; a <i>back</i> target resolved;
	/// nothing more for a target not in <c>Shell_ScreenIds</c>; otherwise the current menu closed and destroyed, the
	/// scripts <see cref="ScriptsToPlay"/> names, a new backdrop when the target's group differs (after a frame of its own
	/// when the old one was 1000, <paramref name="presentFrame"/>), the target's case, the new menu opened, the screens
	/// moved on and the screen drawn. The answer is <see cref="MessageChangedScreen"/>.
	/// </summary>
	public IEnumerable<Es1ShellWait> HandleMessage(Es1MenuMessage message, Func<IEnumerable<Es1ShellWait>> presentFrame) {
		MessageChangedScreen = false;
		Globals.PendingMessage = Es1MenuMessage.None;
		if (message.Code == -1) {
			yield break;
		}

		CurrentMenu?.OnMessage(ref message);
		short target = message.Target;
		if (target == BackTarget) {
			bool fromWarMap = PreviousScreen is 1500 or 1501 && MissionMap == null;
			if (CurrentScreen != MainMenuScreen && !fromWarMap) {
				target = PreviousScreen;
			}

			if (ResumeScreen != -1) {
				CurrentScreen = ResumeScreen;
				ResumeScreen = -1;
			}
		}

		if (Array.IndexOf(ScreenIds, target) < 0) {
			yield break;
		}

		if (target is 3000 or 3333 && CurrentScreen is 1500 or 1501) {
			MapDrawCount = (byte)(MapRefresh != 0 ? 3 : 0);
		}

		if (CurrentMenu is { } closing) {
			closing.Close();
			closing.Dtor();
			CurrentMenu = null;
		}

		var display = _devices.Display;
		if (ScriptsToPlay == 1) {
			ScriptsToPlay = 0;
			// The raster slot 0008662c, VGA_8's slot 31, takes the cursor off.
			display.Cursor.Hide();
			// PLACEHOLDER: Ttm_SetPathPrefix(intro\), ShellMusic_Play(0xd), Shell_PlayTtm (PlayTtm) of intro\iintro.ttm and
			// intro\earth_w.ttm, and Ttm_SetPathPrefix(res32\); no ported screen sets ScriptsToPlay.
		}

		if (ScriptsToPlay == 2) {
			display.Cursor.Hide();
			// PLACEHOLDER: Ttm_SetPathPrefix(intro\), Shell_PlayTtm (PlayTtm) of intro\credit_a.ttm and intro\credit_b.ttm,
			// and Ttm_SetPathPrefix(res32\); no ported screen sets ScriptsToPlay.
		}

		short group = Es1ShellBackdrop.ForScreen(BackdropId, target);
		if ((group == 0 || group == BackdropId) && AnmMode != 2) {
			DrawsBackdrop = false;
		} else {
			if (BackdropId == MainMenuScreen) {
				display.Cursor.Hide();
				foreach (var wait in presentFrame()) {
					yield return wait;
				}
			}

			DrawsBackdrop = true;
			BackdropId = group;
			_backdrop.Show(group);
		}

		if (ScriptsToPlay == 2) {
			_backdrop.MusicTrack = 1;
			_backdrop.MusicDue = true;
			ScriptsToPlay = 0;
		}

		OpenScreenCase(target);
		CurrentMenu?.Open(target);
		PreviousScreen = target == MainMenuScreen ? MainMenuScreen : CurrentScreen;
		CurrentScreen = target;
		DrawScreen();
		MessageChangedScreen = true;
	}

	/// <summary>
	/// <c>Shell_HandleMessage</c>'s step 9, the case of the table at <c>00082060</c> (targets at <c>00082104</c>) for
	/// <paramref name="target"/>: most make the screen's object the current menu (docs/es1/shell/menus.md#the-screens).
	/// The main menu's, SINGLE MISSION's, MISSION PARAMS' and the quit question's are ported, and screen 9999's exit code;
	/// with any other the screen becomes current with no menu, as retail's does for the cases that make none, its backdrop
	/// shown with no window.
	/// </summary>
	private void OpenScreenCase(short target) {
		switch (target) {
			case MainMenuScreen:
				// 0003d6f2: a MainMenu.
				if (_menuContext != null) {
					CurrentMenu = new Es1MainMenu(_menuContext);
				}

				break;
			case LoadingScreen:
				// 0003cab5.
				ExitCode = (short)(DemoRequested ? 5 : 2);
				// PLACEHOLDER: a SimLoadMenu, ShellBackdrop_LoadPalette(9999), 0007fcd5 cleared, anm\esiege2.anm with 0007fcd7 2
				// and the rect 0009b000 copied to the visible page: the loading screen, S2's, which the war map's BEGIN MISSION >
				// and the options menu's VIEW DEMO reach.
				break;
			case QuitAlertScreen:
				// 0003d553: a QuitAlert, which step 10 opens as menu 999.
				if (_menuContext != null) {
					CurrentMenu = new Es1QuitAlert(_menuContext);
				}

				break;
			case 1490:
			case 1499:
				// PLACEHOLDER: 0003cccc and 0003ccdd: MessageBox_SetKind(Shell_MessageBox, 2), and for 1499 0 in Shell_Mode 0 and
				// 1 otherwise; the message box is S2's. Neither makes a menu, so until it lands INSTANT ACTION and SINGLE
				// MISSION's BRIEFING > show its backdrop with no window, and the quit keys' question is the way out.
				break;
			case 1520:
			case 1510:
				// PLACEHOLDER: 0003cd09 (MessageBox_SetKind(Shell_MessageBox, 4) once, by the byte 00081d20) and 0003cd27,
				// DebriefingMenu_Ctor; S2's debriefing.
				break;
			case Es1MissionMenu.Screen:
				// 0003e3ea: a MissionMenu, SINGLE MISSION.
				if (_menuContext != null) {
					CurrentMenu = new Es1MissionMenu(_menuContext);
				}

				break;
			case Es1MissionOptionsAlert.Screen:
				// 0003e589: a MissionOptionsAlert, MISSION PARAMS; at 0003e718 options 12 to 16 into the five words, each byte
				// zero-extended, and the words into the object's +0x26 to +0x2e (docs/es1/shell/single-mission.md#the-mission-params-menu).
				if (_menuContext != null) {
					var alert = new Es1MissionOptionsAlert(_menuContext);
					short[] words = _menuContext.Data.MissionParams;
					for (int i = 0; i < words.Length; i++) {
						words[i] = _menuContext.Preferences[Es1MissionOptionsAlert.FirstOption + i];
					}

					Array.Copy(words, alert.OpenedParams, words.Length);
					CurrentMenu = alert;
				}

				break;
			case 1500:
			case 1501:
				// PLACEHOLDER: 0003ef9b: WarMapMenu_Ctor, the war map; S5's.
				break;
			case 1200:
			case 1210:
			case 1220:
			case 1230:
			case 1240:
			case 1250:
			case 1260:
			case 1270:
			case 1330:
			case 5555:
				// PLACEHOLDER: the career's screens, S3's: CareerMenu, NewCareerAlert, CareerRecordsMenu, CareerStatsAlert,
				// SaveWarningAlert, KIAWarningAlert, DeleteAlert, BattlesetAlert and TechReleaseAlert.
				break;
			case 1610:
			case 1700:
			case 1710:
			case 1800:
			case 1820:
			case 1900:
			case 1910:
			case 1920:
			case 2000:
			case 2010:
			case 3000:
			case 3333:
			case 6666:
			case 7770:
			case 7771:
			case 7772:
			case 4444:
				// PLACEHOLDER: the hangar's screens, S4's: 0002c743 with ShellBackdrop_LoadPalette(1610), ServiceBayMenu,
				// ArmingMenu, RepairMenu, RepairPrefsAlert, 0002f1aa, 0002986f(3) with 000307c9 (which sets the hangar test's
				// dword 0007fce4), SalvageAllocAlert, UnArmedAlert, UnAssignedAlert, AnyDisabledAlert and ScrapAlert.
				break;
			case 1300:
			case 1310:
			case 1320:
				// PLACEHOLDER: OptionsMenu, PrefsMenu and screen 1320's code at 0003e9ae (the keyboard's subscribers and mode);
				// the options menus, which the plan's shell modules S1–S5 do not list yet.
				break;
		}
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

	/// <summary>The menu the quit question opens as on backdrop 1000, whose window is the main menu's rect.</summary>
	private const short QuitAlertMainMenu = 888;

	/// <summary>The backdrop group whose palette the quit question loads again, the service bay's.</summary>
	private const short ServiceBayBackdrop = 4000;

	/// <summary>
	/// <c>Shell_OpenQuitAlert</c> (<c>000408cc</c>), the frame loop's step 6 after a quit key
	/// (docs/es1/shell/main-menu.md#the-quit-question): <see cref="MapDrawCount"/> 3 while <see cref="MapRefresh"/> is set
	/// and 0 otherwise; then, unless the current screen is 999 already: with a current menu,
	/// <see cref="PreviousScreen"/> kept in <see cref="ResumeScreen"/> and the menu closed and destroyed;
	/// <see cref="DrawsBackdrop"/> cleared; an <see cref="Es1QuitAlert"/> made the current menu and opened as menu 888 on
	/// backdrop 1000, as 999 after backdrop 4000's palette is loaded again on backdrop 4000, and as 999 on any other; the
	/// screens moved on to 999; and the screen drawn. The backdrop and the music stay.
	/// </summary>
	public void OpenQuitAlert() {
		MapDrawCount = (byte)(MapRefresh != 0 ? 3 : 0);
		// The context exists from the first screen on, before the frame loop reads a quit key; the test is an engine guard.
		if (CurrentScreen == QuitAlertScreen || _menuContext == null) {
			return;
		}

		if (CurrentMenu is { } closing) {
			ResumeScreen = PreviousScreen;
			closing.Close();
			closing.Dtor();
			CurrentMenu = null;
		}

		DrawsBackdrop = false;
		var alert = new Es1QuitAlert(_menuContext);
		CurrentMenu = alert;
		if (BackdropId == MainMenuScreen) {
			alert.Open(QuitAlertMainMenu);
		} else {
			if (BackdropId == ServiceBayBackdrop) {
				_backdrop.LoadPalette(ServiceBayBackdrop);
			}

			alert.Open(QuitAlertScreen);
		}

		PreviousScreen = CurrentScreen;
		CurrentScreen = QuitAlertScreen;
		DrawScreen();
	}

	/// <summary>
	/// <c>Shell_CloseScreens</c> (<c>0003c081</c>), the shutdown's step 3: <c>0004199c</c>; <c>Ttm_Shutdown</c>, the
	/// scripts' (<see cref="Es1TtmManager.Shutdown"/>); the current menu destroyed through its slot <c>+4</c>
	/// (<see cref="Es1Menu.Dtor"/>), which leaves its window as it stands; and the render contexts <c>0007fcd8</c> and
	/// <c>0007fcdc</c> destroyed, which this engine makes afresh for each use.
	/// </summary>
	public void CloseScreens() {
		Scripts?.Shutdown();
		CurrentMenu?.Dtor();
	}
}
