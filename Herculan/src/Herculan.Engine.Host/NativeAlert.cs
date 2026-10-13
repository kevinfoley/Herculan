using System.Diagnostics;
using System.Runtime.InteropServices;
using Herculan.Engine.Host.Install;

namespace Herculan.Engine.Host;

/// <summary>
/// The operating system's own error box, shown with no window of the host's behind it — the stand-in for the
/// <c>MessageBoxA</c> titled <c>Error</c> that DBSIM's assert raises before it quits. Each platform is reached
/// as <see cref="NativePathPicker"/> reaches it: <c>MessageBoxW</c> on Windows, <c>osascript</c>'s
/// <c>display alert</c> on macOS, and <c>zenity</c> or <c>kdialog</c> on Linux. The message goes to standard
/// error as well, unless the caller put it there already, so a Linux desktop with neither tool still says it
/// somewhere.
/// </summary>
static class NativeAlert {
	/// <summary>
	/// Shows <paramref name="message"/> under <paramref name="title"/> and returns once it is closed;
	/// <paramref name="writeToError"/> false leaves it off standard error, for a caller that wrote it there already.
	/// </summary>
	public static void ShowError(string title, string message, bool writeToError = true) {
		if (writeToError) {
			Console.Error.WriteLine(message);
		}

		try {
			if (OperatingSystem.IsWindows()) {
				MessageBoxW(0, message, title, MbOk | MbIconError | MbSetForeground);
			} else {
				ShowWithTool(title, message);
			}
		} catch (Exception ex) when (ex is ExternalException or InvalidOperationException) {
			// The message is on standard error already; a box that will not open is no reason to lose the exit.
		}
	}

	private const uint MbOk = 0x0;
	private const uint MbIconError = 0x10;
	private const uint MbSetForeground = 0x10000;

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

	private static void ShowWithTool(string title, string message) {
		ProcessStartInfo info;
		if (OperatingSystem.IsMacOS()) {
			string script = $"display alert \"{NativePathPicker.AppleScriptEscape(title)}\" "
				+ $"message \"{NativePathPicker.AppleScriptEscape(message)}\" as critical";
			info = new ProcessStartInfo("osascript") { ArgumentList = { "-e", script } };
		} else if (NativePathPicker.LinuxTool() is "zenity") {
			info = new ProcessStartInfo("zenity") { ArgumentList = { "--error", "--title", title, "--text", message } };
		} else if (NativePathPicker.LinuxTool() is "kdialog") {
			info = new ProcessStartInfo("kdialog") { ArgumentList = { "--title", title, "--error", message } };
		} else {
			return;
		}

		info.UseShellExecute = false;
		using var process = Process.Start(info);
		process?.WaitForExit();
	}
}
