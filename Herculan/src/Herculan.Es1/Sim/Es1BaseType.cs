using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Anim;
using Herculan.Engine.Content;

namespace Herculan.Es1.Sim;

/// <summary>
/// One structure type's <c>0x3e</c>-byte runtime record at <c>g_BaseTypeTable</c> (<c>ES1DBSIM 000bc18a</c>) + type ×
/// <c>0x3e</c>: <c>BASES.DAT</c>'s 54 bytes (docs/es1/formats/bases-dat.md#the-type-record) and the collision model slot
/// <c>+0x3a</c> the load sets (docs/es1/formats/bases-dat.md#the-load). Named here are the fields the structure code reads
/// (docs/es1/formats/bases-dat.md, "What the structure code reads them as").
/// </summary>
public sealed class Es1BaseType {
	internal Es1BaseType(short type, Es1BaseTypeRecord record, ColliderNode[]? collisionModel) {
		Type = type;
		Record = record;
		CollisionModel = collisionModel;
	}

	/// <summary>The type number, the record's place in <c>BASES.DAT</c>.</summary>
	public short Type { get; }

	/// <summary>The file's 54 bytes as <c>HercWorks.Core</c> reads them.</summary>
	public Es1BaseTypeRecord Record { get; }

	/// <summary><c>+0x00</c>, the type's index, which the status screen draws and names the structure by.</summary>
	public short Index => Record.TypeIndex;

	/// <summary><c>+0x02</c>, the shape's index in its set.</summary>
	public short ShapeIndex => Record.ShapeIndex;

	/// <summary><c>+0x04</c>, the wreck's index in <c>dgs\bhulks</c>.</summary>
	public short HulkIndex => Record.HulkTypeIndex;

	/// <summary>
	/// <c>+0x06</c>, the animation thread count; not 0 also picks the animated shape sets and installs the collision model.
	/// </summary>
	public short ThreadCount => Record.ShapeSet;

	/// <summary><c>+0x08</c>, the hit points.</summary>
	public short HitPoints => Word(0x08);

	/// <summary><c>+0x0c</c>–<c>+0x10</c>, the point, in the structure's frame, of the collapse's large explosion and its smoke.</summary>
	public (short X, short Y, short Z) CollapsePoint => Triple(0x0c);

	/// <summary><c>+0x12</c>–<c>+0x16</c>, the center of the box the collapse's small explosions are scattered in.</summary>
	public (short X, short Y, short Z) ScatterCenter => Triple(0x12);

	/// <summary><c>+0x18</c>–<c>+0x1c</c>, the half-extents of that box.</summary>
	public (short X, short Y, short Z) ScatterHalfExtents => Triple(0x18);

	/// <summary><c>+0x1e</c>–<c>+0x22</c>, the weak spot's center, in the structure's frame.</summary>
	public (short X, short Y, short Z) WeakSpot => Triple(0x1e);

	/// <summary><c>+0x24</c>, the weak spot's radius.</summary>
	public short WeakSpotRadius => Word(0x24);

	/// <summary><c>+0x26</c> and <c>+0x28</c>, the two threads' playback rates.</summary>
	public short ThreadRate(int thread) => Word(0x26 + 2 * thread);

	/// <summary><c>+0x2a</c>, the cell sequence a standing structure steps, −1 for none.</summary>
	public short CellSequence => Word(0x2a);

	/// <summary><c>+0x2c</c>, the cell sequence's frame interval in counts.</summary>
	public short CellInterval => Word(0x2c);

	/// <summary><c>+0x30</c>, the body radius of a type with a collision model.</summary>
	public short BodyRadius => Word(0x30);

	/// <summary><c>+0x32</c>, how far up the structure anything aiming at it aims.</summary>
	public short AimHeight => Word(0x32);

	/// <summary><c>+0x34</c>, 1 on the armed type, which the HERCs' AI and the outnumbered damage test.</summary>
	public short Word34 => Word(0x34);

