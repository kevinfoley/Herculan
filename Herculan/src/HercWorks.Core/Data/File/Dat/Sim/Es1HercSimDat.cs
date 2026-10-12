using System.Text;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\[chassis].dat, in SIMVOL0.VOL: the 192-byte chassis record <c>MechType_InitOne</c>
/// (<c>ES1DBSIM 00019cf0</c>) reads into its type record's <c>+2</c> (docs/es1/formats/chassis-dat.md#the-layout). Its
/// bytes are ES2's record offsets 0-191 (<see cref="HercSimDat"/>); it has none of ES2's HUD speed scale, stride pair or
/// debris name. Read by <see cref="Io.Transform.Dbsim.Es1HercSimDataTransformer"/>.
///
/// <para>Each member takes the name and description of ES2's field at its offset. Which of them ES1 reads as ES2 does is
/// an Open item of the doc; the members that differ say so.</para>
/// </summary>
public class Es1HercSimDat {
	/// <summary>The file's length, the bytes the loader reads.</summary>
	public const int Length = 192;

	/// <inheritdoc cref="HercSimDat.SpeedTurn"/>
	public short SpeedTurn { get; set; }

	/// <inheritdoc cref="HercSimDat.SpeedReverse"/>
	public short SpeedReverse { get; set; }

	/// <inheritdoc cref="HercSimDat.SpeedForward"/>
	public short SpeedForward { get; set; }

	/// <inheritdoc cref="HercSimDat.SpeedAccelDecel"/>
	public short SpeedAccelDecel { get; set; }

	/// <inheritdoc cref="HercSimDat.TurnAccelDecel"/>
	public short TurnAccelDecel { get; set; }

