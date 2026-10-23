using System.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>What File &gt; New mission was asked for: the mission's name, its zone, its theater, and whether it is played by night.</summary>
internal sealed record NewMissionRequest(string Name, ZoneChoice Zone, short Theater, bool Night);

/// <summary>
/// File &gt; New mission's modal: a name, taken as Save As takes one but never an archive mission's
/// (<see cref="MissionSaving.WhyNotNewName"/>), and asking first when the install already has a saved mission of that
/// name; a zone from the ones the install has (<see cref="MissionZones"/>); a theater (<see cref="TheaterNames"/>); and
/// day or night. The dialog only asks: the host builds and opens the mission (<see cref="TakeRequest"/>)
/// and reports back through <see cref="Creating"/> and <see cref="Failure"/>.
/// </summary>
internal sealed class NewMissionDialog {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 640f;
	private const float ListHeight = 300f;
	private const float TheaterWidth = 160f;

	private readonly MissionSaving _saving;
	private readonly Func<IReadOnlyList<ZoneChoice>> _listZones;
	private readonly Func<IReadOnlyList<string>> _listTheaters;
	private IReadOnlyList<ZoneChoice>? _zones;
	private IReadOnlyList<string>? _theaters;
	private bool _open;
	private bool _raised;
	private bool _askingReplace;
	private string _typedName = "";
	private string? _problem;
	private int _zone;
	private int _theater;
	private bool _night;
	private NewMissionRequest? _request;

	/// <param name="saving">The save path, whose name rules a new mission takes.</param>
	/// <param name="listZones">Lists the zones; called the first time the dialog opens.</param>
	/// <param name="listTheaters">Labels the theaters, in theater order; called the first time the dialog opens.</param>
	public NewMissionDialog(MissionSaving saving, Func<IReadOnlyList<ZoneChoice>> listZones,
			Func<IReadOnlyList<string>> listTheaters) {
		_saving = saving;
		_listZones = listZones;
		_listTheaters = listTheaters;
	}

	/// <summary>The mission being built and opened, which the dialog names in place of its fields until that ends.</summary>
	public string? Creating { get; set; }

	/// <summary>Why the last build or open failed, shown under the fields until the next is asked for.</summary>
	public string? Failure { get; set; }

	/// <summary>Whether the dialog is up, which takes the keyboard.</summary>
	public bool IsOpen => _open;

	public void Open() {
		_zones ??= _listZones();
		_theaters ??= _listTheaters();
		_open = true;
		_askingReplace = false;
		_problem = null;
		Failure = null;
	}

	public void Close() {
		_open = false;
		Creating = null;
	}

	/// <summary>The mission the user asked for since the last call, or null.</summary>
	public NewMissionRequest? TakeRequest() {
		var request = _request;
		_request = null;
		return request;
	}

	/// <summary>Draws the dialog, if it is up. Call once per frame, inside the ImGui frame, outside any menu.</summary>
	public void Draw() {
		// Once closed the popup is taken down below, in the frame after a Close made outside one.
		if (!_open && !_raised) {
			return;
		}

		const string title = "New mission###new-mission";
		if (!_raised) {
			ImGui.OpenPopup(title);
			_raised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			// Taken down by something other than a choice: raised again while the dialog still waits on one.
			_raised = false;
			return;
		}

		if (_open && Creating != null) {
			ImGui.TextUnformatted($"Creating {Creating}...");
		} else if (_open && _askingReplace) {
			DrawReplace();
		} else if (_open) {
			DrawFields();
		}

		if (!_open) {
			ImGui.CloseCurrentPopup();
			_raised = false;
		}

		ImGui.EndPopup();
	}

	private void DrawFields() {
		var zones = _zones ?? [];
		ImGui.TextWrapped($"Name: up to {SavedMissions.MaxNameLength} letters, digits or _. File > Save writes it into the install as "
			+ $"{MissionFileLaunch.MissionFolder}\\<name>{MissionFileLaunch.MissionExtension}.");
		if (ImGui.IsWindowAppearing()) {
			ImGui.SetKeyboardFocusHere();
		}

		ImGui.SetNextItemWidth(-1f);
		bool nameWrong = _problem != null;
		if (nameWrong) {
			EditorText.PushProblemField();
		}

		bool entered = ImGui.InputText("##name", ref _typedName, 32, ImGuiInputTextFlags.EnterReturnsTrue);
		if (nameWrong) {
			EditorText.PopProblemField();
		}

		Spacer();

		ImGui.TextUnformatted("Zone:");
		if (zones.Count == 0) {
			ImGui.TextWrapped("The install has no terrain zones.");
		} else if (ImGui.BeginListBox("##zones", new Vector2(-1f, ScaledImGui.Scaled(ListHeight)))) {
			for (int i = 0; i < zones.Count; i++) {
				if (ImGui.Selectable(zones[i].Label, i == _zone)) {
					_zone = i;
				}
			}

			ImGui.EndListBox();
		}

		Spacer();
		var theaters = _theaters ?? [];
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Theater:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TheaterWidth));
		if (ImGui.BeginCombo("##theater", _theater < theaters.Count ? theaters[_theater] : $"{_theater}")) {
			for (int i = 0; i < theaters.Count; i++) {
				if (ImGui.Selectable(theaters[i], i == _theater)) {
					_theater = i;
				}
			}

			ImGui.EndCombo();
		}

		ImGui.SameLine();
		if (ImGui.RadioButton("Day", !_night)) {
			_night = false;
		}

		ImGui.SameLine();
		if (ImGui.RadioButton("Night", _night)) {
			_night = true;
		}

		Spacer();
		ImGui.TextWrapped("The mission is the player's HERC alone, with one waypoint to reach.");
		Problem(_problem);
		Problem(Failure);
		Spacer();

		int chosen = Buttons("Create", "Cancel");
		if ((chosen == 0 || entered) && zones.Count > 0) {
			string name = _typedName.Trim().ToUpperInvariant();
			_problem = _saving.WhyNotNewName(name);
			Failure = null;
			if (_problem == null) {
				if (_saving.HasSaved(name)) {
					_askingReplace = true;
				} else {
					Ask(name);
				}
			}
		} else if (chosen == 1 || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			Close();
		}
	}

	private void DrawReplace() {
		string name = _typedName.Trim().ToUpperInvariant();
		ImGui.TextWrapped(_saving.ReplaceQuestion(name) + " It is replaced when the new mission is saved.");
		Spacer();
		switch (Buttons("Replace", "Cancel")) {
			case 0:
				_askingReplace = false;
				Ask(name);
				break;
			case 1:
				_askingReplace = false;
				break;
			default:
				if (ImGui.IsKeyPressed(ImGuiKey.Escape)) {
					_askingReplace = false;
				}

				break;
		}
	}

	private void Ask(string name) {
		var zones = _zones!;
		_request = new NewMissionRequest(name, zones[Math.Clamp(_zone, 0, zones.Count - 1)], (short)_theater, _night);
	}

	private static void Problem(string? problem) {
		if (problem != null) {
			EditorText.Problem(problem);
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
}
