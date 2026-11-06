using HercWorks.Core.Io;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Es1.Input;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Shell.MessageBox;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Shell;

/// <summary>
/// ES1's shell, <c>GO.EXE</c>'s <c>Shell_Main</c> (<c>00012ec7</c>), in the core's <see cref="FrontEndWindow"/>
/// (docs/es1/shell/startup.md): the start-up, the frame loop and the shutdown, as far as the main menu and its input. The
/// devices are <see cref="Es1ShellDevices"/>, the shared data <see cref="Es1ShellData"/>, the frame and its input
/// <see cref="Es1ShellFrame"/>, the screens <see cref="Es1ShellScreens"/> over <see cref="Es1ShellBackdrop"/> and
/// <see cref="Es1DirtyList"/> with the menus' window globals <see cref="Es1MenuGlobals"/>, the mouse's callbacks into the
/// menus <see cref="Es1ShellMouse"/>, and the frame-time scale <see cref="Es1ShellTime"/>; the monitor is
/// <see cref="Es1Monitor"/> on the display.
///
/// <para>The start-up's steps 1 to 6 run before the window opens, so a step that ends the program ends it without one;
/// steps 7 on and the frame loop run in the window, on host time (<see cref="Es1ShellFrame"/>); the shutdown runs once
/// the window has closed.</para>
///
/// <para><b>Engine guard:</b> closing the window ends the shell as EXIT TO DOS and the quit question's PROCEED do
/// through <c>Shell_QuitFlag</c>: the pass in progress is left where it stands, as though the flag had been set, and the
/// shutdown runs with <c>Shell_ExitCode</c> as it stands, the code the start read unless a screen set it.</para>
/// </summary>
public static class Es1ShellHost {
	/// <summary>The window's title.</summary>
	public const string Title = "HERCULAN — Metaltech: Earthsiege";

	/// <summary>The program's status after a severity-5 assert, <c>__exit</c>(1) (docs/es1/simulation/asserts.md#severities).</summary>
	public const int AssertExitStatus = 1;

	/// <summary>
	/// Runs the shell on <paramref name="installRoot"/> with <paramref name="arguments"/>, <c>GO.EXE</c>'s own switches
	/// (<see cref="Es1ShellSwitches"/>). Returns <c>Shell_ExitCode</c>, or <see cref="AssertExitStatus"/> when a start-up
	/// step asserts.
	/// </summary>
	/// <param name="imguiFontPath">The faces the frame's ImGui draws in (<see cref="FrontEndWindow"/>).</param>
	/// <param name="menus">
	/// HERCULAN's menu bar and its windows over the shell, which the window disposes once the turn has run. [Shift+Esc]
	/// raises it, plain [Esc] backs out of it while it is up, and the pointer at the window's top left raises it as over
	/// Earthsiege 2's shell; neither key then reaches the shell, nor does the mouse while it is over the bar or one of its
	/// windows, where the system's pointer stands in for the shell's own.
	/// </param>
	/// <param name="screenshotPath"><c>--screenshot</c>'s file, which the window captures and then closes; none for a play.</param>
	/// <param name="window">
	/// The window the launcher's turns share (<see cref="Host.Es1Launcher"/>), which stays open after the shell; none for a
	/// window of the shell's own, closed with it.
	/// </param>
	/// <param name="silent">The host's <c>--no-sound</c>: no audio device, so the sound manager plays into nothing.</param>
	public static int Run(string installRoot, IReadOnlyList<string> arguments, string imguiFontPath, string imguiItalicFontPath,
			IFrontEndMenus menus, string? screenshotPath, HostWindow? window = null, bool silent = false) {
		// 1. Mem_InitHeap(3,700,000) and its assert: this engine has no fixed heap.
		// 2. Shell_ParseCommandLine; without -eggplant this engine goes on (Es1ShellSwitches).
		var switches = Es1ShellSwitches.Parse(arguments);

		// 3. The drive prefix, ALT_DRIVE under -C and PREFER_ALT, before every path: this engine's install paths stand in
		// for it, so -C is read and changes no path. Assert_SetLogDir: this engine keeps no assert log.

		// 4. data\exit.cfg: the code read, 0 written and the code put back, so a run that ends before the shutdown leaves 0.
		if (Es1ExitCfg.Read(installRoot) is not { } exitCode) {
			Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open in {installRoot} (main.cpp line 0x90 asserts here).");
			return AssertExitStatus;
		}

		if (!Es1Shutdown.WriteExitCode(installRoot, 0)) {
			Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open for writing in {installRoot} (main.cpp line 0x88 asserts here).");
			return AssertExitStatus;
		}

		// Shell_StartupNoOp.

		// 5. The archives, mask 0x100 and the .vol extension (VolumeGroup_SetBufferSize, VolRStream_SetGroup).
		// PLACEHOLDER: subsystem phase 1, the bnd\ files its loaders read (docs/es1/formats/bnd-notes.md#the-shells-files) but
		// MAP, SHELLMAP and CTL_ALRT, which Session.Main loads (Es1ShellData.LoadTuning): their readers are later parts'.
		var content = Es1SingleMission.MountShell(installRoot);

		// 6. The heap against BND\MAIN.BND's +0xc and +8: this engine's memory is never that short, so Shell_LowMemory stays clear.

		// The audio device, opened for this turn as the simulator's turn opens its own, and closed after the shutdown.
		string? audioFailure = null;
		var device = silent ? null : OpenAlBackend.TryCreate(out audioFailure);
		using IAudioBackend audio = device as IAudioBackend ?? new NullAudioBackend();
		if (audioFailure != null) {
			Console.Error.WriteLine(device != null
				? $"Audio: {audioFailure}."
				: $"Audio unavailable ({audioFailure}) — the front end runs silent.");
		}

		return new Session(installRoot, content, switches, exitCode, audio).Run(imguiFontPath, imguiItalicFontPath, menus,
			screenshotPath, window);
	}

