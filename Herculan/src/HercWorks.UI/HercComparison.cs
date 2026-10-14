using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Core.Io.Transform.Shell;

namespace HercWorks.UI;

/// <summary>
/// One chassis' gameplay figures for the Compare Hercs screen, read from its three SIMVOL0 files:
/// <c>dat\NAME.DAT</c> (<see cref="HercSimDat"/>), <c>dmg\NAME.DMG</c> (<see cref="HercSimDamage"/>)
/// and <c>gl\NAME.GL</c> (<see cref="GunLayout"/>), and for the nine chassis the shell builds, its
/// price tables (<see cref="ChassisPrices"/>). A figure is null when its file is missing or does not
/// parse.
/// </summary>
public sealed class HercComparison {
	/// <summary>
	/// The first weapon-mount component. Components 19-28 are placeholders a fitted weapon replaces
	/// with its own piece, so they are left out of <see cref="Armor"/> — see
	/// docs/retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece.
	/// </summary>
	public const int FirstMountComponent = 19;

	public required HercLUT Herc { get; init; }

	/// <summary>
	/// <c>gam\herc_inf.dat</c>'s price, in tons — what the Herc Construction screen charges
	/// (docs/retail/formats/herc-catalogs.md#gamherc_infdat--the-chassis-stat-table). Null for a
	/// chassis the shell does not build.
	/// </summary>
	public int? ScrapCostTons { get; init; }

	/// <summary>
	/// What scrapping the chassis yields built, at full condition and with nothing fitted, in tons:
	/// <c>Herc_ScrapValueTons</c> (<c>0041140f</c>) of <see cref="ScrapValueKilograms"/>.
	/// </summary>
	public int? SellValueTons { get; init; }

	/// <summary>
	/// The cockpit readout at full forward throttle, in km/h. A walker's is
	/// <c>315 × SpeedForward / 1024</c> truncated, a flyer's its <see cref="HercSimDat.HudTopSpeed"/>
	/// as stated — docs/retail/simulation/mech-locomotion.md#load-time-speed-rescale.
	/// </summary>
	public int? TopSpeedKph { get; init; }

	/// <inheritdoc cref="HercSimDat.ShieldMaxTotal"/>
	public int? ShieldCapacity { get; init; }

	/// <summary>
	/// What the reactor adds to the Master Energy Pool a second: <c>Mech_ComputeReactorRate</c>'s
	/// literal 20, integrated over a tick at the 25 Hz timestep, <c>Q8(81, 20)</c> = 6 a tick. It
	/// reads nothing of the chassis, so every machine has it; an Energy Pod doubles it
	/// (docs/retail/simulation/reactor-energy-pool.md#reactor-output-rate--mech_computereactorrate-00417d08).
	/// </summary>
	public const int ReactorOutputPerSecond = (WeaponComparison.TickDelta * ReactorRate >> 8) * WeaponComparison.TicksPerSecond;

	/// <summary><c>Mech_ComputeReactorRate</c>'s base rate, per 125 ms.</summary>
	private const int ReactorRate = 20;

	/// <summary>The chassis' hardpoint count, the <c>.GL</c> file's record count.</summary>
	public int? Hardpoints { get; init; }

	/// <summary>The summed armour of the chassis' own components, 0-18; mounts excluded.</summary>
	public int? Armor => ComponentArmor?.Sum(armor => (int)armor);

	/// <summary>The summed maxima of every internal system the <c>.DMG</c> lists.</summary>
	public int? Internals => InternalMaxima?.Sum(maximum => (int)maximum);

	/// <summary>
	/// Each of the chassis' own components' armour, by component index (0-18, fewer when the
	/// <c>.DMG</c> lists fewer). The index space is docs/retail/formats/dmg-damage-file.md#the-two-index-spaces.
	/// </summary>
	public short[]? ComponentArmor { get; init; }

	/// <summary>Each internal system's maximum, by internal index.</summary>
	public short[]? InternalMaxima { get; init; }

	/// <summary>
	/// The type record's flyer flag: the RAZOR, whose components and internals the game names from
	/// <c>STRINGS0.STR</c>'s flyer groups rather than the walker ones.
	/// </summary>
	public bool IsFlyer { get; init; }

