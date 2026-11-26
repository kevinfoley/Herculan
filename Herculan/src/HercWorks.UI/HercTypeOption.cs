using HercWorks.Core.Data.Struct.Herc;

namespace HercWorks.UI;

/// <summary>
/// One entry of the Herc-type dropdown used wherever a mission file stores a Herc type as a raw
/// index into <c>nam\MECHS.NAM</c> (script.dat's Herc roster, player.mec's squad entries).
///
/// <para><b>Why HercLUT can stand in for MECHS.NAM:</b> the retail <c>MECHS.NAM</c> holds 21 names
/// and they match <see cref="HercLUT"/>'s ids 0-20 one-for-one, in order, by
/// <see cref="HercLUT.AbbrevDat"/> — OUTLAW, RAPTOR2, TOMAHAWK … CERBERUS. That makes the shell-side
/// LUT a usable name source here without a loaded VOL. <b>HercLUT's id 21 (Skimmer) is excluded:</b>
/// it has no <c>MECHS.NAM</c> entry at all and is in fact <c>FLYERS.NAM</c> index 0 — a flyer, not a
/// Herc — so offering it would write a type no Herc list can resolve.</para>
///
/// <para>Types a file carries that fall outside the list are kept as their own entries rather than
/// being dropped, so hand-edited or unrecognized data round-trips instead of being silently
/// rewritten to something valid.</para>
/// </summary>
internal sealed class HercTypeOption {
	/// <summary>Highest type with a <c>MECHS.NAM</c> name — the list's 21 entries are 0-20.</summary>
	private const short MaxNamedHercType = 20;

	public required short Id { get; init; }
	public required string Label { get; init; }

	public override string ToString() => Label;

	/// <summary>
	/// The named Hercs plus an entry for every type in use that has no name, in type order.
	/// </summary>
	/// <param name="typesInUse">The Herc types in use.</param>
	public static List<HercTypeOption> Build(IEnumerable<short> typesInUse) {
		var options = HercLUT.Values()
			.Where(herc => herc.Id <= MaxNamedHercType)
			.Select(herc => new HercTypeOption { Id = herc.Id, Label = herc.Name })
			.ToList();

		var known = options.Select(o => o.Id).ToHashSet();
		foreach (short type in typesInUse.Distinct().Where(t => !known.Contains(t))) {
			options.Add(new HercTypeOption { Id = type, Label = $"Unknown type {type}" });
		}

		return options.OrderBy(o => o.Id).ToList();
	}
}
