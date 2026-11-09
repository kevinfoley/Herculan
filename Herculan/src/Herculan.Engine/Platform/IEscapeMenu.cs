namespace Herculan.Engine.Platform;

/// <summary>
/// The host's own layer over a mission, which [Shift+Esc] raises and [Esc] backs out of before the game's views see the
/// key. This engine's own; retail has no such layer.
/// </summary>
public interface IEscapeMenu {
	/// <summary>Whether any of the menu is up.</summary>
	bool Up { get; }

	/// <summary>Takes down whatever of the menu is up; false when nothing was.</summary>
	bool BackOut();

	/// <summary>Raises the menu.</summary>
	void Show();
}
