using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// One entry of a TSGroup's surface array — four {int16 value, int16 flag} slots: the front fill,
/// the front outline, and the same pair for the back face. A poly names its entry with
/// <c>ColorIndexId / 4</c>.
///
/// <para>What the value <i>means</i> is the poly type's, not this record's: a .DBA frame index for
/// TSTexture4Poly, a palette index for TSSolidPoly, a shade-ramp number for TSShadedPoly and
/// TSGouraudPoly. The flag sits in the high half of the int32 the renderers index with — retail uses
/// 1024 on front pairs and 5120 (0x14 in that int32's top byte, "do not draw this face") on back
/// ones. See docs/retail/formats/dts-texture-binding.md, "Poly types and their colour mechanisms".</para>
/// </summary>
public class TSSurfaceEntry {
	public short FrontColor { get; set; }
	public short FrontFlag { get; set; }

	public short FrontLineColor { get; set; }
	public short FrontLineFlag { get; set; }

	public short BackColor { get; set; }
	public short BackFlag { get; set; }

	public short BackLineColor { get; set; }
	public short BackLineFlag { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{");

		str.Append("\"frontColor\" : ").Append(FrontColor).Append(",\n");
		str.Append("\"frontFlag\" : ").Append(FrontFlag).Append(",\n");
		str.Append("\"frontEdgeColor\" : ").Append(FrontLineColor).Append(",\n");
		str.Append("\"frontEdgeFlag\" : ").Append(FrontLineFlag).Append(",\n");
		str.Append("\"backColor\" : ").Append(BackColor).Append(",\n");
		str.Append("\"backFlag\" : ").Append(BackFlag).Append(",\n");
		str.Append("\"backEdgeColor\" : ").Append(BackLineColor).Append(",\n");
		str.Append("\"backEdgeFlag\" : ").Append(BackLineFlag).Append("\n");

		str.Append("}\n");

		return str.ToString();
	}
}
