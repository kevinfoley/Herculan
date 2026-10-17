using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of the player's squad, under the squad's objects and group 0: which Hercs the player and
/// the squadmates pilot in this mission, and the mission's group-0 machines, each as a chassis choice and one row per
/// mount the chassis has, edited through <see cref="MachineFitter"/>; and the squadmates added and removed through
/// <see cref="SquadEditor"/>.
/// </summary>
internal sealed class SquadMachinePanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float WeaponWidth = 150f;
	private const float GuidanceWidth = 70f;

	private readonly MachineFitter _fitter;
	private readonly SquadEditor _editor;
	private readonly MissionSquad? _squad;

	/// <param name="squad">Which Hercs the mission's squad pilots, or null for a plain <c>script.dat</c>.</param>
	public SquadMachinePanel(MachineFitter fitter, SquadEditor editor, MissionSquad? squad) {
		_fitter = fitter;
		_editor = editor;
		_squad = squad;
	}

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
			EditorText.Note("Group 0 names no herc record.");
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
			EditorText.Note($"Group 0's member slot {member} names no herc record.");
		}

		var (reason, names, ownActions) = _editor.WhyNoRemove(member);
		if (reason == null && !names.Any && !ownActions) {
			if (ImGui.Button("Remove")) {
				_editor.RequestRemove(member);
			}
		} else if (reason == null) {
			EditorText.Note($"Cannot be removed while the mission names or needs it: {Needs(names, ownActions)}.");
		} else if (_fitter.SquadSlot(member) is not { } fixedSlot || reason != _fitter.WhyFixed(fixedSlot)) {
			EditorText.Reason(reason, "Cannot be removed: ");
		}

		ImGui.PopID();
	}

	// What names or needs a squadmate, by the block indices this state exports them at.
	private static string Needs(DeleteNamers names, bool ownActions) {
		var needs = names.Objectives.Select(objective => $"objective {objective}")
			.Concat(names.Actions.Select(action => $"action {action}"))
			.Concat(names.Orders.Select(order => $"group {order.Group}'s order {order.Slot}"))
			.ToList();
		if (names.Elsewhere > 0) {
			needs.Add($"{names.Elsewhere} record(s) this state of the mission leaves out");
		}

		if (ownActions) {
			needs.Add("an action it fires when engaged or defeated");
		}

		return string.Join(", ", needs);
	}

	// The chassis choice and the fit of the record at block-7 slot <slot>.
	private void DrawMachine(int slot) {
		if (_fitter.Record(slot) is not { } record) {
			return;
		}

		string? reason = _fitter.WhyFixed(slot);
		ImGui.PushID(slot);
		ImGui.BeginDisabled(reason != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		if (ImGui.BeginCombo("Chassis", ChassisName(record.TypeIndex))) {
			foreach (int chassis in MachineFitter.Chassis) {
				if (ImGui.Selectable(ChassisName(chassis), chassis == record.TypeIndex) && chassis != record.TypeIndex) {
					_fitter.RequestChassis(slot, chassis);
				}
			}

			ImGui.EndCombo();
		}

		EditorText.Note("A new chassis comes with its stock fit.");
		int capacity = MachineFitter.Capacity(record.TypeIndex);
		for (int mount = 0; mount < capacity && mount < record.WeaponRefs.Length; mount++) {
			DrawMount(slot, record.TypeIndex, mount, record.WeaponRefs[mount],
				mount < record.WeaponSecondary.Length ? record.WeaponSecondary[mount] : (short)ShellWeaponUnit.NoGuidance);
		}

		ImGui.EndDisabled();
		ImGui.PopID();
		EditorText.Reason(reason);
	}

	// Why the last fit, add or remove could not be made.
	private void Failures() {
		if (_fitter.Failure is { } failure) {
			ImGui.TextWrapped($"Not changed: {failure}");
		}

		if (_editor.Failure is { } refused) {
			ImGui.TextWrapped($"Not changed: {refused}");
		}
	}

	// One mount: the weapons its socket takes, and for a launcher the guidance kinds.
	private void DrawMount(int slot, int chassis, int mount, short weapon, short guidance) {
		ImGui.PushID(mount);
		ImGui.AlignTextToFramePadding();
		ImGui.Text($"Slot {mount}:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		if (ImGui.BeginCombo("##weapon", WeaponName(weapon))) {
			foreach (int option in _fitter.WeaponsFor(chassis, mount)) {
				if (ImGui.Selectable(WeaponName(option), option == weapon) && option != weapon) {
					_fitter.RequestWeapon(slot, mount, option);
				}
			}

			ImGui.EndCombo();
		}

		if (ShellWeaponsScreen.TakesGuidance(weapon)) {
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(GuidanceWidth));
			if (ImGui.BeginCombo("##guidance", GuidanceName(guidance))) {
				foreach (int kind in ShellWeaponsScreen.GuidanceKinds) {
					if (ImGui.Selectable(GuidanceName(kind), kind == guidance) && kind != guidance) {
						_fitter.RequestGuidance(slot, mount, kind);
					}
				}

				ImGui.EndCombo();
			}
		}

		ImGui.PopID();
	}

	private static string ChassisName(int chassis) => HercLUT.GetById((short)chassis)?.Name ?? $"type {chassis}";

	private static string WeaponName(int weapon) => weapon == -1 ? "(no slot)" : WeaponLUT.GetById(weapon)?.Name ?? $"weapon {weapon}";

	// A launcher left at the filler, as a stock fit leaves the Razor's, reads "Default".
	private static string GuidanceName(int kind) =>
		kind == ShellWeaponUnit.NoGuidance ? "Default" : MissileType.GetById(kind)?.Abbrev ?? $"kind {kind}";
}
