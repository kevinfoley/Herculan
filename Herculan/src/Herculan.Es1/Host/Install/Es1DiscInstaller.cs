using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Es1.Install;
using ImGuiNET;

namespace Herculan.Es1.Host.Install;

/// <summary>
/// Metaltech: Earthsiege's side of the <see cref="InstallPanel"/>: a disc is told by its <c>INSTALL.SCR</c> and
/// <c>GO.EXE</c> (<see cref="Es1Installer"/>), and the one choice is the size, the full install first chosen. A finished
/// install keeps its source as the disc the movies play from (<see cref="Es1Disc"/>), and a minimum or medium one its
/// programs and archives, as <c>ES.BAT</c> names the CD it came from. Retail's installer is the disc's
/// <c>INSTALL.SCR</c> under <c>COPYFILZ.EXE</c>.
/// </summary>
sealed class Es1DiscInstaller(LocalizationTable localization) : IDiscInstaller {
	private static readonly Es1Installer.Size[] Sizes = [Es1Installer.Size.Minimum, Es1Installer.Size.Medium, Es1Installer.Size.Full];

	private Es1Installer? _installer;
	private string? _disc;
	private Es1Installer.Size _size = Es1Installer.Size.Full;
	private long[]? _sizeBytes;

	public string KeyPrefix => "es1_install";

	public string GameName => "Metaltech: Earthsiege";

	public long Bytes => _sizeBytes?[(int)_size] ?? 0;

	public bool Open(GameDisc disc, string location, out string message) {
		if (Es1Installer.Identify(disc) is not { } installer) {
			message = string.Format(localization.GetStringOrKey("es1_install.not_es1"), location,
				Es1Installer.ScriptPath, HercWorks.Core.Io.Transform.RetailGameDetector.Es1Marker);
			return false;
		}

		_installer = installer;
		_disc = disc.Location;
		_sizeBytes = Sizes.Select(installer.PlanBytes).ToArray();
		message = localization.GetStringOrKey("es1_install.found");
		return true;
	}

	public void Close() {
		_installer = null;
		_disc = null;
		_sizeBytes = null;
	}

	public void DrawChoices() {
		ImGui.BeginDisabled(_installer == null);
		ImGui.SeparatorText(localization.GetStringOrKey("install.size"));
		foreach (var size in Sizes) {
			long megabytes = _sizeBytes is { } bytes ? InstallPanel.Megabytes(bytes[(int)size]) : 0;
			if (ImGui.RadioButton(string.Format(localization.GetStringOrKey(SizeKey(size)), megabytes), _size == size)) {
				_size = size;
			}
		}

		ImGui.PushTextWrapPos(0f);
		ImGui.TextDisabled(localization.GetStringOrKey("es1_install.size_note"));
		ImGui.PopTextWrapPos();
		ImGui.EndDisabled();
	}

	public Action<IProgress<InstallProgress>, CancellationToken> Copy(string destination) {
		var installer = _installer ?? throw new InvalidOperationException("No disc is open.");
		string disc = _disc!;
		var size = _size;
		return (progress, cancellation) => {
			installer.Install(destination, size, progress, cancellation);
			// The disc kept for the movies, and for a smaller size the programs and archives left on it; a failed write is
			// logged, and the install stands without it, asking for the disc when it next starts.
			if (Es1Disc.Check(disc, out _) == null) {
				Es1Disc.Remember(disc);
			}
		};
	}

	private static string SizeKey(Es1Installer.Size size) => size switch {
		Es1Installer.Size.Minimum => "install.size_minimum",
		Es1Installer.Size.Medium => "install.size_medium",
		_ => "es1_install.size_full",
	};
}
