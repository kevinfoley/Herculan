using System.Buffers.Binary;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.World;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The mission's loading screen: <c>Sim_InitMissionSession</c> (<c>004614fc</c>) choosing it with
/// <c>Sim_ReadLoadingScreenChoice</c> (<c>004613b4</c>) and showing it with <c>Sim_ShowLoadingScreen</c>
/// (<c>00461344</c>), up while the mission loads (docs/retail/simulation/mission-loading.md). It is held in the host's
/// window (<see cref="HostWindow.Hold"/>) and comes down when the mission's turn starts. The <c>hba</c> picture is the
/// one panel mode 3 takes, as every other bank this engine's cockpit draws is (<see cref="HudSpriteSheet"/>).
/// </summary>
sealed class LoadingScreen(HostWindow window) : IDisposable {
	private RgbaImage? _image;
	private GpuTexture? _texture;
	private Overlay2DRenderer? _overlay;
	private bool _disposed;

	/// <summary>
	/// Shows the loading screen for the mission at <paramref name="scriptPath"/>, out of <paramref name="content"/>. A
	/// mission whose screen cannot be read loads behind black, having said so.
	/// </summary>
	public void Show(GameContent content, string scriptPath) {
		if (NameFor(scriptPath) is not { } name) {
			Console.Error.WriteLine($"No loading screen: {scriptPath} or its {MissionLoader.PlayerFileName} could not be read.");
			window.PumpEvents();
			return;
		}

		_image = Decode(content, name);
		if (_image == null) {
			Console.Error.WriteLine($"No loading screen: {HudSpriteSheet.ResourceFolder}\\{name}.HBA or dpl\\{name}.DPL is missing.");
			window.PumpEvents();
			return;
		}

		window.Hold(Draw);
	}

	/// <inheritdoc cref="HostWindow.PumpEvents"/>
	public void PumpEvents() => window.PumpEvents();

	/// <summary>
	/// <c>Sim_ReadLoadingScreenChoice</c>'s choice as the resource name <c>Sim_InitMissionSession</c> builds from it:
	/// <c>LD_</c>, then <c>FLYR</c> when the fifth <c>int16</c> of <c>player.mec</c>, the player's machine type, is
	/// 8 (the RAZOR) and <c>HERC</c> otherwise, then the theater in the script's first <c>int16</c>, plus one. Null when
	/// either file cannot be read that far.
	/// </summary>
	public static string? NameFor(string scriptPath) {
		if (ReadInt16(scriptPath, 0) is not { } theater
			|| ReadInt16(MissionLoader.PlayerPathFor(scriptPath), 8) is not { } machineType) {
			return null;
		}

		return $"LD_{(machineType == ControlsPanel.RazorTypeIndex ? "FLYR" : "HERC")}{(char)('1' + theater)}";
	}

	private static short? ReadInt16(string path, int offset) {
		try {
			using var file = File.OpenRead(path);
			Span<byte> bytes = stackalloc byte[2];
			file.Position = offset;
			return file.Read(bytes) == 2 ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : null;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	// Frame 0 of the bitmap through the screen's own palette, which Palette_UploadWhole installs whole before the blit.
	private static RgbaImage? Decode(GameContent content, string name) {
		if (content.Read("dpl", name + ".DPL") is not { } paletteBytes
			|| new DynamixPaletteTransformer().Parse(paletteBytes) is not DynamixPalette palette
			|| content.Read(HudSpriteSheet.ResourceFolder, name + "." + HudSpriteSheet.ResourceFolder.ToUpperInvariant())
				is not { } bankBytes
			|| new DynamixBitmapArrayTransformer().Parse(bankBytes) is not DynamixBitmapArray { Images: [{ } frame, ..] }
			|| frame.Cols <= 0 || frame.Rows <= 0) {
			return null;
		}

		int width = frame.Cols;
		int height = frame.Rows;
		var pixels = new byte[width * height * 4];
		byte[] indices = frame.ImageData ?? [];
		for (int i = 0; i < Math.Min(indices.Length, width * height); i++) {
			if (palette.Colors.TryGetValue(indices[i], out var entry)) {
				var color = entry.GetColor();
				pixels[i * 4] = color.R;
				pixels[i * 4 + 1] = color.G;
				pixels[i * 4 + 2] = color.B;
			}

			pixels[i * 4 + 3] = 255;
		}

		return new RgbaImage(pixels, width, height);
	}

	// Blitted at the screen's origin, (0, 0), and placed in the window as the simulator places all its 640x480 screen
	// art (AlertPanelLayout.Placement).
	private void Draw(GL gl, Vector2D<int> framebuffer) {
		if (_disposed || _image is not { } image) {
			return;
		}

		_texture ??= new GpuTexture(gl, image.Pixels, image.Width, image.Height);
		_overlay ??= new Overlay2DRenderer(gl);
		var place = AlertPanelLayout.Placement.CreateAt(framebuffer.X, framebuffer.Y, 0, 0);
		var (x0, y0) = place.ToWindow(0, 0);
		var (x1, y1) = place.ToWindow(image.Width, image.Height);
		_overlay.Begin(0, 0, framebuffer.X, framebuffer.Y);
		_overlay.Clear();
		_overlay.AddTexturedQuad(x0, y0, x1, y1, 0f, 0f, 1f, 1f);
		_overlay.Submit(_texture);
		_overlay.End();
	}

	public void Dispose() {
		_disposed = true;
		_texture?.Dispose();
		_overlay?.Dispose();
	}

	private sealed record RgbaImage(byte[] Pixels, int Width, int Height);
}
