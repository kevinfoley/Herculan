namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - ES1's wld\worldN.wld, in SIMVOL0.VOL — a Metaltech: Earthsiege theater's sky, horizon strip, clouds,
/// scattered points, distance bands and ground texture (docs/es1/formats/wld-world.md#the-layout). ES2's file of the
/// same name is <see cref="WorldData"/>, whose layout this one is less two leading <c>int16</c>. Read by
/// <see cref="Io.Transform.Dbsim.Es1WorldDataTransformer"/>.
/// </summary>
public class Es1WorldData {
	/// <summary>Shorts in <see cref="Header"/>.</summary>
	public const int HeaderShorts = 12;

	/// <summary>
	/// The 12 leading shorts, in file order (docs/es1/formats/wld-world.md#the-header-shorts): 0-7 the sky backdrop as
	/// ES2's, 8 <see cref="PointColor"/>, 9 <see cref="FlatSetSelector"/>, 10 and 11 unexplained.
	/// </summary>
	public short[] Header { get; set; } = new short[HeaderShorts];

	/// <summary>Header short 0, ES2's <see cref="WorldData.HorizonGap"/>.</summary>
	public short HorizonGap => HeaderShort(0);

	/// <summary>Header short 1, ES2's <see cref="WorldData.ZenithColor"/>.</summary>
	public short ZenithColor => HeaderShort(1);

	/// <summary>Header short 2, ES2's <see cref="WorldData.SkyBandHeight"/>.</summary>
	public short SkyBandHeight => HeaderShort(2);

	/// <summary>Header short 3, ES2's <see cref="WorldData.SkyBandCount"/>.</summary>
	public short SkyBandCount => HeaderShort(3);

	/// <summary>
	/// Header short 4, ES2's <see cref="WorldData.GroundColor"/>; ES1 also builds the ground texture's fade tables
	/// from it (docs/es1/formats/wld-world.md#the-ground-texture).
	/// </summary>
	public short GroundColor => HeaderShort(4);

	/// <summary>Header short 5, ES2's <see cref="WorldData.GroundBandHeight"/>.</summary>
	public short GroundBandHeight => HeaderShort(5);

	/// <summary>Header short 6, ES2's <see cref="WorldData.GroundBandCount"/>.</summary>
	public short GroundBandCount => HeaderShort(6);

	/// <summary>Header short 7, ES2's <see cref="WorldData.HorizonOffset"/>.</summary>
	public short HorizonOffset => HeaderShort(7);

	/// <summary>
	/// Header short 8: the scattered points' palette index, which the load stores at the points object's
	/// <c>+0x15</c> (docs/es1/formats/wld-world.md#the-scattered-points).
	/// </summary>
	public short PointColor => HeaderShort(8);

	/// <summary>
	/// Header short 9, ES2's <see cref="WorldData.FlatSetSelector"/>: 0 loads <c>dts\flat</c>, anything else
	/// <c>dts\flat2</c> (docs/es1/formats/wld-world.md#the-ground-shape-set).
	/// </summary>
	public short FlatSetSelector => HeaderShort(9);

	private short HeaderShort(int index) => Header.Length > index ? Header[index] : (short)0;

	/// <inheritdoc cref="WorldData.DistanceBandsA"/>
	public int[] DistanceBandsA { get; set; } = [];

	/// <inheritdoc cref="WorldData.DistanceBandsB"/>
	public int[] DistanceBandsB { get; set; } = [];

	/// <inheritdoc cref="WorldData.RampRows"/>
	public short RampRows { get; set; }

	/// <inheritdoc cref="WorldData.RampRows"/>
	public short RampColumns { get; set; }

	/// <inheritdoc cref="WorldData.RampTableA"/>
	public int[] RampTableA { get; set; } = [];

	/// <inheritdoc cref="WorldData.BetweenRampTables"/>
	public short BetweenRampTables { get; set; }

	/// <inheritdoc cref="WorldData.RampTableB"/>
	public int[] RampTableB { get; set; } = [];

	/// <inheritdoc cref="WorldData.RampExtraA"/>
	public byte[] RampExtraA { get; set; } = new byte[4];

	/// <inheritdoc cref="WorldData.RampExtraA"/>
	public byte[] RampExtraB { get; set; } = new byte[4];

	/// <summary>
	/// The number of clouds the load builds from <see cref="CloudBank"/> (docs/es1/formats/wld-world.md#the-clouds).
	/// ES2's file has an <c>int16</c> here that it reads and does not use.
	/// </summary>
	public short CloudCount { get; set; }

	/// <summary>
	/// The number of scattered points; 0 builds none (docs/es1/formats/wld-world.md#the-scattered-points). ES2's file
	/// has an <c>int16</c> here that it reads and does not use.
	/// </summary>
	public short PointCount { get; set; }

	/// <inheritdoc cref="WorldData.Trailer2"/>
	public int DistanceOffsetSmall { get; set; }

	/// <inheritdoc cref="WorldData.Trailer3"/>
	public int DistanceOffsetLarge { get; set; }

	/// <summary>
	/// The sky backdrop's bitmap bank, <c>dba\&lt;name&gt;</c>, loaded into its <c>+0x50</c> when not empty
	/// (docs/es1/formats/wld-world.md#the-load).
	/// </summary>
	public string SkyBank { get; set; } = "";

	/// <summary>The clouds' bank, <c>dba\&lt;name&gt;</c>; empty builds no clouds.</summary>
	public string CloudBank { get; set; } = "";

	/// <summary>The impact palette, <c>dpl\&lt;name&gt;</c>, loaded when not empty.</summary>
	public string ImpactPalette { get; set; } = "";

	/// <summary>The ground texture, <c>dba\&lt;name&gt;.64k</c> (docs/es1/formats/wld-world.md#the-ground-texture).</summary>
	public string GroundTextureBank { get; set; } = "";

	/// <summary>The ground texture's fade distances, <c>dat\&lt;name&gt;</c> (docs/es1/formats/wld-world.md#the-ground-texture).</summary>
	public string GroundFadeFile { get; set; } = "";
}
