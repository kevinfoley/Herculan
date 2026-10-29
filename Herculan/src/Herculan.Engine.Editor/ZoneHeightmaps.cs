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
/// until <see cref="Dispose"/>. A terrain image's heights (<see cref="TerrainImages"/>) are pictured the same way
/// (<see cref="Picture"/>).
/// </summary>
internal sealed class ZoneHeightmaps : IDisposable {
	private readonly GL _gl;
	private readonly GameContent _simulator;
	private readonly Dictionary<short, GpuTexture?> _textures = [];

	// The terrain images pictured, by their heights array; few, since a form asks for the one image it holds.
	private readonly Dictionary<byte[], GpuTexture> _pictures = new(ReferenceEqualityComparer.Instance);

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

	/// <summary>
	/// The picture of <paramref name="heights"/>, <paramref name="side"/> rows of <paramref name="side"/> in image order,
	/// uploaded the first time this array is asked for and kept until <see cref="Forget"/> or <see cref="Dispose"/>.
	/// </summary>
	public GpuTexture Picture(byte[] heights, int side) {
		if (!_pictures.TryGetValue(heights, out var texture)) {
			texture = Stretched(heights, side, side);
			_pictures[heights] = texture;
		}

		return texture;
	}

	/// <summary>Frees the picture of <paramref name="heights"/>, once nothing asks for it any more.</summary>
	public void Forget(byte[] heights) {
		if (_pictures.Remove(heights, out var texture)) {
			texture.Dispose();
		}
	}

	// Zone <zone>'s heightmap image, or null when its .dba cannot be read; the reason is logged.
	private DynamixBitmap? Image(short zone) {
		DynamixBitmap image;
		try {
			image = TerrainZoneLoader.ReadHeightmapImage(_simulator, zone);
		} catch (InvalidDataException failure) {
			Console.Error.WriteLine($"Zone {zone}'s heightmap: {failure.Message}");
			return null;
		}

		if (image.ImageData!.Length < image.Cols * image.Rows) {
			Console.Error.WriteLine($"Zone {zone}'s heightmap is {image.Cols}x{image.Rows} with {image.ImageData.Length} pixels.");
			return null;
		}

		return image;
	}

	public void Dispose() {
		foreach (var texture in _textures.Values.Concat(_pictures.Values)) {
			texture?.Dispose();
		}

		_textures.Clear();
		_pictures.Clear();
	}

	private GpuTexture? Upload(short zone) =>
		Image(zone) is { } image ? Stretched(image.ImageData!, image.Cols, image.Rows) : null;

	// <heights>, <height> rows of <width>, stretched so the lowest is black and the highest white.
	private GpuTexture Stretched(byte[] heights, int width, int height) {
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
