namespace Herculan.Engine.Content;

/// <summary>
/// CONTROLS settings that stand in for some of <c>prefs.cfg</c>'s while they apply, read and written through
/// the simulator's preferences in their place, and saved somewhere other than <c>prefs.cfg</c>, or nowhere: a gamepad's
/// in Original mode (<see cref="Input.GamepadControls"/>), and in Earthsiege 2 a Custom profile's fixed rows.
/// </summary>
public interface IControlsOverride {
	/// <summary>Whether option <paramref name="index"/>, by its <c>prefs.cfg</c> offset, is one of these.</summary>
	bool Covers(int index);

	/// <summary>Option <paramref name="index"/>'s byte, for an index <see cref="Covers"/> takes.</summary>
	byte this[int index] { get; set; }

	/// <summary>What the CONTROLS panel's RECOMMEND puts in option <paramref name="index"/>.</summary>
	byte Recommended(int index);

	/// <summary>Whether the CONTROLS panel's axis rows are fixed rather than the player's to step.</summary>
	bool AxisRowsFixed { get; }

	/// <summary>Whether the CONTROLS panel's button rows and its RECOMMEND are fixed too, so the panel takes no click but DONE.</summary>
	bool ButtonRowsFixed { get; }

	/// <summary>Writes these settings where they are kept, as the CONTROLS panel's DONE would write <c>prefs.cfg</c>.</summary>
	void Save();
}
