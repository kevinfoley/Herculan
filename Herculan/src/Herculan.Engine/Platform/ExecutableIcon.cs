using System.Runtime.InteropServices;

namespace Herculan.Engine.Platform;

/// <summary>
/// On Windows, gives a window the running executable's own icon (the host's <c>&lt;ApplicationIcon&gt;</c>) at the sizes
/// for the window's display scale, and loads them again when that scale changes. GLFW's own class icon is the system's
/// default unless the executable has a resource named <c>GLFW_ICON</c>, which the .NET SDK's
/// <c>&lt;ApplicationIcon&gt;</c> does not produce.
///
/// <para>GLFW makes the process per-monitor DPI aware (v2), and Windows leaves such a window's icons as they are when the
/// scale changes: icons loaded once at start-up stay at the start-up scale's sizes, enlarged on the taskbar after a
/// change to a higher scale. So this loads the large and small icons at <c>SM_CXICON</c> and <c>SM_CXSMICON</c> for the
/// window's DPI, sets them with <c>WM_SETICON</c>, and does it again on <c>WM_DPICHANGED</c>, which it receives by
/// subclassing the window. The taskbar picks up a <c>WM_SETICON</c> at once.</para>
///
/// <para>The shell does not pick a native size for each place it shows the icon. Measured on Windows 11 at 100% scale:
/// the title bar draws the small icon (16 px), while the taskbar button scales the large icon (32 px) into 24 px and
/// the Alt+Tab header scales it to 16 px. The large icon's art has to hold up at both of those sizes. For a size the
/// icon group lacks, Windows loads the next larger entry up to 64 px and scales it down; above 64 px (a 250% scale and
/// up) it enlarges the 64 px entry rather than reducing the 256 px one.</para>
///
/// <para>Alt+Tab keeps the icon it read when it first saw the window, so <see cref="EngineWindow"/> creates its windows
/// hidden and shows them after this has run.</para>
///
/// <para>The window class's icons are set to the executable's as well, at the system's sizes: GLFW registers its class
/// with the system's default application icon, which a window that starts full screen showed on its taskbar button
/// with only the window's own icons set.</para>
/// </summary>
internal sealed class ExecutableIcon : IDisposable {
	// The resource id the .NET SDK gives the icon group <ApplicationIcon> embeds.
	private const nint ApplicationIconId = 32512;
	private const nint IconGroupType = 14;

	private const uint ImageIcon = 1;
	private const uint SetIconMessage = 0x0080, DpiChangedMessage = 0x02E0;
	private const nint SmallIcon = 0, LargeIcon = 1;
	private const int LargeIconWidth = 11, SmallIconWidth = 49;
	private const int WindowProcIndex = -4;
	private const int ClassIconIndex = -14, ClassSmallIconIndex = -34;
	private const uint DefaultSize = 0x40, Shared = 0x8000;

	private readonly nint _hwnd;
	private readonly nint _module;
	// Held for as long as the window can call it.
	private readonly WindowProc _subclass;
	private readonly nint _previousProc;
	private nint _large, _small;

	private ExecutableIcon(nint hwnd, nint module) {
		_hwnd = hwnd;
		_module = module;
		SetClassIcons(hwnd, module);
		Load(GetDpiForWindow(hwnd));
		_subclass = OnMessage;
		_previousProc = SetWindowLongPtrW(hwnd, WindowProcIndex, Marshal.GetFunctionPointerForDelegate(_subclass));
	}

	/// <summary>Gives <paramref name="hwnd"/> the executable's icon; null, changing nothing, when the executable has none.</summary>
	public static ExecutableIcon? TrySet(nint hwnd) {
		nint module = GetModuleHandleW(null);
		return FindResourceW(module, ApplicationIconId, IconGroupType) != 0 ? new ExecutableIcon(hwnd, module) : null;
	}

	private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam) {
		nint result = CallWindowProcW(_previousProc, hwnd, message, wParam, lParam);
		if (message == DpiChangedMessage) {
			Load((uint)(wParam & 0xFFFF));
		}

		return result;
	}

	private void Load(uint dpi) {
		int large = GetSystemMetricsForDpi(LargeIconWidth, dpi);
		int small = GetSystemMetricsForDpi(SmallIconWidth, dpi);
		nint largeIcon = LoadImageW(_module, ApplicationIconId, ImageIcon, large, large, 0);
		nint smallIcon = LoadImageW(_module, ApplicationIconId, ImageIcon, small, small, 0);
		if (largeIcon == 0 || smallIcon == 0) {
			Destroy(largeIcon);
			Destroy(smallIcon);
			return;
		}

		SendMessageW(_hwnd, SetIconMessage, LargeIcon, largeIcon);
		SendMessageW(_hwnd, SetIconMessage, SmallIcon, smallIcon);
		Destroy(_large);
		Destroy(_small);
		(_large, _small) = (largeIcon, smallIcon);
	}

	// Shared icons, which live as long as the module, since the class outlives any one window and its own icons.
	private static void SetClassIcons(nint hwnd, nint module) {
		nint large = LoadImageW(module, ApplicationIconId, ImageIcon, 0, 0, DefaultSize | Shared);
		nint small = LoadImageW(module, ApplicationIconId, ImageIcon,
			GetSystemMetrics(SmallIconWidth), GetSystemMetrics(SmallIconWidth), Shared);
		if (large != 0) {
			SetClassIcon(hwnd, ClassIconIndex, large);
		}
		if (small != 0) {
			SetClassIcon(hwnd, ClassSmallIconIndex, small);
		}
	}

	private static void SetClassIcon(nint hwnd, int index, nint icon) {
		if (Environment.Is64BitProcess) {
			SetClassLongPtrW(hwnd, index, icon);
		} else {
			SetClassLongW(hwnd, index, (int)icon);
		}
	}

	private static void Destroy(nint icon) {
		if (icon != 0) {
			DestroyIcon(icon);
		}
	}

	/// <summary>Undoes the subclassing if the window still exists, and frees the icons.</summary>
	public void Dispose() {
		if (IsWindow(_hwnd)) {
			SetWindowLongPtrW(_hwnd, WindowProcIndex, _previousProc);
		}

		Destroy(_large);
		Destroy(_small);
		_large = _small = 0;
	}

	private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern nint GetModuleHandleW(string? moduleName);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern nint FindResourceW(nint module, nint name, nint type);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint LoadImageW(nint instance, nint name, uint type, int width, int height, uint load);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern bool DestroyIcon(nint icon);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern uint GetDpiForWindow(nint hwnd);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern int GetSystemMetricsForDpi(int index, uint dpi);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint SetClassLongPtrW(nint hwnd, int index, nint value);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern int SetClassLongW(nint hwnd, int index, int value);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern nint CallWindowProcW(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

	[DllImport("user32.dll", ExactSpelling = true)]
	private static extern bool IsWindow(nint hwnd);
}
