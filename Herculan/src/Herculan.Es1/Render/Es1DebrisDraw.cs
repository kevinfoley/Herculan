using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Effects;
using Herculan.Es1.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The debris' share of the frame (docs/es1/simulation/destruction-effects.md#drawing-and-deleting): every piece of
/// <c>g_DebrisPool</c> whose position's Z is not negative and that lies within tag 1's last distance band, drawn as
/// <c>Debris_Draw</c> (<c>ES1DBSIM 000065c4</c>) draws it: its root at its frame, each <c>TSDetailPart</c> at the level the
/// piece's detail bias picks (docs/es1/simulation/object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08),
/// in the ramps of tag 1's band, with the effect lights picked by its position and its shape's radius. The roots of
/// <c>dts\debris</c> draw with no bank, as ES1 binds none to them; a thrown gun's roots of <c>dts\mechwpn2</c> draw from
/// <c>dba\wpntex</c>, the bank <c>Weapons_LoadResourceTables</c> stores in each of them.
/// </summary>
public sealed class Es1DebrisDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1DebrisPool _pool;
	private readonly Dictionary<TSObject, ((GpuMesh Mesh, CellGate Gate)[] Pieces, uint? Texture)> _roots =
		new(ReferenceEqualityComparer.Instance);
	private readonly List<IDisposable> _owned = new();
	private readonly List<SceneItem> _items = new();

	/// <summary>Uploads every root of <c>dts\debris</c> and <c>dts\mechwpn2</c>, the second from <paramref name="models"/>' <c>dba\wpntex</c>.</summary>
	public Es1DebrisDraw(GL gl, Es1ZoneScene scene, Es1DebrisPool pool, Es1MechModels models) {
		_scene = scene;
		_pool = pool;
		foreach (var root in pool.Shapes) {
			Upload(gl, root, null, null);
		}

		foreach (var root in pool.GunShapes) {
			Upload(gl, root, models.WeaponAtlas, models.WeaponTexture);
		}
	}

	/// <summary>The pieces' items the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SceneItem> Items => _items;

	/// <summary>A root's detail levels, each gated on its level, or with no detail part its one mesh.</summary>
	private void Upload(GL gl, TSObject root, TextureAtlas? atlas, uint? texture) {
		if (_roots.ContainsKey(root)) {
			return;
		}

		var surfaces = _scene.Shading.Surfaces;
		var builds = new List<(MeshVertex[] Vertices, int Triangles, int Points, CellGate Gate)>();
		var levels = DtsMeshBuilder.BuildDetailLevels(root, atlas, surfaces);
		if (levels.Length > 0) {
			builds.AddRange(levels.Select(level => (level.Vertices, level.TriangleVertexCount, level.PointVertexCount, level.Gate)));
		} else {
			var build = DtsMeshBuilder.BuildRoot(root, atlas, surfaces);
			builds.Add((build.Vertices, build.TriangleVertexCount, build.PointVertexCount, CellGate.Ungated));
		}

		var pieces = new List<(GpuMesh, CellGate)>();
		foreach (var (vertices, triangles, points, gate) in builds) {
			if (vertices.Length == 0) {
				continue;
			}

			var mesh = new GpuMesh(gl, vertices, triangles, points);
			_owned.Add(mesh);
			pieces.Add((mesh, gate));
		}

		_roots[root] = (pieces.ToArray(), texture);
	}

	/// <summary>
	/// The pool's pieces placed for <paramref name="camera"/>, walked as <c>ObjList_RebuildFrame</c> adds them, each
	/// detail level picked under a focal length of 2^<paramref name="focalShift"/>. With <paramref name="flatRamps"/> each
	/// takes <c>WorldShades_ResetRamps</c>' ramps (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, int focalShift, bool flatRamps = false) {
		_items.Clear();
		var shading = _scene.Shading;
		foreach (var piece in _pool.Live) {
			if (piece.Shape is not { } shape || !_roots.TryGetValue(shape, out var root)) {
				continue;
			}

			int distance = camera.Position.ApproxDistanceTo(piece.Position);
			if (piece.Position.Z < 0 || shading.IsBeyondLastBand(Es1Debris.TypeTag, distance)) {
				continue;
			}

			var frame = piece.Frame;
			var transform = WorldScale.ToRenderMatrix(frame);
			foreach (var (mesh, gate) in root.Pieces) {
				if (gate.Detail is { } detail && Es1ShapeDetail.SelectLevel(detail,
						Es1ShapeDetail.EyeDistance(PartFrame(frame, detail), camera.Position), focalShift, piece.DetailBias) != gate.Level) {
					continue;
				}

				_items.Add(new SceneItem(mesh, transform, root.Texture) {
					RampBlock = flatRamps ? shading.FlatBlock : shading.Block(Es1Debris.TypeTag, distance),
					// The effect lights are picked by its position and its shape's radius, slot +0x14's.
					LightAnchor = (piece.Position, (shape as TSBasePart)?.Radius ?? 0),
				});
			}
		}
	}

	/// <summary>The space a detail part measures the eye in: its node's place at rest, through the piece's frame.</summary>
	private static Transform3 PartFrame(Transform3 frame, PartDetail detail) {
		const float k = WorldScale.WorldUnitsPerMeter;
		var origin = detail.Origin;
		var at = frame.TransformPoint((int)MathF.Round(origin.X * k), (int)MathF.Round(-origin.Z * k), (int)MathF.Round(origin.Y * k));
		frame.X = at.X;
		frame.Y = at.Y;
		frame.Z = at.Z;
		return frame;
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}
}
