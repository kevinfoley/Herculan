namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// FILE - ES1's <c>bnd\CAM.BND</c> in SIMVOL0.VOL: 20 bytes, a dword and eight <c>int16</c>. Each <c>int16</c> is a
/// constant of ES1's camera code, in the order below; the simulator was not found to read the file
/// (docs/es1/formats/bnd-notes.md#bndcambnd). Read by <see cref="Io.Transform.Bnd.Es1CamTransformer"/>.
/// </summary>
public class Es1Cam {
	/// <summary>Offset 0: read as seconds since 1970, a date near the file's own (docs/es1/formats/bnd-notes.md#the-first-dword).</summary>
	public uint Stamp { get; set; }

	/// <summary>Offset 4: the orbit's least distance. 2500.</summary>
	public short MinOrbitDistance { get; set; }

	/// <summary>Offset 6: the orbit's greatest distance. 30000.</summary>
	public short MaxOrbitDistance { get; set; }

	/// <summary>Offset 8: the Q8 scale from a steering axis to an angle rate. <c>0x800</c>.</summary>
	public short AngleRateScale { get; set; }

	/// <summary>Offset 10: the most an angle rate moves in a frame. <c>0xc0</c>.</summary>
	public short AngleRateStep { get; set; }

	/// <summary>Offset 12: the Q8 scale from the throttle axis to the zoom rate or speed. <c>0x400</c>.</summary>
	public short ZoomRateScale { get; set; }

	/// <summary>Offset 14: the most the zoom rate or speed moves in a frame. <c>0x50</c>.</summary>
	public short ZoomRateStep { get; set; }

	/// <summary>Offset 16: the pitch limit, either way. <c>0x3000</c>.</summary>
	public short PitchLimit { get; set; }

	/// <summary>Offset 18: the eye's least height. 100.</summary>
	public short MinEyeHeight { get; set; }
}
