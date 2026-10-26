using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Effects;

/// <summary>
/// <c>debris.cpp</c>'s pool and tables (docs/es1/simulation/destruction-effects.md#debris): <c>g_DebrisPool</c>
/// (<c>ES1DBSIM 000aeef8</c>), <see cref="Capacity"/> pieces, with its delete queue <c>g_DebrisDeleteQueue</c>
/// (<c>000aeefc</c>), and what <c>Debris_LoadResources</c> (<c>0000607c</c>) loads at phase 2: <c>dat\debris</c>'s groups
/// into <c>g_DebrisGroups</c> (<c>000bc1c0</c>) and <c>dts\debris</c>'s roots into <c>g_DebrisShapes</c> (<c>000bc1c6</c>)
/// (docs/es1/formats/debris-dat.md#the-load). It also keeps <c>WeaponModels_Mechwpn2Roots</c> (<c>000bc2c8</c>), the
/// <c>dts\mechwpn2</c> roots <c>Weapons_LoadResourceTables</c> loads (docs/es1/formats/weapons-dat-sim.md), which a thrown
/// gun is built on.
///
/// <para><see cref="Live"/> is in allocation order, the order a <c>Pool_Prev</c> walk from the end visits it.</para>
/// </summary>
public sealed class Es1DebrisPool {
	/// <summary><c>Debris_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x4e</c>, <c>0x96</c>): 150 pieces.</summary>
	public const int Capacity = 150;

	/// <summary><c>Debris_ThrowRandomBearing</c>'s pitch: <c>Math_RandomBelow</c> of the span less the floor.</summary>
	public const short PitchSpan = 24000, PitchFloor = 10000;

	private readonly List<Es1Debris> _live = new();
	private readonly List<Es1Debris> _deleteQueue = new();

	/// <summary>The live pieces in allocation order, oldest first.</summary>
	public IReadOnlyList<Es1Debris> Live => _live;

	/// <summary><c>g_DebrisGroups</c>, <c>dat\debris</c>'s groups (docs/es1/formats/debris-dat.md#the-layout).</summary>
	public IReadOnlyList<Es1DebrisGroup> Groups { get; private init; } = [];

	/// <summary><c>g_DebrisShapes</c>, the roots of <c>dts\debris</c>.</summary>
	public IReadOnlyList<TSObject> Shapes { get; private init; } = [];

	/// <summary>
	/// <c>WeaponModels_Mechwpn2Roots</c>, the roots of <c>dts\mechwpn2</c>, which <c>Weapons_LoadResourceTables</c> loads
	/// while <c>g_LowMemory</c> is 0 (<c>dts\mwpn2b</c> otherwise), always so here (<see cref="Input.Es1Preferences.LowMemory"/>).
	/// </summary>
	public IReadOnlyList<TSObject> GunShapes { get; private init; } = [];

	/// <summary>
	/// The phase-2 loads through <c>HercWorks.Core</c>: <c>dat\debris</c>, <c>dts\debris</c> and <c>dts\mechwpn2</c>. A
	/// missing file, where retail asserts on <c>dat\debris</c>, leaves its table empty.
	/// </summary>
	public static Es1DebrisPool Load(GameContent content) => new() {
		Groups = content.Read("dat", "DEBRIS.DAT") is { } bytes && new Es1DebrisDatTransformer().Parse(bytes) is { } data
			? data.Groups
			: [],
		Shapes = Roots(content, "DEBRIS.DTS"),
		GunShapes = Roots(content, "MECHWPN2.DTS"),
	};

