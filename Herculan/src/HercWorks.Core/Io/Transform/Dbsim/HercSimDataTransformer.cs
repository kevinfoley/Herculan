using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Ported from org.hercworks.core.io.transform.dbsim.HercSimDataTransformer.
///
/// Expects the 216-byte record ALONE — i.e. <see cref="HercWorks.Vol.VolEntry.RawBytes"/>, which
/// VolFileReader has already advanced past the 9-byte VOL entry prefix (1 compression-type byte,
/// 4 size bytes, 4 magic bytes; see VolEntry's doc comment). Files dumped straight out of a VOL
/// to disk normally KEEP that prefix, so feeding one of those in unmodified shifts every field by
/// 9 bytes and yields plausible-looking nonsense rather than an obvious failure. Skip 9 bytes
/// first if the source is a raw dump. DBSIM reads the same 216 bytes into MECH_TYPE_DATA at
/// record offset 2, so a field at record offset N here is the exe's typeRecord+N+2.
/// </summary>
public class HercSimDataTransformer : ByteTransformer<HercSimDat> {
	public override HercSimDat? Parse(byte[]? inputArray) {
		Index = 0;

		if (inputArray == null || inputArray.Length <= 0) {
			// TODO (carried over from Java): null input
			return null;
		}

		var data = new HercSimDat();

		SetBytes(inputArray);

		data.SpeedTurn = IndexShortLE();
		data.SpeedReverse = IndexShortLE();
		data.SpeedForward = IndexShortLE();
		data.SpeedAccelDecel = IndexShortLE();
		data.TurnAccelDecel = IndexShortLE();

		data.CameraPartId = IndexShortLE();

		data.AnimId_Walk = IndexShortLE();

		data.AnimId_Run = IndexShortLE();
		data.AnimId_StopMove = IndexShortLE();
		data.AnimId_StopReverse = IndexShortLE();
		data.RideHeight = IndexShortLE();
		data.HitCenterHeight = IndexShortLE();

		data.HitRadius = IndexShortLE();

		data.AnimId_TorsoTwist = IndexShortLE();
		data.TorsoTwistMaxRate = IndexShortLE();

		data.TorsoTwistAccel = IndexShortLE();

		data.TorsoTwistLimit = IndexShortLE();
		data.AnimId_TorsoPitch = IndexShortLE();
		data.TorsoPitchMaxRate = IndexShortLE();
		data.TorsoPitchAccel = IndexShortLE();
		data.TorsoPitchMax = IndexShortLE();
		data.TorsoPitchMin = IndexShortLE();

		data.GaitThreshold = IndexShortLE();

		for (int i = 0; i < 20; i++) {
			data.ModelLoDBoneIds[i] = IndexByte();
		}

		data.AiRatingBase = IndexShortLE();

		data.AnimId_Death = IndexShortLE();
		data.LegsCritFlags2 = IndexShortLE();
		data.LegCount = IndexShortLE();
		data.VanishesOnDeath = IndexShortLE();

		data.Mass = IndexShortLE();

		data.FlyerFlag = IndexShortLE();

		data.CockpitColorScheme = IndexShortLE();

		data.SalvageScale = IndexShortLE();
		data.WeaponMountsDestructible = IndexShortLE();

		data.NameBytes = IndexSegment(12);

		data.EyeOffsetY = IndexShortLE();
		data.EyeOffsetZ = IndexShortLE();

		data.OrbitCentreY = IndexShortLE();

		data.OrbitCentreZ = IndexShortLE();

		Skip(2); // blank bytes 0x106

		data.GaitThresholdReverse = IndexShortLE();
		data.BodyRadius = IndexShortLE();

		data.ModelFlagsShadow1 = IndexShortLE();
		data.ModelFlagsShadow2 = IndexShortLE();

		data.Unk116_val = IndexShortLE();
		data.Unk118_val = IndexShortLE();
		data.Unk120_val = IndexShortLE();

		data.AnimId_TurnInPlace = IndexShortLE();

		var seg500 = new short[12];
		for (int i = 0; i < 12; i++) {
			seg500[i] = IndexShortLE();
		}
		data.AiRatingSystemPenalty = seg500;

		data.TextureGroup = IndexShortLE();

		data.FootfallTriggerWalk = IndexShortLE();
		data.FootfallTriggerReverse = IndexShortLE();
		data.FootfallTriggerRun = IndexShortLE();
		data.FootfallTriggerLand = IndexShortLE();

		Skip(12);

		data.FootfallRearmWalk = IndexShortLE();
		data.FootfallRearmReverse = IndexShortLE();
		data.FootfallRearmRun = IndexShortLE();

		Skip(14);

		data.ShieldMaxTotal = IndexShortLE();
		data.HudTopSpeed = IndexShortLE();
		data.StrideScaleDivisor = IndexShortLE();
		data.StrideScaleNumerator = IndexShortLE();

		Skip(6);

		data.DebrisFile = IndexSegment(12);

		// The per-part byte arrays, taken at their own offsets rather than in sequence: they overlap
		// shorts already read above, and the sequential pass emits those shorts unchanged. See
		// HercSimDat.LegKinds for why both readings of the same bytes exist, and why the count is the
		// kind list's own terminator rather than LegCount.
		int parts = TerminatedLength(inputArray, LegKindsOffset);
		data.LegKinds = ReadLegBytes(inputArray, LegKindsOffset, parts);
		data.LegPartIds = ReadLegBytes(inputArray, LegPartIdsOffset, parts);

		return data;
	}

