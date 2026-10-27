namespace Herculan.Engine.Editor;

/// <summary>A ready-made base the Toolbox's Bases tab offers: what it is called, its base formation, and what stands in each member slot.</summary>
/// <param name="Name">The editor's own name for it; no retail file names a formation.</param>
/// <param name="FormationId">Its <c>dat\BFORMS.DAT</c> formation, one with a pad layout (<see cref="World.BaseFormationTable.LayoutFor"/>).</param>
/// <param name="Types">The <c>dat\BASES.DAT</c> type of each member, by member slot from 0: structures, and the ground vehicles some retail bases hold among them.</param>
internal sealed record BaseTemplate(string Name, int FormationId, IReadOnlyList<int> Types);

/// <summary>
/// The bases the Toolbox's Bases tab offers (<see cref="StructurePlacer.AddBase"/>): one per <c>dat\BFORMS.DAT</c> formation
/// with a pad layout, eleven in all. The retail missions use each pad formation as a fixed template, and each type list
/// here is the building list retail places in that formation most often, by member slot (<c>HercWorks.Query formations</c>),
/// its ground vehicles included where retail has them. The names are the editor's own: no retail file names a formation.
/// </summary>
internal static class BaseTemplates {
	public static IReadOnlyList<BaseTemplate> All { get; } = [
		new("Refinery with fuel tanks", 5, [31, 35, 39, 39, 39, 35, 15, 15, 15, 39, 39, 35]),
		new("Cybrid core compound", 7, [36, 38, 37, 38, 37, 38, 37, 39, 35, 35, 40, 32, 39, 30, 32]),
		new("Power station depot", 8, [11, 16, 19, 19, 45, 45, 9, 11]),
		new("Power station and refineries", 9, [33, 41, 41, 41, 39, 39, 31, 31, 31, 14, 14]),
		new("Mining facility", 10, [2, 19, 2, 19, 0, 0, 0]),
		new("Hangar base", 11, [11, 18, 18, 47, 19, 19, 8, 4, 4, 8]),
		new("Bunker base", 12, [17, 18, 18, 18, 11, 16, 3, 8, 8, 19, 6, 11]),
		new("Listening post", 13, [5, 8, 45, 18, 19, 18, 8]),
		new("Herc factory", 14, [46, 1, 1, 3, 3, 19, 19, 0, 0, 0]),
		new("Small Cybrid compound", 15, [37, 38, 37, 32, 39, 35, 32, 56, 55]),
		new("Large Cybrid mine", 16, [32, 36, 36, 26, 26, 39, 39, 39, 35, 32]),
	];
}