	/// <summary>
	/// <c>+0x3a</c>, the type's 6-byte slot of <c>000bc194</c> holding its <c>dat\basecol</c> model, which the load sets
	/// only for a type whose <see cref="ThreadCount"/> is not 0; null for the others.
	/// </summary>
	public ColliderNode[]? CollisionModel { get; }

	/// <summary>Whether <c>+0x3a</c> is not 0, the test the structures' gathers and the body radius make.</summary>
	public bool HasCollisionModel => CollisionModel != null;

	/// <summary>The <c>int16</c> at <paramref name="offset"/>, one of <c>+0x08</c> to <c>+0x34</c>.</summary>
	public short Word(int offset) => Record.Fields08[(offset - 0x08) / 2];

	/// <summary>The three <c>int16</c> from <paramref name="offset"/>.</summary>
	private (short X, short Y, short Z) Triple(int offset) => (Word(offset), Word(offset + 2), Word(offset + 4));
}

/// <summary>
/// One shape a structure's instance can hold (its <c>+4</c>): the object <c>BaseType_ResolveShape</c> (<c>000636a4</c>)
/// loads from a set, or a wreck of <c>dgs\bhulks</c>. A <c>.DGS</c> record is a <c>GridShape</c>, which carries its own
/// cell sequences and collision volume and wraps its geometry; a <c>dts\bases_an</c> or <c>bases_ae</c> root is a
/// <c>TSShape</c>, with no volume.
/// </summary>
public sealed class Es1BaseShape {
	private Es1BaseShape(string key, TSObject? geometry, short[]? sequences, Es1GridShape? volume, short radius,
			ShapeAnimation? animation, DynamixBitmapArray? textureBank) {
		Key = key;
		Geometry = geometry;
		Sequences = sequences;
		Volume = volume;
		Radius = radius;
		Animation = animation;
		TextureBank = textureBank;
	}

	/// <summary>The set and index it was loaded from, which names it.</summary>
	public string Key { get; }

	/// <summary>The geometry it is drawn from, or null for a record with none.</summary>
	public TSObject? Geometry { get; }

	/// <summary><c>+0x20</c>, each cell sequence's frame count, which the cell step wraps on.</summary>
	public short[]? Sequences { get; }

	/// <summary>The collision volume the walk and ray tests read, or null for one whose grid is empty or a <c>.DTS</c> root.</summary>
	public Es1GridShape? Volume { get; }

	/// <summary><c>+8</c>, the bounding radius, which slot <c>+0x14</c> and the shape tests read.</summary>
	public short Radius { get; }

	/// <summary>The animation data of a <c>.DTS</c> root, which an animated instance's threads play; null for a <c>.DGS</c> record.</summary>
	public ShapeAnimation? Animation { get; }

	/// <summary><c>+0x26</c>, the bank its textured faces take; null for none.</summary>
	public DynamixBitmapArray? TextureBank { get; }

	/// <summary>The frame count of cell sequence <paramref name="sequence"/>, the shape's <c>+0x20</c> entry; 0 past the array.</summary>
	public int FrameCount(int sequence) =>
		Sequences != null && sequence >= 0 && sequence < Sequences.Length ? (ushort)Sequences[sequence] : 0;

	internal static Es1BaseShape FromGrid(string key, GridShape shape, DynamixBitmapArray? bank) =>
		new(key, shape.Geometry, shape.SequenceList, shape.Collision.IsSolid ? new Es1GridShape(shape.Collision) : null,
			shape.BoundingRadius, null, bank);

	internal static Es1BaseShape FromRoot(string key, TSObject root, DynamixBitmapArray? bank) =>
		new(key, root, (root as TSShape)?.SequenceList, null, root is TSBasePart part ? (short)part.Radius : (short)0,
			ShapeAnimation.FromRoot(root), bank);
}

