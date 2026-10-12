using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's chassis file (<see cref="Es1HercSimDat"/>; docs/es1/formats/chassis-dat.md#the-layout), field by
/// field in file order. Writing a parsed file gives back its bytes.
///
/// <para>A file of any length but <see cref="Es1HercSimDat.Length"/> throws <see cref="InvalidDataException"/>, so a file
/// this model would not give back whole is rejected. The retail loader reads the first 192 bytes and asserts when the
/// read fails.</para>
/// </summary>
public class Es1HercSimDataTransformer : ByteTransformer<Es1HercSimDat> {
	/// <exception cref="InvalidDataException">The file is not <see cref="Es1HercSimDat.Length"/> bytes.</exception>
	public override Es1HercSimDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		if (inputArray.Length != Es1HercSimDat.Length) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {Es1HercSimDat.Length}.");
		}

		return new Es1HercSimDat {
			SpeedTurn = IndexShortLE(),
			SpeedReverse = IndexShortLE(),
			SpeedForward = IndexShortLE(),
			SpeedAccelDecel = IndexShortLE(),
			TurnAccelDecel = IndexShortLE(),
			CameraPartId = IndexShortLE(),
			AnimId_Walk = IndexShortLE(),
			AnimId_Run = IndexShortLE(),
			AnimId_StopMove = IndexShortLE(),
			AnimId_StopReverse = IndexShortLE(),
			RideHeight = IndexShortLE(),
			HitCenterHeight = IndexShortLE(),
			HitRadius = IndexShortLE(),
			AnimId_TorsoTwist = IndexShortLE(),
			TorsoTwistMaxRate = IndexShortLE(),
			TorsoTwistAccel = IndexShortLE(),
			TorsoTwistLimit = IndexShortLE(),
			AnimId_TorsoPitch = IndexShortLE(),
			TorsoPitchMaxRate = IndexShortLE(),
			TorsoPitchAccel = IndexShortLE(),
			TorsoPitchMax = IndexShortLE(),
			TorsoPitchMin = IndexShortLE(),
			GaitThreshold = IndexShortLE(),
			ModelLoDBoneIds = IndexSegment(20),
			AiRatingBase = IndexShortLE(),
			AnimId_Death = IndexShortLE(),
			LowMemoryRootSkip = IndexShortLE(),
			LegCount = IndexShortLE(),
			VanishesOnDeath = IndexShortLE(),
			Mass = IndexShortLE(),
			FlyerFlag = IndexShortLE(),
			CockpitColorScheme = IndexShortLE(),
			SalvageScale = IndexShortLE(),
			WeaponMountsDestructible = IndexShortLE(),
			NameBytes = IndexSegment(12),
			EyeOffsetY = IndexShortLE(),
			EyeOffsetZ = IndexShortLE(),
			OrbitCentreY = IndexShortLE(),
			OrbitCentreZ = IndexShortLE(),
			TypeNumberSlot = IndexShortLE(),
			GaitThresholdReverse = IndexShortLE(),
			BodyRadius = IndexShortLE(),
			ShadowBytes = IndexSegment(10),
			AnimId_TurnInPlace = IndexShortLE(),
			AiRatingSystemPenalty = IndexShortLEArray(12),
			TextureGroup = IndexShortLE(),
			FootfallTriggerWalk = IndexShortLE(),
			FootfallTriggerReverse = IndexShortLE(),
			FootfallTriggerRun = IndexShortLE(),
			FootfallTriggerLand = IndexShortLE(),
			Zero158 = IndexSegment(12),
			FootfallRearmWalk = IndexShortLE(),
			FootfallRearmReverse = IndexShortLE(),
			FootfallRearmRun = IndexShortLE(),
			Zero176 = IndexSegment(14),
			ShieldMaxTotal = IndexShortLE(),
		};
	}

	public override byte[]? Write(Es1HercSimDat source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);

		foreach (short value in new[] {
			source.SpeedTurn, source.SpeedReverse, source.SpeedForward, source.SpeedAccelDecel, source.TurnAccelDecel,
			source.CameraPartId, source.AnimId_Walk, source.AnimId_Run, source.AnimId_StopMove, source.AnimId_StopReverse,
			source.RideHeight, source.HitCenterHeight, source.HitRadius, source.AnimId_TorsoTwist, source.TorsoTwistMaxRate,
			source.TorsoTwistAccel, source.TorsoTwistLimit, source.AnimId_TorsoPitch, source.TorsoPitchMaxRate,
			source.TorsoPitchAccel, source.TorsoPitchMax, source.TorsoPitchMin, source.GaitThreshold,
		}) {
			w.Write(value);
		}

		w.Write(source.ModelLoDBoneIds);
		foreach (short value in new[] {
			source.AiRatingBase, source.AnimId_Death, source.LowMemoryRootSkip, source.LegCount, source.VanishesOnDeath,
			source.Mass, source.FlyerFlag, source.CockpitColorScheme, source.SalvageScale, source.WeaponMountsDestructible,
		}) {
			w.Write(value);
		}

		w.Write(source.NameBytes);
		foreach (short value in new[] {
			source.EyeOffsetY, source.EyeOffsetZ, source.OrbitCentreY, source.OrbitCentreZ, source.TypeNumberSlot,
			source.GaitThresholdReverse, source.BodyRadius,
		}) {
			w.Write(value);
		}

		w.Write(source.ShadowBytes);
		w.Write(source.AnimId_TurnInPlace);
		foreach (short value in source.AiRatingSystemPenalty) {
			w.Write(value);
		}

		foreach (short value in new[] {
			source.TextureGroup, source.FootfallTriggerWalk, source.FootfallTriggerReverse, source.FootfallTriggerRun,
			source.FootfallTriggerLand,
		}) {
			w.Write(value);
		}

		w.Write(source.Zero158);
		w.Write(source.FootfallRearmWalk);
		w.Write(source.FootfallRearmReverse);
		w.Write(source.FootfallRearmRun);
		w.Write(source.Zero176);
		w.Write(source.ShieldMaxTotal);

		w.Flush();
		return o.ToArray();
	}
}
