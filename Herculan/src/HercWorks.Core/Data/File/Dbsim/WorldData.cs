namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/WLD/ worldX.wld — one theater's environment descriptor: sky and fog parameters,
/// two distance-band tables, a colour-ramp pair, and the resource names the terrain wears.
///
/// <para><b>The file is variable-length, not a fixed struct.</b> Every array in it is preceded by
/// its own count or dimension, so the layout is a walk, matching <c>maybe_World_LoadTheater</c>
/// (<c>0042e010</c>) read for read:</para>
/// <code>
/// 14 x int16                       -- 0-7 the sky backdrop (hzline), 9 the ground-shape set
/// int32 count, count x int32       -- distance bands A
/// int32 count, count x int32       -- distance bands B
/// int16 rampRows, int16 rampColumns
/// rampColumns x int32              -- ramp table A
/// int16                            -- loose field between the tables
/// rampColumns x int32              -- ramp table B
/// 4 bytes, 4 bytes                 -- two more entries expanded the same way (Palette_InterpolateIndexRanges, 00430d08)
/// int16, int16, int32, int32
/// 5 x null-terminated string
/// </code>
///
/// <para>A fixed-offset reading of the middle — two raw blocks of 190 and 48 bytes — parses every
/// retail file only because their arrays happen to have the same lengths; the counts are what size
/// it.</para>
///
/// <para>The five trailing strings are constant in retail data except the third and fourth:
/// <c>world24</c>, <c>clouds2</c>, <c>impact&lt;n&gt;</c> (one per world file), the terrain texture
/// bank (<c>urban</c>, <c>bsnow</c>, <c>volcan</c>, <c>ice</c>, <c>moon</c>), then literally
/// <c>tex</c> — five separately terminated strings, not one dotted name. The fourth is the one
/// <c>Terrain_BindTextureBank</c> receives. Layout: docs/retail/formats/terrain-texturing.md.</para>
/// </summary>
public class WorldData {
	/// <summary>Shorts in <see cref="Header"/>.</summary>
	public const int HeaderShorts = 14;

	/// <summary>
	/// The 14 leading shorts, in file order. Shorts 0-7 build the theater's sky backdrop, the
	/// <c>hzline</c> (<see cref="HorizonGap"/> through <see cref="HorizonOffset"/>); short 9 is
	/// <see cref="FlatSetSelector"/>. Only shorts 3 and 4 vary across the ten retail files.
	/// </summary>
	public short[] Header { get; set; } = new short[HeaderShorts];

	/// <summary>
	/// Header short 0, the <c>hzline</c>'s <c>+0x6c</c>: half of it is how many rows above the horizon
	/// line the first band past the horizon colour starts. 2 in retail data. See
	/// docs/retail/formats/distance-fog-and-sky.md, "The object".
	/// </summary>
	public short HorizonGap => HeaderShort(0);

	/// <summary>Header short 1: the sky's zenith palette index, and the base its bands count up from. 208 in retail data.</summary>
	public short ZenithColor => HeaderShort(1);

	/// <summary>Header short 2, <c>+0x54</c>: the sky's band height in screen rows. 6 in retail data.</summary>
	public short SkyBandHeight => HeaderShort(2);

	/// <summary>
	/// Header short 3, <c>+0x58</c>: how many sky bands, the horizon colour and the zenith fill
	/// included. 16 in <c>WORLD0</c>, <c>WORLD2</c> and <c>WORLD6</c>, 15 in the rest.
	/// </summary>
	public short SkyBandCount => HeaderShort(3);

	/// <summary>
	/// The horizon colour, <c>+0x28</c>: <c>Hzline_SetColors</c> (<c>0042ebbc</c>) stores
	/// <see cref="ZenithColor"/> + <see cref="SkyBandCount"/> - 1, and it paints both the band at the
	/// line and everything below it.
	/// </summary>
	public int HorizonColor => (ZenithColor + SkyBandCount - 1) & 0xffffff;

	/// <summary>
	/// Header short 4, <c>+0x2c</c>: the first ground colour. Read only by the ground-band branch of
	/// <c>Hzline_FillGround</c> (<c>0042f0b0</c>), which no frame takes.
	/// </summary>
	public short GroundColor => HeaderShort(4);

	/// <summary>Header short 5, <c>+0x5c</c>: the ground band height. See <see cref="GroundColor"/>.</summary>
	public short GroundBandHeight => HeaderShort(5);

	/// <summary>Header short 6, <c>+0x60</c>: the ground band count. See <see cref="GroundColor"/>.</summary>
	public short GroundBandCount => HeaderShort(6);

