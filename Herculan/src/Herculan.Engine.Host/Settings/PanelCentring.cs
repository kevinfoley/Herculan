using System.Numerics;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// When the menu bar's panels go back to the middle of the window. Each is centred as it appears, and ImGui then
/// keeps it where it is relative to the window's top-left corner, so switching full screen on or off would leave it
/// off to one side. Once such a switch's resize has settled, <see cref="Recentre"/> holds for one frame and every
/// panel up is centred again. An ordinary resize by the window's frame leaves the panels where they are.
/// </summary>
sealed class PanelCentring {
	// How many frames the window's size must hold still before a switch's resize counts as done. Leaving full screen
	// for a maximized window resizes it twice, restored and then maximized, and the two need not land on one frame.
	private const int SettleFrames = 10;

	private bool? _fullScreen;
	private bool _pending;
	private Vector2 _size;
	private int _stillFrames;

	/// <summary>Whether the panels are centred again this frame.</summary>
	public bool Recentre { get; private set; }

	/// <summary>The panels' position condition this frame: always while <see cref="Recentre"/> holds, else only as one appears.</summary>
	public ImGuiCond Condition => Recentre ? ImGuiCond.Always : ImGuiCond.Appearing;

	/// <summary>Takes this frame's full-screen state. Call once per frame inside the ImGui frame, before the panels draw.</summary>
	public void Update(bool fullScreen) {
		Recentre = false;
		var size = ImGui.GetMainViewport().Size;

		if (_fullScreen != fullScreen) {
			_pending = _fullScreen != null;
			_fullScreen = fullScreen;
			_stillFrames = 0;
		} else if (_pending) {
			_stillFrames = size == _size ? _stillFrames + 1 : 0;
			if (_stillFrames >= SettleFrames) {
				Recentre = true;
				_pending = false;
			}
		}

		_size = size;
	}
}