/// <summary>
/// <c>base.cpp</c>'s tables as <c>Base_LoadResources</c> (<c>ES1DBSIM 000046ac</c>) loads them at subsystem phase 2
/// (docs/es1/formats/bases-dat.md#the-load) and <c>BaseType_LoadShape</c> (<c>000045a4</c>) fills them
/// (docs/es1/simulation/structures.md#loading-the-shapes--basetype_loadshape-000045a4): the type records, the formations,
/// the wrecks, the texture bank and the four shape tables.
/// </summary>
public sealed class Es1BaseTypes {
	/// <summary>The four shape sets, by (animated, enemy): <c>dgs\bases</c>, <c>dgs\bases_e</c>, <c>dts\bases_an</c>, <c>dts\bases_ae</c>.</summary>
	public const string StaticSet = "BASES", StaticEnemySet = "BASES_E", AnimatedSet = "BASES_AN", AnimatedEnemySet = "BASES_AE";

	/// <summary>The wrecks' set and the bank the standing shapes take.</summary>
	public const string HulkSet = "BHULKS", BankName = "BASETEX";

	private readonly GameContent _content;
	private readonly Dictionary<string, GridShapeLibrary?> _grids = new();
	private readonly Dictionary<string, DynamixThreeSpaceModel?> _roots = new();
	private readonly Dictionary<(string Set, short Index), Es1BaseShape?> _tables = new();

	private Es1BaseTypes(GameContent content) {
		_content = content;
	}

	/// <summary><c>g_BaseTypeTable</c> (<c>000bc18a</c>), its count <c>000bc188</c>.</summary>
	public IReadOnlyList<Es1BaseType> Types { get; private init; } = [];

	/// <summary><c>dat\bforms</c>' formations, <c>000bc190</c>, its count <c>000bc18e</c>.</summary>
	public Es1BaseFormations Formations { get; private init; } = new();

	/// <summary><c>BaseHulkShapes</c> (<c>000bc180</c>), every root of <c>dgs\bhulks</c>, loaded with no bank.</summary>
	public IReadOnlyList<Es1BaseShape> Hulks { get; private init; } = [];

	/// <summary>
	/// <c>BaseTexture</c> (<c>000bc184</c>), <c>dba\basetex</c>, which the load reads only while <c>g_LowMemory</c> is 0, always
	/// so here (<see cref="Input.Es1Preferences.LowMemory"/>); null when the install lacks it.
	/// </summary>
	public DynamixBitmapArray? Texture { get; private init; }

	/// <summary>The record of <paramref name="type"/>, or null outside the table.</summary>
	public Es1BaseType? this[short type] => type >= 0 && type < Types.Count ? Types[type] : null;

	/// <summary>The wreck <paramref name="index"/> of <see cref="Hulks"/>, or null outside them.</summary>
	public Es1BaseShape? Hulk(short index) => index >= 0 && index < Hulks.Count ? Hulks[index] : null;

	/// <summary>
	/// <c>Base_LoadResources</c>: <c>dat\bforms</c> and <c>dat\bases</c>, each asserting when it does not open;
	/// <c>dat\basecol</c>'s models, one a type in type order, installed (<c>+0x3a</c>) on a type whose <c>+0x06</c> is not 0;
	/// <c>dgs\bhulks</c> through <c>Shape_LoadAllRoots</c> with no skip, which binds no bank; the shape tables empty; and
	/// <c>dba\basetex</c>. The tables' sizes, the greatest <c>+0x02</c> and <c>+0x04</c> plus one, are not kept: a table
	/// here holds the entries <see cref="LoadShape"/> fills.
	/// </summary>
	/// <exception cref="FileNotFoundException"><c>dat\bforms</c>, <c>dat\bases</c> or <c>dat\basecol</c> is missing, which retail asserts on.</exception>
	public static Es1BaseTypes Load(GameContent content) {
		var formations = new Es1BaseFormationsTransformer().Parse(content.ReadRequired("dat", "BFORMS.DAT")) ?? new Es1BaseFormations();
		var records = new Es1BasesDatTransformer().Parse(content.ReadRequired("dat", "BASES.DAT")) ?? new Es1BasesDat();
		var models = new Es1BaseCollisionModelsTransformer().Parse(content.ReadRequired("dat", "BASECOL.DAT"))?.Models ?? [];
		var types = new Es1BaseType[records.Types.Length];
		for (short i = 0; i < types.Length; i++) {
			var record = records.Types[i];
			var model = record.ShapeSet != 0 && i < models.Length ? Es1HitSpheres.WithBounds(models[i].Nodes ?? []) : null;
			types[i] = new Es1BaseType(i, record, model);
		}

		var hulks = content.Read("dgs", HulkSet + ".DGS") is { } hulkBytes
			? new BasesDgsTransformer().Parse(hulkBytes)?.Shapes ?? []
			: [];
		return new Es1BaseTypes(content) {
			Types = types,
			Formations = formations,
			Hulks = hulks.Select((shape, index) => Es1BaseShape.FromGrid($"{HulkSet}#{index}", shape, null)).ToArray(),
			Texture = content.Read("dba", BankName + ".DBA") is { } bankBytes ? new DynamixBitmapArrayTransformer().Parse(bankBytes) : null,
		};
	}

