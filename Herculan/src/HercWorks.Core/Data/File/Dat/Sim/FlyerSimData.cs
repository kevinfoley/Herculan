namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - dat\[flyer].dat — the type record of a <c>Flyer</c>-class unit, SKIMMER.DAT the one retail
/// example: nine little-endian shorts and a 28-byte NUL-padded name, 46 bytes in all. It is its own
/// layout, not a truncated <see cref="HercSimDat"/>, though the first seven shorts sit where
/// HercSimDat's do (SpeedTurn .. AnimId_Walk).
///
/// <para>DBSIM reads the payload straight into the first 0x2e bytes of its 0x70-byte flyer type
/// record (<c>FlyerType_LoadResources</c>, <c>00422ed0</c>), so the offsets here are that record's
/// offsets too. See docs/retail/simulation/ai-flyers.md.</para>
/// </summary>
public class FlyerSimData {
	public short SpeedTurn { get; set; }
	public short SpeedReverse { get; set; }

	/// <summary>Offset 4 — the travel speed the flyer's vtable <c>+0x38</c> reads.</summary>
	public short SpeedForward { get; set; }
	public short SpeedAccelDecel { get; set; }

	/// <summary>Offset 8, at the offset of <see cref="HercSimDat.TurnAccelDecel"/>. 150 in SKIMMER.DAT.</summary>
	public short TurnAccelDecel { get; set; }

	/// <summary>Offset 10, at the offset of <see cref="HercSimDat.CameraPartId"/>. 4 in SKIMMER.DAT.</summary>
	public short CameraPartId { get; set; }

	/// <summary>Offset 12, at the offset of <see cref="HercSimDat.AnimId_Walk"/>. -1 in SKIMMER.DAT, as on the RAZOR.</summary>
	public short AnimId_Walk { get; set; }

	/// <summary>
	/// Offset 0x0e — the bank angle limit, as a binary angle. The flyer AI's roll controller
	/// (<c>Flyer_SteerAndFly</c>, <c>004222fc</c>) holds the commanded bank at it, with a 1500-unit
	/// hysteresis band. 14000 in SKIMMER.DAT, about 77 degrees.
	/// </summary>
	public short MaxBankAngle { get; set; }

	/// <summary>Offset 0x10. 1500 in SKIMMER.DAT; meaning not established.</summary>
	public short Unk16_val { get; set; }

	/// <summary>Offsets 0x12-0x2d — NUL-padded ASCII name, "Landskimmer" in SKIMMER.DAT.</summary>
	public byte[]? NameBytes { get; set; }
}
