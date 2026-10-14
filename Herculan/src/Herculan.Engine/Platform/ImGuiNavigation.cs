using System.Numerics;
using System.Runtime.InteropServices;

namespace Herculan.Engine.Platform;

/// <summary>
/// The pieces of Dear ImGui's internal navigation API that the hosts need and ImGui.NET does not wrap, called straight
/// into the cimgui library ImGui.NET ships, which exports them on every platform it ships for.
/// </summary>
public static class ImGuiNavigation {
	private const string Library = "cimgui";

	// ImGuiNavLayer_Menu: a window's menu bar, beside ImGuiNavLayer_Main, its body.
	private const int MenuLayer = 1;

	/// <summary>
	/// Focuses the window being drawn and puts the keyboard and gamepad navigation on its menu bar, with the highlight
	/// on the bar's first item from the next frame. Call between <c>BeginMainMenuBar</c> (or <c>BeginMenuBar</c>) and
	/// the bar's first item.
	///
	/// <para>ImGui has no public way to do it. Its own is a tap of the gamepad's X with the window focused, and a main
	/// menu bar never keeps the focus long enough for one: as it appears it takes the focus with the navigation on its
	/// empty body, and <c>EndMainMenuBar</c> then hands the focus back to the window under it, which is none when the bar
	/// is all there is.</para>
	/// </summary>
	public static void FocusMenuBar() {
		nint window = igGetCurrentWindow();
		igFocusWindow(window, 0);
		igSetNavID(0, MenuLayer, 0, default);
		igNavInitWindow(window, true);
		igNavRestoreHighlightAfterMove();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ImRect {
		public Vector2 Min;
		public Vector2 Max;
	}

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern nint igGetCurrentWindow();

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void igFocusWindow(nint window, int flags);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void igSetNavID(uint id, int navLayer, uint focusScopeId, ImRect rectRel);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void igNavInitWindow(nint window, [MarshalAs(UnmanagedType.U1)] bool forceReinit);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void igNavRestoreHighlightAfterMove();
}
