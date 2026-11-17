using System.Numerics;
using HercWorks.Disc;
using Herculan.Engine;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Es1.Install;
using ImGuiNET;

namespace Herculan.Es1.Host.Install;

/// <summary>
/// The body of Metaltech: Earthsiege's "install from a disc" window: a disc folder or image, a destination, and the copy
/// itself run off the frame loop with a progress bar (<see cref="Es1Installer"/>). A finished install keeps its source as
/// the disc the movies play from (<see cref="Es1Disc"/>), as the full install's <c>ES.BAT</c> names the CD it came from.
/// Retail's installer is the disc's <c>INSTALL.SCR</c> under <c>COPYFILZ.EXE</c>; nothing here imitates its screens.
/// </summary>
sealed class Es1InstallPanel : IDisposable {
	/// <summary>What one frame of the panel left it at.</summary>
	public enum Outcome {
		Open,

		/// <summary>An install finished and the player chose to use it; <see cref="Installed"/> names it.</summary>
		Installed,
		Dismissed,
	}

	private readonly LocalizationTable _localization;
	private readonly NativePathPicker.FileFilter _imageFilter;
	private string _sourcePath = "";
	private string _destination = "";
	private GameDisc? _source;
	private Es1Installer? _installer;
	private long _bytes;
	private string? _sourceMessage;
	private bool _sourceOk;
	private string? _error;
	private string? _done;

	private Task<string?>? _picking;
	private bool _pickingSource;

	private Task? _installing;
	private CancellationTokenSource? _cancellation;
	private readonly object _progressGate = new();
	private InstallProgress _progress;

	public Es1InstallPanel(LocalizationTable localization) {
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

		ImGui.TextWrapped(_localization.GetStringOrKey("es1_install.message"));
		ImGui.Spacing();

		bool locked = Busy || _picking != null || _done != null;
		ImGui.BeginDisabled(locked);

		ImGui.SeparatorText(_localization.GetStringOrKey("install.source"));
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		string folderLabel = _localization.GetStringOrKey("install.browse_folder");
		string imageLabel = _localization.GetStringOrKey("install.browse_image");
		float buttonsWidth = NativePathPicker.IsAvailable
			? ButtonRow.LabelWidth(folderLabel) + ButtonRow.LabelWidth(imageLabel) + spacing * 2
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
				Pick(source: true, _localization.GetStringOrKey("es1_install.picker_folder_title"), _sourcePath, owner, null);
			}
			ImGui.SameLine();
			if (ImGui.Button(imageLabel)) {
				Pick(source: true, _localization.GetStringOrKey("es1_install.picker_image_title"), _sourcePath, owner, _imageFilter);
			}
		}

		if (_sourceMessage != null) {
			if (_sourceOk) {
				ImGui.TextUnformatted(_sourceMessage);
			} else {
				Error(_sourceMessage);
			}
		}

		ImGui.SeparatorText(_localization.GetStringOrKey("install.destination"));
		string browseLabel = _localization.GetStringOrKey("install_prompt.browse");
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X
			- (NativePathPicker.IsAvailable ? ButtonRow.LabelWidth(browseLabel) + spacing : 0f));
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
		ImGui.TextWrapped(_localization.GetStringOrKey("es1_install.note"));
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
			ImGui.ProgressBar(fraction, new Vector2(-1f, 0f), string.IsNullOrEmpty(progress.File) ? ""
				: string.Format(_localization.GetStringOrKey("install.copying"), progress.File, progress.FileIndex + 1, progress.FileCount));
		} else if (_done != null) {
			ImGui.TextWrapped(_done);
		}

		// The two buttons sit at the foot of the window, sharing its width.
		string goLabel = _localization.GetStringOrKey(_done != null ? "install.use" : "install.install");
		string stopLabel = _localization.GetStringOrKey(Busy ? "general.cancel" : "general.close");
		Span<float> widths = [ButtonRow.LabelWidth(goLabel), ButtonRow.LabelWidth(stopLabel)];
		var row = new ButtonRow(widths);
		float foot = ImGui.GetWindowHeight() - row.Height - ImGui.GetStyle().WindowPadding.Y;
		if (ImGui.GetCursorPosY() < foot) {
			ImGui.SetCursorPosY(foot);
		}

		if (_done != null) {
			if (row.Button(goLabel)) {
				outcome = Outcome.Installed;
			}
		} else {
			ImGui.BeginDisabled(locked || _installer == null);
			if (row.Button(goLabel)) {
				StartInstall();
			}
			ImGui.EndDisabled();
		}

		if (row.Button(stopLabel)) {
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

	// Opens the source field's disc and measures the install, replacing whatever was open.
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

		_installer = Es1Installer.Identify(_source);
		if (_installer == null) {
			_sourceMessage = string.Format(_localization.GetStringOrKey("es1_install.not_es1"), full,
				Es1Installer.ScriptPath, HercWorks.Core.Io.Transform.RetailGameDetector.Es1Marker);
			CloseSource();
			return;
		}

		_bytes = _installer.PlanBytes();
		_sourceOk = true;
		_sourceMessage = string.Format(_localization.GetStringOrKey("es1_install.found"), (_bytes + (1 << 20) - 1) >> 20);
	}

	private void CloseSource() {
		_source?.Dispose();
		_source = null;
		_installer = null;
		_bytes = 0;
		_sourceOk = false;
		_sourceMessage = null;
	}

	private void StartInstall() {
		_error = null;
		string path = _destination.Trim().Trim('"');
		if (_installer is not { } installer || _source is not { } source) {
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
				DiscCopy.DestinationProblem.IsAFile => "install.destination_file",
				DiscCopy.DestinationProblem.NotEmpty => "install.destination_not_empty",
				_ => "install.destination_inside_disc",
			}), destination);
			return;
		}

		if (DiscCopy.FreeSpace(destination) is { } free && free < _bytes) {
			_error = string.Format(_localization.GetStringOrKey("install.no_space"), (_bytes + (1 << 20) - 1) >> 20, free >> 20);
			return;
		}

		var cancellation = new CancellationTokenSource();
		// Only the latest report is kept, for the frame loop to read; a retry starts from none, not the last run's.
		lock (_progressGate) {
			_progress = default;
		}

		var progress = new ActionProgress<InstallProgress>(report => {
			lock (_progressGate) {
				_progress = report;
			}
		});
		_cancellation = cancellation;
		_destination = destination;
		string disc = source.Location;
		_installing = Task.Run(() => {
			installer.Install(destination, progress, cancellation.Token);
			// The disc kept for the movies; a failed write is logged, and the install stands without it.
			if (Es1Disc.Check(disc, out _) == null) {
				Es1Disc.Remember(disc);
			}
		});
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
			_done = string.Format(_localization.GetStringOrKey("es1_install.done"), _destination);
			Console.WriteLine($"Installed Metaltech: Earthsiege into {_destination}.");
		} else if (finished.Exception?.InnerException is OperationCanceledException || finished.IsCanceled) {
			_error = _localization.GetStringOrKey("install.cancelled");
		} else {
			string reason = finished.Exception?.InnerException?.Message ?? "";
			Console.Error.WriteLine($"The install failed: {reason}");
			_error = string.Format(_localization.GetStringOrKey("install.failed"), reason);
		}
	}

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
