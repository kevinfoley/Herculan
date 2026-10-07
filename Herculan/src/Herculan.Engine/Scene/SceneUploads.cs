using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Scene;

/// <summary>
/// The mission's models and terrain on the GPU: one upload per distinct model, however many objects share it —
/// a mission routinely fields several of the same machine and a row of identical structures.
/// </summary>
public sealed class SceneUploads : IDisposable {
	private readonly List<IDisposable> _disposables = new();

	public SceneUploads(GL gl, MissionScene scene) {
		TerrainMesh = new GpuMesh(gl, scene.TerrainMesh);
		TerrainTexture = scene.TerrainBank != null ? scene.TerrainBank.Atlas.Upload(gl, indexed: true) : null;

		// Which models are actually going to be drawn a node at a time: one whose segments exist *and*
		// whose object has an animation thread to pose them with. A shape that carries no ANAnimList has
		// neither, and has to keep the flat mesh or its cells, which have the rest pose baked in — its
		// segments alone would put every part at the shape's origin.
		//
		// The question is asked of the object rather than of its class: the eight animated-library
		// structure types get a shape instance and threads out of Base_Construct's tail the same way a
		// machine does (see BaseObject's constructor), and every other structure type gets neither.
		//
		// Asked of every root of a machine's LOD chain, not of the one it starts on: the crude roots are
		// posed by the same thread on the same transform ids (see SceneModelLibrary.MechDetailRoots), so
		// each of them has to be uploaded as segments too or a machine loses its animation the moment the
		// distance to the eye picks a coarser one.
		var animatedKeys = scene.Objects
			.Where(o => o.Object.Shape is { Threads.Count: > 0 })
			.SelectMany(o => o.Detail?.Roots ?? (o.Model is { } single ? new[] { single } : Array.Empty<SceneModel>()))
			.Where(m => m.Segments.Length > 0)
			.Select(m => m.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// A model that is drawn posed uploads its segments instead of its flat mesh: the two are the same
		// triangles, and only one of them is ever drawn.
		foreach (var model in scene.Models) {
			if (animatedKeys.Contains(model.Key)) {
				Segments[model.Key] = model.Segments.Select(segment => new GpuMesh(gl, segment.Vertices, segment.TriangleVertexCount, segment.PointVertexCount)).ToArray();
				_disposables.AddRange(Segments[model.Key]);
			} else if (model.Cells.Length > 0) {
				// A shape whose cells damage drives uploads every one of them and draws the ones its
				// object's cell frames name, rather than being rebuilt each time a part comes off.
				Cells[model.Key] = model.Cells.Select(cell => new GpuMesh(gl, cell.Vertices, cell.TriangleVertexCount, cell.PointVertexCount)).ToArray();
				_disposables.AddRange(Cells[model.Key]);
			} else if (model.Mesh.Length > 0) {
				// A pure billboard shape — every EMP round, every impact effect — has no triangles at all
				// and gets no mesh; its atlas below is the whole of it.
				Meshes[model.Key] = new GpuMesh(gl, model.Mesh, model.TriangleVertexCount, model.PointVertexCount);
			}

			if (model.Atlas != null) {
				Textures[model.Key] = model.Atlas.Upload(gl, indexed: true);
				if (model.Sprites.Length > 0) {
					SpriteTextures[model.Key] = model.Atlas.Upload(gl);
				}
			}
		}

		_disposables.AddRange(Meshes.Values);
		_disposables.AddRange(Textures.Values);
		_disposables.AddRange(SpriteTextures.Values);
	}

	public GpuMesh TerrainMesh { get; }
	public GpuTexture? TerrainTexture { get; }

	/// <summary>Each model drawn whole, by key.</summary>
	public Dictionary<string, GpuMesh> Meshes { get; } = new();

	/// <summary>Each model's atlas as palette indices, which a lit mesh is drawn through.</summary>
	public Dictionary<string, GpuTexture> Textures { get; } = new();

	/// <summary>
	/// Billboards blit a frame unlit, so they need the atlas as COLOUR where a lit mesh needs it as palette
	/// indices (see PaletteRampTable). Only the shapes that actually carry sprites get the second upload.
	/// </summary>
	public Dictionary<string, GpuTexture> SpriteTextures { get; } = new();

	/// <summary>
	/// An object whose shape animates is drawn a node at a time, so each mesh here is one geometry segment
	/// riding one transform of one object — see MissionScene.PosedTransformOf. A machine and an animated
	/// structure are both drawn this way: a radar mast's dish and an armed tower's turret are nodes an
	/// animation thread moves, exactly as a leg is.
	/// </summary>
	public Dictionary<string, GpuMesh[]> Segments { get; } = new();

	/// <summary>
	/// And the same for a shape split by cell rather than by node -- a flyer, or a structure of one of the 57
	/// types that carry no animation, which loses parts to damage but has no posed nodes.
	/// </summary>
	public Dictionary<string, GpuMesh[]> Cells { get; } = new();

	/// <summary>The model's indexed atlas handle, or none.</summary>
	public uint? TextureOf(string key) => Textures.TryGetValue(key, out var bound) ? bound.Handle : null;

	public void Dispose() {
		TerrainMesh.Dispose();
		TerrainTexture?.Dispose();
		foreach (var disposable in _disposables) {
			disposable.Dispose();
		}
	}
}
