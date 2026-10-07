using System.Text;

namespace HercWorks.Core.Data.File.Dts.Poly;

/// <summary>
/// A poly lit per vertex: the same ramp number as <see cref="TSShadedPoly"/>, with a normal per
/// corner. See docs/retail/formats/dts-texture-binding.md, "TSGouraudPoly — same ramp number, per-vertex
/// light, no .RMP row".
/// </summary>
public class TSGouraudPoly : TSSolidPoly {
	/// <summary>
	/// Offset into the group's <see cref="TSGroup.Indexes"/> of the per-corner normal indices,
	/// parallel to <see cref="TSPoly.VertexList"/>.
	/// </summary>
	public short NormalList { get; set; }

	public TSGouraudPoly() : base(TSObjectHeader.TSGouraudPoly) { }

	public TSGouraudPoly(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"color\" : ").Append(ColorIndexId).Append(",\n");
		str.Append("\"normalList\" : ").Append(NormalList);

		return str;
	}
}
