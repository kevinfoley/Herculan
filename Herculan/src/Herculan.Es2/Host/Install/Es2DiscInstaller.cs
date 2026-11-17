using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Es2.Install;
using ImGuiNET;

namespace Herculan.Es2.Host.Install;

/// <summary>
/// Earthsiege 2's side of the <see cref="InstallPanel"/>: a disc is told by its <c>SIERRA.INF</c>
/// (<see cref="RetailInstaller"/>), and the choices are the install size, whether to copy what retail always reads from
/// the disc too (on by default), and the language. Retail's installer is Sierra's <c>SETUP.EXE</c>.
/// </summary>
sealed class Es2DiscInstaller(LocalizationTable localization) : IDiscInstaller {
	private static readonly RetailInstaller.Size[] Sizes = [RetailInstaller.Size.Minimum, RetailInstaller.Size.Medium, RetailInstaller.Size.Maximum];

	private RetailInstaller? _installer;
	private RetailInstaller.Size _size = RetailInstaller.Size.Maximum;
	private bool _discFiles = true;
	private RetailInstaller.Language _language = RetailInstaller.Language.English;
	private long[]? _sizeBytes;

	public string KeyPrefix => "install";

	public string GameName => "Earthsiege 2";

	public long Bytes => _sizeBytes?[(int)_size] ?? 0;

	public bool Open(GameDisc disc, string location, out string message) {
		var installer = RetailInstaller.Identify(disc, out var problem, out string? version);
		if (installer == null) {
			message = problem == RetailInstaller.Problem.NoScript
				? string.Format(localization.GetStringOrKey("install.no_script"), location, RetailInstaller.ScriptFileName)
				: string.Format(localization.GetStringOrKey("install.unknown_version"), location, version ?? "?");
			return false;
		}

		if (installer.CheckSource() is { } discProblem) {
			message = discProblem switch {
				GameInstall.DiscDirectoryProblem.Whitespace => string.Format(localization.GetStringOrKey("disc_prompt.whitespace"), location, HercWorks.Core.Data.File.Cfg.Drive.FileName),
				GameInstall.DiscDirectoryProblem.NotLatin1 => string.Format(localization.GetStringOrKey("disc_prompt.not_latin1"), location, HercWorks.Core.Data.File.Cfg.Drive.FileName),
				_ => string.Format(localization.GetStringOrKey("install.source_missing"), location),
			};
			return false;
		}

		_installer = installer;
		MeasureSizes();
		message = string.Format(localization.GetStringOrKey("install.found"), installer.BuildName);
		return true;
	}

	public void Close() {
		_installer = null;
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
		ImGui.TextDisabled(localization.GetStringOrKey("install.size_note"));
		ImGui.PopTextWrapPos();
		if (ImGui.Checkbox(localization.GetStringOrKey("install.disc_files"), ref _discFiles)) {
			MeasureSizes();
		}

		ImGui.SeparatorText(localization.GetStringOrKey("install.language"));
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.BeginCombo("##language", localization.GetStringOrKey(LanguageKey(_language)))) {
			foreach (var language in RetailInstaller.Languages) {
				if (ImGui.Selectable(localization.GetStringOrKey(LanguageKey(language)), language == _language) && language != _language) {
					_language = language;
					MeasureSizes();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();
	}

	public Action<IProgress<InstallProgress>, CancellationToken> Copy(string destination) {
		var installer = _installer ?? throw new InvalidOperationException("No disc is open.");
		var size = _size;
		var language = _language;
		bool discFiles = _discFiles;
		return (progress, cancellation) => installer.Install(destination, size, language, discFiles, progress, cancellation);
	}

	private void MeasureSizes() {
		_sizeBytes = _installer is { } installer
			? Sizes.Select(size => installer.PlanBytes(installer.Plan(size, _language, _discFiles))).ToArray()
			: null;
	}

	private static string SizeKey(RetailInstaller.Size size) => size switch {
		RetailInstaller.Size.Minimum => "install.size_minimum",
		RetailInstaller.Size.Medium => "install.size_medium",
		_ => "install.size_maximum",
	};

	/// <summary>The localization key of <paramref name="language"/>'s name, which the Settings menu shows too.</summary>
	internal static string LanguageKey(RetailInstaller.Language language) => language switch {
		RetailInstaller.Language.French => "install.language_french",
		RetailInstaller.Language.German => "install.language_german",
		_ => "install.language_english",
	};
}
