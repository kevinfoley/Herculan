using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// The operating system's own choose-a-folder dialog. Neither .NET nor Silk.NET has one, so each platform
/// is reached its own way, with no package added for it: the shell's <c>IFileOpenDialog</c> in folder mode
/// on Windows, <c>osascript</c>'s <c>choose folder</c> on macOS, and <c>zenity</c> or <c>kdialog</c> on
/// Linux, whichever is installed. A Linux desktop with neither has no picker; <see cref="IsAvailable"/>
/// says so, and the caller falls back to a typed path.
/// </summary>
static class NativeFolderPicker {
	/// <summary>Whether this platform has a picker to show.</summary>
	public static bool IsAvailable =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || LinuxTool() != null;

	/// <summary>
	/// Shows the picker without blocking the caller's frame loop, so the window behind it keeps drawing.
	/// Completes with the folder chosen, or null when the player cancelled or there is no picker.
	/// <paramref name="owner"/> is the native window to parent the dialog to on Windows (0 for none);
	/// <paramref name="initialFolder"/>, when it exists, is where the dialog opens.
	/// </summary>
	public static Task<string?> PickAsync(string title, string? initialFolder, nint owner) {
		return OperatingSystem.IsWindows()
			? PickOnStaThread(title, initialFolder, owner)
			: Task.Run(() => PickWithTool(title, initialFolder));
	}

	// --- Windows -----------------------------------------------------------------------------------------

	/// <summary>
	/// The shell's dialogs are apartment-threaded COM objects, and the host's top-level-statement Main runs
	/// in the multithreaded apartment, so the dialog gets a thread of its own.
	/// </summary>
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static Task<string?> PickOnStaThread(string title, string? initialFolder, nint owner) {
		var completion = new TaskCompletionSource<string?>();
		var thread = new Thread(() => {
			try {
				completion.SetResult(PickWindows(title, initialFolder, owner));
			} catch (Exception ex) {
				completion.SetException(ex);
			}
		}) { IsBackground = true, Name = "Folder picker" };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return completion.Task;
	}

	private const uint FosPickFolders = 0x20;
	private const uint FosForceFileSystem = 0x40;
	private const uint FosPathMustExist = 0x800;
	private const uint SigdnFileSysPath = 0x80058000;
	private const int ErrorCancelled = unchecked((int)0x800704C7);

	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static string? PickWindows(string title, string? initialFolder, nint owner) {
		var dialog = (IFileDialog)new FileOpenDialog();
		try {
			dialog.GetOptions(out uint options);
			dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
			dialog.SetTitle(title);

			if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)) {
				Guid shellItemId = typeof(IShellItem).GUID;
				if (SHCreateItemFromParsingName(Path.GetFullPath(initialFolder), 0, ref shellItemId, out var folder) == 0) {
					dialog.SetFolder(folder);
					Marshal.ReleaseComObject(folder);
				}
			}

			int result = dialog.Show(owner);
			if (result == ErrorCancelled) {
				return null;
			}
			Marshal.ThrowExceptionForHR(result);

			dialog.GetResult(out var item);
			try {
				item.GetDisplayName(SigdnFileSysPath, out nint pathPointer);
				try {
					return Marshal.PtrToStringUni(pathPointer);
				} finally {
					Marshal.FreeCoTaskMem(pathPointer);
				}
			} finally {
				Marshal.ReleaseComObject(item);
			}
		} finally {
			Marshal.ReleaseComObject(dialog);
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
	private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, out IShellItem item);

	[ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
	private class FileOpenDialog { }

	// IModalWindow's one method, then IFileDialog's in vtable order. Only the ones this class calls carry
	// real signatures; the rest hold their slots.
	[ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IFileDialog {
		[PreserveSig] int Show(nint owner);
		void SetFileTypes();
		void SetFileTypeIndex();
		void GetFileTypeIndex();
		void Advise();
		void Unadvise();
		void SetOptions(uint options);
		void GetOptions(out uint options);
		void SetDefaultFolder();
		void SetFolder(IShellItem folder);
		void GetFolder();
		void GetCurrentSelection();
		void SetFileName();
		void GetFileName();
		void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
		void SetOkButtonLabel();
		void SetFileNameLabel();
		void GetResult(out IShellItem item);
	}

	[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellItem {
		void BindToHandler();
		void GetParent();
		void GetDisplayName(uint form, out nint name);
	}

	// --- macOS and Linux ---------------------------------------------------------------------------------

	private static string? PickWithTool(string title, string? initialFolder) {
		string? start = !string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)
			? Path.GetFullPath(initialFolder)
			: null;

		ProcessStartInfo info;
		if (OperatingSystem.IsMacOS()) {
			string script = $"POSIX path of (choose folder with prompt \"{AppleScriptEscape(title)}\""
				+ (start != null ? $" default location (POSIX file \"{AppleScriptEscape(start)}\")" : "") + ")";
			info = new ProcessStartInfo("osascript") { ArgumentList = { "-e", script } };
		} else if (LinuxTool() is "zenity") {
			info = new ProcessStartInfo("zenity") { ArgumentList = { "--file-selection", "--directory", "--title", title } };
			if (start != null) {
				info.ArgumentList.Add("--filename");
				info.ArgumentList.Add(start + Path.DirectorySeparatorChar);
			}
		} else if (LinuxTool() is "kdialog") {
			info = new ProcessStartInfo("kdialog") { ArgumentList = { "--title", title, "--getexistingdirectory", start ?? "." } };
		} else {
			return null;
		}

		info.RedirectStandardOutput = true;
		info.UseShellExecute = false;
		using var process = Process.Start(info);
		if (process == null) {
			return null;
		}

		string output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit();

		// Every one of the three exits nonzero on cancel.
		return process.ExitCode == 0 && output.Length > 0 ? output : null;
	}

	private static string AppleScriptEscape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

	private static string? linuxTool;
	private static bool linuxToolProbed;

	/// <summary>The first of <c>zenity</c> and <c>kdialog</c> on the <c>PATH</c>, on Linux; null elsewhere.</summary>
	private static string? LinuxTool() {
		if (!linuxToolProbed) {
			linuxToolProbed = true;
			if (OperatingSystem.IsLinux()) {
				string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
				linuxTool = new[] { "zenity", "kdialog" }
					.FirstOrDefault(tool => directories.Any(directory => File.Exists(Path.Combine(directory, tool))));
			}
		}
		return linuxTool;
	}
}
