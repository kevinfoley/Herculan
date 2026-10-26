using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Es1.Effects;

/// <summary>
/// The ground-shape file (docs/es1/formats/wld-world.md#the-ground-shape-set): <c>g_FlatObjPool</c>
/// (<c>ES1DBSIM 000af044</c>), <see cref="Capacity"/> places that the HERCs' shadows and the impact effects' ground shapes
/// share, with its delete queue <c>g_FlatObjDeleteQueue</c> (<c>000af048</c>), and the shapes <c>FlatObj_LoadResources</c>
/// (<c>00006cf8</c>) loads into <c>g_FlatShapes</c> (<c>000bc1d8</c>). A shape queued for delete keeps its place until
/// <see cref="FlushDeletes"/>.
///
/// <para>Retail builds the pool in the theater's load, before the second pass builds the HERCs; this engine loads the
/// zone after the HERCs, so the pool counts from the start and <see cref="LoadShapes"/> gives it only its shapes.</para>
/// </summary>
public sealed class Es1FlatObjPool {
	/// <summary><c>FlatObj_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x3b</c>, <c>0x96</c>): 150 places.</summary>
	public const int Capacity = 150;

	private int _live;
	private int _queued;

	/// <summary><c>g_FlatShapes</c>, the flat shape set's roots; empty until <see cref="LoadShapes"/>.</summary>
	public IReadOnlyList<TSObject> Shapes { get; private set; } = [];

	/// <summary>The places taken, live and queued for delete.</summary>
	public int InUse => _live + _queued;

	/// <summary>
	/// <c>FlatObj_LoadResources</c>(<paramref name="selector"/>)'s shapes, <c>dts\flat</c> for a selector of 0 and
	/// <c>dts\flat2</c> otherwise.
	/// </summary>
	public void LoadShapes(GameContent content, int selector) =>
		Shapes = content.Read("dts", selector == 0 ? "FLAT.DTS" : "FLAT2.DTS") is { } bytes
			&& new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }
				? roots
				: [];

	/// <summary>The frame count of sequence 0 of root <paramref name="root"/>, 0 without one.</summary>
	public int FrameCount(int root) =>
		root >= 0 && root < Shapes.Count && Shapes[root] is TSShape { SequenceList: [var count, ..] } ? (ushort)count : 0;

	/// <summary><c>Pool_Alloc</c> (<c>000638c8</c>) on the pool: whether a place was free, which it then takes.</summary>
	internal bool Alloc() {
		if (InUse >= Capacity) {
			return false;
		}

		_live++;
		return true;
	}

	/// <summary><c>ObjectPool_QueueForDelete</c> on <c>g_FlatObjDeleteQueue</c>: a live shape's place held until the flush.</summary>
	internal void QueueForDelete() {
		_live--;
		_queued++;
	}

	/// <summary><c>FlatObjPool_FlushDeletes</c> (<c>00006d50</c>), phase 5: each queued shape's place given back.</summary>
	public void FlushDeletes() => _queued = 0;
}
