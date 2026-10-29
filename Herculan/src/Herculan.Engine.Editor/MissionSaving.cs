using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// File &gt; Save, Save As, Export script.dat and Export Mission Bundle, and the prompt that asks to save unsaved changes
/// before the mission is left or played, each an ImGui modal. A Save writes the open mission's <c>.MSN</c>, text and about
/// file as loose files in the install (<see cref="SavedMissions"/>) under its own name, which the game then loads in place
/// of an archive's copy; a Save As
/// writes them under a name typed in, which the mission then has. Either runs the mission check over the <c>.MSN</c> first
/// (<see cref="MissionLint"/>) and, when it finds anything, lists it as the Problems list words it (<see cref="ProblemText"/>)
/// and asks before writing; and asks before a save that would first
/// put a loose file over an archive mission or one HERCULAN comes with (<see cref="CustomMissions.FindIncluded"/>), or
/// replace a loose file under another mission's name. Whether the mission
/// has changed since it was last saved or opened is its undo history's (<see cref="EditHistory.Dirty"/>).
///
/// <para>Export Mission Bundle shows the about file's fields for a last look, the date filled in with today's when it has
/// none, and writes <c>&lt;name&gt;.hcm</c> (<see cref="MissionBundle"/>) into a folder, saving the mission first when it
/// has unsaved changes, a change made in those fields included, so that the bundle holds what the install holds. The bundle
/// takes the mission's text in the shell's language alone, the one text the editor edits.</para>
///
/// <para>The folder is typed in or chosen with the system's folder picker (<see cref="NativePathPicker"/>). It starts as the
/// install's mission folder (<see cref="CustomMissions.Folder"/>), whose bundles the game's Custom Missions window lists,
/// and the export makes that one folder if the install has none yet; any other has to exist.</para>
///
/// <para>Every choice is made inside the ImGui frame, and what follows one only writes files or calls back: a
/// continuation that ends the editor sets the host's flag, which closes the window once the frame is rendered.</para>
/// </summary>
internal sealed class MissionSaving {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 460f;

	// How many of the mission check's findings the prompt lists before saying how many more there are.
	private const int ListedFindings = 6;

	private enum Prompt {
		None,
		Unsaved,
		SaveAsName,
		Findings,
		Override,
		Replace,
		ExportFolder,
		BundleFolder,
		Message,
	}

	// One save under way, through the prompts it may need.
	private sealed class SaveRequest(string name, bool saveAs, Action? then) {
		public string Name { get; } = name;
		public bool SaveAs { get; } = saveAs;
		public Action? Then { get; } = then;
		public bool Checked { get; set; }
		public bool NameConfirmed { get; set; }
	}

	private readonly string _installRoot;
	private readonly Func<GameContent> _shellContent;
	private readonly string _handoffDirectory;
	private EditorMission? _mission;

	private Prompt _prompt;
	private bool _raised;
	private SaveRequest? _request;
	private Action? _leaving;
	private string _leavingWhat = "";
	private bool _leavingDiscards;
	private IReadOnlyList<string> _findings = [];
	private string _message = "";
	private string _typedName = "";
	private string _typedFolder = "";
	private string? _typedProblem;

	// Export Mission Bundle's about file as its fields show it, the folder typed, the bundle file that folder already
	// holds, looked up as the folder changes, and the folder picker while it is open.
	private readonly MissionAboutFields _bundleFields = new();
	private MissionAbout _bundleAbout = MissionAbout.Blank;
	private string _bundleFolder;
	private (string Folder, string? Existing) _bundleTarget = ("", null);
	private Task<string?>? _bundlePicking;

	/// <param name="installRoot">The install the missions are saved into.</param>
	/// <param name="shellContent">VSHELL's mount of the install, whose language picks the text file's extension.</param>
	/// <param name="handoffDirectory">The scratch folder the open writes the handoff into, which an export copies from.</param>
	public MissionSaving(string installRoot, Func<GameContent> shellContent, string handoffDirectory) {
		_installRoot = installRoot;
		_shellContent = shellContent;
		_handoffDirectory = handoffDirectory;
		_bundleFolder = CustomMissions.Folder(installRoot);
	}

