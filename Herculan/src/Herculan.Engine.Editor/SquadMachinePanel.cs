using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of the player's squad, under the squad's objects and group 0: which Hercs the player and
/// the squadmates pilot in this mission, and the mission's group-0 machines, each as a chassis choice and one row per
/// mount the chassis has, edited through <see cref="MachineFitter"/> (drawn by <see cref="MachineFitView"/>); and the squadmates added and removed through
/// <see cref="SquadEditor"/>.
/// </summary>
internal sealed class SquadMachinePanel {
	private readonly MachineFitter _fitter;
	private readonly MachineFitView _fit;
	private readonly SquadEditor _editor;
	private readonly MissionSquad? _squad;

	/// <param name="squad">Which Hercs the mission's squad pilots, or null for a plain <c>script.dat</c>.</param>
	public SquadMachinePanel(MachineFitter fitter, SquadEditor editor, MissionSquad? squad) {
		_fitter = fitter;
		_fit = new MachineFitView(fitter);
		_editor = editor;
		_squad = squad;
	}

	/// <summary>
	/// Draws what references something as links, one line per record, as the object delete view lists it; set by the
	/// Properties panel, which holds the links.
	/// </summary>
	public Action<DeleteNamers>? References { get; set; }

	/// <summary>The player's machine: which Herc the player pilots, then group 0's first member's record.</summary>
	public void DrawPlayer() {
		DrawPlayerMachine();
		Failures();
	}

	/// <summary>Group 0's view: the player's machine, then each squadmate, with Add and Remove.</summary>
	public void DrawSquad() {
		DrawPlayerMachine();
		ImGui.Separator();
		var squadmates = _editor.Squadmates.ToList();
		ImGui.Text($"Squadmates: {squadmates.Count}");
		if (WhereSquadmatesComeFrom() is { } from) {
			ImGui.TextWrapped(from);
		}

		if (LeftBehind(squadmates.Count) is { } behind) {
			ImGui.TextWrapped(behind);
		}

		if (squadmates.Count > 1) {
			EditorText.Note("Order matters: each takes the squad position of its place in the list, and removing one moves "
				+ "those after it up.");
		}

		foreach (int member in squadmates) {
			ImGui.Separator();
			DrawSquadmate(member);
		}

		ImGui.Separator();
		string? noAdd = _editor.WhyNoAdd;
		ImGui.BeginDisabled(noAdd != null);
		if (ImGui.Button("Add a squadmate")) {
			_editor.RequestAdd();
		}

		ImGui.EndDisabled();
		if (noAdd == null) {
			EditorText.Note("A new squadmate is the player's machine's chassis, with its stock fit.");
		} else {
			EditorText.Reason(noAdd);
		}
		Failures();
	}

	/// <summary>
	/// The view of a squadmate's own object: the squadmate group 0's member slots give block-7 slot <paramref name="slot"/>.
	/// </summary>
	public void DrawSquadmateObject(int slot) {
		ImGui.Separator();
		int? member = _editor.Squadmates.Cast<int?>().FirstOrDefault(member => _fitter.SquadSlot(member!.Value) == slot);
		if (member is not { } squadmate) {
			EditorText.Note("Group 0 lists no squadmate for this Herc.");
			return;
		}

		CampaignNote();
		if (WhereSquadmatesComeFrom() is { } from) {
			ImGui.TextWrapped(from);
		}

		DrawSquadmate(squadmate);
		Failures();
	}

	// The player's machine: which Herc the player pilots, then group 0's first member's record.
	private void DrawPlayerMachine() {
		ImGui.Separator();
		ImGui.Text("Player's machine");
		CampaignNote();
		if (WhichHerc() is { } piloted) {
			ImGui.TextWrapped(piloted);
		}

		if (_fitter.SquadSlot(0) is { } slot) {
			DrawMachine(slot);
		} else {
			EditorText.Note("Group 0 references no herc record.");
		}
	}

	// Which Herc the player pilots in this mission, by the launch's own rule (MissionSquad).
	private string? WhichHerc() => _squad?.PlayerMachine switch {
		PlayerMachineSource.Mission => "In this mission the player pilots the Herc below.",
		PlayerMachineSource.HercType => $"In this mission the player pilots a stock {ChassisName(_squad.HercTypeChassis)}, "
			+ "the Herc Type chosen on the practice screen, not the Herc below.",
		_ => null,
	};

	// A campaign mission's squad is the career's (MissionSquad), whatever group 0 holds.
	private void CampaignNote() {
		if (_squad?.PlayerMachine == PlayerMachineSource.CareerSquad) {
			EditorText.Emphasis("In campaign missions, the default HERCs and loadouts selected here are not used.");
		}
	}

	// Which Hercs the squadmates pilot in a training-mode load, by the launch's own rule (MissionSquad): group 0's. A
	// campaign mission says so once, through CampaignNote.
	private string? WhereSquadmatesComeFrom() => _squad?.PlayerMachine switch {
		PlayerMachineSource.Mission or PlayerMachineSource.HercType =>
			"In this mission the player's squadmates pilot the Hercs below, group 0's members after the player's machine.",
		_ => null,
	};

	// The squad build's retail bug, where the squadmates are group 0's: the last squadmate listed never goes on strength
	// (docs/retail/shell/main-menu.md#starting-a-practice-mission).
	private string? LeftBehind(int squadmates) {
		if (squadmates == 0 || _squad?.PlayerMachine is not (PlayerMachineSource.Mission or PlayerMachineSource.HercType)) {
			return null;
		}

		const string bug = "The last squadmate listed stays behind (a retail bug), so ";
		return squadmates switch {
			1 => bug + "this squadmate does not appear in the mission and the player goes alone.",
			2 => bug + "only the first of these appears in the mission.",
			_ => bug + $"only the first {squadmates - 1} of these appear in the mission.",
		};
	}

	// A squadmate: its machine, and Remove or why it cannot be removed.
	private void DrawSquadmate(int member) {
		ImGui.PushID($"squadmate{member}");
		ImGui.Text($"Squadmate {member}");
		if (_fitter.SquadSlot(member) is { } slot) {
			DrawMachine(slot);
		} else {
			EditorText.Note($"Group 0's member slot {member} references no herc record.");
		}

		var (reason, names, ownActions) = _editor.WhyNoRemove(member);
		if (reason == null && !names.Any && !ownActions) {
			if (ImGui.Button("Remove")) {
				_editor.RequestRemove(member);
			}
		} else if (reason == null) {
			ImGui.TextWrapped("Cannot be removed while these reference or need it:");
			References?.Invoke(names);
			if (ownActions) {
				ImGui.TextWrapped("An action it fires when engaged or defeated");
			}
		} else if (_fitter.SquadSlot(member) is not { } fixedSlot || reason != _fitter.WhyFixed(fixedSlot)) {
			EditorText.Reason(reason, "Cannot be removed: ");
		}

		ImGui.PopID();
	}

	// The chassis choice and the fit of the record at block-7 slot <slot>.
	private void DrawMachine(int slot) =>
		_fit.Draw(slot, MachineFitter.Chassis, ChassisName, "A new chassis comes with its stock fit.");

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
