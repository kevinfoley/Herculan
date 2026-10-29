using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The Custom Missions window the shell's menu bar opens (<see cref="Settings.HostMenuBar"/>), HERCULAN's own: every
/// custom mission in the install and every one HERCULAN comes with (<see cref="CustomMissions"/>), each with Play, its
/// author and About, and Delete, which asks first and is not offered for one HERCULAN comes with; and Import..., which copies a mission bundle the player picks into the install's mission folder,
/// asking first before it replaces one of the same file name. Play writes the mission's handoff into a scratch directory of
/// its own, so the install's <c>DATA\</c> keeps the career's, and hands it to the caller, which ends the shell's turn for it.
///
/// <para>A mission's name, author and about text are anyone's, so they are drawn as text and never as ImGui format strings or labels.</para>
/// </summary>
sealed class CustomMissionsWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 640f;
	private const float PanelHeight = 420f;
	private const float PromptWidth = 460f;
	private const float AboutHeight = 220f;
	private const float TooltipWidth = 420f;

	// The name column is at least NameColumnWidth wide, and leaves NameGap after the widest name it shows.
	private const float NameColumnWidth = 160f;
	private const float NameGap = 10f;

	// "###" so that each popup's ID is this part alone, the same for OpenPopup and for the titled BeginPopupModal.
	private const string DeleteId = "###custom_delete";
	private const string ReplaceId = "###custom_replace";
	private const string AboutId = "###custom_about";

	/// <summary>Where Play writes the mission's handoff, and where the mission's results are written.</summary>
	private static readonly string PlayDirectory = Path.Combine(Path.GetTempPath(), "herculan-custom-mission");

	private readonly LocalizationTable _localization;
	private readonly string _installRoot;
	private readonly GameContent _shellContent;
	private readonly Action<ShellLaunch> _play;
	private IReadOnlyList<CustomMission> _missions = [];

	// What the list shows for each of _missions, in step: its name, and when missions of more than one form share the name,
	// its file's extension after it, or for one HERCULAN comes with a mark saying so, so the rows can be told apart.
	private IReadOnlyList<string> _names = [];
	private string? _status;
	private Task<string?>? _picking;

	// What each popup is up for, null while it is down, and whether it has been opened in ImGui.
	private (CustomMission Mission, IReadOnlyList<string> Files)? _deleting;
	private CustomMissionImport? _replacing;
	private CustomMission? _about;
	private bool _deleteRaised;
	private bool _replaceRaised;
	private bool _aboutRaised;

	/// <param name="shellContent">VSHELL's mount of the install, which a mission's load reads through.</param>
	/// <param name="play">Takes the handoff Play wrote and ends the shell's turn for it once the frame is drawn.</param>
	public CustomMissionsWindow(LocalizationTable localization, string installRoot, GameContent shellContent, Action<ShellLaunch> play) {
		_localization = localization;
		_installRoot = installRoot;
		_shellContent = shellContent;
		_play = play;
	}

	/// <summary>Whether the window is open.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Opens the window on the missions the install holds now.</summary>
	public void Open() {
		IsOpen = true;
		_status = null;
		Refresh();
	}

	/// <summary>Closes the window and whichever of its prompts is up.</summary>
	public void Close() {
		IsOpen = false;
		_deleting = null;
		_replacing = null;
		_about = null;
	}

	/// <summary>[Esc]'s step back: takes down the prompt or the about box that is up, else closes the window.</summary>
	public void BackOut() {
		if (_deleting != null || _replacing != null || _about != null) {
			_deleting = null;
			_replacing = null;
			_about = null;
		} else {
			Close();
		}
	}

	/// <summary>
	/// Draws the window, if it is open. Call once per frame, inside the ImGui frame. <paramref name="owner"/> is the native
	/// window the file picker belongs to, and <paramref name="centre"/> when the window is centred (<see cref="Settings.PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(nint owner, ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		TakePicked();
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), ScaledImGui.Scaled(PanelHeight)), ImGuiCond.Appearing);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		bool stayOpen = true;
		if (ImGui.Begin(Text("custom_missions.title") + "###custom_missions", ref stayOpen, ImGuiWindowFlags.NoCollapse)) {
			// The list takes what the status line and the button row leave.
			float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y
				+ (_status != null ? ImGui.GetTextLineHeightWithSpacing() * 2 : 0f);
			DrawList(-footer);
			ImGui.Separator();
			if (_status != null) {
				Wrapped(_status);
			}

			LocalizedImGuiHelpers.DrawButtonRow(_localization, ("custom_missions.import", () => StartImport(owner)),
				("general.close", Close));

			DrawDeletePrompt();
			DrawReplacePrompt();
			DrawAbout();
		}

		ImGui.End();
		if (!stayOpen) {
			Close();
		}
	}

	private void DrawList(float height) {
		if (_missions.Count == 0) {
			if (ImGui.BeginChild("##missions", new Vector2(0f, height))) {
				Wrapped(string.Format(Text("custom_missions.empty"), CustomMissions.Folder(_installRoot)));
			}

			ImGui.EndChild();
			return;
		}

		var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
		if (!ImGui.BeginTable("##missions", 5, flags, new Vector2(0f, height))) {
			return;
		}

		string nameHeader = Text("custom_missions.mission");
		float widestName = _names.Append(nameHeader).Max(name => ImGui.CalcTextSize(name).X);
		float nameWidth = MathF.Max(ScaledImGui.Scaled(NameColumnWidth), widestName + ScaledImGui.Scaled(NameGap));

		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableSetupColumn(nameHeader, ImGuiTableColumnFlags.WidthFixed, nameWidth);
		ImGui.TableSetupColumn(Text("custom_missions.author"), ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableSetupColumn("##play");
		ImGui.TableSetupColumn("##about");
		ImGui.TableSetupColumn("##delete");
		ImGui.TableHeadersRow();

		for (int i = 0; i < _missions.Count; i++) {
			var mission = _missions[i];
			ImGui.PushID(i);
			ImGui.TableNextRow();

			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(_names[i]);
			string fileKey = mission.Form switch {
				CustomMissionForm.Included => "custom_missions.included_file",
				CustomMissionForm.Bundle => "custom_missions.bundle_file",
				_ => "custom_missions.loose_file",
			};
			Tooltip(string.Format(Text(fileKey), mission.Path)
				+ (mission.Problem != null ? "\n\n" + string.Format(Text("custom_missions.cannot_play"), mission.Problem) : ""));

			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(mission.About?.Author ?? "");

			ImGui.TableNextColumn();
			ImGui.BeginDisabled(mission.Problem != null);
			if (ImGui.Button(Text("custom_missions.play"))) {
				Play(mission);
			}

			ImGui.EndDisabled();
			if (mission.Problem != null) {
				Tooltip(string.Format(Text("custom_missions.cannot_play"), mission.Problem));
			}

			ImGui.TableNextColumn();
			ImGui.BeginDisabled(mission.About is not { IsBlank: false });
			if (ImGui.Button(Text("custom_missions.about"))) {
				_about = mission;
			}

			ImGui.EndDisabled();

			ImGui.TableNextColumn();
			bool included = mission.Form == CustomMissionForm.Included;
			ImGui.BeginDisabled(included);
			if (ImGui.Button(Text("custom_missions.delete"))) {
				AskDelete(mission);
			}

			ImGui.EndDisabled();
			if (included) {
				Tooltip(Text("custom_missions.cannot_delete_included"));
			}

			ImGui.PopID();
		}

		ImGui.EndTable();
	}

	private void Play(CustomMission mission) {
		if (CustomMissions.WriteHandoff(_shellContent, _installRoot, mission, PlayDirectory, out string? failure) is not { } script) {
			Console.Error.WriteLine($"Cannot play {mission.Name}: {failure}");
			_status = $"{string.Format(Text("custom_missions.play_failed"), mission.Name)} {failure}";
			return;
		}

		_play(new ShellLaunch(script, ShellWorkingFiles.DataDirectory(_installRoot)));
	}

	private void AskDelete(CustomMission mission) {
		_status = null;
		try {
			_deleting = (mission, CustomMissions.Files(_installRoot, mission));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_status = $"{string.Format(Text("custom_missions.delete_failed"), mission.Name)} {ex.Message}";
		}
	}

	private void DrawDeletePrompt() {
		if (!RaisePrompt(DeleteId, _deleting != null, ref _deleteRaised, "custom_missions.delete_title")) {
			return;
		}

		if (_deleting is var (mission, files)) {
			Wrapped(string.Format(Text("custom_missions.delete_message"), mission.Name));
			foreach (string file in files) {
				ImGui.Bullet();
				ImGui.TextUnformatted(Path.GetFileName(file));
			}

			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(6)));
			Wrapped(Text("custom_missions.cannot_undo"));
			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
			LocalizedImGuiHelpers.DrawButtonRow(_localization, ("custom_missions.delete", () => Delete(mission)),
				("general.cancel", () => _deleting = null));
		}

		EndPrompt(_deleting == null, ref _deleteRaised);
	}

	private void Delete(CustomMission mission) {
		_deleting = null;
		try {
			CustomMissions.Delete(_installRoot, mission);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_status = $"{string.Format(Text("custom_missions.delete_failed"), mission.Name)} {ex.Message}";
		}

		Refresh();
	}

	private void StartImport(nint owner) {
		if (_picking != null) {
			return;
		}

		_status = null;
		if (!NativePathPicker.IsAvailable) {
			_status = string.Format(Text("custom_missions.no_picker"), CustomMissions.Folder(_installRoot));
			return;
		}

		_picking = NativePathPicker.PickAsync(Text("custom_missions.picker_title"), null, owner,
			new NativePathPicker.FileFilter(Text("custom_missions.picker_filter"), [MissionBundle.Extension.TrimStart('.')]));
	}

	// The picker's answer, once it has one: the bundle chosen is read and checked, then written, or the replace prompt goes up.
	private void TakePicked() {
		if (_picking is not { IsCompleted: true } picked) {
			return;
		}

		_picking = null;
		string? source = picked.IsCompletedSuccessfully ? picked.Result : null;
		if (picked.IsFaulted) {
			_status = picked.Exception?.InnerException?.Message;
		}

		if (source == null) {
			return;
		}

		if (CustomMissions.ReadImport(_installRoot, source, out string? failure) is not { } import) {
			_status = $"{string.Format(Text("custom_missions.import_failed"), Path.GetFileName(source))} {failure}";
			return;
		}

		if (import.Replaces) {
			_replacing = import;
		} else {
			Import(import);
		}
	}

	private void DrawReplacePrompt() {
		if (!RaisePrompt(ReplaceId, _replacing != null, ref _replaceRaised, "custom_missions.replace_title")) {
			return;
		}

		if (_replacing is { } import) {
			Wrapped(string.Format(Text("custom_missions.replace_message"), Path.GetFileName(import.Destination)));
			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
			LocalizedImGuiHelpers.DrawButtonRow(_localization, ("custom_missions.replace", () => Import(import)),
				("general.cancel", () => _replacing = null));
		}

		EndPrompt(_replacing == null, ref _replaceRaised);
	}

	private void Import(CustomMissionImport import) {
		_replacing = null;
		string file = Path.GetFileName(import.Destination);
		try {
			CustomMissions.Import(import);
			_status = string.Format(Text("custom_missions.imported"), file);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_status = $"{string.Format(Text("custom_missions.import_failed"), file)} {ex.Message}";
		}

		Refresh();
	}

	private void DrawAbout() {
		if (!RaisePrompt(AboutId, _about != null, ref _aboutRaised, "custom_missions.about_title")) {
			return;
		}

		if (_about is { About: { } about } mission) {
			ImGui.TextUnformatted(mission.Name);
			ImGui.Separator();
			Field("custom_missions.author", about.Author);
			Field("custom_missions.date", about.Date);
			Field("custom_missions.version", about.Version?.ToString(System.Globalization.CultureInfo.CurrentCulture));
			if (!string.IsNullOrEmpty(about.Description)) {
				ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(4)));
				if (ImGui.BeginChild("##description", new Vector2(0f, ScaledImGui.Scaled(AboutHeight)), ImGuiChildFlags.Border)) {
					Wrapped(about.Description);
				}

				ImGui.EndChild();
			}

			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
			LocalizedImGuiHelpers.DrawButtonRow(_localization, ("general.close", () => _about = null));
		}

		EndPrompt(_about == null, ref _aboutRaised);
	}

	// One about-file field, its name and its value, or nothing when the file leaves it out.
	private void Field(string key, string? value) {
		if (string.IsNullOrEmpty(value)) {
			return;
		}

		ImGui.TextUnformatted(Text(key) + ":");
		ImGui.SameLine();
		ImGui.TextUnformatted(value);
	}

	// Opens the popup <id> once <up> asks for it and begins it, titled by <titleKey>; returns whether it is open this frame,
	// when the caller draws its body and then EndPrompt.
	private bool RaisePrompt(string id, bool up, ref bool raised, string titleKey) {
		if (!up && !raised) {
			return false;
		}

		bool raising = false;
		if (up && !raised) {
			ImGui.OpenPopup(id);
			raised = true;
			raising = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PromptWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		// Not open, though raised: another modal opened over it closed it, so it is raised again on the next frame.
		if (!ImGui.BeginPopupModal(Text(titleKey) + id, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			raised = false;
			return false;
		}

		if (raising) {
			ImGuiNavigation.FocusWindowItems();
		}

		return true;
	}

	// Ends the popup RaisePrompt began, closing it from inside once <down>, however it was taken down.
	private static void EndPrompt(bool down, ref bool raised) {
		if (down) {
			ImGui.CloseCurrentPopup();
			raised = false;
		}

		ImGui.EndPopup();
	}

	private void Refresh() {
		try {
			_missions = CustomMissions.List(_installRoot);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_missions = [];
			_status = ex.Message;
		}

		var inBothForms = _missions
			.GroupBy(mission => mission.Name, StringComparer.OrdinalIgnoreCase)
			.Where(named => named.Select(mission => mission.Form).Distinct().Count() > 1)
			.Select(named => named.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		_names = _missions
			.Select(mission => !inBothForms.Contains(mission.Name) ? mission.Name
				: mission.Form == CustomMissionForm.Included ? $"{mission.Name} {Text("custom_missions.included")}"
				: mission.Name + Path.GetExtension(mission.Path))
			.ToList();
	}

	private string Text(string key) => _localization.GetStringOrKey(key);

	// <text> wrapped at the window's edge, drawn as it stands.
	private static void Wrapped(string text) {
		ImGui.PushTextWrapPos(0f);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
	}

	// <text> as the last item's tooltip, drawn as it stands, while the pointer is over the item, enabled or not.
	private static void Tooltip(string text) {
		if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			return;
		}

		ImGui.BeginTooltip();
		ImGui.PushTextWrapPos(ScaledImGui.Scaled(TooltipWidth));
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}
}
