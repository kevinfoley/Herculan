using System.Text;

namespace HercWorks.Core.Data.File.Dts.Poly;

/// <summary>
/// A textured poly: its surface value is a frame index into the shape's bound <c>.DBA</c>. Three- and
/// four-vertex polys both occur. See docs/retail/rendering/dts-texture-binding.md, "TSTexture4Poly — frame
/// index, ramp row by light, fullbright on demand".
/// </summary>
public class TSTexture4Poly : TSSolidPoly {
	public TSTexture4Poly() : base(TSObjectHeader.TSTexture4Poly) { }

	public TSTexture4Poly(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));

		str = JsonString(str);
		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		return base.JsonString(str);
	}
}
