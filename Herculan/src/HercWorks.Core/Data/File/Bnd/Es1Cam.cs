namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// FILE - ES1's <c>bnd\CAM.BND</c> in SIMVOL0.VOL: ES2's <see cref="Cam"/> less its last two fields, 20 bytes, each
/// <c>int16</c> a constant of ES1's camera code; the simulator was not found to read the file
/// (docs/es1/formats/bnd-notes.md#bndcambnd). Read by <see cref="Io.Transform.Bnd.Es1CamTransformer"/>.
/// </summary>
public class Es1Cam {
	/// <summary>Offset 0: read as seconds since 1970, a date near the file's own (docs/es1/formats/bnd-notes.md#the-first-dword).</summary>
	public uint Stamp { get; set; }

	/// <inheritdoc cref="Cam.MinOrbitDistance"/>
	public short MinOrbitDistance { get; set; }

	/// <inheritdoc cref="Cam.MaxOrbitDistance"/>
	public short MaxOrbitDistance { get; set; }

	/// <inheritdoc cref="Cam.AngleRateScale"/>
	public short AngleRateScale { get; set; }

	/// <inheritdoc cref="Cam.AngleRateStep"/>
	public short AngleRateStep { get; set; }

	/// <inheritdoc cref="Cam.ZoomRateScale"/>
	public short ZoomRateScale { get; set; }

	/// <inheritdoc cref="Cam.ZoomRateStep"/>
	public short ZoomRateStep { get; set; }

	/// <inheritdoc cref="Cam.PitchLimit"/>
	public short PitchLimit { get; set; }

	/// <summary>Offset 18: the eye's least height, over ES1's flat ground at height 0. 100.</summary>
	public short MinEyeHeight { get; set; }
}