	/// <summary>Raised after a Save or a Save As has written the mission.</summary>
	public event Action? Saved;

	/// <summary>The mission saved, or null with none open. Opening one takes it as saved.</summary>
	public EditorMission? Mission {
		get => _mission;
		set {
			_mission = value;
			_mission?.Mover.History.MarkSaved();
		}
	}

	/// <summary>Takes <paramref name="mission"/>, a new mission no file holds yet, as the one saved, and as unsaved until it is.</summary>
	public void OpenNew(EditorMission mission) {
		Mission = mission;
		mission.Mover.History.MarkNeverSaved();
	}

	/// <summary>Whether a prompt is up, which takes the keyboard.</summary>
	public bool IsOpen => _prompt != Prompt.None;

	/// <summary>Whether the open mission has changed since it was last saved or opened.</summary>
	public bool Dirty => _mission?.Mover.History.Dirty == true;

	/// <summary>Why Save, Save As and Export are unavailable, or null when they are.</summary>
	public string? WhyNoSave => _mission == null ? "No mission is open." : null;

	/// <summary>Saves the mission under its own name.</summary>
	public void Save() {
		if (WhyNoSave == null) {
			Continue(new SaveRequest(_mission!.Name, saveAs: false, then: null));
		}
	}

	/// <summary>Asks for a name and saves the mission under it.</summary>
	public void SaveAs() {
		if (WhyNoSave == null) {
			_typedName = _mission!.Name;
			_typedProblem = null;
			Show(Prompt.SaveAsName);
		}
	}

	/// <summary>Asks for a folder and exports the handoff into it (<see cref="HandoffExport"/>).</summary>
	public void Export() {
		if (WhyNoSave == null) {
			_typedProblem = null;
			Show(Prompt.ExportFolder);
		}
	}

	/// <summary>Asks for a folder and writes the mission's bundle into it, after a last look at its about file.</summary>
	public void ExportBundle() {
		if (WhyNoSave == null) {
			var about = _mission!.About.About;
			_bundleAbout = string.IsNullOrEmpty(about.Date) ? about with { Date = MissionAboutFields.Today } : about;
			_bundleTarget = ("", null);
			_typedProblem = null;
			Show(Prompt.BundleFolder);
		}
	}

	/// <summary>
	/// Runs <paramref name="proceed"/>, which leaves the mission, at once when it has no unsaved changes; otherwise asks
	/// first whether to save them, saying the mission is being left for <paramref name="what"/> ("quitting"), and runs it
	/// after a save or a discard, and not on Cancel. Answers whether it ran now.
	/// </summary>
	public bool Leave(string what, Action proceed) => Ask(what, proceed, discards: true);

	/// <summary>
	/// Runs <paramref name="proceed"/>, which reads the mission's saved files, at once when it has no unsaved changes; otherwise
	/// asks as <see cref="Leave"/> does, for <paramref name="what"/> ("playing it"), except that a new mission no file holds
	/// yet is offered no Discard, since there would be nothing to read. Answers whether it ran now.
	/// </summary>
	public bool SaveFirst(string what, Action proceed) =>
		Ask(what, proceed, discards: _mission?.Mover.History.NeverSaved != true);

	private bool Ask(string what, Action proceed, bool discards) {
		if (!Dirty) {
			proceed();
			return true;
		}

		_leaving = proceed;
		_leavingWhat = what;
		_leavingDiscards = discards;
		_request = null;
		Show(Prompt.Unsaved);
		return false;
	}

	/// <summary>Puts up <paramref name="message"/> with an OK button.</summary>
	public void Tell(string message) => ShowMessage(message);

