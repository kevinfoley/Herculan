using System.Numerics;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Es1.Projectiles;
using Herculan.Es1.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The projectile pool's share of the frame (docs/es1/rendering/polygon-shading.md#the-object-list): every element of
/// <c>g_ProjectilePool</c> whose position's Z is not negative and that lies within its type tag's last distance band.
/// A missile and a gun round draw through their shape instance, their file's copy of <c>TS_OBJ</c>'s render, as the
/// zone's shapes draw, on the cell their sequence stands on, in the ramps of tag 3's band; a beam tracer draws its quads
/// in its palette color, <c>BeamTracer_Draw</c> (<c>ES1DBSIM 000087e0</c>, docs/es1/simulation/projectiles.md#drawing--beamtracer_draw-000087e0).
///
/// <para>The tracer's quads are a mesh of their own each frame, unlit faces of the palette index, in world space with
/// the identity model transform, which the GPU clips at the near plane as retail's <c>Poly_ClipRingToNearPlane</c> does;
/// they are drawn with the objects and their depth test (<see cref="Es1WorldDraw"/>).</para>
/// </summary>
public sealed class Es1ProjectileDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1ProjectilePool _pool;
	private readonly Dictionary<TSObject, GpuMesh?[]> _meshes = new(ReferenceEqualityComparer.Instance);
	private readonly List<IDisposable> _owned = new();
	private readonly List<SceneItem> _items = new();
	private readonly List<GpuMesh> _beamMeshes = new();
	private readonly uint? _bulletTexture;
	private readonly GL _gl;

	/// <summary>
	/// Uploads every cell of every root of <c>dts\rockets</c> and <c>dts\bullets</c>, the second textured from
	/// <c>dba\bullets</c>, the bank <c>Bullet_LoadResources</c> stores in each of its roots.
	/// </summary>
	public Es1ProjectileDraw(GL gl, GameContent content, Es1ZoneScene scene, Es1ProjectilePool pool) {
		_gl = gl;
		_scene = scene;
		_pool = pool;

		TextureAtlas? bulletAtlas = null;
		if (scene.Palette != null && content.Read("dba", "BULLETS.DBA") is { } bankBytes
				&& new DynamixBitmapArrayTransformer().Parse(bankBytes) is { } bank) {
			bulletAtlas = TextureAtlas.Build(bank, scene.Palette);
			if (bulletAtlas != null) {
				var texture = bulletAtlas.Upload(gl, indexed: true);
				_owned.Add(texture);
				_bulletTexture = texture.Handle;
			}
		}

		foreach (var root in pool.RocketShapes) {
			UploadCells(root, null);
		}

		foreach (var root in pool.BulletShapes) {
			UploadCells(root, bulletAtlas);
		}
	}

	/// <summary>The missiles', gun rounds' and beam tracers' items the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SceneItem> Items => _items;

	/// <summary>
	/// One mesh per cell frame of <paramref name="root"/>, as many as its longest sequence holds; a cell with no geometry
	/// is null.
	/// </summary>
	private void UploadCells(TSObject root, TextureAtlas? atlas) {
		if (_meshes.ContainsKey(root)) {
			return;
		}

		int frames = Math.Max(1, (root as TSShape)?.SequenceList?.Select(count => (int)(ushort)count).DefaultIfEmpty(1).Max() ?? 1);
		var cells = new GpuMesh?[frames];
		for (int frame = 0; frame < frames; frame++) {
			var build = DtsMeshBuilder.BuildRoot(root, atlas, _scene.Shading.Surfaces, frame);
			if (build.Vertices.Length > 0) {
				var mesh = new GpuMesh(_gl, build.Vertices, build.TriangleVertexCount, build.PointVertexCount);
				_owned.Add(mesh);
				cells[frame] = mesh;
			}
		}

		_meshes[root] = cells;
	}

	/// <summary>
	/// The pool's elements placed for <paramref name="camera"/>, walked back from the end as <c>ObjList_RebuildFrame</c>
	/// adds them: each missile and gun round an item on its shape's cell, each beam tracer one of its quads
	/// (<see cref="BeamItem"/>). With <paramref name="flatRamps"/> each takes <c>WorldShades_ResetRamps</c>' ramps
	/// (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, bool flatRamps = false) {
		_items.Clear();
		foreach (var mesh in _beamMeshes) {
			mesh.Dispose();
		}

		_beamMeshes.Clear();
		var shading = _scene.Shading;
		var live = _pool.Live;
		for (int i = live.Count - 1; i >= 0; i--) {
			var projectile = live[i];
			int distance = camera.Position.ApproxDistanceTo(projectile.Position);
			if (projectile.Position.Z < 0 || shading.IsBeyondLastBand(Es1Projectile.TypeTag, distance)) {
				continue;
			}

			if (projectile is Es1BeamTracer tracer) {
				if (BeamItem(tracer) is { } beam) {
					_items.Add(beam);
				}

				continue;
			}

			if (projectile.Shape is not { } shape || !_meshes.TryGetValue(shape, out var cells)) {
				continue;
			}

			int cell = projectile.DrawnCellFrame;
			if (cell < 0 || cell >= cells.Length || cells[cell] is not { } mesh) {
				continue;
			}

			uint? texture = projectile is Es1Bullet ? _bulletTexture : null;
			_items.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(projectile.Frame), texture) {
				RampBlock = flatRamps ? shading.FlatBlock : shading.Block(Es1Projectile.TypeTag, distance),
				// The effect lights are picked by its position and its shape's radius, slot +0x14's.
				LightAnchor = (projectile.Position, (shape as TSBasePart)?.Radius ?? 0),
			});
		}
	}

	/// <summary>
	/// <c>BeamTracer_Draw</c>'s fill: each quad, in <see cref="Es1BeamTracer.Quad"/>'s point order, two unlit triangles of
	/// <see cref="Es1BeamTracer.Color"/>'s palette entry, drawn from either side; null with no quads or no palette.
	/// </summary>
	private SceneItem? BeamItem(Es1BeamTracer tracer) {
		if (tracer.QuadCount <= 0 || _scene.Palette is not { } palette) {
			return null;
		}

		var color = Es1Horizon.ColorOf(palette, tracer.Color) ?? Vector3.Zero;
		var vertices = new List<MeshVertex>();
		void Add(Vector3 position) => vertices.Add(new MeshVertex(position, Vector3.UnitZ, color, unlit: true,
			solidPaletteIndex: tracer.Color));
		for (int quad = 0; quad < tracer.QuadCount; quad++) {
			var (a, b, c, d) = tracer.Quad(quad);
			var pa = WorldScale.ToRender(a);
			var pc = WorldScale.ToRender(c);
			Add(pa);
			Add(WorldScale.ToRender(b));
			Add(pc);
			Add(pa);
			Add(pc);
			Add(WorldScale.ToRender(d));
		}

		var mesh = new GpuMesh(_gl, vertices.ToArray(), vertices.Count);
		_beamMeshes.Add(mesh);
		return new SceneItem(mesh, Matrix4x4.Identity);
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}

		foreach (var mesh in _beamMeshes) {
			mesh.Dispose();
		}
	}
}
