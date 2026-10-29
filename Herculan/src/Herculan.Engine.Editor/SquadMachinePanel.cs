using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of the player's squad, under the squad's objects and group 0, read from the layout by GUID
/// (docs/herculan/editor-design.md#the-players-squad): which HERCs the player and the squadmates pilot in this
/// mission, and the mission's own HERCs of the squad, each as a chassis choice and one row per hardpoint, edited through
/// <see cref="MachineFitter"/> (drawn by <see cref="MachineFitView"/>); and the squadmates added and removed through
/// <see cref="SquadEditor"/>. Squadmates are numbered from 1 by their place in the squad's list. In a mission that loads with
/// the career's squad (<see cref="PlayerMachineSource.CareerSquad"/>), each HERC's chassis and weapons are under a collapsed
/// header, since they are not used there.
/// </summary>
internal sealed class SquadMachinePanel {
	private readonly MachineFitter _fitter;
	private readonly MachineFitView _fit;
	private readonly SquadEditor _editor;
	private readonly MissionSquad _squad;

	/// <param name="squad">Which HERCs the mission's squad pilots.</param>
	/// <param name="presets">The loadout preset choice each HERC's fit offers.</param>
	public SquadMachinePanel(MachineFitter fitter, SquadEditor editor, MissionSquad squad, LoadoutPresetView presets) {
		_fitter = fitter;
		_fit = new MachineFitView(fitter, presets);
		_editor = editor;
		_squad = squad;
	}

	/// <summary>
	/// Draws what references something as links, one line per record, as the object delete view lists it; set by the
	/// Properties panel, which holds the links.
	/// </summary>
	public Action<DeleteNamers>? References { get; set; }

	// A campaign mission's squad is the career's (MissionSquad), whatever the mission's own HERCs are.
	private bool Campaign => _squad.PlayerMachine == PlayerMachineSource.CareerSquad;

	/// <summary>The view of the player's own HERC: which HERC the player pilots, then the mission's own.</summary>
	public void DrawPlayer() {
		DrawPlayerHerc();
		Failures();
	}

	/// <summary>The squad's group view: the player's HERC, then each squadmate, with Add and Remove.</summary>
	public void DrawSquad() {
		DrawPlayerHerc();
		ImGui.Separator();
		var squadmates = _editor.Squadmates;
		ImGui.Text($"Squadmates: {squadmates.Count}");
		if (squadmates.Count > 0 && !Campaign) {
			EditorText.Note("Squadmates pilot the HERCs listed below.");
		}

		if (LeftBehind(squadmates.Count) is { } behind) {
			ImGui.TextWrapped(behind);
		}

		for (int number = 1; number <= squadmates.Count; number++) {
			ImGui.Separator();
			DrawSquadmate(number, squadmates[number - 1]);
		}

		ImGui.Separator();
		string? noAdd = _editor.WhyNoAdd;
		ImGui.BeginDisabled(noAdd != null);
		if (ImGui.Button("Add a squadmate")) {
			_editor.RequestAdd();
		}

		ImGui.EndDisabled();
		EditorText.Tooltip(noAdd == null ? "A new squadmate takes the player's HERC's chassis, with its stock fit, after the last." : null);
		EditorText.Reason(noAdd);

		Failures();
	}

	/// <summary>The view of a squadmate's own object, the herc of row-12 GUID <paramref name="guid"/>.</summary>
	public void DrawSquadmateObject(short guid) {
		ImGui.Separator();
		var squadmates = _editor.Squadmates;
		int number = squadmates.ToList().IndexOf(guid) + 1;
		if (number == 0) {
			EditorText.Note("Not in the squad in play: it comes after a gap in the squad's list, and the HERCs after a gap are not used.");
			return;
		}

		DrawSquadmate(number, guid);
		Failures();
	}

	// The player's HERC: which HERC the player pilots, then the mission's own.
	private void DrawPlayerHerc() {
		ImGui.Text("Player's HERC");
		if (WhichHerc() is { } piloted) {
			ImGui.TextWrapped(piloted);
		}

		if (_editor.Player is { } player) {
			Pickers("player", () => DrawHerc(player));
		} else {
			EditorText.Note("The player's squad has no HERC for the player.");
		}
	}

	// Which HERC the player pilots in this mission, by the launch's own rule (MissionSquad).
	private string? WhichHerc() => _squad.PlayerMachine switch {
		PlayerMachineSource.Mission => "In this mission the player pilots the HERC below.",
		PlayerMachineSource.HercType => $"In this mission the player pilots a stock {ChassisName(_squad.HercTypeChassis)}, "
			+ "the Herc Type chosen on the practice screen, not the HERC below.",
		_ => null,
	};

	// The squad build's retail bug, where the squadmates are the mission's: the last squadmate listed never goes on strength
	// (docs/retail/shell/main-menu.md#starting-a-practice-mission).
	private string? LeftBehind(int squadmates) {
		if (squadmates == 0 || Campaign) {
			return null;
		}

		const string bug = "In training missions, the last squadmate listed stays behind (a retail bug), so ";
		return squadmates switch {
			1 => bug + "this squadmate does not appear in the mission and the player goes alone.",
			2 => bug + "only the first of these appears in the mission.",
			_ => bug + $"only the first {squadmates - 1} of these appear in the mission.",
		};
	}

	// Squadmate <number>, from 1, the herc of row-12 GUID <guid>: its HERC, and Remove or why it cannot be removed.
	private void DrawSquadmate(int number, short guid) {
		ImGui.PushID($"squadmate{number}");
		ImGui.Text($"Squadmate {number}");
		Pickers("HERC", () => DrawHerc(guid));
		var (reason, names, ownActions) = _editor.WhyNoRemove(number);
		if (reason == null && !names.Any && !ownActions) {
			if (ImGui.Button("Remove")) {
				_editor.RequestRemove(number);
			}

			if (number < _editor.Squadmates.Count) {
				EditorText.Note("Each squadmate after it moves up one place.");
			}
		} else if (reason == null) {
			ImGui.TextWrapped("Cannot be removed while these reference or need it:");
			References?.Invoke(names);
			if (ownActions) {
				ImGui.TextWrapped("A trigger it fires when engaged or defeated");
			}
		} else {
			EditorText.Reason(reason, "Cannot be removed: ");
		}

		ImGui.PopID();
	}

	// A HERC's chassis and weapons: in a campaign mission, which does not use them, under a collapsed header.
	private void Pickers(string id, Action draw) {
		if (!Campaign) {
			draw();
			return;
		}

		ImGui.PushID(id);
		if (ImGui.CollapsingHeader("HERC and weapons (not used in the campaign)")) {
			draw();
		}

		ImGui.PopID();
	}

	// The chassis choice and the hardpoints of the herc of row-12 GUID <guid>.
	private void DrawHerc(short guid) =>
		_fit.Draw(guid, MachineFitter.Chassis, ChassisName, "A new chassis comes with its stock fit.");

	// Why the last fit, add or remove could not be made.
	private void Failures() {
		if (_fitter.Failure is { } failure) {
			ImGui.TextWrapped($"Not changed: {failure}");
		}

		if (_editor.Failure is { } refused) {
			ImGui.TextWrapped($"Not changed: {refused}");
		}
	}

	private static string ChassisName(int chassis) => MachineFitView.ChassisName(chassis);
}