	/// <summary>
	/// Draws the prompt that is up, if one is. Call once per frame, inside the ImGui frame, outside any menu.
	/// <paramref name="owner"/> is the native window the folder picker belongs to (0 for none).
	/// </summary>
	public void Draw(nint owner) {
		if (_prompt == Prompt.None) {
			return;
		}

		string title = Title(_prompt) + "###mission-saving";
		if (!_raised) {
			ImGui.OpenPopup(title);
			_raised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			// Taken down by something other than a choice: raised again, since the prompt still waits on one.
			_raised = false;
			return;
		}

		var shown = _prompt;
		switch (shown) {
			case Prompt.Unsaved:
				DrawUnsaved();
				break;
			case Prompt.SaveAsName:
				DrawSaveAsName();
				break;
			case Prompt.Findings:
				DrawFindings();
				break;
			case Prompt.Override:
				DrawOverride();
				break;
			case Prompt.Replace:
				DrawReplace();
				break;
			case Prompt.ExportFolder:
				DrawExportFolder();
				break;
			case Prompt.BundleFolder:
				DrawBundleFolder(owner);
				break;
			case Prompt.Message:
				ImGui.TextWrapped(_message);
				if (Buttons("OK") == 0 || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
					Close();
				}

				break;
		}

		// A choice that took this prompt down, or put up the next, closes this one; the next is raised next frame.
		if (_prompt != shown || !_raised) {
			ImGui.CloseCurrentPopup();
			_raised = false;
		}

		ImGui.EndPopup();
	}

	private static string Title(Prompt prompt) => prompt switch {
		Prompt.Unsaved => "Unsaved changes",
		Prompt.SaveAsName => "Save As",
		Prompt.Findings => "Mission check",
		Prompt.Override => "Save into the install",
		Prompt.Replace => "Replace a saved mission",
		Prompt.ExportFolder => "Export script.dat",
		Prompt.BundleFolder => "Export Mission Bundle",
		_ => "Mission editor",
	};

	private void DrawUnsaved() {
		ImGui.TextWrapped(_leavingDiscards
			? $"{_mission?.Name} has unsaved changes. Save them before {_leavingWhat}?"
			: $"{_mission?.Name} has never been saved. Save it before {_leavingWhat}?");
		Spacer();
		// Without Discard, Cancel is the second button.
		int chosen = _leavingDiscards ? Buttons("Save", "Discard", "Cancel") : Buttons("Save", "Cancel") switch { 1 => 2, var other => other };
		switch (chosen) {
			case 0:
				Continue(new SaveRequest(_mission!.Name, saveAs: false, then: TakeLeaving()));
				break;
			case 1:
				var proceed = TakeLeaving();
				Close();
				proceed?.Invoke();
				break;
			case 2:
				Cancel();
				break;
			default:
				if (ImGui.IsKeyPressed(ImGuiKey.Escape)) {
					Cancel();
				}

				break;
		}
	}

	private void DrawSaveAsName() {
		ImGui.TextWrapped($"Saved into the install as {MissionFileLaunch.MissionFolder}\\<name>{MissionFileLaunch.MissionExtension}: "
			+ $"up to {SavedMissions.MaxNameLength} letters, digits or _.");
		Spacer();
		if (ImGui.IsWindowAppearing()) {
			ImGui.SetKeyboardFocusHere();
		}

		ImGui.SetNextItemWidth(-1f);
		bool entered = ImGui.InputText("##name", ref _typedName, 32, ImGuiInputTextFlags.EnterReturnsTrue);
		Problem();
		int chosen = Buttons("Save", "Cancel");
		if (chosen == 0 || entered) {
			string name = _typedName.Trim().ToUpperInvariant();
			_typedProblem = SavedMissions.WhyNotName(name);
			if (_typedProblem == null) {
				Continue(new SaveRequest(name, saveAs: true, then: null));
			}
		} else if (chosen == 1 || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			Cancel();
		}
	}

	private void DrawFindings() {
		ImGui.TextWrapped($"The mission check found {Count(_findings.Count, "problem")} in this mission:");
		Spacer();
		foreach (string finding in _findings.Take(ListedFindings)) {
			ImGui.Bullet();
			ImGui.SameLine();
			ImGui.TextWrapped(finding);
		}

		if (_findings.Count > ListedFindings) {
			ImGui.TextWrapped($"and {_findings.Count - ListedFindings} more.");
		}

		Spacer();
		Confirm("Save anyway");
	}

	private void DrawOverride() {
		string name = _request!.Name;
		bool withText = _mission!.Source.Text != null;
		string files = Relative(SavedMissions.MissionPath(_installRoot, name)) + (withText ? $" and {Relative(TextPath(name))}" : "");
		string replaced = InArchive(name) ? $"the archive's {name}" : $"the {name} that comes with HERCULAN";
		ImGui.TextWrapped($"Saving writes {files} into the install, which the game then loads instead of {replaced}; "
			+ $"delete {(withText ? "them" : "it")} to go back.");
		Spacer();
		Confirm("Save");
	}

