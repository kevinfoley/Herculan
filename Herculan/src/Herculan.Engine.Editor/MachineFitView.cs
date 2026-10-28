using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A herc's chassis choice and one row per hardpoint, each a weapon choice and, for a launcher, a guidance choice, edited
/// through <see cref="MachineFitter"/>: the squad's HERCs in <see cref="SquadMachinePanel"/>, and a herc outside the squad
/// in <see cref="RosterFitView"/>. A hardpoint is a fit slot, numbered from 1 as the shell's repair screen numbers its
/// mounts (docs/retail/shell/weapons-and-repair.md#a-row-is-four-text-columns); a Human chassis's offer what its arming
/// layout lets them take, a Cybrid's what retail missions fit on them or, under Unrestricted, any HERC weapon
/// (<see cref="MachineFitter.WeaponsFor"/>). A hardpoint that offers nothing is drawn disabled with the reason. The chassis's
/// loadout presets are chosen under the chassis and saved and deleted under the hardpoints (<see cref="LoadoutPresetView"/>).
/// </summary>
internal sealed class MachineFitView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float WeaponWidth = 150f;
	private const float GuidanceWidth = 70f;

	private readonly MachineFitter _fitter;
	private readonly LoadoutPresetView _presets;

	public MachineFitView(MachineFitter fitter, LoadoutPresetView presets) {
		_fitter = fitter;
		_presets = presets;
	}

	private bool Unrestricted => _fitter.CybridUnrestricted;

	/// <summary>
	/// Draws the chassis choice and the hardpoints of the herc of row-12 GUID <paramref name="guid"/>:
	/// <paramref name="chassis"/> offered under <paramref name="name"/>, <paramref name="note"/> under the choice; given
	/// <paramref name="option"/>, the template of the option being edited, each field as a load picking it makes it, and each
	/// edit of a field a template supplies written there (<see cref="MachineFitter.Fit"/>).
	/// </summary>
	public void Draw(short guid, IEnumerable<int> chassis, Func<int, string> name, string note, MsnRecordId? option = null) {
		if (_fitter.Fit(guid, option) is not { } record) {
			return;
		}

		string? reason = _fitter.WhyFixed(guid);
		int type = record.Type;
		ImGui.PushID(guid);
		ImGui.BeginDisabled(reason != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		if (EditorText.BeginCombo("Chassis", name(type))) {
			foreach (int choice in chassis) {
				if (ImGui.Selectable(name(choice), choice == type) && choice != type) {
					_fitter.RequestChassis(guid, choice, option);
				}
			}

			ImGui.EndCombo();
		}

		_presets.DrawChoice(guid, type, record.Weapons, record.Ammo, ScaledImGui.Scaled(WeaponWidth), option);
		EditorText.Note(note);
		var mounts = _fitter.Mounts(type);
		bool cybrid = HercGroups.IsCybrid(type);
		if (cybrid && (mounts.Count == 0 || !Unrestricted)) {
			EditorText.Note(mounts.Count == 0
				? "It has no hardpoints, so it carries no weapon."
				: "Each hardpoint offers only weapons used on that hardpoint in vanilla.");
		}

		var fixedMounts = new List<(int Mount, string Reason)>();
		foreach (int mount in mounts) {
			short weapon = mount < record.Weapons.Length ? record.Weapons[mount] : (short)-1;
			short guidance = mount < record.Ammo.Length ? record.Ammo[mount] : RosterFit.Filler;
			string? noWeapons = _fitter.WhyNoWeapons(type, mount);
			if (noWeapons != null) {
				fixedMounts.Add((mount, noWeapons));
			}

			DrawMount(guid, type, mount, weapon, guidance, noWeapons != null, cybrid, option);
		}

		ImGui.EndDisabled();
		ImGui.PopID();
		if (fixedMounts.Count > 0 && fixedMounts.Count == mounts.Count && fixedMounts.All(item => item.Reason == fixedMounts[0].Reason)) {
			EditorText.Note(fixedMounts[0].Reason);
		} else {
			foreach (var (mount, why) in fixedMounts) {
				EditorText.Note($"{Hardpoint(mount)}: {why}");
			}
		}

		_presets.DrawButtons(guid, type, name(type), record.Weapons, record.Ammo, option, reason != null);
		EditorText.Reason(reason);
	}

	/// <summary>A chassis name as the shell's chassis table gives it, for the squad's HERCs.</summary>
	public static string ChassisName(int chassis) => HercLUT.GetById((short)chassis)?.Name ?? $"type {chassis}";

	// One hardpoint: the weapons it offers, and for a launcher the guidance kinds. A Cybrid's weapon the hardpoint does not
	// offer, under Vanilla one no retail mission fits there, is listed first, marked, as the current choice.
	private void DrawMount(short guid, int chassis, int mount, short weapon, short guidance, bool fixedWeapon, bool cybrid,
			MsnRecordId? option) {
		ImGui.PushID(mount);
		ImGui.AlignTextToFramePadding();
		ImGui.Text($"{Hardpoint(mount)}:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		var options = _fitter.WeaponsFor(chassis, mount).ToList();
		bool foreign = cybrid && !fixedWeapon && !options.Contains(weapon);
		string marker = Unrestricted ? " (not offered here)" : " (not from a retail mission)";
		string current = foreign ? WeaponName(weapon) + marker : WeaponName(weapon);
		// A hardpoint whose only offer is what it holds, such as one retail missions always leave empty, has no choice to make.
		bool onlyChoice = !foreign && options.All(choice => choice == weapon);
		ImGui.BeginDisabled(fixedWeapon || onlyChoice);
		if (EditorText.BeginCombo("##weapon", current)) {
			if (foreign) {
				ImGui.Selectable(current, true);
			}

			foreach (int choice in options) {
				if (ImGui.Selectable(WeaponName(choice), choice == weapon) && choice != weapon) {
					_fitter.RequestWeapon(guid, mount, choice, option);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (ShellWeaponsScreen.TakesGuidance(weapon)) {
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(GuidanceWidth));
			if (EditorText.BeginCombo("##guidance", GuidanceName(guidance))) {
				foreach (int kind in ShellWeaponsScreen.GuidanceKinds) {
					if (ImGui.Selectable(GuidanceName(kind), kind == guidance) && kind != guidance) {
						_fitter.RequestGuidance(guid, mount, kind, option);
					}
				}

				ImGui.EndCombo();
			}
		}

		ImGui.PopID();
	}

	// Fit slot <mount>, numbered from 1.
	private static string Hardpoint(int mount) => $"Hardpoint {mount + 1}";

	// No weapon (0), or none in a slot the record leaves unset, which builds no mount either, reads "(empty)".
	private static string WeaponName(int weapon) => weapon <= 0 ? "(empty)" : WeaponLUT.GetById(weapon)?.Name ?? $"weapon {weapon}";

	// A launcher left at the filler, as a stock fit leaves the Razor's, reads "Default".
	private static string GuidanceName(int kind) =>
		kind == ShellWeaponUnit.NoGuidance ? "Default" : MissileType.GetById(kind)?.Abbrev ?? $"kind {kind}";
}
