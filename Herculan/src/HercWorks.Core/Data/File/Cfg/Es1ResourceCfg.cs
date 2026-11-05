namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// ES1's <c>resource.cfg</c>, the sound set-up <c>SOUNDSET.EXE</c> writes in the install folder, read with
/// <see cref="Io.Read.Es1Profile"/>: the names of the six values both programs take for their sound manager
/// (docs/es1/formats/sound-samples.md#the-driver, docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc).
/// The driver numbers and ports are written in hexadecimal, read with <see cref="Io.Read.Es1Profile.TryScanHex"/>.
/// </summary>
public static class Es1ResourceCfg {
	/// <summary>The file's name, in the install folder.</summary>
	public const string FileName = "resource.cfg";

	/// <summary>The digital sounds' section, with <see cref="Driver"/>, <see cref="Irq"/>, <see cref="Dma"/> and <see cref="Port"/>.</summary>
	public const string SoundFx = "SoundFX";

	/// <summary>The songs' section, with <see cref="Driver"/> and <see cref="Port"/>.</summary>
	public const string Music = "Music";

	/// <summary>The device number of the section's driver, hexadecimal; empty for none.</summary>
	public const string Driver = "Driver#";

	/// <summary>The card's interrupt line, decimal.</summary>
	public const string Irq = "IRQ";

	/// <summary>The card's DMA channel, decimal.</summary>
	public const string Dma = "DMA";

	/// <summary>The card's port address, hexadecimal.</summary>
	public const string Port = "PortAddress";
}
