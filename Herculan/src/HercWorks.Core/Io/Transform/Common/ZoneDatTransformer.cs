using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>Reads and writes a zone's 16-byte <c>dat\zoneNNNN.dat</c> header (<see cref="ZoneDat"/>).</summary>
public class ZoneDatTransformer : ByteTransformer<ZoneDat> {
	/// <summary>Bytes the header occupies.</summary>
	public const int Size = 16;

	/// <summary>The four <c>int32</c>s, or null when the file is shorter than <see cref="Size"/>.</summary>
	public override ZoneDat? Parse(byte[]? bytes) {
		if (bytes is not { Length: >= Size }) {
			return null;
		}

		return new ZoneDat {
			WidthShift = BitConverter.ToInt32(bytes, 0),
			HeightShift = BitConverter.ToInt32(bytes, 4),
			CellSizeShift = BitConverter.ToInt32(bytes, 8),
			HeightScale = BitConverter.ToInt32(bytes, 12),
		};
	}

	public override byte[]? Write(ZoneDat source) {
		var bytes = new byte[Size];
		BitConverter.GetBytes(source.WidthShift).CopyTo(bytes, 0);
		BitConverter.GetBytes(source.HeightShift).CopyTo(bytes, 4);
		BitConverter.GetBytes(source.CellSizeShift).CopyTo(bytes, 8);
		BitConverter.GetBytes(source.HeightScale).CopyTo(bytes, 12);
		return bytes;
	}
}
