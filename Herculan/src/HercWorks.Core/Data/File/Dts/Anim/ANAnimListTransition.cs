using System.Text;

namespace HercWorks.Core.Data.File.Dts.Anim;

/// <summary>
/// One entry of an <see cref="ANAnimList"/>'s transition pool: a hop into another sequence, offered by
/// the frames that index it (<see cref="ANSequenceFrame.FirstTransition"/>).
/// </summary>
public class ANAnimListTransition {
	/// <summary>How long the transition frame lasts, in animation ticks.</summary>
	public short Duration { get; set; }

	public short DestSequence { get; set; }
	public short DestFrame { get; set; }

	/// <summary>
	/// The root motion covered while the transition plays, as an index into
	/// <see cref="ANAnimList.Transforms"/>. See docs/retail/simulation/mech-locomotion.md.
	/// </summary>
	public short TransformIndex { get; set; }

	/// <summary>Offset of the record in the buffer it was read from.</summary>
	public int Index { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{ \"class\" : \"").Append(GetType().Name).Append("\",\n");
		str.Append("\"index\" : ").Append(Index).Append(",\n");
		str.Append("\"duration\" :").Append(Duration).Append(",\n");
		str.Append("\"destSequence\" :").Append(DestSequence).Append(",\n");
		str.Append("\"destFrame\" :").Append(DestFrame).Append(",\n");
		str.Append("\"transformIndex\" :").Append(TransformIndex).Append("\n");
		str.Append("}\n");

		return str.ToString();
	}
}
