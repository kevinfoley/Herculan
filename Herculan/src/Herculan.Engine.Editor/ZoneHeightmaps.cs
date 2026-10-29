using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Terrain;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Editor;

/// <summary>
/// File &gt; New mission's picture of a zone: its <c>dba\zoneNNNN.dba</c> heightmap
/// (<see cref="TerrainZoneLoader.ReadHeightmapImage"/>) as a grayscale texture, image row 0 at the top, stretched so the
/// zone's lowest ground is black and its highest white. Each zone is uploaded the first time it is asked for and kept
/// until <see cref="Dispose"/>.
/// </summary>
internal sealed class ZoneHeightmaps : IDisposable {
	private readonly GL _gl;
	private readonly GameContent _simulator;
	private readonly Dictionary<short, GpuTexture?> _textures = [];

	/// <param name="simulator">DBSIM's mount, which holds the zones.</param>
	public ZoneHeightmaps(GL gl, GameContent simulator) {
		_gl = gl;
		_simulator = simulator;
	}

	/// <summary>Zone <paramref name="zone"/>'s heightmap, or null when its <c>.dba</c> cannot be read.</summary>
	public GpuTexture? For(short zone) {
		if (!_textures.TryGetValue(zone, out var texture)) {
			texture = Upload(zone);
			_textures[zone] = texture;
		}

		return texture;
	}

	public void Dispose() {
		foreach (var texture in _textures.Values) {
			texture?.Dispose();
		}

		_textures.Clear();
	}

	private GpuTexture? Upload(short zone) {
		DynamixBitmap image;
		try {
			image = TerrainZoneLoader.ReadHeightmapImage(_simulator, zone);
		} catch (InvalidDataException failure) {
			Console.Error.WriteLine($"Zone {zone}'s heightmap: {failure.Message}");
			return null;
		}

		int width = image.Cols;
		int height = image.Rows;
		byte[] heights = image.ImageData!;
		if (heights.Length < width * height) {
			Console.Error.WriteLine($"Zone {zone}'s heightmap is {width}x{height} with {heights.Length} pixels.");
			return null;
		}

		var pixels = heights.AsSpan(0, width * height);
		int low = pixels[0], high = pixels[0];
		foreach (byte raw in pixels) {
			low = Math.Min(low, raw);
			high = Math.Max(high, raw);
		}

		int range = Math.Max(1, high - low);
		var rgba = new byte[width * height * 4];
		for (int i = 0; i < width * height; i++) {
			byte gray = (byte)((pixels[i] - low) * 255 / range);
			rgba[i * 4] = gray;
			rgba[i * 4 + 1] = gray;
			rgba[i * 4 + 2] = gray;
			rgba[i * 4 + 3] = 255;
		}

		return new GpuTexture(_gl, rgba, width, height);
	}
}
