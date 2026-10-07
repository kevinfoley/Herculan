
namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// FILE - SIMVOL0\BND\CAM.BND — 24 content bytes, every one mapped to a field, offset 0 first; what
/// the fields mean is open. <c>.BND</c> is a build-time source format DBSIM never opens. Values
/// below are the retail file's. See <c>docs/retail/formats/bnd-notes.md#cambnds-full-24-byte-record</c>.
/// </summary>
public class Cam {
	/// <summary>Offset 0, unknown. 54 (0x36).</summary>
	public byte Unknown0 { get; set; }

	/// <summary>Unknown. 208.</summary>
	public byte Unknown1 { get; set; }

	/// <summary>Unknown. 52.</summary>
	public byte Unknown2 { get; set; }

	/// <summary>Unknown. 49 (0x31, ASCII '1') — the same value at the same offset in MECH.BND and MECHSYS.BND.</summary>
	public byte Unknown3 { get; set; }

	/// <summary>Unknown <c>int16</c>. 2500.</summary>
	public short Distance1 { get; set; }

	/// <summary>Unknown <c>int16</c>. 30000.</summary>
	public short Distance2 { get; set; }

	/// <summary>0.</summary>
	public byte Blank1 { get; set; }

	/// <summary>Unknown. 8.</summary>
	public byte Unknown4 { get; set; }

	/// <summary>Unknown. 192.</summary>
	public byte Unknown5 { get; set; }

	/// <summary>0.</summary>
	public byte Blank2 { get; set; }

	/// <summary>0.</summary>
	public byte Blank3 { get; set; }

	/// <summary>Unknown. 4.</summary>
	public byte Unknown6 { get; set; }

	/// <summary>Unknown. 80 (0x50).</summary>
	public byte Unknown7 { get; set; }

	/// <summary>0.</summary>
	public byte Blank4 { get; set; }

	/// <summary>0.</summary>
	public byte Blank5 { get; set; }

	/// <summary>Unknown. 48.</summary>
	public byte Unknown8 { get; set; }

	/// <summary>Unknown. 38.</summary>
	public byte Unknown9 { get; set; }

	/// <summary>Unknown. 2.</summary>
	public byte Unknown10 { get; set; }

	/// <summary>Unknown <c>int16</c>. 500.</summary>
	public short Value3 { get; set; }

	/// <summary>Unknown <c>int16</c>. 8000.</summary>
	public short Value4 { get; set; }
}
