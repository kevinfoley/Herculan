using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Dbsim;

namespace HercWorks.UI;

/// <summary>
/// One chassis' gameplay figures for the Compare Hercs screen, read from its three SIMVOL0 files:
/// <c>dat\NAME.DAT</c> (<see cref="HercSimDat"/>), <c>dmg\NAME.DMG</c> (<see cref="HercSimDamage"/>)
/// and <c>gl\NAME.GL</c> (<see cref="GunLayout"/>). A figure is null when its file is missing or
/// does not parse.
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
	/// The cockpit readout at full forward throttle, in km/h. A walker's is
	/// <c>315 × SpeedForward / 1024</c> truncated, a flyer's its <see cref="HercSimDat.HudTopSpeed"/>
	/// as stated — docs/retail/simulation/mech-locomotion.md#load-time-speed-rescale.
	/// </summary>
	public int? TopSpeedKph { get; init; }

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

	public static HercComparison Load(HercLUT herc) {
		var missing = new List<string>();
		string stem = herc.AbbrevDat;

		var dat = Read(new HercSimDataTransformer(), "dat", stem + ".DAT", missing);
		var dmg = Read(new HercDamageFileTransformer(), "dmg", stem + ".DMG", missing);
		var gl = Read(new GunLayoutTransformer(), "gl", stem + ".GL", missing);

		return new HercComparison {
			Herc = herc,
			TopSpeedKph = dat == null ? null : dat.FlyerFlag != 0 ? dat.HudTopSpeed : (315 * dat.SpeedForward) >> 10,
			Hardpoints = gl?.TotalGuns,
			ComponentArmor = dmg?.ComponentData?.Take(FirstMountComponent).Select(piece => piece.Armor).ToArray(),
			InternalMaxima = dmg?.Internals?.Select(system => system.Armor).ToArray(),
			IsFlyer = dat is { FlyerFlag: not 0 },
			Missing = missing
		};
	}

	private static T? Read<T>(Core.Io.Transform.ByteTransformer<T> transformer, string folder, string fileName,
		List<string> missing) where T : class {
		try {
			if (GamePaths.FindSimFile(folder, fileName) is { } file && transformer.Parse(file.Content) is { } parsed) {
				return parsed;
			}
		} catch (Exception) {
			// A truncated or malformed file is reported the same way as an absent one.
		}

		missing.Add($@"{folder}\{fileName}");
		return null;
	}
}
