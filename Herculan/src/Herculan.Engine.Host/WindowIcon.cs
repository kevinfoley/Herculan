using HercWorks.Help.Html;
using Herculan.Engine.Platform;
using Silk.NET.Core;

namespace Herculan.Engine.Host;

/// <summary>
/// The HERCULAN icon for the title bar and taskbar button where the executable's own icon is not used — any platform
/// but Windows (<see cref="HostWindow.Icons"/>) — read from the <c>HE Icon *.png</c> files the project links from
/// <c>Branding/</c> into <c>Assets/Icon</c> beside the executable. Without these, GLFW gives its windows the system's
/// default icon.
///
/// <para>Loose files rather than an embedded resource so the artwork, which is not under the MIT license, stays a
/// visible file covered by <c>Branding/README.md</c>. A missing or unreadable file is skipped, and with none the window
/// keeps the default icon.</para>
/// </summary>
internal static class WindowIcon {
	/// <summary>Every size that loaded, for the system to pick from.</summary>
	public static RawImage[] Load() {
		string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon");
		if (!Directory.Exists(folder)) {
			return [];
		}

		var images = new List<RawImage>();
		foreach (string path in Directory.EnumerateFiles(folder, "HE Icon *.png")) {
			try {
				images.Add(Decode(File.ReadAllBytes(path)));
			} catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Skipping window icon {path}: {e.Message}");
			}
		}

		return [.. images];
	}

	// The largest icon read, a side in pixels.
	private const int MaxSide = 1024;

	// The icon in <png>; an InvalidDataException with the reason when it is not a PNG PngDecoder reads.
	private static RawImage Decode(byte[] png) {
		if (PngDecoder.Decode(png, MaxSide, out string? failure) is not { } image) {
			throw new InvalidDataException(failure);
		}

		return new RawImage(image.Width, image.Height, image.Rgba);
	}
}
