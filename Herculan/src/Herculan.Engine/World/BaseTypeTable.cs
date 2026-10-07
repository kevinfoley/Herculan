using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>Which model library a base type's shape comes out of.</summary>
public enum BaseShapeSource {
	/// <summary>
	/// <c>dgs\BASES.DGS</c> — the static-structure library, covering 57 of the 65 structure types.
	/// Read by <see cref="HercWorks.Core.Io.Transform.Dbsim.BasesDgsTransformer"/> — see
	/// docs/retail/formats/dgs-hd0-notes.md for the format.
	/// </summary>
	StaticLibrary,

	/// <summary>
	/// <c>dts\BASES_AN.DTS</c> — the animated-structure library, an ordinary DTS whose eight roots
	/// are eight unrelated objects rather than LODs of one.
	/// </summary>
	AnimatedLibrary
}

/// <summary>
/// What a structure type shoots — <c>BASES.DAT</c> <c>+0x2e</c>, which is read as a value and not
/// just a flag: the armed tick branches on it to pick the projectile, and the ground vehicle tick
/// branches on it to decide whether the vehicle fights at all.
/// </summary>
public enum BaseArmament {
	/// <summary>Zero — unarmed. A ground vehicle with this runs the plain tick instead of the armed one.</summary>
	None,

	/// <summary>One — a gun. Fires <c>PROJ.DAT</c> row 2 and leads its target by that round's speed.</summary>
	Gun,

	/// <summary>Two — a launcher. Fires <c>ROCKETS.DAT</c> row 0, which tracks, so it does not lead.</summary>
	Launcher
}

/// <summary>
/// One destructible part of a structure — the 30-byte sub-record a type's nested array holds, of
/// which four fields are read by the two damage paths (<c>Base_ApplyDamage</c>, <c>00404d70</c>, and
/// the blast sweep's position accessor <c>Base_ComponentPosition</c>) and the rest are not touched by anything
/// traced.
///
/// <para>Every structure has at least one of these, so "the building's hit points" is really "its
/// components' hit points": a structure falls when <i>all</i> of them are destroyed, which is what
/// <see cref="Herculan.Engine.Sim.BaseObject.DamageFraction"/> measures.</para>
/// </summary>
/// <param name="MaxDamage">
/// <c>+0</c> — how much damage this part absorbs before it is destroyed. Retail values are 1000 to
/// 30000, and a component sitting at exactly this figure is dead.
/// </param>
/// <param name="DestroyedSubShape">
/// <c>+2</c> — which cell-animation sequence of the shape this component drives, or <c>-1</c> for a
/// component with no separate geometry (every single-component type). It is the only field that
/// ties a component to the model: when the part falls, the sequence steps to
/// <see cref="Herculan.Engine.Sim.BaseObject.CollapsedCell"/> and the part is redrawn as its own
/// rubble.
/// </param>
/// <param name="DestroyedEffect">
/// <c>+4</c> — which of the four death sequences this part runs when it is destroyed, or <c>-1</c>
/// for a part that simply stops existing. It indexes the small fixed tables in the executable at
/// <c>004973fc</c> onward — see <see cref="Herculan.Engine.Sim.StructureDeathSequence"/>.
/// </param>
/// <param name="FireShapeIndex">
/// <c>+6</c> — which root of <c>dts\FIRE.DTS</c> burns on this part at the end of its death
/// sequence, or <c>-1</c> for a part that does not catch.
/// </param>
/// <param name="DebrisGroup">
/// <c>+8</c> — the debris group this part throws as it collapses, in the two-database index space
/// with <c>BASE_DEB</c> installed. A group above
/// <see cref="Herculan.Engine.Sim.BaseObject.LargeDebrisGroup"/> throws two further fixed groups
/// alongside it, which is how the big structures come apart in more pieces than the small ones.
/// </param>
/// <param name="EmitPoint">
/// <c>+0x0a</c> — where on the structure the debris and the fire come from, in its own frame. Its X
/// and Y repeat <paramref name="Position"/>'s on most types and its Z does not: this is up on the
/// part, where <paramref name="Position"/> is at its base.
/// </param>
/// <param name="Position">
/// <c>+0x10</c> — where this part sits in the structure's own frame, the point
/// <see cref="Herculan.Engine.Sim.BaseObject.ComponentPosition"/> hands out and a blast measures its
/// falloff from. Retail values are body-scale offsets in world units — the three sections of a
/// three-part hangar sit at <c>(0, ±3500, 1000)</c> — and the eight-metre spread across a large
/// building is the whole reason one end of it can be levelled by a blast while the other stands.
/// </param>
/// <param name="SmokeSpread">
/// <c>+0x16</c> — the box, half-extents, that the death sequence scatters its smoke and secondary
/// explosions inside, around <paramref name="Position"/>. A long hangar section states a long box,
/// so the smoke walks its length rather than sitting on one point.
/// </param>
/// <param name="ParentComponent">
/// <c>+0x1c</c> — the component this one hangs off, or an index no component has for one that hangs
/// off nothing. Destroying a part takes every part naming it with it, which is the structure
/// equivalent of a machine's bone group.
/// </param>
public readonly record struct BaseComponentType(
	short MaxDamage, short DestroyedSubShape, short DestroyedEffect, short FireShapeIndex,
	short DebrisGroup, Vec3i EmitPoint, Vec3i Position, Vec3i SmokeSpread, short ParentComponent);

