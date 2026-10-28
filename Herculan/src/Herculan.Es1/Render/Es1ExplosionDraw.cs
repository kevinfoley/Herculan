using System.Numerics;
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
/// The impact effects' share of the frame (docs/es1/simulation/impact-effects.md#drawing): every effect of
/// <c>g_ExplosionPool</c> whose position's Z is not negative and that lies within its type tag's last distance band
/// (<see cref="Es1Shading.IsBeyondLastBand"/>), drawn as <c>TS_OBJ</c>'s render draws it on the cell its row's sequence
/// stands on: a root's bitmap parts as Earthsiege's upright billboards out of the root's <c>dba\explo&lt;n&gt;</c> bank
/// (<see cref="SpriteBatch.UprightFocalPixels"/>), and any faces as the zone's shapes draw theirs, in the ramps of its
/// tag's band, with the effect lights picked by its position and its shape's radius. Each live effect's ground shape is
/// drawn with the shadows within <see cref="Es1WorldDraw.ShadowRange"/> of the camera, on its sequence 0's cell, its Z
/// set to 0 (<c>FlatObj_DrawExploCopy</c>, <c>ES1DBSIM 00005ca8</c>).
/// </summary>
public sealed class Es1ExplosionDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1ExplosionPool _pool;
	private readonly GL _gl;
	private readonly List<IDisposable> _owned = new();
	private readonly GpuMesh?[][] _cells;
	private readonly SpriteQuad[][][] _sprites;
	private readonly (TextureAtlas Atlas, uint Sprites, uint Faces)?[] _banks = new (TextureAtlas, uint, uint)?[Es1ExplosionPool.BankCount];
	private readonly GpuMesh?[] _groundCells = [];
	private readonly List<SceneItem> _items = new();
	private readonly List<SceneItem> _groundItems = new();
	private readonly List<SpriteBatch> _batches = new();

	/// <summary>
	/// Uploads <c>dba\explo0</c> to <c>dba\explo8</c>, every cell of every root of <c>dts\explos</c> and the ground
	/// shapes' root of the flat set (<see cref="Es1FlatObjPool.Shapes"/>), and pulls each root's billboards.
	/// </summary>
	public Es1ExplosionDraw(GL gl, GameContent content, Es1ZoneScene scene, Es1ExplosionPool pool) {
		_gl = gl;
		_scene = scene;
		_pool = pool;

		for (int b = 0; b < _banks.Length; b++) {
			if (scene.Palette != null && content.Read("dba", $"EXPLO{b}.DBA") is { } bytes
					&& new DynamixBitmapArrayTransformer().Parse(bytes) is { } bank
					&& TextureAtlas.Build(bank, scene.Palette, transparentIndex0: true) is { } atlas) {
				// PLACEHOLDER: palette index 0 left unpainted, as ES2's billboard leaves it; what VGA_8's scaled blit,
				// slot 29 (0008c744), does with it is unread (docs/es1/rendering/screen-pages.md#open).
				// PLACEHOLDER: the billboards keep the colours of the palette they were built in, where retail's blit
				// writes indices the live palette shows; the plan's hit flash covers them
				// (docs/herculan/plan-es1-first-mission.md#what-the-slice-leaves-out).
				var sprites = atlas.Upload(gl);
				var faces = atlas.Upload(gl, indexed: true);
				_owned.Add(sprites);
				_owned.Add(faces);
				_banks[b] = (atlas, sprites.Handle, faces.Handle);
			}
		}

		_cells = new GpuMesh?[pool.Shapes.Count][];
		_sprites = new SpriteQuad[pool.Shapes.Count][][];
		for (int root = 0; root < pool.Shapes.Count; root++) {
			_cells[root] = UploadCells(pool.Shapes[root], Bank(root)?.Atlas);
			_sprites[root] = DtsSpriteBuilder.Build(pool.Shapes[root]);
		}

		if (Es1ExplosionPool.GroundShapeRoot < pool.FlatObjects.Shapes.Count) {
			_groundCells = UploadCells(pool.FlatObjects.Shapes[Es1ExplosionPool.GroundShapeRoot], null);
		}
	}

	/// <summary>The effects' faces the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SceneItem> Items => _items;

	/// <summary>The effects' ground shapes the last <see cref="Update"/> placed, drawn with the shadows.</summary>
	public IReadOnlyList<SceneItem> GroundItems => _groundItems;

	/// <summary>The effects' billboards the last <see cref="Update"/> placed.</summary>
	public IReadOnlyList<SpriteBatch> Sprites => _batches;

	private (TextureAtlas Atlas, uint Sprites, uint Faces)? Bank(int root) =>
		root < _pool.RootBanks.Count && _pool.RootBanks[root] is { } bank && bank >= 0 && bank < _banks.Length ? _banks[bank] : null;

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
	/// The pool's effects placed for <paramref name="camera"/>, walked as <c>ObjList_RebuildFrame</c> adds them, under a
	/// projection whose <c>Raster_FocalShift</c> is <paramref name="focalShift"/>. With <paramref name="flatRamps"/> each
	/// takes <c>WorldShades_ResetRamps</c>' ramps (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, int focalShift, bool flatRamps = false) {
		_items.Clear();
		_groundItems.Clear();
		_batches.Clear();
		var shading = _scene.Shading;
		foreach (var effect in _pool.Live) {
			if (effect.GroundShape is { Destroyed: false } ground
					&& camera.Position.ApproxDistanceTo(ground.Position) < Es1WorldDraw.ShadowRange) {
				var groundFrame = ground.PlaceForDraw();
				// Drawn outside the object list, it picks no lights; the retail sets' root holds solid faces alone, which
				// no light changes.
				if (ground.CellFrame < _groundCells.Length && _groundCells[ground.CellFrame] is { } groundMesh) {
					_groundItems.Add(new SceneItem(groundMesh, WorldScale.ToRenderMatrix(groundFrame)));
				}
			}

			int distance = camera.Position.ApproxDistanceTo(effect.Position);
			if (effect.Position.Z < 0 || shading.IsBeyondLastBand(effect.Tag, distance)) {
				continue;
			}

			int root = effect.Shape != null ? IndexOf(effect.Shape) : -1;
			if (root < 0) {
				continue;
			}

			var frame = Transform3.FromEuler(0, effect.Roll, 0);
			frame.X = effect.Position.X;
			frame.Y = effect.Position.Y;
			frame.Z = effect.Position.Z;
			var transform = WorldScale.ToRenderMatrix(frame);
			int cell = effect.DrawnCellFrame;
			var bank = Bank(root);
			if (cell >= 0 && cell < _cells[root].Length && _cells[root][cell] is { } mesh) {
				_items.Add(new SceneItem(mesh, transform, bank?.Faces) {
					RampBlock = flatRamps ? shading.FlatBlock : shading.Block(effect.Tag, distance),
					LightAnchor = (effect.Position, (effect.Shape as TSBasePart)?.Radius ?? 0),
				});
			}

			if (bank is { } sprites && _sprites[root].Length > 0) {
				_batches.Add(new SpriteBatch(_sprites[root], sprites.Atlas, sprites.Sprites, transform, cell,
					UprightFocalPixels: 1 << focalShift));
			}
		}
	}

	private int IndexOf(TSObject shape) {
		for (int i = 0; i < _pool.Shapes.Count; i++) {
			if (ReferenceEquals(_pool.Shapes[i], shape)) {
				return i;
			}
		}

		return -1;
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}
}
