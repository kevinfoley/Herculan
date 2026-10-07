using System.Numerics;
using HercWorks.Disc;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Install;
using ImGuiNET;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// The body of the "install from a disc" window: a disc folder or image, the size and language the disc offers,
/// a destination, and the copy itself run off the frame loop with a progress bar (<see cref="RetailInstaller"/>).
/// It draws into whatever ImGui window is current, so <see cref="InstallWindow"/> shows it as a window of its own
/// and <see cref="Settings.SettingsWindow"/> as a floating one. Retail's installer is Sierra's <c>SETUP.EXE</c>;
/// nothing here imitates its screens.
/// </summary>
sealed class InstallPanel : IDisposable {
	/// <summary>What one frame of the panel left it at.</summary>
	public enum Outcome {
		Open,

		/// <summary>An install finished and the player chose to use it; <see cref="Installed"/> names it.</summary>
		Installed,
		Dismissed,
	}

	private static readonly RetailInstaller.Size[] Sizes = [RetailInstaller.Size.Minimum, RetailInstaller.Size.Medium, RetailInstaller.Size.Maximum];

	private readonly LocalizationTable _localization;
	private readonly NativePathPicker.FileFilter _imageFilter;
	private string _sourcePath = "";
	private string _destination = "";
	private GameDisc? _source;
	private RetailInstaller? _installer;
	private string? _sourceMessage;
	private bool _sourceOk;
	private RetailInstaller.Size _size = RetailInstaller.Size.Maximum;
	private RetailInstaller.Language _language = RetailInstaller.Language.English;
	private long[]? _sizeBytes;
	private string? _error;
	private string? _done;

	private Task<string?>? _picking;
	private bool _pickingSource;

	private Task? _installing;
	private CancellationTokenSource? _cancellation;
	private readonly object _progressGate = new();
	private InstallProgress _progress;

	public InstallPanel(LocalizationTable localization) {
		_localization = localization;
		_imageFilter = new NativePathPicker.FileFilter(_localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"]);
	}

	/// <summary>The install made, in full; set once <see cref="Draw"/> has returned <see cref="Outcome.Installed"/>.</summary>
	public string? Installed { get; private set; }

	/// <summary>Whether a copy is running, which closing the panel would leave unfinished.</summary>
	public bool Busy => _installing is { IsCompleted: false };

	/// <summary>Draws the panel into the current window. <paramref name="owner"/> is the native window the pickers belong to.</summary>
	public Outcome Draw(nint owner) {
		var outcome = Outcome.Open;
		TakePick();
		TakeInstall();

		ImGui.TextWrapped(_localization.GetStringOrKey("install.message"));
		ImGui.Spacing();

		bool locked = Busy || _picking != null || _done != null;
		ImGui.BeginDisabled(locked);

		ImGui.SeparatorText(_localization.GetStringOrKey("install.source"));
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		string folderLabel = _localization.GetStringOrKey("install.browse_folder");
		string imageLabel = _localization.GetStringOrKey("install.browse_image");
		float buttonsWidth = NativePathPicker.IsAvailable
			? ButtonWidth(folderLabel) + ButtonWidth(imageLabel) + spacing * 2
			: 0f;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - buttonsWidth);
		if (ImGui.InputTextWithHint("##source", _localization.GetStringOrKey("install.source_hint"), ref _sourcePath, 1024,
				ImGuiInputTextFlags.EnterReturnsTrue)) {
			OpenSource();
		}
		if (ImGui.IsItemDeactivatedAfterEdit()) {
			OpenSource();
		}

		if (NativePathPicker.IsAvailable) {
			ImGui.SameLine();
			if (ImGui.Button(folderLabel)) {
				Pick(source: true, _localization.GetStringOrKey("install.picker_folder_title"), _sourcePath, owner, null);
			}
			ImGui.SameLine();
			if (ImGui.Button(imageLabel)) {
				Pick(source: true, _localization.GetStringOrKey("install.picker_image_title"), _sourcePath, owner, _imageFilter);
			}
		}

		if (_sourceMessage != null) {
			if (_sourceOk) {
				ImGui.TextUnformatted(_sourceMessage);
			} else {
				Error(_sourceMessage);
			}
		}

		ImGui.BeginDisabled(_installer == null);
		ImGui.SeparatorText(_localization.GetStringOrKey("install.size"));
		foreach (var size in Sizes) {
			long megabytes = _sizeBytes is { } bytes ? (bytes[(int)size] + (1 << 20) - 1) >> 20 : 0;
			if (ImGui.RadioButton(string.Format(_localization.GetStringOrKey(SizeKey(size)), megabytes), _size == size)) {
				_size = size;
			}
		}
		ImGui.TextDisabled(_localization.GetStringOrKey("install.size_note"));

		ImGui.SeparatorText(_localization.GetStringOrKey("install.language"));
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.BeginCombo("##language", _localization.GetStringOrKey(LanguageKey(_language)))) {
			foreach (var language in RetailInstaller.Languages) {
				if (ImGui.Selectable(_localization.GetStringOrKey(LanguageKey(language)), language == _language) && language != _language) {
					_language = language;
					MeasureSizes();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();

		ImGui.SeparatorText(_localization.GetStringOrKey("install.destination"));
		string browseLabel = _localization.GetStringOrKey("install_prompt.browse");
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X
			- (NativePathPicker.IsAvailable ? ButtonWidth(browseLabel) + spacing : 0f));
		ImGui.InputTextWithHint("##destination", _localization.GetStringOrKey("install.destination_hint"), ref _destination, 1024);
		if (NativePathPicker.IsAvailable) {
			ImGui.SameLine();
			if (ImGui.Button(browseLabel + "##destination_browse")) {
				Pick(source: false, _localization.GetStringOrKey("install.picker_destination_title"), _destination, owner, null);
			}
		}

		ImGui.EndDisabled();

		ImGui.Spacing();
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.4f, 1f));
		ImGui.TextWrapped(_localization.GetStringOrKey("install.retail_note"));
		ImGui.PopStyleColor();

