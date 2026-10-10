using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// One of a <see cref="TSShape"/>'s own node transforms: a 32-byte transform record, stored in the
/// file exactly as it sits in memory — a row-major 3x3 Q14 matrix (<c>+0x00</c>), a rank byte
/// (<c>+0x12</c>), one byte of unknown meaning (<c>+0x13</c>), then an <c>int32</c> translation
/// (<c>+0x14</c>/<c>+0x18</c>/<c>+0x1c</c>). See docs/retail/rendering/dts-node-posing.md, "The shape's own
/// node transforms".
/// </summary>
public class TSShapeNodeTransform {
	/// <summary>Nine Q14 entries, row-major.</summary>
	public short[] Matrix { get; set; } = new short[9];

	/// <summary>How much of <see cref="Matrix"/> is meaningful: translation only, Z rotation only, or full 3x3.</summary>
	public byte Rank { get; set; }

	/// <summary>The byte between <see cref="Rank"/> and the translation; meaning not established, kept verbatim.</summary>
	public byte Byte13 { get; set; }

	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }

	/// <summary>Offset of the record in the buffer it was read from.</summary>
	public int Index { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{ \"class\" : \"").Append(GetType().Name).Append("\",\n");
		str.Append("\"index\" : ").Append(Index).Append(",\n");
		str.Append("\"matrix\" : [").Append(string.Join(", ", Matrix)).Append("],\n");
		str.Append("\"rank\" : ").Append(Rank).Append(",\n");
		str.Append("\"byte13\" : ").Append(Byte13).Append(",\n");
		str.Append("\"translation\" : [").Append(X).Append(", ").Append(Y).Append(", ").Append(Z).Append("]\n");
		str.Append("}");

		return str.ToString();
	}
}