/// <summary>
/// One entry of the base-type table: everything the engine needs to draw a placed structure and to
/// shoot at it.
/// </summary>
/// <param name="Index">The type index a mission's block-9 record carries.</param>
/// <param name="ShapeIndex">Which shape inside <paramref name="Source"/>'s library this type is.</param>
/// <param name="Source">Which library that is.</param>
/// <param name="TextureBankName">
/// <c>BASETEX</c>, <c>VEHTEX</c> or <c>RAZORTEX</c> — the <c>.DBA</c> the type's polys sample from.
/// </param>
/// <param name="HulkTypeIndex">
/// <c>+0x04</c> — the wreck this type leaves behind, or <c>-1</c> for one that leaves none. The hit
/// test reads it too: a <i>destroyed</i> structure that has a hulk switches over to the coarse
/// volume test regardless of what it used while it was standing.
/// </param>
/// <param name="HitRadius">
/// <c>+0x2a</c> — the coarse collision radius, in world units, that the type's vtable <c>+0x5c</c>
/// hands out (<c>Base_GetBodyRadius</c>, <c>004035a4</c>). <b>This is the type's own stated figure, not a measurement of
/// the model</b>, which is what the engine substituted before; retail values run 1000 to 9600, and
/// four types state zero.
/// </param>
/// <param name="Invulnerable">
/// <c>+0x1e != 0</c> — the type takes no damage at all. Both damage entry points
/// (<c>Base_ApplyDamage</c> and <c>Base_ApplyExplosiveDamage</c>) open by testing it and
/// returning. Three retail types set it (21, 22 and 23), which share a shape family and are the
/// tallest things in the table.
/// </param>
/// <param name="HasCollisionModel">
/// <c>+0x30 != 0</c> — whether this type's <c>dat\BASECOL.DAT</c> sphere model is installed. It is
/// the whole of the hit test's branch: a type with one is tested sphere by sphere and can name the
/// component struck, a type without falls back to the shape's own collision volume and can only ever
/// report component 0. 25 of the 65 retail types set it, and one type (3) carries a full
/// <c>BASECOL.DAT</c> model that the flag leaves switched off — the data is there and unused.
/// </param>
/// <param name="SilhouetteIndex">
/// <c>+0x28</c> - which silhouette the MFD status screen draws for this type, and which type name
/// it prints. It indexes the <c>BASES</c> sprite bank and <c>STRINGS0.STR</c> group 23 for a
/// structure, and the <c>VEHICLES</c> bank and group 24 for a vehicle - which is what
/// <paramref name="IsVehicle"/> selects between. Confirmed by construction: every structure type
/// states 0-30 against group 23's 31 names and the bank's 31 frames, and every vehicle type
/// states 0-3 against group 24's four names and that bank's four frames.
/// </param>
/// <param name="IsVehicle">
/// <c>+0x32 != 0</c> - the same field that picks <c>VEHTEX</c> over <c>BASETEX</c>. Types 45-64 set
/// it and are the mobile ground units; everything below is a building.
/// </param>
/// <param name="FireShapeIndex">
/// <c>+0x08</c> — which root of <c>dts\FIRE.DTS</c> burns on the structure as a whole once every one
/// of its parts is gone, or <c>-1</c>. It is the alternative to the per-part fire, not an addition to
/// it: a type that states both burns whole rather than in pieces.
/// </param>
/// <param name="FirePoint">
/// <c>+0x0a</c> — where that whole-structure fire sits, in the structure's own frame.
/// </param>
/// <param name="DestroyedEffect">
/// <c>+0x10</c> — the death sequence the structure runs as a whole, in place of the failing part's
/// own. Read only once every part is gone, and only when the type and the failing part both state a
/// fire.
/// </param>
/// <param name="AimPointHeight">
/// <c>+0x2c</c> — how far up the structure anything aiming at it aims, in world units. Two vtable
/// slots carry it: <c>+0x24</c> (<c>Base_GetAimNodeTransform</c>, <c>00403548</c>) as the translation of
/// a node transform, and <c>+0x30</c> (<c>Base_GetAimPoint</c>, <c>0040351c</c>) as the Z of an offset
/// triple that the tower ticks add to the target's position unrotated while the camera attach
/// rotates it (docs/retail/simulation/structure-behaviour.md, "What a structure is aimed at"). All 65 retail
/// types state one, 1000 to 2000, so a building is never shot at its own ground origin.
/// </param>
/// <param name="Armament">
/// <c>+0x2e</c> — what the type shoots, and the same field <see cref="ThreatensAttackers"/> reads as
/// a flag. See <see cref="BaseArmament"/>.
/// </param>
/// <param name="AnimThreadCount">
/// <c>+0x06</c> — how many animation threads <c>Base_Construct</c> (<c>00405314</c>) builds on the
/// structure, one per sequence of its shape from sequence 0 up. Retail states 0, 1 or 2, and the
/// eight types that state a non-zero count are exactly the eight the constructor draws from
/// <see cref="BaseShapeSource.AnimatedLibrary"/> — each of whose roots carries an <c>ANAnimList</c>
/// with exactly this many sequences.
///
/// <para>The library choice itself is <i>not</i> read from this field: the original hardcodes it per
/// case in the constructor's switch, and the two agree on retail data. <see cref="Source"/> is
/// derived from this field anyway, which is the same answer everywhere the shipped table reaches.</para>
/// </param>
/// <param name="AnimThreadRates">
/// <c>+0x20</c> — two <c>short</c>s, the playback rate each of those threads is started at. A rate
/// of zero leaves the thread parked for something else to position it, which is what an armed
/// structure's turret seek does with both of its; the radar masts state real rates and spin freely.
/// </param>
/// <param name="ThreatensAttackers">
/// <c>+0x2e != 0</c> — the AI treats this type as dangerous. Nonzero on the two armed structures
/// (gun tower, missile tower), the mobile missile vehicle, the generator and the transport, and it
/// changes two decisions: a crippled machine flees from one instead of pressing the attack, and an
/// attacking machine circles one instead of standing off. See docs/retail/simulation/ai-combat-states.md.
/// </param>
/// <param name="AnimCellSequence">
/// <c>+0x24</c> — which cell sequence <c>Base_ThinkTick</c> (<c>00403ca8</c>) steps to animate the
/// standing structure, or negative for a type that does not animate. It is the same per-sequence
/// cell array damage moves (<see cref="Sim.ShapeCellFrames"/>); this is the idle flipbook rather
/// than a damage state, and it is what makes a radar dish turn.
/// </param>
/// <param name="AnimCellInterval">
/// <c>+0x26</c> — how long one frame of <paramref name="AnimCellSequence"/> holds, in
/// <see cref="Numerics.SimMath.CountdownTimerTick"/>'s unit rather than milliseconds: the 256 every
/// animated type states is a frame every 125 ms. Reloaded into the structure's own countdown each
/// time it expires.
/// </param>
/// <param name="Components">
/// <c>+0x14</c> — the type's destructible parts, in the order the file states them, which is the
/// order both the health array and <c>BASECOL.DAT</c>'s component indices address them in.
/// </param>
public readonly record struct BaseType(
	int Index, int ShapeIndex, BaseShapeSource Source, string TextureBankName,
	short HulkTypeIndex, int HitRadius, bool Invulnerable, bool HasCollisionModel,
	short SilhouetteIndex, bool IsVehicle, short FireShapeIndex, Vec3i FirePoint,
	short DestroyedEffect, short AimPointHeight, BaseArmament Armament,
	short AnimCellSequence, short AnimCellInterval,
	short AnimThreadCount, short[] AnimThreadRates, BaseComponentType[] Components) {

	/// <inheritdoc cref="BaseTypeTable"/>
	/// <summary>
	/// <c>+0x2e != 0</c> — see <see cref="Armament"/>, which is the same field read as the value it
	/// is.
	/// </summary>
	public bool ThreatensAttackers => Armament != BaseArmament.None;
}