		if (_error != null) {
			Error(_error);
		}

		if (Busy) {
			InstallProgress progress;
			lock (_progressGate) {
				progress = _progress;
			}

			float fraction = progress.BytesTotal > 0 ? (float)progress.BytesDone / progress.BytesTotal : 0f;
			ImGui.ProgressBar(fraction, new Vector2(-1f, 0f), progress.File.Length == 0 ? ""
				: string.Format(_localization.GetStringOrKey("install.copying"), progress.File, progress.FileIndex + 1, progress.FileCount));
		} else if (_done != null) {
			ImGui.TextWrapped(_done);
		}

		// The two buttons sit at the foot of the window, half its width each.
		var buttonSize = new Vector2((ImGui.GetContentRegionAvail().X - spacing) * 0.5f, 0f);
		float foot = ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y;
		if (ImGui.GetCursorPosY() < foot) {
			ImGui.SetCursorPosY(foot);
		}

		if (_done != null) {
			if (ImGui.Button(_localization.GetStringOrKey("install.use"), buttonSize)) {
				outcome = Outcome.Installed;
			}
		} else {
			ImGui.BeginDisabled(locked || _installer == null);
			if (ImGui.Button(_localization.GetStringOrKey("install.install"), buttonSize)) {
				StartInstall();
			}
			ImGui.EndDisabled();
		}

		ImGui.SameLine();
		if (ImGui.Button(_localization.GetStringOrKey(Busy ? "general.cancel" : "general.close"), buttonSize)) {
			if (Busy) {
				_cancellation?.Cancel();
			} else {
				outcome = Outcome.Dismissed;
			}
		}

