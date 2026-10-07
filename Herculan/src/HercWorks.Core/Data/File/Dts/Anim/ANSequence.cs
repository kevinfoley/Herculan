using System.Text;

namespace HercWorks.Core.Data.File.Dts.Anim;

/// <summary>
/// An animation sequence that plays once and holds its last frame; <see cref="ANCyclicSequence"/> is
/// the looping kind. See docs/retail/formats/dts-node-posing.md, "Cyclic and one-shot sequences".
/// </summary>
public class ANSequence : TSObject {
	/// <summary>Meaning not established.</summary>
	public short Tick { get; set; }

	/// <summary>
	/// The sequence's rank among the threads playing on one shape: the lowest wins a node two of them
	/// animate, and equal ranks keep the order the threads were added in. See
	/// docs/retail/formats/dts-node-posing.md, "Several threads on one shape".
	/// </summary>
	public short Priority { get; set; }

	/// <summary>
	/// Non-zero when column 0's transform is root motion — a ground displacement applied to the
	/// object — rather than a pose. See docs/retail/simulation/mech-locomotion.md.
	/// </summary>
	public short GroundMovement { get; set; }

	public ANSequenceFrame[]? Frames { get; set; }

	/// <summary>The node (transform id) each column of <see cref="TransformIndices"/> animates.</summary>
	public short[]? PartIds { get; set; }

	/// <summary>
	/// Index into <see cref="ANAnimList.Transforms"/> per (frame, column), row-major by frame.
	/// </summary>
	public short[]? TransformIndices { get; set; }

	public ANSequence() : base(TSObjectHeader.ANSequence) { }

	public ANSequence(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));

		str = JsonString(str);

		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str.Append("\"tick\" : ").Append(Tick).Append(",\n");
		str.Append("\"priority\" : ").Append(Priority).Append(",\n");
		str.Append("\"groundMove\" : ").Append(GroundMovement).Append(",\n");

		str.Append("\"frames\" : [\n");
		for (int s = 0; s < Frames!.Length; s++) {
			str.Append(Frames[s].ToString());
			if (s < Frames.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("],\n");

		str.Append("\"partIds\" : ").Append(PartIds == null ? "null" : "[" + string.Join(", ", PartIds) + "]").Append(",\n");
		str.Append("\"transformIndices\" : ").Append(TransformIndices == null ? "null" : "[" + string.Join(", ", TransformIndices) + "]");

		return str;
	}
}
