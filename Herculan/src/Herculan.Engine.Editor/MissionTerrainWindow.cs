using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The floating "Terrain" window, opened from Mission Settings &gt; Terrain: the zone the mission is played in, with its
/// size and the retail missions played in it (<see cref="ZoneCatalog"/>), then replacing the mission's terrain from an
/// image and exporting its heightmap (<see cref="TerrainView"/>).
/// </summary>
internal sealed class MissionTerrainWindow {
	// Where it first opens and how big, in pixels on a 100% display (ScaledImGui.Scaled).
	private const float StartWidth = 460f;
	private const float StartHeight = 560f;

	private readonly Func<string> _zone;
	private readonly TerrainView _view;
	private bool _open;

	/// <param name="zone">The zone the mission is played in, described (<see cref="ZoneCatalog.Describe"/>, <see cref="ZoneCatalog.DescribeOwn"/>).</param>
	public MissionTerrainWindow(Func<string> zone, TerrainView view) {
		_zone = zone;
		_view = view;
	}

	public void Open() => _open = true;

	/// <summary>Draws the window, if it is open. Call once per frame, inside the ImGui frame.</summary>
	/// <param name="owner">The native window to parent the file and folder pickers to on Windows (0 for none).</param>
	public void Draw(nint owner) {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(StartWidth), ScaledImGui.Scaled(StartHeight)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (ImGui.Begin("Terrain###mission-terrain", ref _open, ImGuiWindowFlags.NoCollapse)) {
			EditorText.Wrapped(_zone());
			ImGui.Separator();
			_view.Draw(owner);
		}

		ImGui.End();
	}
}