/// <summary>
/// <c>dat\BASES.DAT</c> — the game's table of structure types, the thing that turns a mission's
/// block-9 type number into a model, a texture bank and a set of destructible parts. 65 entries in
/// the retail file.
///
/// <para>Read from <c>Base_LoadResources</c> (<c>00405fac</c>, <c>base.cpp</c>'s resource-load
/// sequence), which streams a count and then fixed-shape records with one nested variable-length
/// array each, and from <c>BaseType_LoadShape</c> (<c>00405ebc</c>), which is the whole of the model
/// selection:</para>
/// <code>
/// rec = typeIndex * 0x3c + table
/// if (rec[6] == 0) shape = load("dgs\bases",    rec[2])   // static
/// else             shape = load("dts\bases_an", rec[2])   // animated
/// texture = rec[0x32] == 0 ? basetex : (typeIndex == 0x34 ? razortex : vehtex)
/// </code>
///
/// <para>That lone <c>typeIndex == 0x34</c> case is in the original exactly as written — type 52
/// borrows RAZOR's mech texture bank rather than either structure bank.</para>
///
/// <para><b>The file's field order is the runtime record's field order.</b>
/// <c>Base_LoadResources</c> reads straight into a 60-byte struct offset by offset, and the only
/// place the two diverge is the nested component array, inline on disk and a pointer at
/// <c>+0x14</c> in memory. So every offset named in this file's doc comments is both.</para>
///
/// <para>The record shape is confirmed by construction: walking it consumes the retail file's 6,422
/// content bytes exactly, with nothing left over. It also lines up with the model libraries on the
/// other side — exactly eight types select the animated library, and <c>BASES_AN.DTS</c> holds
/// exactly eight roots, numbered 0-7 the way those eight types reference them.</para>
///
/// <para>The file is parsed by <see cref="BasesDatTransformer"/>; this table carries every field but
/// the two nothing traced reads, <c>+0x00</c> and <c>+0x18</c> (6 bytes).</para>
/// </summary>
public sealed class BaseTypeTable {
	/// <summary>VOL folder and name of the table.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The table's resource name.</summary>
	public const string ResourceName = "BASES.DAT";

