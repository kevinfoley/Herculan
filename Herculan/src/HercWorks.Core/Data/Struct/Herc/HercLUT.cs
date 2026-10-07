namespace HercWorks.Core.Data.Struct.Herc;

/// <summary>
/// Mech types by id. Ids 0-8 are the nine player chassis in the shell's type order
/// (<c>docs/retail/formats/herc-catalogs.md#the-chassis-type-space</c>); ids 0-20 follow retail
/// <c>nam\MECHS.NAM</c> one-for-one, and <see cref="AbbrevDat"/> is that name, the stem of the
/// mech's DBSIM data files (<c>OUTLAW.DMG</c>). Id 21, <see cref="Skimmer"/>, is not in
/// <c>MECHS.NAM</c>: it is <c>FLYERS.NAM</c> index 0.
/// </summary>
public sealed class HercLUT {
	public static readonly HercLUT Outlaw = new(0, "Outlaw", 3, "OUTLAW");
	public static readonly HercLUT RaptorII = new(1, "Raptor II", 5, "RAPTOR2");
	public static readonly HercLUT Tomahawk = new(2, "Tomahawk", 5, "TOMAHAWK");
	public static readonly HercLUT Samson = new(3, "Samson", 8, "SAMSON");
	public static readonly HercLUT Colossus = new(4, "Colossus", 9, "COLOSSUS");
	public static readonly HercLUT Apocalypse = new(5, "Apocalypse", 9, "APOCA");
	public static readonly HercLUT Ogre = new(6, "Ogre", 10, "OGRE");
	public static readonly HercLUT Maverick = new(7, "Maverick", 4, "MAVERICK");
	public static readonly HercLUT Razor = new(8, "Razor", 7, "RAZOR");
	public static readonly HercLUT Mongoose = new(9, "MONGOOSE", 3, "MONGOOSE");
	public static readonly HercLUT Stingray = new(10, "STINGRAY", 3, "STINGRAY");
	public static readonly HercLUT Mirimac = new(11, "MIRIMAC", 5, "MIRIMAC");
	public static readonly HercLUT Ramses = new(12, "RAMSES", 4, "RAMSES");
	public static readonly HercLUT Achilles = new(13, "ACHILLES", 6, "ACHILLES");
	public static readonly HercLUT Hyperion = new(14, "HYPERION", 9, "HYPERION");
	public static readonly HercLUT Pitbull = new(15, "PITBULL", 1, "PITBULL");
	public static readonly HercLUT Spider = new(16, "SPIDER", 1, "SPIDER");
	public static readonly HercLUT Diablo = new(17, "DIABLO", 8, "DIABLO");
	public static readonly HercLUT Headhunter = new(18, "HEADHUNTER", 9, "HEADHUNT");
	public static readonly HercLUT Scarab = new(19, "SCARAB", 4, "SCARAB");
	public static readonly HercLUT Cerberus = new(20, "CERBERUS", 9, "CERBERUS");
	public static readonly HercLUT Skimmer = new(21, "SKIMMER", 3, "SKIMMER");

	private static readonly IReadOnlyList<HercLUT> All = new[]
	{
		Outlaw, RaptorII, Tomahawk, Samson, Colossus, Apocalypse, Ogre, Maverick, Razor, Mongoose,
		Stingray, Mirimac, Ramses, Achilles, Hyperion, Pitbull, Spider, Diablo, Headhunter, Scarab,
		Cerberus, Skimmer
	};

	private static readonly Dictionary<short, HercLUT> ById = All.ToDictionary(h => h.Id);

	public string Name { get; set; }
	public short Id { get; set; }
	/// <summary>
	/// Mount capacity. For the nine player chassis (ids 0-8) this is VSHELL's in-code table that
	/// <c>Herc_CapacityForType</c> (<c>00410d54</c>) reads, which is what a save's <c>+0x4c</c> holds
	/// — not <c>herc_inf.dat</c>'s display figure, which says 4 for the Raptor II. See
	/// <c>docs/retail/formats/herc-catalogs.md#rejected-readings</c>. The figures for ids 9-21 have no
	/// traced source.
	/// </summary>
	public short HardpointMax { get; set; }
	public string AbbrevDat { get; set; }

	/// <summary>Ids 0-8, the nine chassis the shell builds — the ones whose <see cref="HardpointMax"/> is traced.</summary>
	public bool IsPlayerChassis => Id <= Razor.Id;

	private HercLUT(short id, string name, short hardpointMax, string abbrevDat) {
		Name = name;
		Id = id;
		HardpointMax = hardpointMax;
		AbbrevDat = abbrevDat;
	}

	public static HercLUT? GetById(short id) => ById.GetValueOrDefault(id);

	public static IReadOnlyList<HercLUT> Values() => All;

	public static HercLUT? GetByName(string name) =>
		All.FirstOrDefault(h => string.Equals(name, h.Name, StringComparison.OrdinalIgnoreCase));

	public static HercLUT? GetByAbbrev(string abbrev) =>
		All.FirstOrDefault(h => string.Equals(abbrev, h.AbbrevDat, StringComparison.OrdinalIgnoreCase));

	public override string ToString() => Name;
}