	private void DrawReplace() {
		ImGui.TextWrapped(ReplaceQuestion(_request!.Name));
		Spacer();
		Confirm("Replace");
	}

	/// <summary>
	/// Why a new mission cannot be named <paramref name="name"/>, or null when it can: a name Save As takes
	/// (<see cref="SavedMissions.WhyNotName"/>) that no archive mission has, nor one HERCULAN comes with. A new mission opens
	/// as a mission no archive holds, and the game would play it under an archive mission's name as that mission, in its
	/// career position; under an included mission's name, it would hide that mission.
	/// </summary>
	public string? WhyNotNewName(string name) =>
		SavedMissions.WhyNotName(name)
		?? (InArchive(name) ? $"The archives have a mission named {name}; a new mission takes a name of its own."
			: Included(name) ? $"A mission named {name} comes with HERCULAN; a new mission takes a name of its own."
			: null);

	/// <summary>Whether naming a mission <paramref name="name"/> means its save replaces another mission's saved files.</summary>
	public bool HasSaved(string name) => SavedMissions.Exists(_installRoot, name);

	/// <summary>The question asked before a save replaces the saved mission <paramref name="name"/>.</summary>
	public string ReplaceQuestion(string name) =>
		$"The install already has a saved {name}. Replace it? The one there is kept as "
		+ $"{Path.GetFileName(SavedMissions.MissionPath(_installRoot, name))}{SavedMissions.BackupExtension}.";

	private void DrawExportFolder() {
		ImGui.TextWrapped("Writes the generated script.dat and mission.str, with the squad's player.mec and the mission.var, "
			+ "into a folder of your choosing, which Herculan.Engine.Host or DBSIM can run. Not the install's DATA folder.");
		Spacer();
		if (ImGui.IsWindowAppearing()) {
			ImGui.SetKeyboardFocusHere();
		}

		ImGui.SetNextItemWidth(-1f);
		bool entered = ImGui.InputText("##folder", ref _typedFolder, 260, ImGuiInputTextFlags.EnterReturnsTrue);
		Problem();
		int chosen = Buttons("Export", "Cancel");
		if (chosen == 0 || entered) {
			string folder = _typedFolder.Trim();
			_typedProblem = HandoffExport.WhyNotFolder(folder, _installRoot, _handoffDirectory);
			if (_typedProblem == null) {
				RunExport(folder);
			}
		} else if (chosen == 1 || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			Cancel();
		}
	}

	private void DrawBundleFolder(nint owner) {
		if (_bundlePicking is { IsCompleted: true } picked) {
			_bundlePicking = null;
			if (picked.IsCompletedSuccessfully && picked.Result is { } pickedFolder) {
				_bundleFolder = pickedFolder;
				_typedProblem = null;
			} else if (picked.IsFaulted) {
				Console.Error.WriteLine($"The folder picker failed: {picked.Exception?.InnerException?.Message}");
				_typedProblem = "The folder picker failed; type the folder instead.";
			}
		}

		string file = _mission!.Name + MissionBundle.Extension;
		ImGui.TextWrapped($"Writes {file}, one file holding the mission, its text and what it says about itself, into a folder "
			+ "of your choosing. Exported into this install's MSN folder, it is listed in this game's Custom Missions window "
			+ "at once; a player adds it to their own game with Import... there.");
		Spacer();
		bool picking = _bundlePicking != null;
		ImGui.BeginDisabled(picking);
		if (_bundleFields.Draw(_bundleAbout) is { } edited) {
			_bundleAbout = edited;
		}

		Spacer();
		ImGui.Text("Folder:");
		bool canBrowse = NativePathPicker.IsAvailable;
		ImGui.SetNextItemWidth(canBrowse ? -EditorText.ButtonRoom(BrowseLabel) : -1f);
		bool entered = ImGui.InputText("##bundleFolder", ref _bundleFolder, 260, ImGuiInputTextFlags.EnterReturnsTrue);
		if (canBrowse) {
			ImGui.SameLine();
			if (ImGui.Button(BrowseLabel)) {
				_typedProblem = null;
				_bundlePicking = NativePathPicker.PickAsync("Export Mission Bundle into", _bundleFolder.Trim(), owner);
			}
		}

		string folder = _bundleFolder.Trim();
		if (_bundleTarget.Folder != folder) {
			string path = folder.Length > 0 ? Path.Combine(folder, file) : "";
			_bundleTarget = (folder, folder.Length > 0 && File.Exists(path) ? path : null);
		}

		if (_bundleTarget.Existing != null) {
			EditorText.Note($"The folder already holds {file}, which the export replaces.");
		}

		if (picking) {
			ImGui.TextDisabled("Choose a folder in the window that opened.");
		}

		Problem();
		int chosen = Buttons("Export", "Cancel");
		ImGui.EndDisabled();
		if (picking) {
			return;
		}

		if (chosen == 0 || entered) {
			_typedProblem = WhyNoBundle(folder);
			if (_typedProblem == null) {
				// The fields' change is the mission's too, and the bundle holds the mission as it is saved.
				_mission.About.Set(_bundleAbout);
				if (Dirty) {
					Continue(new SaveRequest(_mission.Name, saveAs: false, then: () => WriteBundle(folder)));
				} else {
					WriteBundle(folder);
				}
			}
		} else if (chosen == 1 || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			Cancel();
		}
	}

