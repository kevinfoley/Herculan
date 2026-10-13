using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Platform;

/// <summary>
/// [PrtScn] while a window is full screen (<see cref="EngineWindow.ToggleFullScreen()"/>): the key is kept from Windows
/// and the window's next frame goes on the clipboard instead. Windows' own capture reads the compositor's copy of the
/// screen, which for a window covering its monitor can be frames old, since the driver can present such a window
/// without the compositor. This engine's own feature, at the user's request; retail has nothing behind it.
///
/// <para>The key is caught by a low-level keyboard hook, which sees it before Windows' screen-capture shortcut does,
/// and is taken only when the window is the foreground one and no [Alt], [Ctrl], [Shift] or [Win] is held, so
/// Windows' other capture keys still reach it. The hook is installed for as long as the window is full screen and
/// no longer: every keystroke on the desktop waits on it, and it is answered from this thread's message pump.</para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
sealed unsafe class PrintScreenCapture : IDisposable {
	private readonly nint _window;
	private readonly Action<int, int, byte[]>? _captured;
	private readonly LowLevelKeyboardProc _proc;
	private nint _hook;
	private bool _requested;

	/// <param name="captured">
	/// Handed each captured frame as well as the clipboard: its width, its height and its pixels, rows top-down, three
	/// bytes a pixel in R, G, B order, no padding. Null when nothing wants them.
	/// </param>
	public PrintScreenCapture(nint window, Action<int, int, byte[]>? captured) {
		_window = window;
		_captured = captured;
		// Kept in a field so the delegate the hook calls outlives the call that installed it.
		_proc = OnKey;
		_hook = SetWindowsHookExW(WhKeyboardLl, _proc, GetModuleHandleW(null), 0);
		if (_hook == 0) {
			Console.Error.WriteLine($"[PrtScn] could not install the keyboard hook (error {Marshal.GetLastWin32Error()}); "
				+ "Windows' own capture stays on the key.");
		}
	}

	private nint OnKey(int code, nint message, nint data) {
		if (code == HcAction) {
			var key = (KbdLlHookStruct*)data;
			if (key->VkCode == VkSnapshot && (key->Flags & LlkhfAltDown) == 0 && !Held(VkControl) && !Held(VkShift)
					&& !Held(VkLWin) && !Held(VkRWin) && GetForegroundWindow() == _window) {
				if (message is WmKeyDown or WmSysKeyDown) {
					_requested = true;
				}

				return 1;
			}
		}

		return CallNextHookEx(_hook, code, message, data);
	}

	private static bool Held(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

	/// <summary>
	/// After a frame is drawn and before it is presented: when [PrtScn] has gone down since the last frame, copies the
	/// default framebuffer's back buffer to the clipboard as a 24-bit device-independent bitmap.
	/// </summary>
	public void CaptureIfRequested(GL gl, int width, int height) {
		if (!_requested || width <= 0 || height <= 0) {
			return;
		}

		_requested = false;

		// GL's rows run bottom-up from the bottom-left corner, as a DIB's do with a positive height, and its default
		// pack alignment of 4 pads each row as a DIB's are padded, so the pixels are read straight into place.
		int stride = (width * 3 + 3) & ~3;
		int size = BitmapInfoHeaderSize + stride * height;
		nint memory = GlobalAlloc(GmemMoveable, (nuint)size);
		if (memory == 0) {
			return;
		}

		byte* bytes = (byte*)GlobalLock(memory);
		var header = new Span<byte>(bytes, BitmapInfoHeaderSize);
		header.Clear();
		BinaryPrimitives.WriteInt32LittleEndian(header, BitmapInfoHeaderSize);
		BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
		BinaryPrimitives.WriteInt32LittleEndian(header[8..], height);
		BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
		BinaryPrimitives.WriteInt16LittleEndian(header[14..], 24);
		BinaryPrimitives.WriteInt32LittleEndian(header[20..], stride * height);

		gl.GetInteger(GetPName.ReadFramebufferBinding, out int readFramebuffer);
		gl.GetInteger(GetPName.PackAlignment, out int packAlignment);
		gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
		gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
		gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Bgr, PixelType.UnsignedByte, bytes + BitmapInfoHeaderSize);
		gl.PixelStore(PixelStoreParameter.PackAlignment, packAlignment);
		gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)readFramebuffer);

		// Read out before the clipboard takes the memory: the DIB's bottom-up BGR rows turned top-down RGB.
		byte[]? rgb = null;
		if (_captured != null) {
			rgb = new byte[width * 3 * height];
			byte* pixels = bytes + BitmapInfoHeaderSize;
			for (int y = 0; y < height; y++) {
				byte* source = pixels + (height - 1 - y) * stride;
				int target = y * width * 3;
				for (int x = 0; x < width * 3; x += 3) {
					rgb[target + x] = source[x + 2];
					rgb[target + x + 1] = source[x + 1];
					rgb[target + x + 2] = source[x];
				}
			}
		}

		GlobalUnlock(memory);
		if (rgb != null) {
			_captured!(width, height, rgb);
		}

		// The clipboard owns the memory once SetClipboardData takes it.
		if (OpenClipboard(_window)) {
			bool taken = EmptyClipboard() && SetClipboardData(CfDib, memory) != 0;
			CloseClipboard();
			if (taken) {
				Console.WriteLine($"[PrtScn] copied the frame to the clipboard ({width}x{height}).");
				return;
			}
		}

		GlobalFree(memory);
		Console.Error.WriteLine("[PrtScn] the clipboard could not be written.");
	}

	public void Dispose() {
		if (_hook != 0) {
			UnhookWindowsHookEx(_hook);
			_hook = 0;
		}
	}

	private const int WhKeyboardLl = 13;
	private const int HcAction = 0;
	private const nint WmKeyDown = 0x100;
	private const nint WmSysKeyDown = 0x104;
	private const uint VkSnapshot = 0x2c;
	private const int VkShift = 0x10;
	private const int VkControl = 0x11;
	private const int VkLWin = 0x5b;
	private const int VkRWin = 0x5c;
	private const uint LlkhfAltDown = 0x20;
	private const uint CfDib = 8;
	private const uint GmemMoveable = 0x2;
	private const int BitmapInfoHeaderSize = 40;

	[StructLayout(LayoutKind.Sequential)]
	private struct KbdLlHookStruct {
		public uint VkCode;
		public uint ScanCode;
		public uint Flags;
		public uint Time;
		public nuint ExtraInfo;
	}

	private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookExW(int hookId, LowLevelKeyboardProc proc, nint module, uint threadId);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint hook);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKey);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandleW(string? name);

	[DllImport("user32.dll")]
	private static extern bool OpenClipboard(nint owner);

	[DllImport("user32.dll")]
	private static extern bool EmptyClipboard();

	[DllImport("user32.dll")]
	private static extern nint SetClipboardData(uint format, nint memory);

	[DllImport("user32.dll")]
	private static extern bool CloseClipboard();

	[DllImport("kernel32.dll")]
	private static extern nint GlobalAlloc(uint flags, nuint bytes);

	[DllImport("kernel32.dll")]
	private static extern nint GlobalLock(nint memory);

	[DllImport("kernel32.dll")]
	private static extern bool GlobalUnlock(nint memory);

	[DllImport("kernel32.dll")]
	private static extern nint GlobalFree(nint memory);
}