	/// <summary>One run's objects and its window.</summary>
	private sealed class Session {
		private readonly string _installRoot;
		private readonly GameContent _content;
		private readonly Es1ShellSwitches _switches;
		private readonly Es1ShellDevices _devices = new();
		private readonly Es1ShellTime _time = new();
		private readonly Es1MenuGlobals _globals = new();
		private readonly Es1DirtyList _dirty;
		private readonly Es1ShellBackdrop _backdrop;
		private readonly Es1ShellScreens _screens;
		private readonly Es1ShellFrame _frame;
		private readonly Es1ShellMouse _shellMouse;
		private readonly Es1LipSyncPlayer _lipSync;
		private readonly IAudioBackend _audio;
		private Es1ShellData? _data;
		private Es1Monitor? _monitor;

		// A fatal assert was raised: the window closes at the next update, and no shutdown runs.
		private bool _asserted;

		public Session(string installRoot, GameContent content, Es1ShellSwitches switches, short exitCode, IAudioBackend audio) {
			_installRoot = installRoot;
			_content = content;
			_switches = switches;
			_audio = audio;
			_dirty = new Es1DirtyList(_devices.Display);
			_backdrop = new Es1ShellBackdrop(_devices.Display, content, _dirty);
			_lipSync = new Es1LipSyncPlayer(_devices.Display, content.Read, _globals.MessageBox);
			_screens = new Es1ShellScreens(_devices, _backdrop, _dirty, content, _globals, _lipSync) { ExitCode = exitCode };
			_screens.MapMouse.Now = () => unchecked((uint)_time.AnmClock);
			_frame = new Es1ShellFrame(_devices, _backdrop, _dirty, _screens, _time, switches.DebugMode);
			_screens.PollInput = _frame.PollInput;
			_screens.LatchButton = _frame.LatchButton;
			_shellMouse = new Es1ShellMouse(_globals);
		}

		public int Run(string imguiFontPath, string imguiItalicFontPath, IFrontEndMenus menus, string? screenshotPath,
				HostWindow? shared) {
			using (var owned = shared == null ? new HostWindow() : null) {
				var host = shared ?? owned!;
				string dataDirectory = CaseInsensitivePath.Combine(_installRoot, Es1MissionFiles.DataFolder);
				using var window = new FrontEndWindow(host, Title, screenshotPath, imguiFontPath, imguiItalicFontPath, dataDirectory,
					menus);
				window.Load += (gl, input) => Guard(() => OnLoad(window, gl, input));
				window.Update += delta => {
					if (!_asserted) {
						_devices.Sound.UpdateDriver();
						_audio.Update();
						_devices.HostKeyboard?.Advance(delta);
						Guard(() => _frame.AdvanceHostFrame(TimeSpan.FromSeconds(delta)));
					}

					if (_asserted || _frame.Ended) {
						window.Close();
					}
				};
				window.Render += (delta, gl) => {
					window.BeginFrame(delta);
					ShowSystemPointer(window);
					_monitor?.Present(window.FramebufferSize);
					window.DrawMenuBar(revealOnHover: true);
					window.FrameDrawn(gl);
				};
				window.Closing += () => {
					window.DisposeImGui();
					_monitor?.Dispose();
					_monitor = null;
				};
				window.Run();
			}

			if (_asserted) {
				return AssertExitStatus;
			}

			// The window's close, the engine guard of the class summary, or the loop's end.
			_screens.QuitFlag = true;
			return Shutdown();
		}

