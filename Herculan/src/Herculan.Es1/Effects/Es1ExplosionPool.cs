using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Effects;

/// <summary>
/// <c>explo.cpp</c>'s pool and tables (docs/es1/simulation/impact-effects.md): <c>g_ExplosionPool</c>
/// (<c>ES1DBSIM 000aee88</c>), <see cref="Capacity"/> effects, with its delete queue <c>g_ExplosionDeleteQueue</c>
/// (<c>000aee8c</c>), and what <c>Explosion_LoadResources</c> (<c>00005764</c>) loads at phase 2: <c>dat\explos</c>
/// into <c>g_ExplosionTypeTable</c> (<c>000bc1b0</c>), <c>dts\explos</c> into <c>g_ExplosionShapes</c>
/// (<c>000bc1b6</c>), and each root's bank (docs/es1/formats/explos-dat.md#the-load). The banks' bitmaps are the draw's.
///
/// <para>The in-use list is newest first, as <c>Pool_Alloc</c> (<c>000638c8</c>) puts an element at its head, so a
/// <c>Pool_Prev</c> walk from the end visits the oldest effect first: <see cref="Live"/> is in allocation order and is
/// walked from its start.</para>
/// </summary>
public sealed class Es1ExplosionPool {
	/// <summary><c>Explosion_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x51</c>, <c>0x28</c>): 40 effects.</summary>
	public const int Capacity = 40;

	/// <summary>The banks <c>dba\explo0</c> to <c>dba\explo8</c>, from the name template <c>explo666</c>.</summary>
	public const int BankCount = 9;

	/// <summary>The root of the flat shape set an effect's ground shape is built on.</summary>
	public const int GroundShapeRoot = 1;

	/// <summary>A row's length, <c>0x2c</c> bytes.</summary>
	public const int RowLength = 0x2c;

	private readonly List<Es1Explosion> _live = new();
	private readonly List<Es1Explosion> _deleteQueue = new();

	// The table as the load reads it into memory, the file less its count, which the ramp's read runs on through.
	private byte[] _table = [];

	/// <summary>A pool with no tables, as before the phase-2 load: no type builds an effect.</summary>
	public Es1ExplosionPool(Es1EffectLights lights, Es1FlatObjPool flatObjects) {
		Lights = lights;
		FlatObjects = flatObjects;
	}

	/// <summary>The effect light manager the effects' lights are claimed from.</summary>
	public Es1EffectLights Lights { get; }

	/// <summary><c>g_FlatObjPool</c> and <c>g_FlatShapes</c>, which the effects' ground shapes are built from.</summary>
	public Es1FlatObjPool FlatObjects { get; }

	/// <summary>The live effects in allocation order, oldest first, the order a <c>Pool_Prev</c> walk visits them.</summary>
	public IReadOnlyList<Es1Explosion> Live => _live;

	/// <summary><c>g_ExplosionTypeTable</c>, <c>dat\explos</c>' rows by type id (docs/es1/formats/explos-dat.md#the-row).</summary>
	public IReadOnlyList<Es1ExplosionTypeEntry> Types { get; private set; } = [];

	/// <summary><c>g_ExplosionShapes</c>, the roots of <c>dts\explos</c>.</summary>
	public IReadOnlyList<TSObject> Shapes { get; private set; } = [];

	/// <summary>
	/// The bank each root of <see cref="Shapes"/> keeps at its <c>+0x26</c>: for rows 0 to the root count less one,
	/// row <i>i</i>'s <c>+0x04</c> stored in the root its <c>+0x00</c> names; null for a root no such row names.
	/// </summary>
	public IReadOnlyList<short?> RootBanks { get; private set; } = [];

	/// <summary>The frame count of sequence 0 of the ground shapes' root, 0 without one.</summary>
	public int GroundShapeFrameCount => FlatObjects.FrameCount(GroundShapeRoot);