	/// <inheritdoc cref="HercSimDat.CameraPartId"/>
	public short CameraPartId { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_Walk"/>
	public short AnimId_Walk { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_Run"/>
	public short AnimId_Run { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_StopMove"/>
	public short AnimId_StopMove { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_StopReverse"/>
	public short AnimId_StopReverse { get; set; }

	/// <inheritdoc cref="HercSimDat.RideHeight"/>
	public short RideHeight { get; set; }

	/// <inheritdoc cref="HercSimDat.HitCenterHeight"/>
	public short HitCenterHeight { get; set; }

	/// <inheritdoc cref="HercSimDat.HitRadius"/>
	public short HitRadius { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_TorsoTwist"/>
	public short AnimId_TorsoTwist { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoTwistMaxRate"/>
	public short TorsoTwistMaxRate { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoTwistAccel"/>
	public short TorsoTwistAccel { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoTwistLimit"/>
	public short TorsoTwistLimit { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_TorsoPitch"/>
	public short AnimId_TorsoPitch { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoPitchMaxRate"/>
	public short TorsoPitchMaxRate { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoPitchAccel"/>
	public short TorsoPitchAccel { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoPitchMax"/>
	public short TorsoPitchMax { get; set; }

	/// <inheritdoc cref="HercSimDat.TorsoPitchMin"/>
	public short TorsoPitchMin { get; set; }

	/// <inheritdoc cref="HercSimDat.GaitThreshold"/>
	public short GaitThreshold { get; set; }

	/// <inheritdoc cref="HercSimDat.ModelLoDBoneIds"/>
	public byte[] ModelLoDBoneIds { get; set; } = new byte[20];

	/// <inheritdoc cref="HercSimDat.AiRatingBase"/>
	public short AiRatingBase { get; set; }

	/// <inheritdoc cref="HercSimDat.AnimId_Death"/>
	public short AnimId_Death { get; set; }

	/// <summary>
	/// Offset 70 — how many of <c>dts\&lt;name&gt;</c>'s roots the load skips when ES1's low-memory flag is set
	/// (docs/es1/formats/chassis-dat.md#low-memory): 2 in every retail file. ES2's <see cref="HercSimDat.LegsCritFlags2"/>.
	/// </summary>
	public short LowMemoryRootSkip { get; set; }

	/// <inheritdoc cref="HercSimDat.LegCount"/>
	public short LegCount { get; set; }

	/// <inheritdoc cref="HercSimDat.VanishesOnDeath"/>
	public short VanishesOnDeath { get; set; }

	/// <inheritdoc cref="HercSimDat.Mass"/>
	public short Mass { get; set; }

	/// <inheritdoc cref="HercSimDat.FlyerFlag"/>
	public short FlyerFlag { get; set; }

	/// <inheritdoc cref="HercSimDat.CockpitColorScheme"/>
	public short CockpitColorScheme { get; set; }

	/// <inheritdoc cref="HercSimDat.SalvageScale"/>
	public short SalvageScale { get; set; }

	/// <inheritdoc cref="HercSimDat.WeaponMountsDestructible"/>
	public short WeaponMountsDestructible { get; set; }

	/// <inheritdoc cref="HercSimDat.NameBytes"/>
	public byte[] NameBytes { get; set; } = new byte[12];

	/// <summary><see cref="NameBytes"/> up to its first NUL; ES1's names are mixed case, <c>Roadrunner</c>.</summary>
	public string Name {
		get {
			int end = Array.IndexOf(NameBytes, (byte)0);
			return Encoding.ASCII.GetString(NameBytes, 0, end < 0 ? NameBytes.Length : end);
		}
	}

	/// <inheritdoc cref="HercSimDat.EyeOffsetY"/>
	public short EyeOffsetY { get; set; }

	/// <inheritdoc cref="HercSimDat.EyeOffsetY"/>
	public short EyeOffsetZ { get; set; }

	/// <inheritdoc cref="HercSimDat.OrbitCentreY"/>
	public short OrbitCentreY { get; set; }

	/// <inheritdoc cref="HercSimDat.OrbitCentreY"/>
	public short OrbitCentreZ { get; set; }

	/// <summary>Offset 106 — 0 in every retail file; the load overwrites it with the chassis' type number.</summary>
	public short TypeNumberSlot { get; set; }

	/// <inheritdoc cref="HercSimDat.GaitThresholdReverse"/>
	public short GaitThresholdReverse { get; set; }

	/// <inheritdoc cref="HercSimDat.BodyRadius"/>
	public short BodyRadius { get; set; }

	/// <summary>
	/// Offsets 112-121 as bytes: ES2's shadow lists (<see cref="HercSimDat.LegKinds"/> from 112, <see cref="HercSimDat.LegPartIds"/>
	/// from 117).
	/// </summary>
	public byte[] ShadowBytes { get; set; } = new byte[10];

	/// <inheritdoc cref="HercSimDat.AnimId_TurnInPlace"/>
	public short AnimId_TurnInPlace { get; set; }

	/// <inheritdoc cref="HercSimDat.AiRatingSystemPenalty"/>
	public short[] AiRatingSystemPenalty { get; set; } = new short[12];

	/// <summary>
	/// Offset 148 — the texture group: which of <c>dba\light</c>, <c>medium</c>, <c>heavy</c> and <c>enemy</c>, 0 to 3, the
	/// load binds to every root (docs/es1/formats/chassis-dat.md#the-load). ES2's <see cref="HercSimDat.TextureGroup"/>, of
	/// seven groups.
	/// </summary>
	public short TextureGroup { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallTriggerWalk"/>
	public short FootfallTriggerWalk { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallTriggerWalk"/>
	public short FootfallTriggerReverse { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallTriggerWalk"/>
	public short FootfallTriggerRun { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallTriggerWalk"/>
	public short FootfallTriggerLand { get; set; }

	/// <summary>Offsets 158-169, 0 in every retail file.</summary>
	public byte[] Zero158 { get; set; } = new byte[12];

	/// <inheritdoc cref="HercSimDat.FootfallRearmWalk"/>
	public short FootfallRearmWalk { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallRearmWalk"/>
	public short FootfallRearmReverse { get; set; }

	/// <inheritdoc cref="HercSimDat.FootfallRearmWalk"/>
	public short FootfallRearmRun { get; set; }

	/// <summary>Offsets 176-189, 0 in every retail file.</summary>
	public byte[] Zero176 { get; set; } = new byte[14];

	/// <inheritdoc cref="HercSimDat.ShieldMaxTotal"/>
	public short ShieldMaxTotal { get; set; }
}