		/// <summary>
		/// Runs <paramref name="step"/>; a fatal assert it raises (<see cref="Es1FatalAssertException"/>) takes
		/// <c>Assert_Report</c>'s fatal path (docs/es1/shell/startup.md#a-fatal-assert): the message,
		/// <c>Shell_AssertCleanupHook</c> (<see cref="Es1ShellDevices.AssertCleanup"/>, and <c>Shell_ExitCode</c> 0 written to
		/// <c>data\exit.cfg</c>), then <c>__exit</c>(1), here the window's close with <see cref="AssertExitStatus"/>.
		/// </summary>
		private void Guard(Action step) {
			try {
				step();
			} catch (Es1FatalAssertException assert) {
				Console.Error.WriteLine(assert.Message);
				_devices.AssertCleanup();
				_screens.ExitCode = 0;
				if (!Es1Shutdown.WriteExitCode(_installRoot, 0)) {
					Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open for writing in {_installRoot} (main.cpp line 0x88 asserts here).");
				}

				_asserted = true;
			}
		}

		/// <summary>
		/// The window's load: the monitor on the display, then the start-up's steps 7 on and the frame loop started, which
		/// run until the first wait for the timer; then the host's mouse and keys taken, the cursor standing in for the
		/// host's pointer over the window.
		/// </summary>
		private void OnLoad(FrontEndWindow window, GL gl, IInputContext input) {
			window.Attach(gl, input);
			_monitor = new Es1Monitor(gl, _devices.Display);
			_frame.UploadPalette = _monitor.SetPalette;
			_screens.UploadPalette = _monitor.SetPalette;
			_devices.Mouse.Pointer = () => Es1Monitor.Pointer(window.FramebufferSize, window.ClientSize);
			_devices.Mouse.HostHasPointer = () => window.ImGuiWantsMouse;

			_frame.Start(Main(Es1HostKeyboard.ReadBiosShiftFlags(window.Keyboard)));

			if (window.Mouse is { } mouse) {
				_devices.Mouse.Attach(mouse);
				mouse.Cursor.CursorMode = CursorMode.Hidden;
			}

			if (window.Keyboard is { } keyboard && _devices.HostKeyboard is { } hostKeyboard) {
				hostKeyboard.Attach(keyboard);
				hostKeyboard.HostTakes = key => MenuBarTakes(window, key);
				hostKeyboard.HostHasKeyboard = () => window.ImGuiWantsKeyboard;
			}
		}

		/// <summary>
		/// <b>This engine's own:</b> whether a key-down is the menu bar's, which it then has: [Shift+Esc] alone raises the bar
		/// or backs out of it, and so does plain [Esc] while the bar or one of its windows is up, the keys Earthsiege 2's
		/// shell gives the bar by default; and any key while a field of the bar's windows is typed into. [Esc] with Alt or
		/// Ctrl, and any other [Esc], is the shell's.
		/// </summary>
		private static bool MenuBarTakes(FrontEndWindow window, Key key) {
			if (key == Key.Escape) {
				bool Held(Key modifier) => window.Keyboard?.IsKeyPressed(modifier) == true;
				bool shift = Held(Key.ShiftLeft) || Held(Key.ShiftRight);
				bool alt = Held(Key.AltLeft) || Held(Key.AltRight);
				bool ctrl = Held(Key.ControlLeft) || Held(Key.ControlRight);
				if (!alt && !ctrl && (shift || window.MenuBarUp)) {
					window.MenuBarEscape();
					return true;
				}
			}

			return window.ImGuiWantsKeyboard;
		}

		/// <summary>
		/// <b>This engine's own:</b> the system's pointer while the pointer is over the menu bar or one of its windows, where
		/// the shell takes no mouse events and its own pointer stops; hidden over the shell, which draws its own.
		/// </summary>
		private static void ShowSystemPointer(FrontEndWindow window) {
			if (window.Mouse is not { } mouse) {
				return;
			}

			var mode = window.ImGuiWantsMouse ? CursorMode.Normal : CursorMode.Hidden;
			if (mouse.Cursor.CursorMode != mode) {
				mouse.Cursor.CursorMode = mode;
			}
		}