	/// <summary>Record offset of the per-part kind bytes — the exe's <c>typeRec+0x72</c>.</summary>
	private const int LegKindsOffset = 112;

	/// <summary>Record offset of the per-part part-id bytes — the exe's <c>typeRec+0x77</c>.</summary>
	private const int LegPartIdsOffset = 117;

	/// <summary>
	/// How many bytes from <paramref name="offset"/> come before the first negative one — the count
	/// <c>Mech_Constructor</c> takes (<c>00415e3a</c>-<c>00415e46</c>: <c>MOVSX</c>, <c>INC</c>,
	/// <c>JG</c>). It has no cap of its own; this one stops at the end of the record.
	/// </summary>
	private static int TerminatedLength(byte[] record, int offset) {
		int count = 0;
		while (offset + count < record.Length && (sbyte)record[offset + count] >= 0) {
			count++;
		}

		return count;
	}

	/// <summary>One per-part byte run, clipped to what the record actually holds.</summary>
	private static byte[] ReadLegBytes(byte[] record, int offset, int count) {
		int available = record.Length - offset;
		if (available <= 0 || count <= 0) {
			return Array.Empty<byte>();
		}

		var bytes = new byte[Math.Min(count, available)];
		Array.Copy(record, offset, bytes, 0, bytes.Length);
		return bytes;
	}

