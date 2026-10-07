namespace HercWorks.Core.Data.Struct;

/// <summary>
/// A <c>career.dat</c> stage's campaign index. 0-4 are the five campaign chapters, named by the
/// shell's sector words; 5 is the practice-and-demo stage, which the record sheet labels with the
/// word before them, <c>Razor</c>. See docs/retail/shell/campaign-loop.md#the-campaign-table--gamcareerdat.
/// </summary>
public sealed class MissionSector {
	public static readonly MissionSector Razr = new("RAZR", 5);
	public static readonly MissionSector Alph = new("ALPHA", 0);
	public static readonly MissionSector Delt = new("DELTA", 1);
	public static readonly MissionSector Omic = new("OMICRON", 2);
	public static readonly MissionSector Brav = new("BRAVO", 3);
	public static readonly MissionSector Luna = new("LUNA", 4);

	private static readonly IReadOnlyList<MissionSector> All = new[] { Razr, Alph, Delt, Omic, Brav, Luna };
	private static readonly Dictionary<int, MissionSector> ById = All.ToDictionary(m => (int)m.Id);

	public string Val { get; }
	public short Id { get; }

	private MissionSector(string val, short id) {
		Val = val;
		Id = id;
	}

	public static MissionSector? GetById(int id) => ById.GetValueOrDefault(id);
}