		/// <summary><c>Shell_Main</c> from its step 7 on (docs/es1/shell/startup.md#the-start-up), then the frame loop.</summary>
		private IEnumerable<Es1ShellWait> Main(byte biosShiftFlags) {
			// 7. FileNames_Load.
			var data = Es1ShellData.LoadFileNames(_content);
			data.Sound = _devices.Sound;
			_data = data;

			// Step 5's Map_LoadBnd, ShellMap_LoadBnd and CtlAlert_LoadBnd, here once the shared data exists; nothing between
			// reads them.
			data.LoadTuning(_content);

			// 8. The devices; 9. the frame-time scale; 10. a frame shown.
			_devices.Init(_content, _installRoot, _switches.DebugMode, biosShiftFlags, _globals, _audio);
			_time.Init(_devices.Timer);
			foreach (var wait in _frame.PresentFrame()) {
				yield return wait;
			}

			// 11. The shared data. 12. RenderContext_GetFullScreen into 0007fcd8, the render context the scripts draw
			// through (Es1ShellScreens.GetFullScreen).
			data.Load(_content, _devices.Preferences!, _installRoot, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			_screens.GetFullScreen();

			// 13. maybe_SncList_Init, the talking heads' player; ShellMap_LoadResources, the war map's clock, bitmaps and font;
			// ShellMouse_AddCallbacks, the mouse's three callbacks into the active window.
			_lipSync.Init(_devices.Timer);
			data.LoadMapResources(_content, _devices.Timer);
			_shellMouse.AddCallbacks(_devices.Mouse);

			// 14. The fonts and styles.
			data.LoadStylesAndFonts(_content);

			// 15. The first screen; 16. its palette uploaded and the screen drawn. The backdrop reads CareerRecord_Current
			// through the mission globals.
			var mission = new Es1ShellMission(_installRoot, _content, data);
			_backdrop.CurrentRecord = () => mission.CurrentRecord;
			foreach (var wait in _screens.OpenFirstScreen(_screens.ExitCode, _switches.PlayIntro, data, mission, _frame.PresentFrame,
					_frame.PollDeviceBlock)) {
				yield return wait;
			}

			if (_backdrop.Palette is { } palette) {
				_frame.UploadPalette?.Invoke(palette);
			}

			_backdrop.PaletteDue = false;
			foreach (var wait in _screens.DrawScreen()) {
				yield return wait;
			}

			while (!_screens.QuitFlag) {
				foreach (var wait in _frame.Pass()) {
					yield return wait;
				}
			}
		}

		/// <summary>
		/// The shutdown (docs/es1/shell/startup.md#the-shutdown): the careers log written, <c>Shell_ExitCode</c> to
		/// <c>data\exit.cfg</c>, the screens closed, the data freed, the devices shut down; returns the code.
		/// </summary>
		private int Shutdown() {
			// 1. CareerLog_Write(log, -1); 2. ExitCfg_Write.
			_data?.WriteCareersLog();
			if (!Es1Shutdown.WriteExitCode(_installRoot, _screens.ExitCode)) {
				Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open for writing in {_installRoot} (main.cpp line 0x88 asserts here).");
				return AssertExitStatus;
			}

			// 3. Shell_CloseScreens. 4. ShellMap_ReleaseResources, whose timer callback ends with the session's clock, and
			// Shell_FreeData, which hold nothing else the collector does not free, and ShellMouse_RemoveCallbacks.
			// 5. Shell_ShutdownDevices.
			_screens.CloseScreens();
			_shellMouse.RemoveCallbacks(_devices.Mouse);
			_devices.Shutdown();

			// 6. VolRStream_DeleteGroup and Mem_HeapOff, and under -X an INT 3, which this engine does not raise.
			// 7. Shell_ExitCode returned. The launcher (Host.Es1Launcher) reads the code from data\exit.cfg, not from this.
			return _screens.ExitCode;
		}
	}
}

/// <summary><c>data\exit.cfg</c>'s reader for the shell; its writer is <see cref="Es1Shutdown.WriteExitCode"/>, the simulator's too.</summary>
public static class Es1ExitCfg {
	/// <summary>
	/// <c>ExitCfg_Read</c> (<c>0001289c</c>): the file's <c>int16</c>, or null when it does not open, where retail asserts
	/// at severity 5. A file shorter than two bytes gives 0 for the bytes it lacks, an engine guard.
	/// </summary>
	public static short? Read(string installRoot) {
		string path = Es1MissionFiles.DataPath(installRoot, Es1Shutdown.ExitName);
		if (!File.Exists(path)) {
			return null;
		}

		byte[] bytes;
		try {
			bytes = File.ReadAllBytes(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return null;
		}

		return (short)((bytes.Length > 0 ? bytes[0] : 0) | (bytes.Length > 1 ? bytes[1] << 8 : 0));
	}

	/// <summary>
	/// The launcher's read, <c>Launcher_ReadExitCfg</c> (<c>00000448</c> in <c>ES1ESCD</c>): <paramref name="path"/> opened
	/// for reading and writing, as <c>r+b</c> opens it, and up to its first 4 bytes laid over <paramref name="dword"/>'s
	/// low bytes, the bytes past the file's end keeping their value. Null when the file does not open, where retail asserts.
	/// </summary>
	public static int? ReadOver(string path, int dword) {
		Span<byte> bytes = stackalloc byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, dword);
		try {
			using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
			file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
			return null;
		}

		return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
	}
}