	/// <summary>The animated library, an ordinary DTS the engine can already build meshes from.</summary>
	public const string AnimatedLibraryName = "BASES_AN.DTS";

	/// <summary>The static library, read via <see cref="HercWorks.Core.Io.Transform.Dbsim.BasesDgsTransformer"/>.</summary>
	public const string StaticLibraryName = "BASES.DGS";

	private readonly BaseType[] _types;

	private BaseTypeTable(BaseType[] types) {
		_types = types;
	}

	/// <summary>How many base types the table declares.</summary>
	public int Count => _types.Length;

	/// <summary>A type by index, or null when a mission names one the table does not have.</summary>
	public BaseType? this[int typeIndex] =>
		typeIndex >= 0 && typeIndex < _types.Length ? _types[typeIndex] : null;

	/// <summary>Reads the table through <see cref="BasesDatTransformer"/>, which throws when the walk does not consume the file exactly.</summary>
	public static BaseTypeTable Load(GameContent content) {
		var file = new BasesDatTransformer().Parse(content.ReadRequired(ResourceFolder, ResourceName))
			?? throw new InvalidDataException($"{ResourceFolder}\\{ResourceName} is empty.");

		static Vec3i Vec(short[] xyz) => new(xyz[0], xyz[1], xyz[2]);

		var types = new BaseType[file.Types.Length];
		for (int i = 0; i < types.Length; i++) {
			var record = file.Types[i];
			var components = record.Components
				.Select(c => new BaseComponentType(
					c.MaxDamage, c.DestroyedSubShape, c.DestroyedEffect, c.FireShapeIndex, c.DebrisGroup,
					Vec(c.EmitPoint), Vec(c.Position), Vec(c.SmokeSpread), c.ParentComponent))
				.ToArray();

			types[i] = new BaseType(
				i,
				record.ShapeIndex,
				record.AnimThreadCount == 0 ? BaseShapeSource.StaticLibrary : BaseShapeSource.AnimatedLibrary,
				record.TextureSelector == 0 ? "BASETEX" : i == 0x34 ? "RAZORTEX" : "VEHTEX",
				record.HulkTypeIndex,
				record.HitRadius,
				record.Invulnerable != 0,
				record.CollisionModel != 0,
				record.SilhouetteIndex,
				record.TextureSelector != 0,
				record.FireShapeIndex,
				Vec(record.FirePoint),
				record.DestroyedEffect,
				record.AimPointHeight,
				(BaseArmament)record.Armament,
				record.AnimCellSequence,
				record.AnimCellInterval,
				record.AnimThreadCount,
				record.AnimThreadRates,
				components);
		}

		return new BaseTypeTable(types);
	}
}
