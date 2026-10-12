using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.World;

namespace Herculan.Es1.Scene;

/// <summary>One zone record as the simulator places it: which library shape, and its frame (docs/es1/formats/zone-terrain.md#the-simulators-load).</summary>
/// <param name="ShapeIndex">The record's index into the theater's shape library.</param>
/// <param name="Frame">The rotation <c>BuildEulerRotationMatrixQ14</c> builds from the record's angles, at its position.</param>
public readonly record struct Es1PlacedShape(int ShapeIndex, Transform3 Frame);

/// <summary>
/// What the simulator's world load reads for a zone's ground and what stands on it, loaded for drawing with nothing
/// simulated: the theater's palette and world file, its shape library, and the zone's placed shapes.
/// <c>DBSim_LoadScriptDat</c> (<c>ES1DBSIM 0001d850</c>) has <c>Terrain_LoadZoneShapes</c> load the shapes and then
/// <c>World_LoadTheater</c> the palette and world file (docs/es1/formats/script-dat.md#reading).
/// </summary>
public sealed class Es1ZoneScene {
	private Es1ZoneScene(Es1Theater theater, short zone, DynamixPalette? palette, SurfaceShading shading,
			Es1WorldData? world, IReadOnlyList<MeshBuild?> models, IReadOnlyList<Es1PlacedShape> shapes) {
		Theater = theater;
		Zone = zone;
		Palette = palette;
		Shading = shading;
		World = world;
		Models = models;
		Shapes = shapes;
	}

	/// <summary>The theater and world file the scene was loaded for.</summary>
	public Es1Theater Theater { get; }

	/// <summary>The zone, header word 1; negative loads no shapes.</summary>
	public short Zone { get; }

	/// <summary><c>dpl\</c><see cref="Es1Theater.PaletteName"/>, or null when the install lacks it.</summary>
	public DynamixPalette? Palette { get; }

	/// <summary><see cref="Palette"/> through <see cref="Es1PlaceholderShading"/>.</summary>
	public SurfaceShading Shading { get; }

	/// <summary>
	/// <c>wld\</c><see cref="Es1Theater.WorldName"/>, or null when the install lacks it. Nothing draws from it yet: the
	/// sky, the ground and the clouds it sets up are Open (docs/es1/formats/wld-world.md#open).
	/// </summary>
	public Es1WorldData? World { get; }

	/// <summary>The theater's shape library, one mesh per record; null for a record with no geometry.</summary>
	public IReadOnlyList<MeshBuild?> Models { get; }

	/// <summary>The zone's records in file order.</summary>
	public IReadOnlyList<Es1PlacedShape> Shapes { get; }

	/// <summary>
	/// Loads the scene for <paramref name="zone"/> in <paramref name="theater"/>.
	///
	/// <para>The simulator builds an object for every record whatever its shape index, which it does not check against
	/// the library's count; a record past the library is kept here and has no model to draw.</para>
	/// </summary>
	/// <exception cref="FileNotFoundException">The zone file is missing, which retail asserts on (<c>terrain.cpp</c> line <c>0x32</c>).</exception>
	public static Es1ZoneScene Load(GameContent content, Es1Theater theater, short zone) {
		var palette = content.Read("dpl", theater.PaletteName + ".DPL") is { } paletteBytes
			? new DynamixPaletteTransformer().Parse(paletteBytes)
			: null;

		var world = content.Read("wld", theater.WorldName + ".WLD") is { } worldBytes
			? new Es1WorldDataTransformer().Parse(worldBytes)
			: null;

		var shading = Es1PlaceholderShading.For(palette);
		var library = content.Read("dgs", theater.Name + ".DGS") is { } libraryBytes
			? new BasesDgsTransformer().Parse(libraryBytes)
			: null;

		// Terrain_LoadZoneShapes binds no bitmap bank to the library's shapes, so they are built with none.
		var models = (library?.Shapes ?? Array.Empty<GridShape>())
			.Select(shape => shape.Geometry is { } geometry
				? DtsMeshBuilder.BuildRoot(geometry, atlas: null, shading)
				: (MeshBuild?)null)
			.ToArray();

		var shapes = new List<Es1PlacedShape>();
		if (zone >= 0) {
			byte[] zoneBytes = content.ReadRequired("dat", $"ZONE{zone}.DAT");
			var file = new Es1ZoneDatTransformer().Parse(zoneBytes)
				?? throw new InvalidDataException($"dat\\ZONE{zone}.DAT does not read as an ES1 zone file.");

			foreach (var record in file.Shapes) {
				var frame = Transform3.FromEuler(record.Angles[0], record.Angles[1], record.Angles[2]);
				frame.X = record.X;
				frame.Y = record.Y;
				frame.Z = record.Z;
				shapes.Add(new Es1PlacedShape(record.ShapeIndex, frame));
			}
		}

		return new Es1ZoneScene(theater, zone, palette, shading, world, models, shapes);
	}
}
