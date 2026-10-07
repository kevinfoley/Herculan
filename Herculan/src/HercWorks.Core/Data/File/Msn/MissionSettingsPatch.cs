namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #2 (82 bytes/record) of a <c>.MSN</c> file: the mission's own settings. This is how a mission
/// says which zone it is played in, on which theater and at what time of day, what kind of objective
/// the player's cockpit watches for, and which training mission it is — the values the simulator reads
/// from the ten-word header at the top of <c>script.dat</c>. A record can also name campaign flags to
/// reset to zero as the mission loads. Every retail mission has at least one of these records, and its zone
/// comes from nowhere else.
///
/// <para>Each record carries a condition, so a mission can hold several and have the campaign's
/// progress decide which apply. Of its 41 words, the first is that condition, the next ten are
/// header values (a word left at its "unset" value, 1 for the first and 0 for the rest, changes
/// nothing) and the last thirty are flag indices (<c>-1</c> for none). VSHELL applies each record the
/// moment it reads it and keeps nothing, which is why no other row refers to these.</para>
///
/// <para>Kept here as raw words so a file round-trips unchanged; the load that applies them is
/// <see cref="Io.Transform.Common.MissionGenerator"/>, and the rules are
/// docs/retail/formats/msn-mission-file.md#the-header-patch--row-2.</para>
/// </summary>
public class MissionSettingsPatch {
	/// <summary>Raw 41-short (82-byte) record, preserved verbatim for round-trip fidelity.</summary>
	public short[] Data { get; set; } = new short[41];
}
