using System.Text;

namespace HercWorks.Core.Data.File.Dts.Poly;

/// <summary>
/// An unlit poly whose surface value is a palette index, drawn as a fill plus an outline. See
/// docs/retail/rendering/dts-texture-binding.md, "TSSolidPoly — palette index, unlit, fill plus outline".
/// </summary>
public class TSSolidPoly : TSPoly {
	/// <summary>
	/// Which of the group's surfaces the poly uses, stored as <c>surfaceIndex * 4</c>. The surface
	/// value's meaning depends on the poly type — see <see cref="TSSurfaceEntry"/>.
	/// </summary>
	public short ColorIndexId { get; set; }

	public TSSolidPoly() : base(TSObjectHeader.TSSolidPoly) { }

	public TSSolidPoly(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"colorIndexId\" : ").Append(ColorIndexId);

		return str;
	}
}
