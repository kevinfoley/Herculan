using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A herc's chassis choice and one row per mount, each a weapon choice and, for a launcher, a guidance choice, edited through
/// <see cref="MachineFitter"/>: the squad's machines in <see cref="SquadMachinePanel"/>, and a herc outside the squad in
/// <see cref="RosterFitView"/>. A mount offers what the chassis's arming layout lets it take
/// (<see cref="MachineFitter.WeaponsFor"/>), so only the nine Human chassis have rows; a Cybrid's fit can be shown as it is.
/// </summary>
internal sealed class MachineFitView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float WeaponWidth = 150f;
	private const float GuidanceWidth = 70f;

	private readonly MachineFitter _fitter;

	public MachineFitView(MachineFitter fitter) => _fitter = fitter;

	/// <summary>
	/// Draws the chassis choice and the fit of the record at block-7 slot <paramref name="slot"/>: <paramref name="chassis"/>
	/// offered under <paramref name="name"/>, <paramref name="note"/> under the choice, and each edit written to the
	/// alternative the herc draws the field from when <paramref name="alternative"/>. With <paramref name="showFixed"/>, a
	/// chassis with no arming layout lists its fitted slots as they are, with a note.
	/// </summary>
	public void Draw(int slot, IEnumerable<int> chassis, Func<int, string> name, string note, bool alternative = false,
			bool showFixed = false) {
		if (_fitter.Record(slot) is not { } record) {
			return;
		}

		string? reason = _fitter.WhyFixed(slot);
		ImGui.PushID(slot);
		ImGui.BeginDisabled(reason != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		if (ImGui.BeginCombo("Chassis", name(record.TypeIndex))) {
			foreach (int option in chassis) {
				if (ImGui.Selectable(name(option), option == record.TypeIndex) && option != record.TypeIndex) {
					_fitter.RequestChassis(slot, option, alternative);
				}
			}

			ImGui.EndCombo();
		}

		EditorText.Note(note);
		int capacity = MachineFitter.Capacity(record.TypeIndex);
		for (int mount = 0; mount < capacity && mount < record.WeaponRefs.Length; mount++) {
			DrawMount(slot, record.TypeIndex, mount, record.WeaponRefs[mount],
				mount < record.WeaponSecondary.Length ? record.WeaponSecondary[mount] : (short)ShellWeaponUnit.NoGuidance, alternative);
		}

		ImGui.EndDisabled();
		if (showFixed && !_fitter.HasLayout(record.TypeIndex)) {
			DrawFixedFit(record.WeaponRefs, record.WeaponSecondary);
		}

		ImGui.PopID();
		EditorText.Reason(reason);
	}

	/// <summary>A chassis name as the shell's chassis table gives it, for the squad's machines.</summary>
	public static string ChassisName(int chassis) => HercLUT.GetById((short)chassis)?.Name ?? $"type {chassis}";

	// One mount: the weapons its socket takes, and for a launcher the guidance kinds.
	private void DrawMount(int slot, int chassis, int mount, short weapon, short guidance, bool alternative) {
		ImGui.PushID(mount);
		ImGui.AlignTextToFramePadding();
		ImGui.Text($"Slot {mount}:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(WeaponWidth));
		if (ImGui.BeginCombo("##weapon", WeaponName(weapon))) {
			foreach (int option in _fitter.WeaponsFor(chassis, mount)) {
				if (ImGui.Selectable(WeaponName(option), option == weapon) && option != weapon) {
					_fitter.RequestWeapon(slot, mount, option, alternative);
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
						_fitter.RequestGuidance(slot, mount, kind, alternative);
					}
				}

				ImGui.EndCombo();
			}
		}

		ImGui.PopID();
	}

	// A fit no arming layout covers, read-only: each slot holding a weapon, and a launcher's guidance.
	private static void DrawFixedFit(short[] weapons, short[] guidance) {
		bool any = false;
		for (int mount = 0; mount < weapons.Length; mount++) {
			if (weapons[mount] < 0) {
				continue;
			}

			any = true;
			string kind = ShellWeaponsScreen.TakesGuidance(weapons[mount]) && mount < guidance.Length
				? $" ({GuidanceName(guidance[mount])})"
				: "";
			ImGui.BulletText($"Slot {mount}: {WeaponName(weapons[mount])}{kind}");
		}

		if (!any) {
			EditorText.Note("No weapon.");
		}

		EditorText.Note("Which weapons a Cybrid's mounts take is not known, so its weapons are not changed here.");
	}

	private static string WeaponName(int weapon) => weapon == -1 ? "(no slot)" : WeaponLUT.GetById(weapon)?.Name ?? $"weapon {weapon}";

	// A launcher left at the filler, as a stock fit leaves the Razor's, reads "Default".
	private static string GuidanceName(int kind) =>
		kind == ShellWeaponUnit.NoGuidance ? "Default" : MissileType.GetById(kind)?.Abbrev ?? $"kind {kind}";
}
