namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// A pilot's skill, held at pilot record <c>+0x25</c> as a 0-3 value. Drawn at roster generation
/// against a weight table and stepped up by VSHELL's post-mission pass whenever the pilot's career
/// Herc + Flyer kills reach a multiple of their divisor — capped at Elite, and never for the player,
/// whose level is fixed at registration.
///
/// <para>Distinct from <see cref="PilotRank"/>, which is a separate 0-3 field at <c>+0x29</c>
/// advancing on missions flown. The two read their labels from one contiguous run of eight UI
/// strings, skill from the start and rank from four in, which is the only sense in which they share
/// a numbering.</para>
///
/// See <c>docs/retail/formats/save-games.md</c> and <c>docs/retail/shell/campaign-loop.md</c>.
/// </summary>
public sealed class PilotSkill {
	public static readonly PilotSkill Rookie = new(0, "Rookie");
	public static readonly PilotSkill Regular = new(1, "Regular");
	public static readonly PilotSkill Veteran = new(2, "Veteran");
	public static readonly PilotSkill Elite = new(3, "Elite");

	private static readonly IReadOnlyList<PilotSkill> All = new[] { Rookie, Regular, Veteran, Elite };

	private static readonly Dictionary<short, PilotSkill> ById = All.ToDictionary(s => s.Id);

	public short Id { get; set; }
	public string Label { get; set; }

	private PilotSkill(short id, string label) {
		Id = id;
		Label = label;
	}

	public static PilotSkill? GetById(short id) => ById.GetValueOrDefault(id);

	public static IReadOnlyList<PilotSkill> Values() => All;

	/// <summary>The skill whose label matches, case-insensitively; Rookie when none does.</summary>
	public static PilotSkill GetByName(string name) =>
		All.FirstOrDefault(s => string.Equals(name, s.Label, StringComparison.OrdinalIgnoreCase)) ?? Rookie;

	public override string ToString() => Label;
}