	private static IReadOnlyList<TSObject> Roots(GameContent content, string name) =>
		content.Read("dts", name) is { } bytes && new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }
			? roots
			: [];

	/// <summary>
	/// <c>Debris_ThrowGroup</c> (<c>00005fb8</c>)(group <paramref name="group"/>, <paramref name="point"/>,
	/// <c>g_DebrisShapes</c>) (docs/es1/formats/debris-dat.md#throwing-a-group): <c>Math_RandomBelow</c> of the throw
	/// count pieces, each drawn by weight and thrown on a random bearing (<see cref="ThrowRandomBearing"/>), on
	/// <paramref name="random"/>, the generator <c>000b5bd4</c>. A group past the table, where retail reads past it,
	/// throws none, an engine guard; a throw count of 0, where retail divides by 0, throws none too.
	/// </summary>
	public void ThrowGroup(int group, Vec3i point, SimRandom random) {
		if (group < 0 || group >= Groups.Count || Groups[group] is not { Pieces.Length: > 0 } entry) {
			return;
		}

		short total = 0;
		foreach (var piece in entry.Pieces) {
			total = unchecked((short)(total + piece.Weight));
		}

		int count = random.NextBelow(entry.ThrowCount);
		for (int n = 0; n < count; n++) {
			// The weighted walk: each piece's weight taken off the draw while the draw is greater than it. Retail's walk
			// always stops on a piece, the draw being under the total; it stops on the last one here.
			short draw = (short)random.NextBelow(total);
			int at = 0;
			while (at < entry.Pieces.Length - 1 && entry.Pieces[at].Weight < draw) {
				draw = unchecked((short)(draw - entry.Pieces[at].Weight));
				at++;
			}

			short shape = entry.Pieces[at].ShapeIndex;
			ThrowRandomBearing(Construct(point, shape >= 0 && shape < Shapes.Count ? Shapes[shape] : null), random);
		}
	}

	/// <summary>
	/// <c>Debris_Construct</c> (<c>00006168</c>)(null, <paramref name="point"/>, <paramref name="shape"/>): a piece from
	/// the pool, or null with the pool full, the pieces queued for delete still holding their places.
	/// </summary>
	public Es1Debris? Construct(Vec3i point, TSObject? shape) {
		if (_live.Count + _deleteQueue.Count >= Capacity) {
			return null;
		}

		var piece = new Es1Debris(shape, point);
		_live.Add(piece);
		return piece;
	}

	/// <summary>
	/// <c>Debris_ThrowRandomBearing</c> (<c>00006480</c>)(<paramref name="piece"/>): the pitch drawn first,
	/// <c>Math_RandomBelow</c>(<see cref="PitchSpan"/>) − <see cref="PitchFloor"/>, then the heading,
	/// <c>Math_RandomNext</c>, and <see cref="Es1Debris.Launch"/>. Retail launches a null piece, which the full pool gives,
	/// through the null; here the draws are made and nothing is launched, an engine guard.
	/// </summary>
	private static void ThrowRandomBearing(Es1Debris? piece, SimRandom random) {
		short pitch = unchecked((short)(random.NextBelow(PitchSpan) - PitchFloor));
		short heading = random.Next();
		piece?.Launch(heading, pitch);
	}

	/// <summary>
	/// <c>Sim_MainTick</c>'s walk (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700): each piece, oldest
	/// first, through <c>Debris_TickUpdate</c>, and one that answers non-zero destroyed at once through slot <c>+0xc</c>
	/// with 3 (<c>Debris_Destruct</c>, <c>00006210</c>), which queues it.
	/// </summary>
	public void Tick() {
		for (int i = 0; i < _live.Count;) {
			if (_live[i].Tick() != 0) {
				Destroy(i);
			} else {
				i++;
			}
		}
	}

	/// <summary><c>DebrisPool_FlushDeletes</c> (<c>00005ebc</c>), phase 5: each queued piece given back to the pool.</summary>
	public void FlushDeletes() => _deleteQueue.Clear();

	/// <summary>
	/// <c>Sim_StartMission</c>'s step 7 (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc):
	/// every piece destroyed through slot <c>+0xc</c> with 3, each followed by <c>DebrisPool_FlushDeletes</c>.
	/// </summary>
	public void DeleteAll() {
		while (_live.Count > 0) {
			Destroy(0);
			FlushDeletes();
		}
	}

	private void Destroy(int index) {
		_deleteQueue.Add(_live[index]);
		_live.RemoveAt(index);
	}
}
