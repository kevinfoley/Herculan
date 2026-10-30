using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Shell;

/// <summary>
/// ES1's shell, <c>GO.EXE</c>'s <c>Shell_Main</c> (<c>00012ec7</c>), in the core's <see cref="FrontEndWindow"/>
/// (docs/es1/shell/startup.md): the start-up, the frame loop and the shutdown, as far as the main menu's picture under the
/// cursor. The devices are <see cref="Es1ShellDevices"/>, the shared data <see cref="Es1ShellData"/>, the frame and its
/// input <see cref="Es1ShellFrame"/>, the screens <see cref="Es1ShellScreens"/> over <see cref="Es1ShellBackdrop"/> and
/// <see cref="Es1DirtyList"/>, and the frame-time scale <see cref="Es1ShellTime"/>; the monitor is <see cref="Es1Monitor"/>
/// on the display.
///
/// <para>The start-up's steps 1 to 6 run before the window opens, so a step that ends the program ends it without one;
/// steps 7 on and the frame loop run in the window, on host time (<see cref="Es1ShellFrame"/>); the shutdown runs once
/// the window has closed.</para>
///
/// <para><b>Engine guard:</b> no ported screen sets <c>Shell_QuitFlag</c> yet (<c>EXIT TO DOS</c> and the quit question
/// are P3's), so closing the window ends the shell: the pass in progress is left where it stands, as though the flag had
/// been set, and the shutdown runs with <c>Shell_ExitCode</c> as it stands, the code the start read.</para>
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
	/// <param name="screenshotPath"><c>--screenshot</c>'s file, which the window captures and then closes; none for a play.</param>
	public static int Run(string installRoot, IReadOnlyList<string> arguments, string imguiFontPath, string imguiItalicFontPath,
			string? screenshotPath) {
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
		// PLACEHOLDER: subsystem phase 1, the eight bnd\ files Main_LoadBnd reads (docs/es1/formats/bnd-notes.md#the-shells-files),
		// whose readers are later parts'.
		var content = Es1SingleMission.MountShell(installRoot);

		// 6. The heap against BND\MAIN.BND's +0xc and +8: this engine's memory is never that short, so Shell_LowMemory stays clear.

		return new Session(installRoot, content, switches, exitCode).Run(imguiFontPath, imguiItalicFontPath, screenshotPath);
	}

	/// <summary>One run's objects and its window.</summary>
	private sealed class Session {
		private readonly string _installRoot;
		private readonly GameContent _content;
		private readonly Es1ShellSwitches _switches;
		private readonly Es1ShellDevices _devices = new();
		private readonly Es1ShellTime _time = new();
		private readonly Es1DirtyList _dirty;
		private readonly Es1ShellBackdrop _backdrop;
		private readonly Es1ShellScreens _screens;
		private readonly Es1ShellFrame _frame;
		private Es1ShellData? _data;
		private Es1Monitor? _monitor;

		/// <summary><c>Shell_ExitCode</c> (<c>0007fcac</c>).</summary>
		private short _exitCode;

		public Session(string installRoot, GameContent content, Es1ShellSwitches switches, short exitCode) {
			_installRoot = installRoot;
			_content = content;
			_switches = switches;
			_exitCode = exitCode;
			_dirty = new Es1DirtyList(_devices.Display);
			_backdrop = new Es1ShellBackdrop(_devices.Display, content, _dirty);
			_screens = new Es1ShellScreens(_devices, _backdrop, _dirty);
			_frame = new Es1ShellFrame(_devices, _backdrop, _dirty, _screens, _time, switches.DebugMode);
		}

		public int Run(string imguiFontPath, string imguiItalicFontPath, string? screenshotPath) {
			using (var host = new HostWindow()) {
				string dataDirectory = CaseInsensitivePath.Combine(_installRoot, Es1MissionFiles.DataFolder);
				using var window = new FrontEndWindow(host, Title, screenshotPath, imguiFontPath, imguiItalicFontPath, dataDirectory,
					new Es1ShellMenus());
				window.Load += (gl, input) => OnLoad(window, gl, input);
				window.Update += delta => {
					_devices.HostKeyboard?.Advance(delta);
					_frame.AdvanceHostFrame(TimeSpan.FromSeconds(delta));
					if (_frame.Ended) {
						window.Close();
					}
				};
				window.Render += (delta, gl) => {
					window.BeginFrame(delta);
					_monitor?.Present(window.FramebufferSize);
					window.DrawMenuBar(revealOnHover: false);
					window.FrameDrawn(gl);
				};
				window.Closing += () => {
					window.DisposeImGui();
					_monitor?.Dispose();
					_monitor = null;
				};
				window.Run();
			}

			// The window's close, the engine guard of the class summary, or the loop's end.
			_frame.QuitFlag = true;
			return Shutdown();
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
			_devices.Mouse.Pointer = () => Es1Monitor.Pointer(window.FramebufferSize, window.ClientSize);

			_frame.Start(Main(Es1HostKeyboard.ReadBiosShiftFlags(window.Keyboard)));

			if (window.Mouse is { } mouse) {
				_devices.Mouse.Attach(mouse);
				mouse.Cursor.CursorMode = CursorMode.Hidden;
			}

			if (window.Keyboard is { } keyboard) {
				_devices.HostKeyboard?.Attach(keyboard);
			}
		}

		/// <summary><c>Shell_Main</c> from its step 7 on (docs/es1/shell/startup.md#the-start-up), then the frame loop.</summary>
		private IEnumerable<Es1ShellWait> Main(byte biosShiftFlags) {
			// 7. FileNames_Load.
			var data = Es1ShellData.LoadFileNames(_content);
			_data = data;

			// 8. The devices; 9. the frame-time scale; 10. a frame shown.
			_devices.Init(_content, _installRoot, _switches.DebugMode, biosShiftFlags);
			_time.Init(_devices.Timer);
			foreach (var wait in _frame.PresentFrame()) {
				yield return wait;
			}

			// 11. The shared data. 12. RenderContext_GetFullScreen into 0007fcd8, the render context the scripts draw
			// through, which this engine does not keep.
			data.Load(_content, _devices.Preferences!, _installRoot, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

			// 13. PLACEHOLDER: maybe_SncList_Init (the .SNC list), ShellMap_LoadResources (the briefing map's bitmaps and its
			// timer callback) and ShellMouse_AddCallbacks (the mouse's three callbacks into the active window), later parts'.
			// Until the last lands the cursor stays where the start-up put it.

			// 14. The fonts and styles.
			data.LoadStylesAndFonts(_content);

			// 15. The first screen; 16. its palette uploaded and the screen drawn.
			_screens.OpenFirstScreen(_exitCode, _switches.PlayIntro);
			if (_backdrop.Palette is { } palette) {
				_frame.UploadPalette?.Invoke(palette);
			}

			_backdrop.PaletteDue = false;
			_screens.DrawScreen();

			while (!_frame.QuitFlag) {
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
			if (!Es1Shutdown.WriteExitCode(_installRoot, _exitCode)) {
				Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open for writing in {_installRoot} (main.cpp line 0x88 asserts here).");
				return AssertExitStatus;
			}

			// 3. Shell_CloseScreens. 4. ShellMap_ReleaseResources, Shell_FreeData and ShellMouse_RemoveCallbacks, which hold
			// nothing the collector does not free. 5. Shell_ShutdownDevices.
			_screens.CloseScreens();
			_devices.Shutdown();

			// 6. VolRStream_DeleteGroup and Mem_HeapOff, and under -X an INT 3, which this engine does not raise.
			// 7. Shell_ExitCode returned. PLACEHOLDER: what ES.BAT does with 2 (a mission's launch) or 5 (a demo) is not in
			// GO.EXE (startup.md's Open); the host runs nothing after the shell, S2's to settle.
			return _exitCode;
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
}
