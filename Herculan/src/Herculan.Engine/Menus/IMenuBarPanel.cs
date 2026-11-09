using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// A game's panel that one of <see cref="HostMenuBar"/>'s items opens and that [Esc] backs out of, drawn by the host
/// itself because it needs the host's state. A panel the bar draws as well is an <see cref="IDrawnMenuBarPanel"/>.
/// </summary>
public interface IMenuBarPanel {
	/// <summary>Whether the panel, or anything it raised over itself, is up.</summary>
	bool IsOpen { get; }

	/// <summary>What choosing the panel's item does.</summary>
	void Open();

	/// <summary>
	/// [Esc]'s step back (<see cref="HostMenuBar.BackOut"/>): takes down whatever of the panel is up, discarding an
	/// unsaved edit. The bar calls it on every panel, open or not.
	/// </summary>
	void BackOut();
}

/// <summary>An <see cref="IMenuBarPanel"/> that <see cref="HostMenuBar.Draw"/> draws.</summary>
public interface IDrawnMenuBarPanel : IMenuBarPanel {
	/// <summary>
	/// Draws the panel, when it is up. <paramref name="owner"/> is the native window a picker belongs to, and
	/// <paramref name="centre"/> when the panel is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	void Draw(nint owner, ImGuiCond centre);
}
