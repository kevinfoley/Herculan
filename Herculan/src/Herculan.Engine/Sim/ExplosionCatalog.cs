using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>dat\EXPLOS.DAT</c> — every impact effect the simulation can put on screen, and which shape
/// each one is. The counterpart of <see cref="BulletCatalog"/> for what happens where a shot lands.
///
/// <para><c>Explosion_LoadResources</c> (<c>00407b54</c>), the <c>EXPLO.CPP</c> subsystem's loader, opens three things once at
/// startup: fifteen <c>dba\EXPLO&lt;n&gt;.DBA</c> banks, <c>dts\EXPLOS.DTS</c>, and this table. Every
/// root of that shape file is a flipbook of billboards (see
/// <see cref="Render.DtsSpriteBuilder"/>), and the table's first half says which bank each root is
/// drawn from while its second half is the effect types themselves.</para>
///
/// <para>The types are what <c>PROJ.DAT</c>'s three <c>ImpactFX</c> arrays hold: a shot picks one of
/// four ids from whichever array matches what it hit, and that id is a row here. See
/// <see cref="ImpactEffect"/>.</para>
/// </summary>
public sealed class ExplosionCatalog {
	/// <summary>The resource folder and name <c>Explosion_LoadResources</c> (<c>00407b54</c>) opens, by the literal <c>explos</c>.</summary>
	public const string ResourceFolder = "dat";

	/// <inheritdoc cref="ResourceFolder" />
	public const string TableResource = "EXPLOS.DAT";

	/// <summary>The shape file the table's first half indexes, opened by the same literal.</summary>
	public const string ShapeLibraryName = "EXPLOS.DTS";

	/// <summary>
	/// The bank name template, <c>explo666</c> at <c>00497ba0</c> — the loader overwrites from the
	/// sixth character on with the bank's index, so the names are <c>EXPLO0</c>..<c>EXPLO14</c>.
	/// </summary>
	public const string TextureBankPrefix = "EXPLO";

	private readonly ExplosionData _table;
	private int[] _frameCounts = Array.Empty<int>();

	private ExplosionCatalog(ExplosionData table) {
		_table = table;
	}

	/// <summary>How many effect types the table holds.</summary>
	public int TypeCount => _table.Types?.Length ?? 0;

	/// <summary>How many shapes it names — one per root of <see cref="ShapeLibraryName"/>.</summary>
	public int ShapeCount => _table.Shapes?.Length ?? 0;

	/// <summary>
	/// Parses the table. Returns null when the resource is missing or unreadable, in which case
	/// nothing spawns an impact effect at all — the same silence an unported branch gives.
	/// </summary>
	public static ExplosionCatalog? Load(byte[]? explosDat) =>
		explosDat != null
			&& new ExplosionDataTransformer().Parse(explosDat) is ExplosionData { Types: not null } table
			? new ExplosionCatalog(table)
			: null;

	/// <summary>The type row for <paramref name="typeId"/>, or null when the id is outside the table.</summary>
	public ExplosionTypeEntry? Type(int typeId) =>
		_table.Types is { } types && typeId >= 0 && typeId < types.Length ? types[typeId] : null;

	/// <summary>
	/// The int16 <c>Explosion_TickUpdate</c> (<c>0040813c</c>) reads as frame <paramref name="frame"/>'s light
	/// intensity: the word at <c>row + 0x08 + frame * 2</c>, unbounded. Past the twelve ramp entries
	/// that is the row's proximity radius (low word, then high), its sound id and its object class,
	/// then the next row's fields — docs/retail/simulation/impact-effects.md, "Tick". Past the end of the
	/// table it answers 0, which is this engine's: the original reads whatever follows the table in
	/// memory, and no retail shape's flipbook runs that far.
	/// </summary>
	public short RampWord(int typeId, int frame) {
		int offset = typeId * TypeRowSize + RampOffset + frame * 2;
		if (offset < 0 || Type(offset / TypeRowSize) is not { } row) {
			return 0;
		}

		int field = offset % TypeRowSize;
		return field switch {
			0x00 => row.ShapeIndex,
			0x02 => row.FrameInterval,
			0x04 => row.GroundShape,
			0x06 => row.LightMode,
			< 0x20 => row.FrameIntensity[(field - RampOffset) / 2],
			0x20 => (short)row.ProximityRadius,
			0x22 => (short)(row.ProximityRadius >> 16),
			0x24 => row.SoundId,
			_ => row.ObjectClass,
		};
	}

	/// <summary>A type row's size, <c>Explosion_GetTypeRecord</c> (<c>00407b20</c>)'s stride.</summary>
	private const int TypeRowSize = 0x28;

	/// <summary>Where a type row's intensity ramp starts.</summary>
	private const int RampOffset = 0x08;

	/// <summary>The shape row for <paramref name="shapeIndex"/>, or null when it is outside the table.</summary>
	public ExplosionShapeEntry? Shape(int shapeIndex) =>
		_table.Shapes is { } shapes && shapeIndex >= 0 && shapeIndex < shapes.Length ? shapes[shapeIndex] : null;

	/// <summary>
	/// Tells the catalog how long each shape's flipbook is, which is what decides how long an effect
	/// lives: <c>Explosion_TickUpdate</c> (<c>0040813c</c>) steps the frame counter modulo the shape's own frame count and ends
	/// the effect on the tick it wraps back to zero.
	///
	/// <para>It has to be supplied rather than read here because the count is a property of
	/// <c>EXPLOS.DTS</c>, not of the table — the original reads it off the loaded shape too
	/// (<c>shape+0x20</c>'s per-sequence array). <see cref="Scene.MissionScene"/> builds the shapes
	/// and hands the counts over.</para>
	/// </summary>
	public void BindFrameCounts(IReadOnlyList<int> framesPerShape) {
		_frameCounts = framesPerShape.ToArray();
	}

	/// <summary>
	/// How many frames shape <paramref name="shapeIndex"/>'s flipbook has, or zero when it has none
	/// or the counts were never bound. An effect on a zero-frame shape is dropped on its first timer
	/// expiry, which is what the original's negative-sequence branch does.
	/// </summary>
	public int FrameCount(int shapeIndex) =>
		shapeIndex >= 0 && shapeIndex < _frameCounts.Length ? _frameCounts[shapeIndex] : 0;
}
