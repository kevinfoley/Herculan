namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// A pilot's rank, held at pilot record <c>+0x29</c> as a 0-3 value. Seeded from the pilot's skill
/// at roster generation and stepped up by VSHELL's post-mission pass whenever missions flown reach a
/// multiple of their divisor, capped at Lt Colonel. Unlike skill, it advances for the player too.
///
/// <para>Distinct from <see cref="PilotSkill"/>, the separate 0-3 field at <c>+0x25</c>. The eight
/// UI strings behind the two ladders are one contiguous run — skill indexes it from the start and
/// rank from four in — so a single 0-7 enum will appear to display correctly while conflating two
/// fields the game keeps apart and caps independently.</para>
///
/// See <c>docs/retail/formats/save-games.md</c> and <c>docs/retail/shell/campaign-loop.md</c>.
/// </summary>
public sealed class PilotRank {
	public static readonly PilotRank Lieutenant = new(0, "Lieutenant");
	public static readonly PilotRank Captain = new(1, "Captain");
	public static readonly PilotRank Major = new(2, "Major");
	public static readonly PilotRank LtColonel = new(3, "Lt Colonel");

	private static readonly IReadOnlyList<PilotRank> All =
		new[] { Lieutenant, Captain, Major, LtColonel };

	private static readonly Dictionary<short, PilotRank> ById = All.ToDictionary(r => r.Id);

	public short Id { get; set; }
	public string Label { get; set; }

	private PilotRank(short id, string label) {
		Id = id;
		Label = label;
	}

	public static PilotRank? GetById(short id) => ById.GetValueOrDefault(id);

	public static IReadOnlyList<PilotRank> Values() => All;

	/// <summary>The rank whose label matches, case-insensitively; Lieutenant when none does.</summary>
	public static PilotRank GetByName(string name) =>
		All.FirstOrDefault(r => string.Equals(name, r.Label, StringComparison.OrdinalIgnoreCase)) ?? Lieutenant;

	public override string ToString() => Label;
}
