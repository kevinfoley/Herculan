using HercWorks.Core.Data.File;
﻿using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// One row of the cockpit's weapon panel, as the three weapon-gauge classes draw it. A row is a
/// <c>.GAU</c> weapon slot, and which mount owns it is the mount's own
/// <see cref="WeaponMount.GaugeSlot"/> — the <c>.GL</c> record's byte at <c>+7</c>, not its position
/// in the fit. A slot no mount claims draws its plate and number and nothing else.
///
/// <para>The three classes differ only past the name: an energy mount's row
/// (<c>EnergyWeaponGauge_Ctor</c>, <c>00440a68</c>) carries an LED charge bar, an ammunition mount's (<c>AmmoWeaponGauge_Ctor</c>, <c>00440f78</c>)
/// carries a round count, and a pod's (<c>PodGauge_Ctor</c>, <c>00441524</c>) carries neither and instead widens its
/// name label across the whole row.</para>
/// </summary>
/// <param name="Name">
/// What the row prints — see <see cref="Build"/> for the pod suffix and the destroyed-mount
/// substitution.
/// </param>
/// <param name="Kind">Which gauge class this row is.</param>
/// <param name="Selected">
/// Whether this row draws as armed — the row plate lights and its text brightens. Both halves of a
/// linked pair draw armed together; see <see cref="WeaponMounts.IsArmedRow"/>.
/// </param>
/// <param name="InGroup">Whether the mount is in the current fire group. With <paramref name="Selected"/>
/// clear too, the state box is not drawn at all — which is what a pod's row always looks like.</param>
/// <param name="Ready">Whether the mount could fire right now — <c>WeaponMounts_MountIsReady</c>,
/// which is the mount's own readiness, its link partner's, and whether the selected target is inside
/// the weapon's engagement window. The state box takes its green frame from this and its red one
/// from the absence of it, so a weapon that cannot reach the target reddens here and is stepped past
/// by the chain; see <see cref="WeaponMounts.CanFireNow"/>.</param>
/// <param name="Rounds">Rounds remaining, for an ammunition row.</param>
/// <param name="ChargeMeter">
/// An energy row's bar value over the LED bar's own 0-1024 range. A full capacitor reads about
/// four-fifths of the bar, not all of it — see <see cref="WeaponMount.EnergyChargeScale"/>.
/// </param>
/// <param name="ChargeBar">
/// Whether this row's value field holds an LED bar reading <paramref name="ChargeMeter"/>. True for
/// the energy and ELF rows, whose gauge builds one, and for the <b>Turbo Pod's</b> row, which is the
/// one pod row with a value field at all — <c>TurboPodGauge_Ctor</c> adds a bar over the pod's charge
/// and pulls the name label back out of its way. Every other pod row widens its name across both
/// fields and has nothing to draw there.
/// </param>
/// <param name="PodButton">
/// Whether this pod row's on/off button is on — <c>gauge+0xc2</c>, which only the ECM and Turbo rows
/// ever have moved. It is what <c>PodGauge_Paint</c> (<c>0044171c</c>), the pod row's paint, re-dresses the name on: off
/// draws it in the <c>gray</c> font the constructor seeded over the plate's own background, and on
/// draws it in <c>dark</c> over a green plate flooded across the whole name label. Always false on a
/// row that is not a pod, and on a destroyed one.
/// </param>
/// <param name="Powered">
/// Whether the cockpit's power-up has reached this row yet. A row it has not is not drawn at all —
/// see <see cref="CockpitPowerUp.RowPowered"/>.
/// </param>
/// <param name="DropoutHidden">
/// Whether the sensor dropout is holding the row back — dark, or with its wipe running. Every paint
/// the row has but the plate's bezel tests this and draws nothing; see <see cref="SensorDropout.Hidden"/>.
/// </param>
/// <param name="DropoutFrame">
/// The <see cref="SensorDropout.RowBank"/> frame standing in the plate's hole meanwhile.
/// </param>
public readonly record struct WeaponRowState(
	string Name,
	WeaponMountKind Kind,
	bool Selected,
	bool InGroup,
	bool Ready,
	int Rounds,
	int ChargeMeter,
	bool ChargeBar = false,
	bool PodButton = false,
	bool Powered = true,
	bool DropoutHidden = false,
	int? DropoutFrame = null) {

	/// <summary>A <c>.GAU</c> slot with no mount on it.</summary>
	public static WeaponRowState Empty { get; } =
		new(string.Empty, WeaponMountKind.None, false, false, false, 0, 0);

	/// <summary>The longest name a weapon gauge's own buffer holds — <c>strncpy(gauge+0xb1, name, 12)</c>.</summary>
	public const int NameLength = 12;

	/// <summary>
	/// A pod row's name buffer is one shorter, because <c>PodGauge_Ctor</c> (<c>00441524</c>) seeds it with a leading
	/// space and then appends with <c>strncat(dest, text, 11 - strlen(dest))</c>.
	/// </summary>
	public const int PodNameLength = 11;

	/// <summary>The space <c>PodGauge_Ctor</c> (<c>00441524</c>) seeds a pod row's name with, before the weapon's own name.</summary>
	public const string PodNamePrefix = " ";

	/// <summary>
	/// Which <c>STRINGS0.STR</c> group holds the label a destroyed mount's row prints in place of its
	/// name — the third registration in <c>SimStrings_LoadAll</c> (<c>00437598</c>), a single-entry
	/// group reading <c>OFFLINE</c>.
	/// </summary>
	public const int OfflineStringGroup = 2;

	/// <summary>
	/// And the fourth, also a single entry: the <c>" POD"</c> a pod row's name ends with. Both are
	/// string-table text rather than literals in the executable, which is why <c>SHIELD</c> reads
	/// <c>SHIELD POD</c> in the cockpit but plain <c>SHIELD</c> in the Heads-Down Display's weapon
	/// list.
	/// </summary>
	public const int PodSuffixStringGroup = 3;

	/// <summary>
	/// Lays out one machine's weapon panel: <paramref name="slots"/> rows, each filled by whichever
	/// mount claims it.
	///
	/// <para>Naming follows the gauge constructors exactly. An energy or ammunition row takes the
	/// mount's name as it is; a pod row is built as <c>" " + name</c> capped at
	/// <see cref="PodNameLength"/> and then <c>" POD"</c> appended into whatever room is left, so
	/// <c>SHIELD</c> becomes <c>" SHIELD POD"</c> and <c>ENERGY</c> becomes <c>" ENERGY POD"</c>
	/// exactly filling the buffer. The Turbo Pod is the exception: <c>TurboPodGauge_Ctor</c> overwrites
	/// that buffer with a plain <c>strncpy</c> of the mount name, so its row reads <c>TURBO</c> with
	/// neither the leading space nor the suffix — its value field is taken by the charge bar and there
	/// is no room for either. A destroyed mount prints <c>OFFLINE</c> instead of any of it.</para>
	/// </summary>
	/// <param name="mounts">The machine's mounts.</param>
	/// <param name="slots">How many weapon rows this herc's <c>.GAU</c> declares.</param>
	/// <param name="strings">
	/// <c>STRINGS0.STR</c>, for the two labels above. Without it a pod row prints its bare weapon name
	/// and a destroyed one prints nothing — neither is invented.
	/// </param>
	/// <param name="powerUp">
	/// The cockpit's power-up, which hides the rows it has not reached and holds the charge bars back
	/// while they fill. Null for a panel drawn as it stands.
	/// </param>
	/// <param name="coarseTicks">The current coarse tick, for <paramref name="powerUp"/>'s ramps.</param>
	/// <param name="dropouts">The cockpit's sensor dropouts, one per row, or null for none.</param>
	public static IReadOnlyList<WeaponRowState> Build(WeaponMounts mounts, int slots,
			StringFile? strings, CockpitPowerUp? powerUp = null, long coarseTicks = 0,
			CockpitDropouts? dropouts = null) {
		string offline = strings?.Text(OfflineStringGroup, 0) ?? string.Empty;
		string podSuffix = strings?.Text(PodSuffixStringGroup, 0) ?? string.Empty;

		var rows = new WeaponRowState[Math.Max(slots, 0)];
		for (int slot = 0; slot < rows.Length; slot++) {
			bool powered = powerUp?.RowPowered(slot) ?? true;
			int chargeCap = powerUp?.RowChargeCap(slot, coarseTicks) ?? int.MaxValue;
			if (mounts.BySlot(slot) is not { } mount) {
				rows[slot] = Empty with { Powered = powered };
				continue;
			}

			bool turbo = mount.WeaponId == MechPods.TurboPodWeaponId;

			string name = mount.Disabled
				? offline
				: mount.Kind != WeaponMountKind.Pod
					? Truncate(mount.Name, NameLength)
					: turbo
						? Truncate(mount.Name, PodNameLength)
						: PodName(mount.Name, podSuffix);

			rows[slot] = new WeaponRowState(
				name,
				mount.Kind,
				mounts.IsArmedRow(mount.MountIndex),
				mounts.InCurrentGroup(mount.MountIndex),
				mounts.CanFireNow(mount.MountIndex),
				mount.Rounds,
				// Each bar is scaled to the range its own gauge was built with, so both arrive here in
				// the LED bar's 0-1024 units. The Turbo's range is wider than its tank, so a full pod
				// reads four-fifths of a bar exactly as a charged energy weapon does. The power-up's
				// ramp clamps the value in the gauge's own units, before that scaling.
				turbo
					? (Math.Min(mount.Charge, chargeCap) << 10) / WeaponMount.TurboMeterRange
					: Math.Min(mount.ChargeMeterValue, chargeCap),
				ChargeBar: turbo || mount.Kind is WeaponMountKind.Energy or WeaponMountKind.Elf,
				// The destroyed byte at +0xc3 wins outright in the pod row's paint, which never reaches
				// the button at +0xc2 — so an offline pod's row draws neither the dark font nor the
				// green plate, whatever the button was left at.
				PodButton: mount.PodButton && !mount.Disabled,
				Powered: powered,
				DropoutHidden: dropouts?.Row(slot)?.Hidden ?? false,
				DropoutFrame: dropouts?.Row(slot)?.Frame);
		}

		return rows;
	}

	private static string PodName(string name, string suffix) {
		string built = Truncate(PodNamePrefix + name, PodNameLength);
		return built + Truncate(suffix, PodNameLength - built.Length);
	}

	private static string Truncate(string text, int length) =>
		length <= 0 ? string.Empty : text.Length <= length ? text : text[..length];
}
