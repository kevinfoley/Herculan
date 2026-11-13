using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Effects;
using Herculan.Es1.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The smoke balls' share of the frame (docs/es1/simulation/destruction-effects.md#the-ball--smokeball_construct-00006704):
/// every ball of <c>g_SmokeBallPool</c> whose position's Z is not negative and that lies within tag 4's last distance
/// band, drawn as <c>TSObj_RenderSmokeCopy</c> (<c>ES1DBSIM 000067c0</c>), <c>TS_OBJ</c>'s render, draws it on the cell its
/// sequence 0 stands on: a root's bitmap parts as Earthsiege's upright billboards out of <c>dba\smoke</c>, the bank
/// <c>SmokeBall_LoadResources</c> stores in every root (<see cref="SpriteBatch.UprightFocalPixels"/>), and any faces as the
/// impact effects draw theirs (<see cref="Es1ExplosionDraw"/>), with the effect lights picked by its position and its
/// shape's radius. The emitters draw nothing.
/// </summary>
public sealed class Es1SmokeDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1SmokePool _pool;
	private readonly GL _gl;
	private readonly List<IDisposable> _owned = new();
	private readonly Dictionary<TSObject, (GpuMesh?[] Cells, SpriteQuad[][] Sprites)> _roots = new(ReferenceEqualityComparer.Instance);
	private readonly (TextureAtlas Atlas, uint Texture)? _bank;
	private readonly PaletteTexture? _palette;
	private readonly List<SceneItem> _items = new();
	private readonly List<SpriteBatch> _batches = new();

	/// <summary>
	/// Uploads <c>dba\smoke</c> and every cell of every root of <c>dts\smoke</c>, and pulls each root's billboards, which
	/// are shown through <paramref name="palette"/>, the live palette; with none the bank is not uploaded.
	/// </summary>
	public Es1SmokeDraw(GL gl, GameContent content, Es1ZoneScene scene, Es1SmokePool pool, PaletteTexture? palette) {
		_gl = gl;
		_scene = scene;
		_pool = pool;
		_palette = palette;
		if (palette != null && content.Read("dba", "SMOKE.DBA") is { } bytes
				&& new DynamixBitmapArrayTransformer().Parse(bytes) is { } bank
				&& TextureAtlas.Build(bank, scene.Palette, transparentIndex0: true) is { } atlas) {
			// The bank's indices, which the faces and the billboards both sample, index 0 left unpainted as the impact
			// effects' is (Es1ExplosionDraw).
			var texture = atlas.Upload(gl, indexed: true);
			_owned.Add(texture);
			_bank = (atlas, texture.Handle);
		}

		foreach (var root in pool.Shapes) {
			if (!_roots.ContainsKey(root)) {
				_roots[root] = (UploadCells(root, _bank?.Atlas), DtsSpriteBuilder.Build(root));
			}
		}
	}

	/// <summary>The balls' faces the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SceneItem> Items => _items;

	/// <summary>The balls' billboards the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SpriteBatch> Sprites => _batches;

	/// <summary>One mesh per cell frame of <paramref name="root"/>, as many as its longest sequence holds; null for a cell with no faces.</summary>
	private GpuMesh?[] UploadCells(TSObject root, TextureAtlas? atlas) {
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

		return cells;
	}

	/// <summary>
	/// The pool's balls placed for <paramref name="camera"/>, walked as <c>ObjList_RebuildFrame</c> adds them, under a
	/// projection whose <c>Raster_FocalShift</c> is <paramref name="focalShift"/>. With <paramref name="flatRamps"/> each
	/// takes <c>WorldShades_ResetRamps</c>' ramps (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, int focalShift, bool flatRamps = false) {
		_items.Clear();
		_batches.Clear();
		var shading = _scene.Shading;
		foreach (var ball in _pool.Balls) {
			if (ball.Shape is not { } shape || !_roots.TryGetValue(shape, out var root)) {
				continue;
			}

			int distance = camera.Position.ApproxDistanceTo(ball.Position);
			if (ball.Position.Z < 0 || shading.IsBeyondLastBand(Es1SmokeBall.TypeTag, distance)) {
				continue;
			}

			var frame = Transform3.Identity;
			frame.X = ball.Position.X;
			frame.Y = ball.Position.Y;
			frame.Z = ball.Position.Z;
			var transform = WorldScale.ToRenderMatrix(frame);
			int cell = ball.CellFrame;
			if (cell >= 0 && cell < root.Cells.Length && root.Cells[cell] is { } mesh) {
				_items.Add(new SceneItem(mesh, transform, _bank?.Texture) {
					RampBlock = flatRamps ? shading.FlatBlock : shading.Block(Es1SmokeBall.TypeTag, distance),
					LightAnchor = (ball.Position, (shape as TSBasePart)?.Radius ?? 0),
				});
			}

			if (_bank is { } bank && root.Sprites.Length > 0) {
				_batches.Add(new SpriteBatch(root.Sprites, bank.Atlas, bank.Texture, transform, cell,
					UprightFocalPixels: 1 << focalShift, PaletteHandle: _palette?.Handle));
			}
		}
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}
}
