using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Render;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The sky backdrop against retail's <c>hzline</c>: docs/retail/rendering/distance-fog-and-sky.md, "The sky —
/// hzline".
/// </summary>
public class SkyGradientTests {
	private const int Width = 640;
	private const int Height = 480;

	/// <summary>
	/// The retail fields: zenith 208, 6-row bands, a gap of 1, and <paramref name="bandCount"/>
	/// bands, over a palette whose entry <c>i</c> has red <c>i</c> so a band's colour names its entry.
	/// </summary>
	private static SkyGradient RetailSky(short bandCount = 16, short offset = 0) {
		var world = new WorldData { Header = new short[] { 2, 208, 6, bandCount, 239, 1, 1, offset, 233, 1, 47, 223, 6000, 7 } };
		var palette = new DynamixPalette();
		for (int i = 0; i < 256; i++) {
			var entry = new ColorBytes((byte)i, 0, 0, 255);
			entry.SetColor(RgbaColor.FromArgb(255, i, 0, 0));
			palette.Colors[i] = entry;
		}

		return SkyGradient.From(world, palette)!;
	}

	private static int Entry(SkyGradient sky, int band) => (int)MathF.Round(sky.Bands[band].X * 255f);

	[Fact]
	public void TheRunGoesFromTheHorizonColourDownToTheZenith() {
		var sixteen = RetailSky(16);
		Assert.Equal(16, sixteen.Bands.Length);
		Assert.Equal(223, Entry(sixteen, 0));
		Assert.Equal(208, Entry(sixteen, 15));

		var fifteen = RetailSky(15);
		Assert.Equal(15, fifteen.Bands.Length);
		Assert.Equal(222, Entry(fifteen, 0));
		Assert.Equal(208, Entry(fifteen, 14));
	}

	/// <summary>
	/// Level, entry <c>C - i</c> covers rows <c>H - 6i</c> to <c>H - 6i + 5</c>, the horizon colour
	/// the line's own row and everything below, and the zenith everything above the last band.
	/// </summary>
	[Theory]
	[InlineData(-40, 0)]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(6, 1)]
	[InlineData(7, 2)]
	[InlineData(79, 14)]
	[InlineData(84, 14)]
	[InlineData(85, 15)]
	[InlineData(400, 15)]
	public void LevelBandsAreSixRowsFromTheRowAboveTheLine(int rowsAbove, int band) {
		Assert.Equal(band, RetailSky().BandAt(rowsAbove, rolled: false, cosRoll: 1f));
	}

	/// <summary>
	/// Rolled, the band edges stand <c>6i - 5·cos²r</c> from the line, and the ground fill covers
	/// everything more than a row below it.
	/// </summary>
	[Fact]
	public void RolledBandsSitNearerTheGround() {
		var sky = RetailSky();
		float cos = 1f;
		Assert.Equal(0, sky.BandAt(-1.5f, rolled: true, cos));
		Assert.Equal(1, sky.BandAt(-0.5f, rolled: true, cos));
		Assert.Equal(1, sky.BandAt(0.9f, rolled: true, cos));
		Assert.Equal(2, sky.BandAt(1.1f, rolled: true, cos));
		Assert.Equal(2, sky.BandAt(6.9f, rolled: true, cos));
		Assert.Equal(3, sky.BandAt(7.1f, rolled: true, cos));
	}

	[Fact]
	public void TheLineOffsetAppliesOneAndAHalfTimes() {
		Assert.Equal(6, RetailSky(offset: 4).LineOffset);
	}

	/// <summary>
	/// <c>Hzline_BuildHorizon</c>'s formula is the vanishing line of level ground for the view the
	/// engine's camera builds from the same euler triple: two far horizontal directions project onto
	/// it, and a direction above the horizontal lands on the sky side.
	/// </summary>
	[Theory]
	[InlineData(0, 0)]
	[InlineData(1200, 0)]
	[InlineData(-1500, 0)]
	[InlineData(0, 900)]
	[InlineData(800, -1200)]
	[InlineData(-600, 2500)]
	public void TheLineIsTheCamerasHorizon(int pitch, int roll) {
		var camera = new Camera { Yaw = 3000, Pitch = pitch, Roll = roll, PrincipalPoint = new Vector2(0.5f, 95f / 240f) };
		var viewProjection = camera.ViewMatrix * camera.ProjectionMatrix((float)Width / Height);
		float focal = Height / (2f * MathF.Tan(camera.FieldOfView / 2f));
		var centre = new Vector2(camera.PrincipalPoint.X * Width, (1f - camera.PrincipalPoint.Y) * Height);
		var line = RetailSky().Place((short)pitch, (short)roll, centre, focal);

		var forward = camera.Forward;
		var flat = Vector3.Normalize(new Vector3(forward.X, 0f, forward.Z));
		foreach (float turn in new[] { -0.3f, 0f, 0.3f }) {
			var direction = Vector3.Transform(flat, Matrix4x4.CreateRotationY(turn));
			Assert.InRange(DistanceAbove(direction, viewProjection, line), -0.5f, 0.5f);
		}

		var raised = Vector3.Normalize(flat + new Vector3(0f, 0.1f, 0f));
		Assert.True(DistanceAbove(raised, viewProjection, line) > 10f);
	}

	private static float DistanceAbove(Vector3 direction, Matrix4x4 viewProjection, HorizonPlacement line) {
		var clip = Vector4.Transform(new Vector4(direction, 0f), viewProjection);
		Assert.True(clip.W > 0f);
		var window = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * Width, (clip.Y / clip.W * 0.5f + 0.5f) * Height);
		return Vector2.Dot(window - line.Mid, line.Up) / line.Scale;
	}

	/// <summary>Every retail theater builds a sky: 16 bands ending on 223, or 15 ending on 222.</summary>
	[Fact]
	public void EveryRetailTheaterBuildsItsSky() {
		string? root = GameInstall.Locate(null);
		if (root == null) {
			return;
		}

		var content = GameContent.MountSimulator(root);
		for (int worldIndex = 0; worldIndex < TheaterDescriptor.Count; worldIndex++) {
			var theater = TheaterDescriptor.LoadByWorldIndex(content, worldIndex);
			var palette = new DynamixPaletteTransformer().Parse(content.Read("dpl", theater.PaletteName + ".DPL")!) as DynamixPalette;
			var sky = SkyGradient.From(theater.File, palette);
			Assert.NotNull(sky);

			bool sixteen = worldIndex is 0 or 2 or 6;
			Assert.Equal(sixteen ? 16 : 15, sky.Bands.Length);
			Assert.Equal(sixteen ? 223 : 222, theater.File.HorizonColor);
			Assert.Equal(6, sky.BandHeight);
			Assert.Equal(1, sky.HorizonGap);
			Assert.Equal(0, sky.LineOffset);
		}
	}
}