	/// <summary>Component <paramref name="index"/>'s armour, or null when the chassis has no such component.</summary>
	public int? ComponentArmorAt(int index) =>
		ComponentArmor is { } armor && index < armor.Length ? armor[index] : null;

	/// <summary>Internal <paramref name="index"/>'s maximum, or null when the chassis has no such slot.</summary>
	public int? InternalMaximumAt(int index) =>
		InternalMaxima is { } maxima && index < maxima.Length ? maxima[index] : null;

	/// <summary>Files that were not found or did not parse, for the status line.</summary>
	public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

	/// <summary>Display name: <see cref="HercLUT.Name"/>, with the all-caps enemy names title-cased.</summary>
	public string DisplayName => Herc.Name.Any(char.IsLower)
		? Herc.Name
		: System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Herc.Name.ToLowerInvariant());

	/// <summary>The shell's two price tables, <c>gam\herc_inf.dat</c> and <c>gam\damage.dat</c>, read once for every chassis.</summary>
	public sealed record ChassisPrices(HercInf? Chassis, DamageRepairCost? Values) {
		/// <summary>Reads both, adding any that is not found or does not parse to <paramref name="missing"/>.</summary>
		public static ChassisPrices Load(List<string> missing) => new(
			GamePaths.ReadOrReport(new HercInfoTransformer(), GamePaths.FindGamFile, "gam", "HERC_INF.DAT", missing),
			GamePaths.ReadOrReport(new DamageRepairCostTransformer(), GamePaths.FindGamFile, "gam", "DAMAGE.DAT", missing));
	}

	public static HercComparison Load(HercLUT herc, ChassisPrices prices) {
		var missing = new List<string>();
		string stem = herc.AbbrevDat;

		var dat = ReadSim(new HercSimDataTransformer(), "dat", stem + ".DAT", missing);
		var dmg = ReadSim(new HercDamageFileTransformer(), "dmg", stem + ".DMG", missing);
		var gl = ReadSim(new GunLayoutTransformer(), "gl", stem + ".GL", missing);
		int? price = herc.IsPlayerChassis
			? prices.Chassis?.Data.FirstOrDefault(entry => entry?.HercId == herc.Id)?.SalvageReq
			: null;

		return new HercComparison {
			Herc = herc,
			ScrapCostTons = price,
			SellValueTons = price is { } tons && prices.Values is { } values
				? ScrapValueKilograms(tons, values) / DamageRepairCost.KilogramsPerTon
				: null,
			TopSpeedKph = dat == null ? null : dat.FlyerFlag != 0 ? dat.HudTopSpeed : (315 * dat.SpeedForward) >> 10,
			ShieldCapacity = dat?.ShieldMaxTotal,
			Hardpoints = gl?.TotalGuns,
			ComponentArmor = dmg?.ComponentData?.Take(FirstMountComponent).Select(piece => piece.Armor).ToArray(),
			InternalMaxima = dmg?.Internals?.Select(system => system.Armor).ToArray(),
			IsFlyer = dat is { FlyerFlag: not 0 },
			Missing = missing
		};
	}

	/// <summary>
	/// <c>Herc_ScrapValue</c> (<c>00413b50</c>) for a built machine at condition 100 throughout
	/// (docs/retail/shell/armory.md#scrapping): every external group and internal yields its whole
	/// unit value, and a mount slot at 100 is one <c>Herc_StripMounts</c> returns to stock, so it
	/// yields nothing here.
	/// </summary>
	private static int ScrapValueKilograms(int priceTons, DamageRepairCost values) =>
		Enumerable.Range(0, DamageRepairCost.ExternalGroupCount).Sum(group => values.ExternalGroupValue(priceTons, group))
		+ Enumerable.Range(0, DamageRepairCost.InternalCount).Sum(component => values.InternalValue(priceTons, component));

	private static T? ReadSim<T>(Core.Io.Transform.ByteTransformer<T> transformer, string folder, string fileName,
			List<string> missing) where T : class =>
		GamePaths.ReadOrReport(transformer, name => GamePaths.FindSimFile(folder, name), folder, fileName, missing);
}
