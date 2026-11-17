using System.Numerics;
using HercWorks.Disc;
using Herculan.Engine.Content;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Install;

/// <summary>
/// One game's side of <see cref="InstallPanel"/>: whether a disc is that game's, the choices it offers about the copy,
/// and the copy itself.
/// </summary>
public interface IDiscInstaller {
	/// <summary>
	/// The prefix of the panel's strings that differ by game: <c>.message</c>, <c>.picker_folder_title</c>,
	/// <c>.picker_image_title</c>, <c>.note</c> and <c>.done</c>.
	/// </summary>
	string KeyPrefix { get; }

	/// <summary>The game's name, in the log.</summary>
	string GameName { get; }

	/// <summary>
	/// Takes <paramref name="disc"/>, whose full path is <paramref name="location"/>, and returns true when it is the
	/// game's; otherwise holds nothing of it and returns false. <paramref name="message"/> is what was found, or why the
	/// disc is refused.
	/// </summary>
	bool Open(GameDisc disc, string location, out string message);

	/// <summary>Lets go of the disc <see cref="Open"/> took.</summary>
	void Close();

	/// <summary>The bytes the copy takes with the choices made now; 0 with no disc taken.</summary>
	long Bytes { get; }

	/// <summary>Draws the choices the game offers between the source and the destination, greyed with no disc taken.</summary>
	void DrawChoices();

	/// <summary>The copy into <paramref name="destination"/> with the choices made now, to run off the frame loop.</summary>
	Action<IProgress<InstallProgress>, CancellationToken> Copy(string destination);
}

/// <summary>
/// The body of a game's "install from a disc" window: a disc folder or image, the choices the game offers
/// (<see cref="IDiscInstaller.DrawChoices"/>), a destination, and the copy itself run off the frame loop with a progress
/// bar. It draws into whatever ImGui window is current, so a game can show it as a window of its own or a floating one.
/// Each game's retail installer has screens of its own; nothing here imitates them.
/// </summary>
public sealed class InstallPanel : IDisposable {
	/// <summary>What one frame of the panel left it at.</summary>
	public enum Outcome {
		Open,

		/// <summary>An install finished and the player chose to use it; <see cref="Installed"/> names it.</summary>
		Installed,
		Dismissed,
	}

	private readonly LocalizationTable _localization;
	private readonly IDiscInstaller _installer;
	private readonly NativePathPicker.FileFilter _imageFilter;
	private string _sourcePath = "";
	private string _destination = "";
	private GameDisc? _source;
	private bool _opened;
	private string? _sourceMessage;
	private string? _error;
	private string? _done;

	private Task<string?>? _picking;
	private bool _pickingSource;

	private Task? _installing;
	private CancellationTokenSource? _cancellation;
	private readonly object _progressGate = new();
	private InstallProgress _progress;

	public InstallPanel(LocalizationTable localization, IDiscInstaller installer) {
		_localization = localization;
		_installer = installer;
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

		string prefix = _installer.KeyPrefix;
		ImGui.TextWrapped(_localization.GetStringOrKey(prefix + ".message"));
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
				Pick(source: true, _localization.GetStringOrKey(prefix + ".picker_folder_title"), _sourcePath, owner, null);
			}
			ImGui.SameLine();
			if (ImGui.Button(imageLabel)) {
				Pick(source: true, _localization.GetStringOrKey(prefix + ".picker_image_title"), _sourcePath, owner, _imageFilter);
			}
		}

		if (_sourceMessage != null) {
			if (_opened) {
				ImGui.TextUnformatted(_sourceMessage);
			} else {
				Error(_sourceMessage);
			}
		}

		_installer.DrawChoices();

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
		ImGui.TextWrapped(_localization.GetStringOrKey(prefix + ".note"));
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
			// File is null until the copy's first report, which comes only after the plan is checked and a chunk read.
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
			ImGui.BeginDisabled(locked || !_opened);
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

	/// <summary><paramref name="bytes"/> in whole megabytes, rounded up, as the panel's strings show sizes.</summary>
	public static long Megabytes(long bytes) => (bytes + (1 << 20) - 1) >> 20;

	private static void Error(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
		ImGui.TextWrapped(text);
		ImGui.PopStyleColor();
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

	// Opens the source field's disc and hands it to the game, replacing whatever was open.
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

		_opened = _installer.Open(_source, full, out string message);
		if (!_opened) {
			CloseSource();
		}

		_sourceMessage = message;
	}

	private void CloseSource() {
		if (_opened) {
			_installer.Close();
		}

		_source?.Dispose();
		_source = null;
		_opened = false;
		_sourceMessage = null;
	}

	private void StartInstall() {
		_error = null;
		string path = _destination.Trim().Trim('"');
		if (!_opened || _source is not { } source) {
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

		if (DiscCopy.CheckDestination(source, destination) is { } problem) {
			_error = string.Format(_localization.GetStringOrKey(problem switch {
				DiscCopy.DestinationProblem.IsAFile => "install.destination_file",
				DiscCopy.DestinationProblem.NotEmpty => "install.destination_not_empty",
				_ => "install.destination_inside_disc",
			}), destination);
			return;
		}

		long needed = _installer.Bytes;
		if (DiscCopy.FreeSpace(destination) is { } free && free < needed) {
			_error = string.Format(_localization.GetStringOrKey("install.no_space"), Megabytes(needed), free >> 20);
			return;
		}

		var copy = _installer.Copy(destination);
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
		_installing = Task.Run(() => copy(progress, cancellation.Token));
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
			_done = string.Format(_localization.GetStringOrKey(_installer.KeyPrefix + ".done"), _destination);
			Console.WriteLine($"Installed {_installer.GameName} into {_destination}.");
		} else if (finished.Exception?.InnerException is OperationCanceledException || finished.IsCanceled) {
			_error = _localization.GetStringOrKey("install.cancelled");
		} else {
			string reason = finished.Exception?.InnerException?.Message ?? "";
			Console.Error.WriteLine($"The install failed: {reason}");
			_error = string.Format(_localization.GetStringOrKey("install.failed"), reason);
		}
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
