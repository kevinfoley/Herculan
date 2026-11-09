using HercWorks.Help.Html;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Host;

/// <summary>
/// <c>--save-prtscn</c>: each frame [PrtScn] puts on the clipboard while full screen (<see cref="HostWindow.PrintScreenCaptured"/>)
/// is also written as a PNG into the install's <c>Screenshots</c> folder, named for the moment it was taken. This
/// engine's own feature; retail has nothing behind it. The encode and write run off the render thread, so a capture
/// does not stall the frame.
/// </summary>
static class PrintScreenFiles {
	public const string FolderName = "Screenshots";

	/// <summary>
	/// Has <paramref name="window"/>'s captures written under the install <paramref name="installRoot"/> names at capture
	/// time: the Earthsiege 2 session's, which may change install meanwhile. A capture while it names none is not written.
	/// </summary>
	public static void Attach(HostWindow window, Func<string?> installRoot) =>
		window.PrintScreenCaptured += (width, height, rgb) => {
			if (installRoot() is not { } root) {
				return;
			}

			string folder = Path.Combine(root, FolderName);
			var taken = DateTime.Now;
			Task.Run(() => Write(folder, taken, width, height, rgb));
		};

	private static void Write(string folder, DateTime taken, int width, int height, byte[] rgb) {
		try {
			byte[] png = PngEncoder.Encode(width, height, rgb);
			Directory.CreateDirectory(folder);
			string stem = $"HERCULAN {taken:yyyy-MM-dd HH-mm-ss}";
			string path = Path.Combine(folder, stem + ".png");
			for (int n = 2; ; n++) {
				try {
					using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
					file.Write(png);
					break;
				} catch (IOException) when (File.Exists(path)) {
					path = Path.Combine(folder, $"{stem} ({n}).png");
				}
			}

			Console.WriteLine($"[PrtScn] saved {path}.");
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"[PrtScn] could not save to {folder}: {e.Message}");
		}
	}
}