	/// <summary>
	/// <c>BaseType_LoadShape</c> (<c>000045a4</c>)(<paramref name="side"/>, <paramref name="type"/>): with the type's
	/// <c>+0x06</c> 0 the static table, <c>BaseShapes_Static</c> (<c>000bc176</c>) for side 0 and
	/// <c>BaseShapes_StaticEnemy</c> (<c>000bc17a</c>) for any other, of <c>dgs\bases</c> or <c>dgs\bases_e</c>; otherwise
	/// <c>BaseShapes_Animated</c> (<c>000bc16c</c>) or <c>BaseShapes_AnimatedEnemy</c> (<c>000bc170</c>), of <c>dts\bases_an</c>
	/// or <c>dts\bases_ae</c>. An empty entry <c>+0x02</c> takes chunk <c>+0x02</c> of the set with <see cref="Texture"/> at
	/// its <c>+0x26</c>; a filled one is left. A type outside the table, which retail reads past it, loads nothing here, an
	/// engine guard; a chunk the set lacks, where retail asserts, leaves the entry empty.
	/// </summary>
	public void LoadShape(short side, short type) {
		if (this[type] is not { } record) {
			return;
		}

		var key = (SetFor(record, side), record.ShapeIndex);
		if (_tables.TryGetValue(key, out var filled) && filled != null) {
			return;
		}

		_tables[key] = ResolveShape(key.Item1, record.ShapeIndex);
	}

	/// <summary>The entry <c>+0x02</c> of the table <paramref name="side"/> picks for <paramref name="type"/>, as <see cref="LoadShape"/> filled it; null when empty.</summary>
	public Es1BaseShape? ShapeFor(Es1BaseType type, short side) =>
		_tables.GetValueOrDefault((SetFor(type, side), type.ShapeIndex));

	private static string SetFor(Es1BaseType type, short side) => type.ThreadCount == 0
		? side == 0 ? StaticSet : StaticEnemySet
		: side == 0 ? AnimatedSet : AnimatedEnemySet;

	/// <summary><c>BaseType_ResolveShape</c>(tag, <paramref name="index"/>, the set's path): the set's chunk <paramref name="index"/>.</summary>
	private Es1BaseShape? ResolveShape(string set, short index) {
		string key = $"{set}#{index}";
		if (set is StaticSet or StaticEnemySet) {
			if (!_grids.TryGetValue(set, out var library)) {
				library = _content.Read("dgs", set + ".DGS") is { } bytes ? new BasesDgsTransformer().Parse(bytes) : null;
				_grids[set] = library;
			}

			return library?.Shapes is { } shapes && index >= 0 && index < shapes.Length
				? Es1BaseShape.FromGrid(key, shapes[index], Texture)
				: null;
		}

		if (!_roots.TryGetValue(set, out var model)) {
			model = _content.Read("dts", set + ".DTS") is { } bytes ? new DTSModelTransformer().Parse(bytes) : null;
			_roots[set] = model;
		}

		return model?.Roots is { } roots && index >= 0 && index < roots.Count
			? Es1BaseShape.FromRoot(key, roots[index], Texture)
			: null;
	}
}
