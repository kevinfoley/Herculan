using HercWorks.Core.Data.File.Dts.Part;
using System.Text;

namespace HercWorks.Core.Data.File.Dts.Bsp;

/// <summary>
/// A part list whose children are drawn through a BSP tree: <see cref="TSPartList.Parts"/> is a pool
/// the tree's leaves index, not a list drawn in order. See docs/retail/rendering/dts-texture-binding.md,
/// "TSBSPPart child selection".
/// </summary>
public class TSBSPPart : TSPartList {
	/// <summary>The tree, walked from node 0.</summary>
	public TSBSPPartNode[]? Nodes { get; set; }

	/// <summary>
	/// One per node: the transform id whose world matrix the node's splitting plane is brought into,
	/// -1 for an untransformed plane.
	/// </summary>
	public short[]? Transforms { get; set; }

	public TSBSPPart() : base(TSObjectHeader.TSBSPPart) { }

	public TSBSPPart(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"nodes\" : [");
		for (int s = 0; s < Nodes!.Length; s++) {
			str.Append(Nodes[s].ToString());
			if (s < Nodes.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("]");

		return str;
	}
}
