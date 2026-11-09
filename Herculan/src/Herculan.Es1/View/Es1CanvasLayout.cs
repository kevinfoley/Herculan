using Herculan.Engine.Render;
using Silk.NET.Maths;

namespace Herculan.Es1.View;

/// <summary>
/// <b>This engine's own:</b> where the monitor and the cockpit's canvas around it (<see cref="Es1Canvas"/>) land in a
/// window wider than 4:3 for one frame, under the "High resolution widescreen" tweak
/// (docs/herculan/plan-es1-first-mission.md#decisions, decision 30). ES2's port lays out its three panels the same way
/// (its <c>CockpitScreenLayout</c>).
///
/// <para>The monitor keeps its 4:3 rect's size, fit by height, and the canvas is drawn at the same scale around it: the
/// window shows <see cref="SideColumns"/> canvas columns on each side of the 4:3 rect, at most a side panel's 320. While
/// the monitor's canvas column (<see cref="Es1ViewManager.MonitorCanvasPoint"/>) stays within <see cref="Reach"/> of view
/// 0's, the window follows it, so the monitor stays centred and a glance slides the canvas past; beyond that the window
/// stands with the side panel's outer edge on its own and the monitor moves across it. So a glance in a window that
/// shows <i>e</i> columns beside the monitor slides only 320 − <i>e</i> columns, retail's whole 320 in a 4:3 window. Rows
/// follow the monitor's canvas row throughout.</para>
/// </summary>
public sealed class Es1CanvasLayout {
	private readonly int _left, _top, _width, _height;

	private Es1CanvasLayout(Vector2D<int> window, int left, int top, int width, int height, int sideColumns, int monitorColumn,
			int monitorRow) {
		Window = window;
		_left = left;
		_top = top;
		_width = width;
		_height = height;
		SideColumns = sideColumns;
		Reach = Es1VideoMemory.PageWidth - sideColumns;
		MonitorColumn = monitorColumn;
		MonitorRow = monitorRow;
		WindowColumn = Math.Clamp(monitorColumn, -Reach, Reach);
		MonitorFit = new FrameFit(Es1Monitor.DisplayFrame, window) { ShiftX = CanvasX(Es1VideoMemory.PageWidth + monitorColumn) - left };
	}

	/// <summary>The window's framebuffer size.</summary>
	public Vector2D<int> Window { get; }

	/// <summary><i>e</i>: the canvas columns the window shows beside the 4:3 rect on each side, the last of them perhaps in part; at most 320, 0 in a window no wider than 4:3.</summary>
	public int SideColumns { get; }

	/// <summary>320 − <see cref="SideColumns"/>: how far the monitor's canvas column may move from view 0's with the window following it.</summary>
	public int Reach { get; }

	/// <summary>The monitor's canvas column, from view 0's left edge.</summary>
	public int MonitorColumn { get; }

	/// <summary>The monitor's canvas row, from the canvas's top.</summary>
	public int MonitorRow { get; }

	/// <summary>The window's canvas offset from view 0's: the monitor's column held to ±<see cref="Reach"/>.</summary>
	public int WindowColumn { get; }

	/// <summary>The monitor's 4:3 frame at its place in the window, moved off centre by its column less the window's.</summary>
	public FrameFit MonitorFit { get; }

	/// <summary>
	/// The window's layout for the monitor at canvas column <paramref name="monitorColumn"/> and row
	/// <paramref name="monitorRow"/>.
	/// </summary>
	public static Es1CanvasLayout Create(Vector2D<int> window, int monitorColumn, int monitorRow) {
		var (left, top, width, height) = new FrameFit(Es1Monitor.DisplayFrame, window).Placement;
		int side = Math.Max(0, left);
		int columns = width > 0 ? Math.Min(Es1VideoMemory.PageWidth, (side * Es1VideoMemory.PageWidth + width - 1) / width) : 0;
		return new Es1CanvasLayout(window, left, top, width, height, columns, monitorColumn, monitorRow);
	}

	/// <summary>The window's x of canvas column <paramref name="column"/>'s left edge, counted from the canvas's left, view 3's.</summary>
	public int CanvasX(int column) =>
		_left + (int)MathF.Round((column - Es1VideoMemory.PageWidth - WindowColumn) * _width / (float)Es1VideoMemory.PageWidth);

	/// <summary>The window's y of canvas row <paramref name="row"/>'s top edge.</summary>
	public int CanvasY(int row) => _top + (int)MathF.Round((row - MonitorRow) * _height / (float)Es1VideoMemory.PageHeight);

	/// <summary>Where the canvas's columns <paramref name="column0"/> up to <paramref name="column1"/> and rows <paramref name="row0"/> up to <paramref name="row1"/> land in the window: left, top, width and height.</summary>
	public (int Left, int Top, int Width, int Height) CanvasRect(int column0, int row0, int column1, int row1) {
		int x0 = CanvasX(column0), y0 = CanvasY(row0);
		return (x0, y0, CanvasX(column1) - x0, CanvasY(row1) - y0);
	}

	/// <summary>
	/// The window's columns left of view 0's page, the monitor's rows: where the heads-down display's left edge column
	/// is stretched, under the side panel.
	/// </summary>
	public (int Left, int Top, int Width, int Height) LeftStretch => (0, _top, Math.Max(0, CanvasX(Es1VideoMemory.PageWidth)), _height);

	/// <summary>The window's columns right of view 0's page, where the heads-down display's right edge column is stretched.</summary>
	public (int Left, int Top, int Width, int Height) RightStretch {
		get {
			int x = CanvasX(2 * Es1VideoMemory.PageWidth);
			return (x, _top, Math.Max(0, Window.X - x), _height);
		}
	}

	/// <summary>
	/// The host's pointer mapped onto the monitor's 320 columns wherever they lie in the window and held to their edges,
	/// so a pointer over a side panel rests on the monitor's edge column toward it.
	/// </summary>
	public ScreenPointer Pointer(Vector2D<int> client) =>
		new(MonitorFit, client, new Vector2D<int>(Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight));
}