	// Why the bundle cannot be written into <folder>, or null when it can.
	private string? WhyNoBundle(string folder) {
		if (_mission!.Source.Text == null) {
			return "The mission has no text, and a mission bundle needs it.";
		}

		if (folder.Length == 0) {
			return "Choose a folder.";
		}

		return Directory.Exists(folder) || IsMissionFolder(folder) ? null : $"There is no folder {folder}.";
	}

	// Whether <folder> is the install's mission folder, which an export makes when the install has none yet.
	private bool IsMissionFolder(string folder) {
		try {
			return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(
				Path.GetFullPath(CustomMissions.Folder(_installRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
		} catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
			return false;
		}
	}

	private void WriteBundle(string folder) {
		var mission = _mission!;
		var document = mission.Source;
		string path = Path.Combine(folder, mission.Name + MissionBundle.Extension);
		try {
			Directory.CreateDirectory(folder);
			byte[] msn = new MissionFileTransformer().Write(document.Mission)
				?? throw new InvalidOperationException("The mission file does not write.");
			var texts = new Dictionary<string, byte[]> {
				[Path.GetExtension(TextPath(mission.Name))] = new MissionStringFileTransformer().Write(document.Text!)
					?? throw new InvalidOperationException("The mission's text does not write."),
			};
			File.WriteAllBytes(path, MissionBundle.Write(mission.Name, msn, texts, mission.About.About));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
				or ArgumentException or NotSupportedException) {
			Console.Error.WriteLine($"Cannot export {mission.Name}: {ex.Message}");
			ShowMessage($"Cannot export {mission.Name}: {ex.Message}");
			return;
		}

		Console.WriteLine($"Exported {mission.Name} as {path}.");
		ShowMessage($"Exported {mission.Name} as {path}.");
	}

	// Save (or the prompt's own label) and Cancel, for a prompt on the way to a save.
	private void Confirm(string label) {
		switch (Buttons(label, "Cancel")) {
			case 0:
				Continue(_request!);
				break;
			case 1:
				Cancel();
				break;
			default:
				if (ImGui.IsKeyPressed(ImGuiKey.Escape)) {
					Cancel();
				}

				break;
		}
	}

	// Takes the save on as far as it goes without asking, and puts up the next prompt it needs or writes the files.
	private void Continue(SaveRequest request) {
		_request = request;
		if (!request.Checked) {
			request.Checked = true;
			_findings = Check();
			if (_findings.Count > 0) {
				Show(Prompt.Findings);
				return;
			}
		}

		if (!request.NameConfirmed) {
			request.NameConfirmed = true;
			bool saved = SavedMissions.Exists(_installRoot, request.Name);
			if (!saved && (InArchive(request.Name) || Included(request.Name))) {
				Show(Prompt.Override);
				return;
			}

			if (saved && request.SaveAs && !request.Name.Equals(_mission!.Name, StringComparison.OrdinalIgnoreCase)) {
				Show(Prompt.Replace);
				return;
			}
		}

		Write(request);
	}

	// The mission check's findings as the Problems list words them.
	private List<string> Check() {
		var mission = _mission!;
		return new MissionLint(mission.Source, mission.Index, mission.Names.Types).Findings
			.Select(finding => ProblemText.Of(finding, mission.Names, mission.Index.Layout))
			.ToList();
	}

	private bool InArchive(string name) =>
		MissionFileLaunch.List(_shellContent())
			.Any(file => file.Equals(name + MissionFileLaunch.MissionExtension, StringComparison.OrdinalIgnoreCase));

	// Whether HERCULAN comes with a mission named <name> (CustomMissions.FindIncluded).
	private static bool Included(string name) => CustomMissions.FindIncluded(name) != null;

	private void Write(SaveRequest request) {
		var mission = _mission!;
		var document = mission.Source;
		try {
			byte[] msn = new MissionFileTransformer().Write(document.Mission)
				?? throw new InvalidOperationException("The mission file does not write.");
			byte[]? text = document.Text == null ? null : new MissionStringFileTransformer().Write(document.Text);
			var written = SavedMissions.Write(_installRoot, request.Name, _shellContent().Language, msn, text, mission.About.About);
			Console.WriteLine($"Saved {mission.Name} as {string.Join(" and ", written)}.");
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
				or ArgumentException) {
			Console.Error.WriteLine($"Cannot save {request.Name}: {ex.Message}");
			ShowMessage($"Cannot save {request.Name}: {ex.Message}");
			return;
		}

		mission.Name = request.Name;
		mission.Mover.History.MarkSaved();
		Close();
		Saved?.Invoke();
		request.Then?.Invoke();
	}

	private void RunExport(string folder) {
		var document = _mission!.Source;
		try {
			var written = HandoffExport.Write(folder, _installRoot, _handoffDirectory, document.ScriptBytes, document.MissionText,
				_mission.PlayerSquad.PlayerMec(document.Generator));
			Console.WriteLine($"Exported {_mission.Name} as {string.Join(", ", written)}.");
			ShowMessage($"Exported {_mission.Name} into {folder}: {string.Join(", ", written.Select(Path.GetFileName))}.");
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
				or InvalidOperationException) {
			// The generation runs here, at the first read since the last edit, and fails as an InvalidDataException, an
			// ArgumentException or an InvalidOperationException (MissionDocument.Script).
			_typedProblem = ex.Message;
		}
	}

	private string TextPath(string name) => SavedMissions.TextPath(_installRoot, name, _shellContent().Language);

	// A path in the install, as the game names it.
	private string Relative(string path) => Path.GetRelativePath(_installRoot, path);

	private Action? TakeLeaving() {
		var proceed = _leaving;
		_leaving = null;
		return proceed;
	}

	private void Show(Prompt prompt) {
		_prompt = prompt;
		_raised = false;
	}

	private void ShowMessage(string message) {
		_message = message;
		_request = null;
		Show(Prompt.Message);
	}

	private void Close() {
		_prompt = Prompt.None;
		_raised = false;
	}

	private void Cancel() {
		_request = null;
		_leaving = null;
		Close();
	}

	private void Problem() {
		if (_typedProblem != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, ProblemColor);
			ImGui.TextWrapped(_typedProblem);
			ImGui.PopStyleColor();
		}
	}

	// A row of buttons; the index of the one clicked, or -1.
	private static int Buttons(params string[] labels) {
		int clicked = -1;
		for (int i = 0; i < labels.Length; i++) {
			if (i > 0) {
				ImGui.SameLine();
			}

			if (ImGui.Button(labels[i], new Vector2(ScaledImGui.Scaled(110f), 0f))) {
				clicked = i;
			}
		}

		return clicked;
	}

	private static void Spacer() => ImGui.Dummy(new Vector2(0f, ScaledImGui.Scaled(6f)));

	private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

	private static readonly Vector4 ProblemColor = new(1f, 0.45f, 0.4f, 1f);

	private const string BrowseLabel = "Browse...";
}
