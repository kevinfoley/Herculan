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
	private Es1ZoneScene(Es1Theater theater, short zone, DynamixPalette? palette, Es1Shading shading,
			Es1WorldData? world, IReadOnlyList<MeshBuild?> models, IReadOnlyList<Es1PlacedShape> shapes) {
		Theater = theater;
		Zone = zone;
		Palette = palette;
		Shading = shading;
		World = world;
		Models = models;
		Shapes = shapes;
	}

	/// <summary>The sky and the banded ground the world file sets up, or null without a world file or palette to build them from.</summary>
	public Es1Horizon? Horizon { get; private init; }

	/// <summary>The textured ground, or null when the world file's ground texture or fade file is missing.</summary>
	public Es1TexturedGround? Ground { get; private init; }

	/// <summary>The horizon skyline, or null for a world file that names no sky bank, as every one with <i>d</i> 1 does.</summary>
	public Es1Skyline? Skyline { get; private init; }

	/// <summary>The cloud layer, or null without a world file, for one that names no cloud bank, or without the bank.</summary>
	public Es1Clouds? Clouds { get; private init; }

	/// <summary>The scattered points, or null without a world file or for one whose count is 0.</summary>
	public Es1GroundSpecks? Specks { get; private init; }

	/// <summary>The theater and world file the scene was loaded for.</summary>
	public Es1Theater Theater { get; }

	/// <summary>The zone, header word 1; negative loads no shapes.</summary>
	public short Zone { get; }

	/// <summary><c>dpl\</c><see cref="Es1Theater.PaletteName"/>, or null when the install lacks it.</summary>
	public DynamixPalette? Palette { get; }

	/// <summary>How the shapes are coloured, from <see cref="Palette"/>, <see cref="World"/> and <c>datshades</c>.</summary>
	public Es1Shading Shading { get; }

	/// <summary>
	/// <c>wld\</c><see cref="Es1Theater.WorldName"/>, or null when the install lacks it: <see cref="Horizon"/>,
	/// <see cref="Ground"/> and <see cref="Skyline"/> are built from it.
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

		var shades = content.Read("dat", "SHADES.DAT") is { } shadesBytes
			? new WorldShadesTransformer().Parse(shadesBytes)
			: null;
		var shading = Es1Shading.For(palette, world, shades);
		var library = content.Read("dgs", theater.Name + ".DGS") is { } libraryBytes
			? new BasesDgsTransformer().Parse(libraryBytes)
			: null;

		// Terrain_LoadZoneShapes binds no bitmap bank to the library's shapes, so they are built with none.
		var models = (library?.Shapes ?? Array.Empty<GridShape>())
			.Select(shape => shape.Geometry is { } geometry
				? DtsMeshBuilder.BuildRoot(geometry, atlas: null, shading.Surfaces)
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

		return new Es1ZoneScene(theater, zone, palette, shading, world, models, shapes) {
			Horizon = world != null ? Es1Horizon.From(world, palette) : null,
			Ground = world != null ? LoadGround(content, world) : null,
			Skyline = world is { SkyBank.Length: > 0 }
				&& content.Read("dba", world.SkyBank + ".DBA") is { } bankBytes
					? Es1Skyline.From(new DynamixBitmapArrayTransformer().Parse(bankBytes), palette)
					: null,
			Clouds = world is { CloudBank.Length: > 0 }
				&& content.Read("dba", world.CloudBank + ".DBA") is { } cloudBytes
					? Es1Clouds.From(world, new DynamixBitmapArrayTransformer().Parse(cloudBytes))
					: null,
			Specks = world != null ? Es1GroundSpecks.From(world) : null,
		};
	}

	/// <summary>
	/// <c>GndTex_Load</c>'s two files: <c>dba\&lt;fourth string&gt;.64k</c> and <c>dat\&lt;fifth string&gt;</c>
	/// (docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture). Retail asserts when the fade file does not
	/// open; here the ground is banded instead.
	/// </summary>
	private static Es1TexturedGround? LoadGround(GameContent content, Es1WorldData world) {
		var texture = content.Read("dba", world.GroundTextureBank + ".64K") is { } textureBytes
			? new Es1GroundTextureTransformer().Parse(textureBytes)
			: null;
		var fade = content.Read("dat", world.GroundFadeFile + ".DAT") is { } fadeBytes
			? new Es1GroundFadeTransformer().Parse(fadeBytes)
			: null;
		return Es1TexturedGround.From(world, texture, fade);
	}
}
