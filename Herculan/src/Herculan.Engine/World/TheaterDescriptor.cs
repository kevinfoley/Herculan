using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <summary>
/// One theater's <c>wld\WORLD&lt;n&gt;.WLD</c> descriptor — the file that names, in data rather than
/// in code, which texture bank the terrain wears.
///
/// <para>Decoded from <c>maybe_World_LoadTheater</c> (<c>0042e010</c>), which reads this file
/// field-by-field off a stream and finishes by handing one of its strings to
/// <c>Terrain_BindTextureBank</c>. See docs/retail/formats/terrain-texturing.md.</para>
///
/// <para><b>There are ten descriptors for five theaters.</b> The original builds the base name as
/// <c>world&lt;theaterIndex * 2 + variant&gt;</c>, and retail data pairs up exactly:
/// WORLD0/1 <c>urban</c>, WORLD2/3 <c>bsnow</c>, WORLD4/5 <c>volcan</c>, WORLD6/7 <c>ice</c>,
/// WORLD8/9 <c>moon</c>. Both indices come from <see cref="ScriptDatHeader"/>.</para>
///
/// <para>The file is parsed by <see cref="WorldData"/> in HercWorks.Core, which carries the layout.
/// Much of it is still undecoded — two int32 arrays, two colour-ramp tables sized by a row/column
/// pair, and several loose 16- and 32-bit fields — and is kept raw rather than guessed at; the
/// leading shorts that build the sky are named on <see cref="WorldData"/>. What <i>is</i> certain is the structure, because the walk consumes every one of the
/// ten retail files to its exact last byte, and the strings that fall out of the end are real
/// resource names (see <see cref="TerrainBankName"/> and <see cref="ImpactPaletteName"/>).</para>
/// </summary>
public sealed class TheaterDescriptor {
	/// <summary>Resource folder and extension (the game uses one word for both).</summary>
	public const string ResourceFolder = "wld";

	/// <summary>How many <c>WORLD&lt;n&gt;.WLD</c> files retail data ships.</summary>
	public const int Count = 10;

	private TheaterDescriptor(int index, string paletteName, string terrainBankName,
			string impactPaletteName, IReadOnlyList<string> allStrings, WorldData file) {
		Index = index;
		File = file;
		PaletteName = paletteName;
		TerrainBankName = terrainBankName;
		ImpactPaletteName = impactPaletteName;
		Strings = allStrings;
		FlatSetName = file.FlatSetSelector == 0 ? "FLAT" : "FLAT2";
	}

	/// <summary>
	/// The whole parsed file, for the fields this class does not lift out itself — the sky
	/// backdrop's eight shorts among them (<see cref="Content.SkyGradient"/>).
	/// </summary>
	public WorldData File { get; }

	/// <summary>
	/// The base name of the ground-shape set the theater loads, <c>dts\&lt;name&gt;.DTS</c> with
	/// <c>dba\&lt;name&gt;.DBA</c> bound to every root — <c>FlatObj_LoadResources</c>
	/// (<c>004097a8</c>)'s <c>flat</c> for a zero <see cref="WorldData.FlatSetSelector"/> and
	/// <c>flat2</c> otherwise. Every retail descriptor selects <c>FLAT2</c>, the only set that ships.
	/// See docs/retail/simulation/ground-shapes.md.
	/// </summary>
	public string FlatSetName { get; }

	/// <summary>Which <c>WORLD&lt;n&gt;</c> this is — <c>theaterIndex * 2 + variant</c>.</summary>
	public int Index { get; }

	/// <summary>
	/// The theater's palette, <c>dpl\WORLD&lt;n&gt;.DPL</c>. Not a string in the file — the name is
	/// the descriptor's own, and <c>maybe_World_LoadTheater</c> loads it as its very first act, which
	/// is what makes the retail WORLD0..9 <c>.DPL</c> family a per-theater palette set.
	/// </summary>
	public string PaletteName { get; }

	/// <summary>
	/// The terrain texture bank's base name (<c>urban</c>, <c>bsnow</c>, <c>volcan</c>, <c>ice</c>,
	/// <c>moon</c>) — the string the original passes to <c>Terrain_BindTextureBank</c>, which loads
	/// <c>dba\&lt;name&gt;.DBA</c>. All five exist in retail data and nothing else in the file names
	/// one of them.
	/// </summary>
	public string TerrainBankName { get; }

	/// <summary>
	/// The theater's impact/explosion palette base name (<c>impact&lt;n&gt;</c>), loaded from the
	/// <c>dpl</c> folder. Recorded because it independently corroborates the string order: all ten
	/// <c>IMPACT0.DPL</c>–<c>IMPACT9.DPL</c> exist and each descriptor names its own.
	/// </summary>
	public string ImpactPaletteName { get; }

	/// <summary>
	/// All five trailing strings in file order, for anyone decoding the rest. In retail data the
	/// other two are constant (<c>world24</c>, <c>clouds2</c>) plus a trailing <c>tex</c>; none names
	/// a file that exists, and the original reads them into a scratch buffer and discards them.
	/// </summary>
	public IReadOnlyList<string> Strings { get; }

	/// <summary>
	/// Loads <c>wld\WORLD&lt;theaterIndex * 2 + variant&gt;.WLD</c>.
	/// </summary>
	public static TheaterDescriptor Load(GameContent content, int theaterIndex, int variant = 0) =>
		LoadByWorldIndex(content, theaterIndex * 2 + variant);

	/// <summary>Loads a descriptor by its literal <c>WORLD&lt;n&gt;</c> number.</summary>
	public static TheaterDescriptor LoadByWorldIndex(GameContent content, int worldIndex) {
		if (worldIndex < 0 || worldIndex >= Count) {
			throw new ArgumentOutOfRangeException(nameof(worldIndex), worldIndex,
				$"Retail data has WORLD0..WORLD{Count - 1}.");
		}

		string name = $"WORLD{worldIndex}";
		return Parse(content.ReadRequired(ResourceFolder, name + ".WLD"), worldIndex, name);
	}

	/// <summary>
	/// Pulls the descriptor out of the parsed file. The walk itself — which is
	/// <c>maybe_World_LoadTheater</c>'s own read order — lives in
	/// <see cref="WorldDataTransformer"/>; everything this class needs is the string block at the end
	/// of it.
	/// </summary>
	private static TheaterDescriptor Parse(byte[] bytes, int worldIndex, string baseName) {
		if (new WorldDataTransformer().Parse(bytes) is not WorldData wld) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{baseName}.WLD is too short to be a theater descriptor.");
		}

		string[] strings = {
			wld.WorldTypeStr ?? string.Empty,
			wld.CloudStr ?? string.Empty,
			wld.ImpactStr ?? string.Empty,
			wld.TextureBaseName ?? string.Empty,
			wld.TextureExtension ?? string.Empty,
		};

		return new TheaterDescriptor(worldIndex, baseName, strings[3], strings[2], strings, wld);
	}
}
