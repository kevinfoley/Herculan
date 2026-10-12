
namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// FILE - SIMVOL0\BND\CAM.BND: 24 content bytes, a dword and ten <c>int16</c>, each <c>int16</c> a constant of the
/// camera code in DBSIM. <c>.BND</c> is a build-time source format DBSIM never opens. Values below are the retail
/// file's. See <c>docs/retail/formats/bnd-notes.md#cambnds-full-24-byte-record</c>.
/// </summary>
public class Cam {
	/// <summary>Offset 0: read as seconds since 1970, a date in February 1996 (docs/retail/formats/bnd-notes.md#the-first-dword).</summary>
	public uint Stamp { get; set; }

	/// <summary>Offset 4: the orbit's least distance. 2500.</summary>
	public short MinOrbitDistance { get; set; }

	/// <summary>Offset 6: the orbit's greatest distance. 30000.</summary>
	public short MaxOrbitDistance { get; set; }

	/// <summary>Offset 8: the Q8 scale from a steering axis to an angle rate. <c>0x800</c>.</summary>
	public short AngleRateScale { get; set; }

	/// <summary>Offset 10: the most an angle rate moves in a frame. <c>0xc0</c>.</summary>
	public short AngleRateStep { get; set; }

	/// <summary>Offset 12: the Q8 scale from the throttle axis to the zoom rate or the free camera's speed. <c>0x400</c>.</summary>
	public short ZoomRateScale { get; set; }

	/// <summary>Offset 14: the most the zoom rate or speed moves in a frame. <c>0x50</c>.</summary>
	public short ZoomRateStep { get; set; }

	/// <summary>Offset 16: the pitch limit, either way. <c>0x3000</c>.</summary>
	public short PitchLimit { get; set; }

	/// <summary>Offset 18: the eye's least height over the ground. 550.</summary>
	public short MinEyeHeight { get; set; }

	/// <summary>Offset 20: the free camera's allowance for the ground's slope. 500.</summary>
	public short SlopeAllowance { get; set; }

	/// <summary>Offset 22: how far behind the player's path the chase camera sits. 8000.</summary>
	public short ChaseDelay { get; set; }
}
