namespace Herculan.Engine.Menus;

/// <summary>An item of the <see cref="PauseMenu"/> that raises one of retail's own panels.</summary>
public enum PauseMenuItem {
	/// <summary>The objectives panel, <c>F11</c>'s.</summary>
	Objectives,

	/// <summary>The preferences panel, <c>F12</c>'s.</summary>
	Preferences,

	/// <summary>The mission status panel, <c>Q</c>'s.</summary>
	Quit,
}

/// <summary>
/// The game behind a <see cref="PauseMenu"/>: what holds its mission and what raises its panels. Each game's mission host
/// supplies one. This engine's own.
/// </summary>
public interface IPauseMenuHost {
	/// <summary>Whether Start may raise the menu now: the mission is not over and no retail panel holds the input.</summary>
	bool CanOpen { get; }

	/// <summary>Whether a retail panel the menu raised is over it, which hides the menu until it is down.</summary>
	bool PanelUp { get; }

	/// <summary>Holds the mission as the menu comes up.</summary>
	void Hold();

	/// <summary>Lets the mission run on as the menu goes down.</summary>
	void Resume();

	/// <summary>Whether <paramref name="item"/>'s panel would come up now; the menu greys the item when it would not.</summary>
	bool CanRaise(PauseMenuItem item);

	/// <summary>Raises <paramref name="item"/>'s panel, after which the menu is back.</summary>
	void Raise(PauseMenuItem item);
}