		return outcome;
	}

	private void Pick(bool source, string title, string initial, nint owner, NativePathPicker.FileFilter? filter) {
		_error = null;
		_pickingSource = source;
		_picking = NativePathPicker.PickAsync(title, initial, owner, filter);
	}

	private void TakePick() {
		if (_picking is not { IsCompleted: true } picked) {
			return;
		}

		_picking = null;
		if (picked.IsCompletedSuccessfully && picked.Result is { } chosen) {
			if (_pickingSource) {
				_sourcePath = chosen;
				OpenSource();
			} else {
				_destination = chosen;
			}
		} else if (picked.IsFaulted) {
			Console.Error.WriteLine($"The picker failed: {picked.Exception?.InnerException?.Message}");
			_error = _localization.GetStringOrKey("install_prompt.picker_failed");
		}
	}

	// Opens the source field's disc and works out what it offers, replacing whatever was open.
	private void OpenSource() {
		CloseSource();
		string path = _sourcePath.Trim().Trim('"');
		if (path.Length == 0) {
			return;
		}

		string full;
		try {
			full = Path.GetFullPath(path);
		} catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
			_sourceMessage = string.Format(_localization.GetStringOrKey("install.source_missing"), path);
			return;
		}

		try {
			_source = File.Exists(full) ? GameDisc.OpenImage(full)
				: Directory.Exists(full) ? GameDisc.OpenFolder(full)
				: null;
		} catch (Exception ex) when (ex is DiscFormatException or IOException or UnauthorizedAccessException) {
			_sourceMessage = string.Format(_localization.GetStringOrKey("install.source_unreadable"), full, ex.Message);
			return;
		}

		if (_source == null) {
			_sourceMessage = string.Format(_localization.GetStringOrKey("install.source_missing"), full);
			return;
		}

		_installer = RetailInstaller.Identify(_source, out var problem, out string? version);
		if (_installer == null) {
			_sourceMessage = problem == RetailInstaller.Problem.NoScript
				? string.Format(_localization.GetStringOrKey("install.no_script"), full, RetailInstaller.ScriptFileName)
				: string.Format(_localization.GetStringOrKey("install.unknown_version"), full, version ?? "?");
			CloseSource();
			return;
		}

		if (_installer.CheckSource() is { } discProblem) {
			_sourceMessage = discProblem switch {
				GameInstall.DiscDirectoryProblem.Whitespace => string.Format(_localization.GetStringOrKey("disc_prompt.whitespace"), full, HercWorks.Core.Data.File.Cfg.Drive.FileName),
				GameInstall.DiscDirectoryProblem.NotLatin1 => string.Format(_localization.GetStringOrKey("disc_prompt.not_latin1"), full, HercWorks.Core.Data.File.Cfg.Drive.FileName),
				_ => string.Format(_localization.GetStringOrKey("install.source_missing"), full),
			};
			CloseSource();
			return;
		}

		_sourceOk = true;
		_sourceMessage = string.Format(_localization.GetStringOrKey("install.found"), _installer.BuildName);
		MeasureSizes();
	}

	private void MeasureSizes() {
		_sizeBytes = _installer is { } installer
			? Sizes.Select(size => installer.PlanBytes(installer.Plan(size, _language))).ToArray()
			: null;
	}

	private void CloseSource() {
		_source?.Dispose();
		_source = null;
		_installer = null;
		_sizeBytes = null;
		_sourceOk = false;
		_sourceMessage = null;
	}

	private void StartInstall() {
		_error = null;
		string path = _destination.Trim().Trim('"');
		if (_installer is not { } installer) {
			return;
		}

		if (path.Length == 0) {
			_error = _localization.GetStringOrKey("install.no_destination");
			return;
		}

		string destination;
		try {
			destination = Path.GetFullPath(path);
		} catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
			_error = string.Format(_localization.GetStringOrKey("install.destination_file"), path);
			return;
		}

		if (installer.CheckDestination(destination) is { } problem) {
			_error = string.Format(_localization.GetStringOrKey(problem switch {
				RetailInstaller.DestinationProblem.IsAFile => "install.destination_file",
				RetailInstaller.DestinationProblem.NotEmpty => "install.destination_not_empty",
				_ => "install.destination_inside_disc",
			}), destination);
			return;
		}

		long needed = _sizeBytes?[(int)_size] ?? 0;
		if (RetailInstaller.FreeSpace(destination) is { } free && free < needed) {
			_error = string.Format(_localization.GetStringOrKey("install.no_space"), (needed + (1 << 20) - 1) >> 20, free >> 20);
			return;
		}

		var size = _size;
		var language = _language;
		var cancellation = new CancellationTokenSource();
		// Only the latest report is kept, for the frame loop to read.
		var progress = new ActionProgress<InstallProgress>(report => {
			lock (_progressGate) {
				_progress = report;
			}
		});
		_cancellation = cancellation;
		_destination = destination;
		_installing = Task.Run(() => installer.Install(destination, size, language, progress, cancellation.Token));
	}

	private void TakeInstall() {
		if (_installing is not { IsCompleted: true } finished) {
			return;
		}

		_installing = null;
		_cancellation?.Dispose();
		_cancellation = null;
		if (finished.IsCompletedSuccessfully) {
			Installed = _destination;
			_done = string.Format(_localization.GetStringOrKey("install.done"), _destination);
			Console.WriteLine($"Installed Earthsiege 2 into {_destination}.");
		} else if (finished.Exception?.InnerException is OperationCanceledException || finished.IsCanceled) {
			_error = _localization.GetStringOrKey("install.cancelled");
		} else {
			string reason = finished.Exception?.InnerException?.Message ?? "";
			Console.Error.WriteLine($"The install failed: {reason}");
			_error = string.Format(_localization.GetStringOrKey("install.failed"), reason);
		}
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

	private static float ButtonWidth(string label) => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;

	private static void Error(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
		ImGui.TextWrapped(text);
		ImGui.PopStyleColor();
	}

	/// <summary>Cancels a running copy and waits for it to clean up, then closes the source.</summary>
	public void Dispose() {
		if (_installing is { } running) {
			_cancellation?.Cancel();
			try {
				running.Wait();
			} catch (AggregateException) {
			}
		}

		_cancellation?.Dispose();
		CloseSource();
	}
}
