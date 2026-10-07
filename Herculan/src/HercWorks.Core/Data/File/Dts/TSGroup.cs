using HercWorks.Core.Data.Struct;
using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// A geometry group: a point pool, an index list into it, a surface table, and the polys that
/// reference all three. See docs/retail/formats/dts-texture-binding.md, "Normals live in the point list".
/// </summary>
public class TSGroup : TSBasePart {
	/// <summary>Point indices, which a poly's <c>VertexList</c> and <c>NormalList</c> are offsets into.</summary>
	public short[]? Indexes { get; set; }

	/// <summary>The group's points: poly corners, and the normals that share the same pool.</summary>
	public Vec3Short[]? Points { get; set; }

	/// <summary>
	/// Surface records, one per four on-disk slots; a poly picks one by <c>ColorIndexId / 4</c>.
	/// </summary>
	public TSSurfaceEntry[]? Surfaces { get; set; }

	public TSObject[]? Polys { get; set; }

	public TSGroup() : base(TSObjectHeader.TSGroup) { }

	public TSGroup(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name + "_" + ListIndex));

		str = JsonString(str);
		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str = base.JsonString(str);

		str.Append(",\n");
		str.Append("\"indexes\" : ").Append(Indexes == null ? "[]" : "[" + string.Join(", ", Indexes) + "]").Append(",\n");
		str.Append("\"points\" : [\n");
		for (int s = 0; s < Points!.Length; s++) {
			str.Append(Points[s].ToString());
			if (s < Points.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("],\n");

		str.Append("\"surfaces\" : [\n");
		for (int c = 0; c < Surfaces!.Length; c++) {
			str.Append(Surfaces[c].ToString());

			if (c < Surfaces.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("],\n");

		str.Append("\"polys\" : [\n");
		for (int s = 0; s < Polys!.Length; s++) {
			str.Append(Polys[s].ToString());
			if (s < Polys.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("]");

		return str;
	}
}
