using Herculan.Engine.Platform;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> whether Metaltech: Earthsiege runs full screen, a setting ES1, a DOS program, does not have.
/// Each turn, the shell's, a movie's and a mission's, starts as the last [Alt+Enter] (<see cref="Es1FullScreenKey"/>) left
/// the window, on this launch or an earlier one, so a window that Earthsiege 2 left full screen is taken to ES1's own
/// setting. A <c>--screenshot</c> turn stays as the window is.
/// </summary>
public static class Es1FullScreen {
	private static bool? _setting;

	/// <summary>
	/// Where the setting is kept: <c>es1-full-screen.txt</c> in <see cref="UserConfig.Folder"/>. Its presence means full
	/// screen; it holds nothing. Absent, as on a first start, ES1 runs in a window.
	/// </summary>
	public static string SettingFile => UserConfig.PathOf("es1-full-screen.txt");

	/// <summary>
	/// Whether ES1 runs full screen, read from <see cref="SettingFile"/> once a run. A failed write is logged and leaves the
	/// setting as it was.
	/// </summary>
	public static bool Setting {
		get => _setting ??= File.Exists(SettingFile);
		private set {
			if (value == Setting) {
				return;
			}

			if (value ? UserConfig.Write(SettingFile, "") : DeleteSettingFile()) {
				_setting = value;
			}
		}
	}

	/// <summary>
	/// A turn's start in <paramref name="window"/>: the window taken to <see cref="Setting"/>, the pointer
	/// <paramref name="pointer"/> answers confined and centred as <see cref="EngineWindow.StartFullScreen"/> does, and the
	/// turn's [Alt+Enter], which keeps what it leaves.
	/// </summary>
	public static Es1FullScreenKey Start(EngineWindow window, Func<IMouse?> pointer) {
		window.StartFullScreen(Setting, pointer());
		return Toggle(() => window.ToggleFullScreen(pointer()), () => window.FullScreen);
	}

	/// <summary><see cref="Start(EngineWindow, Func{IMouse?})"/> for the shell's window, once it is attached.</summary>
	/// <param name="keepWindow">Leaves the window as it is, as a <c>--screenshot</c> turn does.</param>
	public static Es1FullScreenKey Start(FrontEndWindow window, bool keepWindow = false) {
		if (!keepWindow) {
			window.StartFullScreen(Setting);
		}

		return Toggle(window.ToggleFullScreen, () => window.FullScreen);
	}

	private static Es1FullScreenKey Toggle(Action toggle, Func<bool> isFullScreen) => new(() => {
		toggle();
		Setting = isFullScreen();
	});

	private static bool DeleteSettingFile() {
		try {
			File.Delete(SettingFile);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not delete {SettingFile}: {ex.Message}");
			return false;
		}
	}
}
