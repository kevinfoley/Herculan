using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One squadmate's pick record, <c>0x2a</c> bytes of the command screen's <c>+0x85</c>
/// (docs/es1/simulation/hdd-command-screen.md#the-screen-object, the layout ES2's,
/// docs/retail/simulation/heads-down-display.md#the-link-line): the pick being made, the current one, and the one the
/// last <c>XMIT</c> sent, the saved one. <c>HddPilotPick_ElementCtor</c> (<c>ES1DBSIM 0003f418</c>) builds the two
/// points; the command screen's constructor clears the two held bytes.
/// </summary>
public sealed class Es1HddPickRecord {
	/// <summary>The kinds: a point on the map, a unit.</summary>
	public const int PointKind = 0, UnitKind = 1;

	/// <summary><c>+0x85</c>, the saved point.</summary>
	public Vec3i SavedPoint { get; set; }

	/// <summary><c>+0x91</c>, the current point.</summary>
	public Vec3i CurrentPoint { get; set; }

	/// <summary><c>+0x9d</c>, the saved unit.</summary>
	public IEs1SimObject? SavedUnit { get; set; }

	/// <summary><c>+0xa1</c>, the current unit.</summary>
	public IEs1SimObject? CurrentUnit { get; set; }

	/// <summary><c>+0xa5</c>, the saved pick is held.</summary>
	public bool SavedHeld { get; set; }

	/// <summary><c>+0xa6</c>, the current pick is held.</summary>
	public bool CurrentHeld { get; set; }

	/// <summary><c>+0xa7</c>, the saved pick's kind, <see cref="PointKind"/> or <see cref="UnitKind"/>.</summary>
	public int SavedKind { get; set; }

	/// <summary><c>+0xab</c>, the current pick's kind.</summary>
	public int CurrentKind { get; set; }
}