	/// <summary>
	/// Header short 7, <c>+0x30</c>: the horizon line's vertical offset in rows, which the draw applies
	/// one and a half times (<c>Hzline_BuildHorizon</c> adds it, <c>Hzline_DrawWithOffset</c> half of
	/// it again). 0 in retail data.
	/// </summary>
	public short HorizonOffset => HeaderShort(7);

	private short HeaderShort(int index) => Header.Length > index ? Header[index] : (short)0;

	/// <summary>
	/// <see cref="Header"/> short 9, byte 18 — which ground-shape set the theater loads.
	/// <c>World_LoadTheater</c> reads it into <c>World_FlatSetSelector</c> (<c>0049aeea</c>) and
	/// hands it to <c>FlatObj_LoadResources</c> (<c>004097a8</c>): 0 loads <c>flat</c>, anything else
	/// <c>flat2</c>. 1 in all ten retail files. See docs/retail/simulation/ground-shapes.md.
	/// </summary>
	public short FlatSetSelector => HeaderShort(9);

	/// <summary>
	/// The distance-band thresholds for an object with a type tag (<c>WorldShades_BandsTagged</c>,
	/// <c>004cfd84</c>). See docs/retail/formats/distance-fog-and-sky.md, "Distance colour bands".
	/// </summary>
	public int[] DistanceBandsA { get; set; } = Array.Empty<int>();

	/// <summary>
	/// The distance-band thresholds for type tag 0 (<c>WorldShades_BandsTag0</c>, <c>004cfd88</c>).
	/// See docs/retail/formats/distance-fog-and-sky.md, "Distance colour bands".
	/// </summary>
	public int[] DistanceBandsB { get; set; } = Array.Empty<int>();

	/// <summary>
	/// Ramp dimensions: the band count and the column count the two ramp tables expand into
	/// <c>WorldShades_LevelRanges</c>. See docs/retail/formats/terrain-texturing.md, "The world&lt;N&gt;
	/// descriptor — layout".
	/// </summary>
	public short RampRows { get; set; }

	/// <inheritdoc cref="RampRows"/>
	public short RampColumns { get; set; }

	/// <summary>First colour-ramp table, <see cref="RampColumns"/> entries.</summary>
	public int[] RampTableA { get; set; } = Array.Empty<int>();

	/// <summary>The loose short the original reads between the two ramp tables.</summary>
	public short BetweenRampTables { get; set; }

	/// <summary>Second colour-ramp table, also <see cref="RampColumns"/> entries.</summary>
	public int[] RampTableB { get; set; } = Array.Empty<int>();

	/// <summary>
	/// Two further 4-byte entries the original expands through the same helper as the ramp tables
	/// (<c>Palette_InterpolateIndexRanges</c>, <c>00430d08</c>) into <c>WorldShades_BlendRanges</c>
	/// (<c>004cfd80</c>). Kept raw. See docs/retail/formats/distance-fog-and-sky.md, "Distance colour
	/// bands".
	/// </summary>
	public byte[] RampExtraA { get; set; } = new byte[4];

	/// <inheritdoc cref="RampExtraA"/>
	public byte[] RampExtraB { get; set; } = new byte[4];

	/// <summary>The two loose shorts between the ramp section and the distance offsets.</summary>
	public short Trailer0 { get; set; }

	/// <inheritdoc cref="Trailer0"/>
	public short Trailer1 { get; set; }

	/// <summary>
	/// <c>WorldShades_DistanceOffsets</c> (<c>004cfd6c</c>): the offset a tag-5 object's distance
	/// takes before its band is counted, for a radius under 5000. See
	/// docs/retail/formats/distance-fog-and-sky.md, "Distance colour bands".
	/// </summary>
	public int Trailer2 { get; set; }

	/// <summary>The same offset for a tag-5 object whose radius is 5000 or more.</summary>
	public int Trailer3 { get; set; }

	/// <summary>World type tag, <c>world24</c> in every retail file.</summary>
	public string? WorldTypeStr { get; set; }

	/// <summary>Cloud layer name, <c>clouds2</c> in every retail file.</summary>
	public string? CloudStr { get; set; }

	/// <summary>Impact/explosion palette base name, <c>impact0</c>..<c>impact9</c>.</summary>
	public string? ImpactStr { get; set; }

	/// <summary>
	/// Terrain texture bank base name — the string <c>Terrain_BindTextureBank</c> receives, which
	/// loads <c>dba\&lt;name&gt;.DBA</c>.
	/// </summary>
	public string? TextureBaseName { get; set; }

	/// <summary>Literally <c>tex</c> in every retail file; separately terminated, not a suffix.</summary>
	public string? TextureExtension { get; set; }

	public WorldData() { }
}
