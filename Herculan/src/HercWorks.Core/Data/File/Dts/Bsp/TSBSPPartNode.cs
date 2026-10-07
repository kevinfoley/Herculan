using HercWorks.Core.Data.Struct;
using System.Text;

namespace HercWorks.Core.Data.File.Dts.Bsp;

/// <summary>
/// One 14-byte node of a <see cref="TSBSPPart"/>'s tree: a splitting plane and its two sides. See
/// docs/retail/formats/dts-texture-binding.md, "TSBSPPart child selection".
/// </summary>
public class TSBSPPartNode {
	/// <summary>Offset of the record in the buffer it was read from.</summary>
	public int Index { get; set; }

	public int ByteLen { get; set; }

	/// <summary>The record's bytes as read.</summary>
	public byte[]? Data { get; set; }

	/// <summary>The splitting plane's normal.</summary>
	public Vec3Short? Normal { get; set; }

	/// <summary>The plane's offset: a point's side is the sign of <c>dot(Normal, point) - Coeff</c>.</summary>
	public int Coeff { get; set; }

	/// <summary>
	/// What lies on each side: negative for nothing, <c>0x4000 | i</c> for the leaf
	/// <c>Parts[i]</c>, otherwise the index of another node.
	/// </summary>
	public short Front { get; set; }

	/// <inheritdoc cref="Front"/>
	public short Back { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{ \n\"class\" : \"TSBSPPartNode\",\n");
		str.Append("\"index\" : ").Append(Index).Append(",\n");
		str.Append("\"byteLen\" : ").Append(ByteLen).Append(",\n");
		str.Append("\"data\" : ").Append(Data == null ? "null" : "[" + string.Join(", ", Data) + "]").Append(",\n");
		str.Append("\"normal\" : ").Append(Normal).Append(",\n");
		str.Append("\"coeff\" : ").Append(Coeff).Append(",\n");
		str.Append("\"front\" : ").Append(Front).Append(",\n");
		str.Append("\"back\" : ").Append(Back).Append("\n");
		str.Append("}\n");

		return str.ToString();
	}
}