	/// <summary>
	/// The phase-2 load through <c>HercWorks.Core</c>: <c>dat\explos</c>, <c>dts\explos</c> and the roots' banks. A
	/// missing file, where retail asserts on <c>dat\explos</c>, leaves its table empty.
	/// </summary>
	public static Es1ExplosionPool Load(GameContent content, Es1EffectLights lights, Es1FlatObjPool flatObjects) {
		var transformer = new Es1ExplosionDataTransformer();
		var data = content.Read("dat", "EXPLOS.DAT") is { } bytes ? transformer.Parse(bytes) : null;
		var shapes = Roots(content, "EXPLOS.DTS");
		var types = data?.Types ?? [];
		var banks = new short?[shapes.Count];
		for (int i = 0; i < shapes.Count && i < types.Length; i++) {
			if (types[i].ShapeIndex >= 0 && types[i].ShapeIndex < banks.Length) {
				banks[types[i].ShapeIndex] = types[i].TextureBankIndex;
			}
		}

		return new Es1ExplosionPool(lights, flatObjects) {
			Types = types,
			Shapes = shapes,
			RootBanks = banks,
			_table = data != null && transformer.Write(data) is { Length: > 2 } file ? file[2..] : [],
		};
	}

	private static IReadOnlyList<TSObject> Roots(GameContent content, string name) =>
		content.Read("dts", name) is { } bytes && new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }
			? roots
			: [];

	/// <summary>
	/// The <c>int16</c> at <paramref name="offset"/> from row <paramref name="typeId"/>'s start, read on into the rows
	/// after it as retail reads the table's memory; 0 past the table's end, where retail reads the heap, an engine guard.
	/// </summary>
	public short RowWord(int typeId, int offset) {
		int at = typeId * RowLength + offset;
		return at >= 0 && at + 2 <= _table.Length ? BitConverter.ToInt16(_table, at) : (short)0;
	}

	/// <summary>
	/// <c>Explosion_Construct</c> (<c>00005af0</c>)(null, <paramref name="point"/>, <paramref name="typeId"/>)
	/// (docs/es1/simulation/impact-effects.md#construction--explosion_construct-00005af0): an effect from the pool, or
	/// null with the pool full, the effects queued for delete still holding their places. The type byte is the id's low
	/// byte, read signed; a type past the table, where retail reads past it, builds none, an engine guard, as does a
	/// root past the shapes build an effect with no shape. <paramref name="random"/> is the simulation generator
	/// <c>000b5bd4</c>, which the effect draws its turn from.
	/// </summary>
	public Es1Explosion? Construct(Vec3i point, short typeId, SimRandom random) {
		sbyte type = unchecked((sbyte)typeId);
		if (_live.Count + _deleteQueue.Count >= Capacity || type < 0 || type >= Types.Count) {
			return null;
		}

		short root = Types[type].ShapeIndex;
		var effect = new Es1Explosion(this, type, root >= 0 && root < Shapes.Count ? Shapes[root] : null, point, random);
		_live.Add(effect);
		return effect;
	}

	/// <summary>Whether <paramref name="effect"/> is live, <c>Pool_Prev</c>'s walk for it.</summary>
	public bool Contains(Es1Explosion? effect) => effect != null && _live.Contains(effect);

	/// <summary>
	/// <c>Sim_MainTick</c>'s walk (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700): each effect, oldest
	/// first, through <c>Explosion_TickUpdate</c>, and one that answers non-zero destroyed at once through slot
	/// <c>+0xc</c> with 3 (<c>Explosion_Destruct</c>), which queues it.
	/// </summary>
	public void Tick(Es1SimClock clock) {
		for (int i = 0; i < _live.Count;) {
			var effect = _live[i];
			if (effect.Tick(clock) != 0) {
				Destroy(i);
			} else {
				i++;
			}
		}
	}

	/// <summary><c>ExplosionPool_FlushDeletes</c> (<c>00005750</c>), phase 5: each queued effect given back to the pool.</summary>
	public void FlushDeletes() => _deleteQueue.Clear();

	/// <summary>
	/// <c>Sim_StartMission</c>'s step 7 (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc):
	/// every effect destroyed through slot <c>+0xc</c> with 3, each followed by <c>ExplosionPool_FlushDeletes</c>,
	/// <c>EffectLightPool_FlushDeletes</c> and <c>FlatObjPool_FlushDeletes</c>.
	/// </summary>
	public void DeleteAll() {
		while (_live.Count > 0) {
			Destroy(0);
			FlushDeletes();
			Lights.FlushDeletes();
			FlatObjects.FlushDeletes();
		}
	}

	/// <summary>The effect at <paramref name="index"/> destroyed and queued.</summary>
	private void Destroy(int index) {
		var effect = _live[index];
		effect.Destroy();
		_live.RemoveAt(index);
		_deleteQueue.Add(effect);
	}
}
