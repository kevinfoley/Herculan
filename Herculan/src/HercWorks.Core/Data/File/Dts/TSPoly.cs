using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// A polygon with no surface of its own, so nothing fills it; a cell animation uses one as its
/// blank cell. See docs/retail/formats/mech-shape-drawing.md, "A destroyed component hides its own geometry".
/// </summary>
public class TSPoly : TSObject {
	/// <summary>Index into the group's points of the face's stored normal (<c>poly+4</c>).</summary>
	public short Normal { get; set; }

	/// <summary>Index into the group's points of the face's centre (<c>poly+6</c>).</summary>
	public short Center { get; set; }

	public short VertexCount { get; set; }

	/// <summary>Offset into the group's <see cref="TSGroup.Indexes"/> of the first corner.</summary>
	public short VertexList { get; set; }

	public TSPoly() : base(TSObjectHeader.TSPoly) { }

	public TSPoly(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));
		str = JsonString(str);

		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str.Append("\"normal\" : ").Append(Normal).Append(",\n");
		str.Append("\"center\" : ").Append(Center).Append(",\n");
		str.Append("\"vertexCount\" : ").Append(VertexCount).Append(",\n");
		str.Append("\"vertexList\" : ").Append(VertexList).Append("\n");

		return str;
	}
}
