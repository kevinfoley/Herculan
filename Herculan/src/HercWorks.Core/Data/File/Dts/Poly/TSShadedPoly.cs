using System.Text;

namespace HercWorks.Core.Data.File.Dts.Poly;

/// <summary>
/// A flat-lit poly: its surface value is a shade-ramp number, and the face's light level picks the
/// step along that ramp. See docs/retail/formats/dts-texture-binding.md, "TSShadedPoly — shade-ramp number,
/// per-face light, fixed .RMP row".
/// </summary>
public class TSShadedPoly : TSSolidPoly {
	public TSShadedPoly() : base(TSObjectHeader.TSShadedPoly) { }

	public TSShadedPoly(TSObjectHeader hdr) : base(hdr) { }

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
