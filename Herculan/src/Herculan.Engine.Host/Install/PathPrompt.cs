using System.Numerics;
using Herculan.Engine.Host.Localization;
using ImGuiNET;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// The body of a "choose a folder" or "choose a file" prompt: a message, a path field with the platform's own
/// picker beside it (<see cref="NativePathPicker"/>), the reason the last choice was refused, and an accept and
/// a dismiss button at the foot, accept greyed while the refused choice is still in the field. It draws into whatever ImGui window is current, so <see cref="InstallPrompt"/>
/// shows it as a window of its own and <see cref="Settings.SettingsWindow"/> as a modal.
/// </summary>
sealed class PathPrompt {
	/// <summary>What one frame of the prompt left it at.</summary>
	public enum Outcome {
		Open,
		Accepted,
		Dismissed,

		/// <summary>The optional third button, which leaves the path unchosen.</summary>
		Alternative,
	}

	private readonly LocalizationTable _localization;
	private readonly string _keyPrefix;
	private readonly string _dismissKey;
	private readonly Func<string, string?> _refusal;
	private readonly string _messageKey;
	private readonly NativePathPicker.FileFilter? _fileFilter;
	private readonly string? _alternativeKey;
	private string _path;
	private string? _error;
	// The path field as it stood when Accept refused it, while the refusal stands: accept is greyed until the field
	// changes, which takes the refusal down.
	private string? _refusedPath;
	private Task<string?>? _picking;

	/// <param name="keyPrefix">
	/// The prompt's strings: <c>message</c>, <c>path_hint</c>, <c>picker_title</c>, <c>accept</c> and
	/// <c>no_path</c> under it, beside the shared <c>install_prompt.browse</c>, <c>picker_open</c> and
	/// <c>picker_failed</c>.
	/// </param>
	/// <param name="dismissKey">The dismiss button's string.</param>
	/// <param name="refusal">Why a typed or picked path cannot be taken, or null when it can.</param>
	/// <param name="initialPath">What the path field starts with.</param>
	/// <param name="messageKey">The message's string, in place of the prefix's own <c>message</c>.</param>
	/// <param name="fileFilter">The files the picker offers; null picks a folder.</param>
	/// <param name="alternativeKey">The string of a third button at the foot, between the two, or null for none.</param>
	public PathPrompt(LocalizationTable localization, string keyPrefix, string dismissKey,
			Func<string, string?> refusal, string initialPath = "", string? messageKey = null,
			NativePathPicker.FileFilter? fileFilter = null, string? alternativeKey = null) {
		_localization = localization;
		_keyPrefix = keyPrefix;
		_dismissKey = dismissKey;
		_refusal = refusal;
		_path = initialPath;
		_messageKey = messageKey ?? keyPrefix + ".message";
		_fileFilter = fileFilter;
		_alternativeKey = alternativeKey;
	}

	/// <summary>The path accepted, in full; set once <see cref="Draw"/> has returned <see cref="Outcome.Accepted"/>.</summary>
	public string? Chosen { get; private set; }

	/// <summary>
	/// Draws the prompt into the current window, filling it to its foot. <paramref name="owner"/> is the
	/// native window the picker belongs to. <paramref name="messageArguments"/> fill the message's slots.
	/// </summary>
	public Outcome Draw(nint owner, params object[] messageArguments) {
		var outcome = Outcome.Open;
		if (_picking is { IsCompleted: true } picked) {
			_picking = null;
			if (picked.IsCompletedSuccessfully && picked.Result is { } chosen) {
				_path = chosen;
				if (Accept()) {
					outcome = Outcome.Accepted;
				}
			} else if (picked.IsFaulted) {
				Console.Error.WriteLine($"The picker failed: {picked.Exception?.InnerException?.Message}");
				_error = _localization.GetStringOrKey("install_prompt.picker_failed");
			}
		}

		ImGui.TextWrapped(string.Format(_localization.GetStringOrKey(_messageKey), messageArguments));
		ImGui.Spacing();

		bool picking = _picking != null;
		ImGui.BeginDisabled(picking);

		string browseLabel = _localization.GetStringOrKey("install_prompt.browse");
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		bool canBrowse = NativePathPicker.IsAvailable;
		float browseWidth = canBrowse
			? ImGui.CalcTextSize(browseLabel).X + ImGui.GetStyle().FramePadding.X * 2
			: 0f;

		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (canBrowse ? browseWidth + spacing : 0f));
		if (ImGui.InputTextWithHint("##path", _localization.GetStringOrKey(_keyPrefix + ".path_hint"), ref _path, 1024,
				ImGuiInputTextFlags.EnterReturnsTrue) && Accept()) {
			outcome = Outcome.Accepted;
		}

		// A path typed since the refusal is a new choice, which the refusal no longer speaks for.
		if (_refusedPath != null && _path != _refusedPath) {
			_refusedPath = null;
			_error = null;
		}

		if (canBrowse) {
			ImGui.SameLine();
			if (ImGui.Button(browseLabel)) {
				_refusedPath = null;
				_error = null;
				_picking = NativePathPicker.PickAsync(_localization.GetStringOrKey(_keyPrefix + ".picker_title"), _path, owner, _fileFilter);
			}
		}

		if (_error != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
			ImGui.TextWrapped(_error);
			ImGui.PopStyleColor();
		} else if (picking) {
			ImGui.TextDisabled(_localization.GetStringOrKey("install_prompt.picker_open"));
		}

		// The buttons sit at the foot of the window, sharing its width, or under the content when that runs past the foot.
		string acceptLabel = _localization.GetStringOrKey(_keyPrefix + ".accept");
		string? alternativeLabel = _alternativeKey == null ? null : _localization.GetStringOrKey(_alternativeKey);
		string dismissLabel = _localization.GetStringOrKey(_dismissKey);
		Span<float> widths = stackalloc float[alternativeLabel == null ? 2 : 3];
		widths[0] = ButtonRow.LabelWidth(acceptLabel);
		widths[^1] = ButtonRow.LabelWidth(dismissLabel);
		if (alternativeLabel != null) {
			widths[1] = ButtonRow.LabelWidth(alternativeLabel);
		}

		var row = new ButtonRow(widths);
		float foot = ImGui.GetWindowHeight() - row.Height - ImGui.GetStyle().WindowPadding.Y;
		if (ImGui.GetCursorPosY() < foot) {
			ImGui.SetCursorPosY(foot);
		}
		ImGui.BeginDisabled(_refusedPath != null);
		if (row.Button(acceptLabel) && Accept()) {
			outcome = Outcome.Accepted;
		}
		ImGui.EndDisabled();
		if (alternativeLabel != null && row.Button(alternativeLabel)) {
			outcome = Outcome.Alternative;
		}
		if (row.Button(dismissLabel)) {
			outcome = Outcome.Dismissed;
		}

		ImGui.EndDisabled();
		return outcome;
	}

	/// <summary>
	/// Takes the path field's path if <c>refusal</c> has nothing against it; otherwise says why not, and greys the accept
	/// button until the field changes.
	/// </summary>
	internal bool Accept() {
		string path = _path.Trim().Trim('"');
		_error = path.Length == 0 ? _localization.GetStringOrKey(_keyPrefix + ".no_path") : _refusal(path);
		if (_error != null) {
			_refusedPath = _path;
			return false;
		}

		Chosen = Path.GetFullPath(path);
		return true;
	}
}
