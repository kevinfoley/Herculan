using Herculan.Es1.View;

namespace Herculan.Es1.Sim;

/// <summary>
/// The frame's own steps, which a routine inside the tick runs as <c>Sim_Main</c>'s frame runs them
/// (docs/es1/simulation/main-loop.md#the-frame): <c>PreferencesPanel_Raise</c> (<c>ES1DBSIM 00057820</c>) draws frames
/// of its own while the tick stands (docs/es1/simulation/preferences.md#raising-the-panel--preferencespanel_raise-00057820).
/// The host's frame loop (<see cref="Host.Es1FrameLoop"/>) is the one implementation, so a frame drawn inside the tick is
/// the frame the loop draws after it. <c>Cam_Update</c> is the camera's own (<see cref="Es1Camera.Update"/>).
/// </summary>
public interface IEs1Frame {
	/// <summary><c>Sim_RenderFrame</c> (<c>00059c08</c>): the cockpit's frame, then the 3D pass into the draw page.</summary>
	void RenderFrame();

	/// <summary>
	/// <c>Sim_EndFrame</c> (<c>00059b40</c>) with <c>Sim_Frozen</c> <paramref name="frozen"/>: the view change, the page
	/// flip and the view command, yielding each wait for the monitor its routines make.
	/// </summary>
	IEnumerable<Es1Retrace> EndFrame(bool frozen);
}
