using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/INI_[herc].DAT — the stock weapon fit a practice mission's player machine is
/// built with when the practice screen chooses its chassis, one file per type. A bare HERC catalog record (<see cref="ShellHercData"/>) with
/// no leading hangar slot: type, build percent (100), build missions remaining (0), hardpoint count,
/// then per occupied hardpoint its index and a weapon unit (id, condition, ammo type; 5 = none).
///
/// <para>Hardpoints serialize sparsely, each preceded by its index, so the count is authoritative
/// and the indices need not be contiguous: INI_APOC fits 0-5 and 7. See
/// docs/formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis.</para>
/// </summary>
public class InitHerc {
	/// <summary>
	/// Source file name and directory, and the file's own bytes.
	/// <see cref="Io.Read.DatFileReader.ParseIniHercDatStats"/> builds an InitHerc from a VOL entry's
	/// name/path and then walks <see cref="RawBytes"/> to fill in the hardpoint table.
	/// </summary>
	public string? FileName { get; set; }

	/// <inheritdoc cref="FileName"/>
	public string? GameDirPath { get; set; }

	/// <inheritdoc cref="FileName"/>
	public byte[]? RawBytes { get; set; }

	/// <summary>
	/// Not part of the format: retail <c>INI_*.DAT</c> files carry no such prefix, and neither
	/// <see cref="Io.Read.DatFileReader.ParseIniHercDatStats"/> nor
	/// <see cref="Io.Transform.Shell.InitHercTransformer"/> uses it.
	/// </summary>
	public static readonly byte[] Header = { 0x66, 0x1F, 0xAF, 0x55 };

	public ShellHercData? Data { get; set; }

	public InitHerc() { }

	public InitHerc(string fileName, string dirPath) {
		FileName = fileName;
		GameDirPath = dirPath;
	}
}