	public override byte[]? Write(HercSimDat data) {
		using var outStream = new MemoryStream();

		Emit(outStream, WriteShortLE(data.SpeedTurn));
		Emit(outStream, WriteShortLE(data.SpeedReverse));
		Emit(outStream, WriteShortLE(data.SpeedForward));
		Emit(outStream, WriteShortLE(data.SpeedAccelDecel));
		Emit(outStream, WriteShortLE(data.TurnAccelDecel));

		Emit(outStream, WriteShortLE(data.CameraPartId));

		Emit(outStream, WriteShortLE(data.AnimId_Walk));

		Emit(outStream, WriteShortLE(data.AnimId_Run));
		Emit(outStream, WriteShortLE(data.AnimId_StopMove));
		Emit(outStream, WriteShortLE(data.AnimId_StopReverse));
		Emit(outStream, WriteShortLE(data.RideHeight));

		Emit(outStream, WriteShortLE(data.HitCenterHeight));

		Emit(outStream, WriteShortLE(data.HitRadius));

		Emit(outStream, WriteShortLE(data.AnimId_TorsoTwist));
		Emit(outStream, WriteShortLE(data.TorsoTwistMaxRate));
		Emit(outStream, WriteShortLE(data.TorsoTwistAccel));
		Emit(outStream, WriteShortLE(data.TorsoTwistLimit));
		Emit(outStream, WriteShortLE(data.AnimId_TorsoPitch));
		Emit(outStream, WriteShortLE(data.TorsoPitchMaxRate));
		Emit(outStream, WriteShortLE(data.TorsoPitchAccel));
		Emit(outStream, WriteShortLE(data.TorsoPitchMax));
		Emit(outStream, WriteShortLE(data.TorsoPitchMin));

		Emit(outStream, WriteShortLE(data.GaitThreshold));

		for (int i = 0; i < 20; i++) {
			outStream.WriteByte(data.ModelLoDBoneIds[i]);
		}

		Emit(outStream, WriteShortLE(data.AiRatingBase));

		Emit(outStream, WriteShortLE(data.AnimId_Death));
		Emit(outStream, WriteShortLE(data.LegsCritFlags2));
		Emit(outStream, WriteShortLE(data.LegCount));
		Emit(outStream, WriteShortLE(data.VanishesOnDeath));

		Emit(outStream, WriteShortLE(data.Mass));

		Emit(outStream, WriteShortLE(data.FlyerFlag));

		Emit(outStream, WriteShortLE(data.CockpitColorScheme));

		Emit(outStream, WriteShortLE(data.SalvageScale));

		Emit(outStream, WriteShortLE(data.WeaponMountsDestructible));

		// write name
		outStream.Write(data.NameBytes!, 0, data.NameBytes!.Length);

		Emit(outStream, WriteShortLE(data.EyeOffsetY));
		Emit(outStream, WriteShortLE(data.EyeOffsetZ));

		Emit(outStream, WriteShortLE(data.OrbitCentreY));

		Emit(outStream, WriteShortLE(data.OrbitCentreZ));

		// blank bytes 0x106
		outStream.WriteByte(0x00);
		outStream.WriteByte(0x00);

		Emit(outStream, WriteShortLE(data.GaitThresholdReverse));
		Emit(outStream, WriteShortLE(data.BodyRadius));

		Emit(outStream, WriteShortLE(data.ModelFlagsShadow1));
		Emit(outStream, WriteShortLE(data.ModelFlagsShadow2));

		Emit(outStream, WriteShortLE(data.Unk116_val));
		Emit(outStream, WriteShortLE(data.Unk118_val));
		Emit(outStream, WriteShortLE(data.Unk120_val));
		Emit(outStream, WriteShortLE(data.AnimId_TurnInPlace));

		// range
		for (int i = 0; i < 12; i++) {
			Emit(outStream, WriteShortLE(data.AiRatingSystemPenalty![i]));
		}

		Emit(outStream, WriteShortLE(data.TextureGroup));

		Emit(outStream, WriteShortLE(data.FootfallTriggerWalk));
		Emit(outStream, WriteShortLE(data.FootfallTriggerReverse));
		Emit(outStream, WriteShortLE(data.FootfallTriggerRun));
		Emit(outStream, WriteShortLE(data.FootfallTriggerLand));

		// 158 - 169 - BLANK BYTES
		for (int i = 0; i < 12; i++) {
			outStream.WriteByte(0x00);
		}

		Emit(outStream, WriteShortLE(data.FootfallRearmWalk));
		Emit(outStream, WriteShortLE(data.FootfallRearmReverse));
		Emit(outStream, WriteShortLE(data.FootfallRearmRun));

		// 176 - 189 - BLANK BYTES
		for (int i = 0; i < 14; i++) {
			outStream.WriteByte(0x00);
		}

		Emit(outStream, WriteShortLE(data.ShieldMaxTotal));
		Emit(outStream, WriteShortLE(data.HudTopSpeed));
		Emit(outStream, WriteShortLE(data.StrideScaleDivisor));
		Emit(outStream, WriteShortLE(data.StrideScaleNumerator));

		// 198 - 203 - BLANK BYTES
		for (int i = 0; i < 6; i++) {
			outStream.WriteByte(0x00);
		}

		outStream.Write(data.DebrisFile!, 0, data.DebrisFile!.Length);

		return outStream.ToArray();
	}

	private static void Emit(MemoryStream outArr, byte[] data) => outArr.Write(data, 0, data.Length);
}
