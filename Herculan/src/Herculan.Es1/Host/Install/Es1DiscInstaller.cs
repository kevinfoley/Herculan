using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Es1.Install;

namespace Herculan.Es1.Host.Install;

/// <summary>
/// Metaltech: Earthsiege's side of the <see cref="InstallPanel"/>: a disc is told by its <c>INSTALL.SCR</c> and
/// <c>GO.EXE</c> (<see cref="Es1Installer"/>), and there are no choices, only the full install. A finished install keeps
/// its source as the disc the movies play from (<see cref="Es1Disc"/>), as the full install's <c>ES.BAT</c> names the CD
/// it came from. Retail's installer is the disc's <c>INSTALL.SCR</c> under <c>COPYFILZ.EXE</c>.
/// </summary>
sealed class Es1DiscInstaller(LocalizationTable localization) : IDiscInstaller {
	private Es1Installer? _installer;
	private string? _disc;

	public string KeyPrefix => "es1_install";

	public string GameName => "Metaltech: Earthsiege";

	public long Bytes { get; private set; }

	public bool Open(GameDisc disc, string location, out string message) {
		if (Es1Installer.Identify(disc) is not { } installer) {
			message = string.Format(localization.GetStringOrKey("es1_install.not_es1"), location,
				Es1Installer.ScriptPath, HercWorks.Core.Io.Transform.RetailGameDetector.Es1Marker);
			return false;
		}

		_installer = installer;
		_disc = disc.Location;
		Bytes = installer.PlanBytes();
		message = string.Format(localization.GetStringOrKey("es1_install.found"), InstallPanel.Megabytes(Bytes));
		return true;
	}

	public void Close() {
		_installer = null;
		_disc = null;
		Bytes = 0;
	}

	public void DrawChoices() {
	}

	public Action<IProgress<InstallProgress>, CancellationToken> Copy(string destination) {
		var installer = _installer ?? throw new InvalidOperationException("No disc is open.");
		string disc = _disc!;
		return (progress, cancellation) => {
			installer.Install(destination, progress, cancellation);
			// The disc kept for the movies; a failed write is logged, and the install stands without it.
			if (Es1Disc.Check(disc, out _) == null) {
				Es1Disc.Remember(disc);
			}
		};
	}
}
