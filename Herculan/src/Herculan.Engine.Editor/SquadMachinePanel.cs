using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of the player's machine, under the player's own object and group 0: which Herc the player
/// pilots in this mission, and the mission's group-0 machine as a chassis choice and one row per mount the chassis has, each
/// edited through <see cref="MachineFitter"/>.
/// </summary>
internal sealed class SquadMachinePanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float WeaponWidth = 150f;
	private const float GuidanceWidth = 70f;

	private readonly MachineFitter _fitter;
	private readonly MissionSquad? _squad;

	/// <param name="squad">Which Hercs the mission's squad pilots, or null for a plain <c>script.dat</c>.</param>
	public SquadMachinePanel(MachineFitter fitter, MissionSquad? squad) {
		_fitter = fitter;
		_squad = squad;
	}

	/// <summary>The player's machine: which Herc the player pilots, then group 0's first member's record.</summary>
	public void DrawPlayer() {
		ImGui.Separator();
		ImGui.Text("Player's machine");
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
		PlayerMachineSource.CareerSquad => "This is a campaign mission: the player pilots their own Herc from the career's "
			+ "squad (DATA\\player.mec), not the Herc below.",
		_ => null,
	};

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
		if (reason != null) {
			EditorText.Note(reason);
		}

		if (_fitter.Failure is { } failure) {
			ImGui.TextWrapped($"Not changed: {failure}");
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
