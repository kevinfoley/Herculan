using System.Text;

namespace HercWorks.Core.Data.File.Dts.Part;

/// <summary>
/// A flipbook: <b>one child per animation frame</b>, not a container whose children are all drawn.
/// <c>TSCellAnimPart_Render</c> (<c>004767e4</c>) draws
/// <c>children[cellFrames[AnimSequence] % childCount]</c> and nothing else.
///
/// <para><see cref="AnimSequence"/> (<c>part+0x16</c>) picks which entry of the drawing shape
/// instance's per-sequence frame counters the part reads. Children need not be bitmaps — BULLETS.DTS
/// root 8 animates real TSGroup geometry this way. See docs/retail/formats/dts-billboards.md,
/// "TSCellAnimPart_Render".</para>
/// </summary>
public class TSCellAnimPart : TSPartList {
	public short AnimSequence { get; set; }

	public TSCellAnimPart() : base(TSObjectHeader.TSCellAnimPart) { }

	public TSCellAnimPart(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"animSequence\" : ").Append(AnimSequence);

		return str;
	}
}
