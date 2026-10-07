using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Host;

/// <summary>
/// What outlives one turn of the shell or one mission: the install, which the Settings menu can change for
/// the next turn, the install's disc, the player's interface strings and the font every ImGui window is drawn in.
/// </summary>
sealed class HostSession(string installRoot, LocalizationTable localization, string imguiFontPath) : IDisposable {
	private string _installRoot = installRoot;
	private GameDisc? _disc;
	private bool _discOpen;

	/// <summary>The install the next shell turn and mission read; <see cref="Settings.SettingsWindow"/> changes it.</summary>
	public string InstallRoot {
		get => _installRoot;
		set {
			_installRoot = value;
			ReopenDisc();
		}
	}

	/// <summary>
	/// The install's disc (<see cref="GameInstall.OpenDisc"/>), opened on first use and held until the install or
	/// its <c>drive.cfg</c> changes; null when it names none that can be opened. An image's audio layout is
	/// started in the background as it opens (<see cref="ImageMusicSource.BeginLayout"/>), so the first
	/// mission's music does not wait for it.
	/// </summary>
	public GameDisc? Disc {
		get {
			if (!_discOpen) {
				_discOpen = true;
				_disc?.Dispose();
				_disc = GameInstall.OpenDisc(_installRoot);
				if (_disc?.Image is { } image) {
					ImageMusicSource.BeginLayout(image);
				}
			}

			return _disc;
		}
	}

	/// <summary>
	/// Whether an install <see cref="Settings.SettingsWindow"/> switches to is remembered for the next launch
	/// (<see cref="GameInstall.Remember"/>); <c>--ask-install</c> turns it off.
	/// </summary>
	public bool RememberInstall { get; init; } = true;

	/// <summary>Whether every window's [PrtScn] captures are also saved (<see cref="PrintScreenFiles"/>); <c>--save-prtscn</c>.</summary>
	public bool SavePrintScreens { get; init; }

	public LocalizationTable Localization { get; } = localization;

	public string ImGuiFontPath { get; } = imguiFontPath;

	/// <summary>
	/// Where the last shell or mission window was when it closed, so the next one opens at the same size and place,
	/// maximized if it was; null until one has closed.
	/// </summary>
	public WindowPlacement? WindowPlacement { get; set; }

	/// <summary>
	/// Makes the next use of <see cref="Disc"/> close the disc and open whatever <c>drive.cfg</c> now names. The
	/// disc stays open until then, so a shell turn still drawing after the change keeps reading it.
	/// </summary>
	public void ReopenDisc() => _discOpen = false;

	public void Dispose() {
		_disc?.Dispose();
		_disc = null;
		_discOpen = false;
	}
}
