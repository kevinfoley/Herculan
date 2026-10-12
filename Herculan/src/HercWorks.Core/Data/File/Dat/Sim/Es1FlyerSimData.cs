namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\[flyer].dat, in SIMVOL0.VOL, <c>SKIMMER.DAT</c> the one retail file: the 34 bytes <c>FlyerType_LoadResources</c>
/// (<c>ES1DBSIM 0001c6c8</c>) reads into the first 34 bytes of its 46-byte flyer type record. Its first 18 bytes are ES2's
/// nine shorts (<see cref="FlyerSimData"/>); its last 16 lie where ES2's name begins. Layout:
/// docs/es1/formats/bullets-skimmer-dat.md#datskimmerdat. Read by <see cref="Io.Transform.Dbsim.Es1FlyerSimDataTransformer"/>.
/// </summary>
public class Es1FlyerSimData {
	/// <summary>The file's length, the bytes the loader reads.</summary>
	public const int Length = 0x22;

	/// <inheritdoc cref="FlyerSimData.SpeedTurn"/>
	public short SpeedTurn { get; set; }

	/// <inheritdoc cref="FlyerSimData.SpeedReverse"/>
	public short SpeedReverse { get; set; }

	/// <inheritdoc cref="FlyerSimData.SpeedForward"/>
	public short SpeedForward { get; set; }

	/// <inheritdoc cref="FlyerSimData.SpeedAccelDecel"/>
	public short SpeedAccelDecel { get; set; }

	/// <inheritdoc cref="FlyerSimData.TurnAccelDecel"/>
	public short TurnAccelDecel { get; set; }

	/// <inheritdoc cref="FlyerSimData.CameraPartId"/>
	public short CameraPartId { get; set; }

	/// <inheritdoc cref="FlyerSimData.AnimId_Walk"/>
	public short AnimId_Walk { get; set; }

	/// <inheritdoc cref="FlyerSimData.MaxBankAngle"/>
	public short MaxBankAngle { get; set; }

	/// <inheritdoc cref="FlyerSimData.Unk16_val"/>
	public short Unk16_val { get; set; }

	/// <summary>Offsets 0x12-0x21, where ES2's file holds the first 16 bytes of its name; 0 in <c>SKIMMER.DAT</c>.</summary>
	public byte[] Bytes12 { get; set; } = new byte[16];
}
