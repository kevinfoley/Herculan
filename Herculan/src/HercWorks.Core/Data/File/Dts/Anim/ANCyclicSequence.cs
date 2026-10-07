using System.Text;

namespace HercWorks.Core.Data.File.Dts.Anim;

/// <summary>
/// A looping <see cref="ANSequence"/>. The chunk's class is the whole difference: the frame step wraps
/// instead of clamping. See docs/retail/formats/dts-node-posing.md, "Cyclic and one-shot sequences".
/// </summary>
public class ANCyclicSequence : ANSequence {
	public ANCyclicSequence() : base(TSObjectHeader.ANCyclicSequence) { }

	public ANCyclicSequence(TSObjectHeader hdr) : base(hdr) { }

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
