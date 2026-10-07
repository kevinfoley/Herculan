namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// One entry of a HERC's 66-byte status block — an external facet, an internal component or a
/// hardpoint — with its condition, 0-100. The <see cref="Id"/> is the entry's index within its
/// array, which follows <see cref="Herc.HercExternals"/> and <see cref="Herc.HercInternals"/>
/// rather than reading front-to-back. See <c>docs/retail/formats/save-games.md#the-66-byte-status-block</c>.
/// </summary>
public class ShellHercPart {
	public short Id { get; set; }
	public string? Label { get; set; }
	public short Health { get; set; }

	public ShellHercPart() { }

	public ShellHercPart(short id, string label) {
		Id = id;
		Label = label;
	}

	public ShellHercPart(short id, string label, short health) {
		Id = id;
		Label = label;
		Health = health;
	}

	public override string ToString() {
		return $"ShellHercPart [id={Id}, label={Label}, health={Health}]";
	}
}
