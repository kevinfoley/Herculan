using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The five theaters a mission can be played on, as the editor names them. No retail text names a theater, so each is
/// named by the terrain bank its day descriptor loads (<see cref="TheaterDescriptor.TerrainBankName"/>,
/// docs/retail/formats/wld-world.md): the data's own word for it.
/// </summary>
internal static class TheaterNames {
	/// <summary>How many theaters there are: a day and a night descriptor each.</summary>
	public const int Count = TheaterDescriptor.Count / 2;

	/// <summary>Each theater's label, in theater order: its number and its terrain bank, or its number alone when the install has no descriptor for it.</summary>
	public static IReadOnlyList<string> Labels(GameContent simulator) =>
		Enumerable.Range(0, Count).Select(theater => {
			try {
				return $"{theater}: {TheaterDescriptor.Load(simulator, theater).TerrainBankName}";
			} catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) {
				return $"{theater}";
			}
		}).ToList();
}
