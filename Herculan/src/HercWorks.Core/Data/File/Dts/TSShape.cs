using HercWorks.Core.Data.File.Dts.Part;
using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// A shape root: a part list, then the per-sequence frame counts and the shape's own node
/// transforms. See docs/retail/rendering/dts-node-posing.md, "The shape's own node transforms".
/// </summary>
public class TSShape : TSPartList {
	/// <summary>
	/// Frame count per animation sequence (<c>shape+0x20</c>) — what a cell-animation counter is
	/// taken modulo. See docs/retail/rendering/dts-billboards.md, "TSCellAnimPart_Render (004767e4)".
	/// </summary>
	public short[]? SequenceList { get; set; }

	/// <summary>The shape's own node transforms (<c>shape+0x18</c>); empty in every retail model.</summary>
	public TSShapeNodeTransform[]? NodeTransforms { get; set; }

	public TSShape() : base(TSObjectHeader.TSShape) { }

	public TSShape(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));

		str = JsonString(str);
		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str = base.JsonString(str);

		str.Append(",\n");
		str.Append("\"sequences\" : ").Append(ArrayToString(SequenceList)).Append(",\n");
		str.Append("\"nodeTransforms\" : ").Append(ArrayToString(NodeTransforms));

		return str;
	}

	private static string ArrayToString<T>(T[]? arr) =>
		arr == null ? "null" : "[" + string.Join(", ", arr) + "]";
}
