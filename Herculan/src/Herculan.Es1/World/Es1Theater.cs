namespace Herculan.Es1.World;

/// <summary>
/// One of the simulator's four theaters and the resources it names: the shape library and shape table its zones
/// place shapes from (docs/es1/formats/zone-terrain.md#which-zone-and-which-theater), and the palette and world file
/// that header words 8 and 9 pick among its six (docs/es1/formats/wld-world.md#which-file).
/// </summary>
/// <param name="Index">Header word 0, an index into <see cref="Names"/>.</param>
/// <param name="WorldVariant">Header word 9, <i>v</i>.</param>
/// <param name="WorldDetail">Header word 8, <i>d</i>.</param>
public sealed record Es1Theater(int Index, int WorldVariant, int WorldDetail) {
	/// <summary>
	/// <c>Terrain_TheaterNames</c> (<c>ES1DBSIM 000924a0</c>), the simulator's order. The shell's map screen orders
	/// them differently (docs/es1/formats/zone-terrain.md#open).
	/// </summary>
	public static IReadOnlyList<string> Names { get; } = ["desert", "temper", "ice", "urban"];

	/// <summary>How many world variants (<see cref="WorldVariant"/>) a theater's six world files hold beside its two details.</summary>
	public const int WorldVariants = 3;

	/// <summary>How many values <see cref="WorldDetail"/> takes.</summary>
	public const int WorldDetails = 2;

	/// <summary>The theater's name, which names its shape library <c>dgs\&lt;name&gt;</c>.</summary>
	public string Name => Names[Index];

	/// <summary>The palette, <c>dpl\world&lt;M&gt;</c>, M = 6<i>t</i> + 2<i>v</i>.</summary>
	public string PaletteName => $"WORLD{6 * Index + 2 * WorldVariant}";

	/// <summary>The world file, <c>wld\world&lt;N&gt;</c>, N = M + <i>d</i>.</summary>
	public string WorldName => $"WORLD{6 * Index + 2 * WorldVariant + WorldDetail}";

	/// <summary>Whether the three words name a theater and a world file the simulator's tables hold.</summary>
	public bool IsValid =>
		Index >= 0 && Index < Names.Count
		&& WorldVariant >= 0 && WorldVariant < WorldVariants
		&& WorldDetail >= 0 && WorldDetail < WorldDetails;
}
