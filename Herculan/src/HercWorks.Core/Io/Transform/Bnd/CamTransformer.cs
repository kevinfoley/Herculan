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
			Unknown0 = IndexByte(),
			Unknown1 = IndexByte(),
			Unknown2 = IndexByte(),
			Unknown3 = IndexByte(),
			Distance1 = IndexShortLE(),
			Distance2 = IndexShortLE(),
			Blank1 = IndexByte(),
			Unknown4 = IndexByte(),
			Unknown5 = IndexByte(),
			Blank2 = IndexByte(),
			Blank3 = IndexByte(),
			Unknown6 = IndexByte(),
			Unknown7 = IndexByte(),
			Blank4 = IndexByte(),
			Blank5 = IndexByte(),
			Unknown8 = IndexByte(),
			Unknown9 = IndexByte(),
			Unknown10 = IndexByte(),
			Value3 = IndexShortLE(),
			Value4 = IndexShortLE(),
		};

		return cam;
	}

	public override byte[]? Write(Cam cam) {
		using var outStream = new MemoryStream();

		void Emit(byte[] bytes) => outStream.Write(bytes, 0, bytes.Length);
		void WriteByte(byte b) => outStream.WriteByte(b);

		WriteByte(cam.Unknown0);
		WriteByte(cam.Unknown1);
		WriteByte(cam.Unknown2);
		WriteByte(cam.Unknown3);
		Emit(WriteShortLE(cam.Distance1));
		Emit(WriteShortLE(cam.Distance2));
		WriteByte(cam.Blank1);
		WriteByte(cam.Unknown4);
		WriteByte(cam.Unknown5);
		WriteByte(cam.Blank2);
		WriteByte(cam.Blank3);
		WriteByte(cam.Unknown6);
		WriteByte(cam.Unknown7);
		WriteByte(cam.Blank4);
		WriteByte(cam.Blank5);
		WriteByte(cam.Unknown8);
		WriteByte(cam.Unknown9);
		WriteByte(cam.Unknown10);
		Emit(WriteShortLE(cam.Value3));
		Emit(WriteShortLE(cam.Value4));

		return outStream.ToArray();
	}
}
