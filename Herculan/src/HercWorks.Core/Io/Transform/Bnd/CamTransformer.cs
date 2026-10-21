using HercWorks.Core.Data.File.Bnd;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Bnd;

/// <summary>
/// Transforms byte[] data to and from CAM.BND (see <see cref="Cam"/>). Matches this one file by
/// name (see <see cref="TransformerRegistry"/>): every other .BND file has its own record shape. Round-trips
/// the retail file byte-exact.
///
/// <para>Reads the entry's content, offset 0 first. A loose <c>.BND</c> unpacked with a tool that
/// keeps the VOL entry prefix carries nine extra leading bytes that are no part of the format —
/// strip them with <see cref="HercWorks.Vol.VolEntryPrefixCodec"/> before parsing, as the editors
/// do. See docs/retail/formats/vol-archive.md.</para>
/// </summary>
public class CamTransformer : ByteTransformer<Cam> {
	public override Cam? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		SetBytes(inputArray);

		var cam = new Cam {
			Stamp = (uint)IndexIntLE(),
			MinOrbitDistance = IndexShortLE(),
			MaxOrbitDistance = IndexShortLE(),
			AngleRateScale = IndexShortLE(),
			AngleRateStep = IndexShortLE(),
			ZoomRateScale = IndexShortLE(),
			ZoomRateStep = IndexShortLE(),
			PitchLimit = IndexShortLE(),
			MinEyeHeight = IndexShortLE(),
			SlopeAllowance = IndexShortLE(),
			ChaseDelay = IndexShortLE(),
		};

		return cam;
	}

	public override byte[]? Write(Cam cam) {
		using var outStream = new MemoryStream();

		void Emit(byte[] bytes) => outStream.Write(bytes, 0, bytes.Length);

		Emit(WriteIntLE((int)cam.Stamp));
		foreach (short value in new[] {
			cam.MinOrbitDistance, cam.MaxOrbitDistance, cam.AngleRateScale, cam.AngleRateStep, cam.ZoomRateScale,
			cam.ZoomRateStep, cam.PitchLimit, cam.MinEyeHeight, cam.SlopeAllowance, cam.ChaseDelay,
		}) {
			Emit(WriteShortLE(value));
		}

		return outStream.ToArray();
	}
}
