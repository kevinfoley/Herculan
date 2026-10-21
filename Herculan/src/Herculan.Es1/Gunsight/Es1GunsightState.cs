using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The gunsight's <c>0x26</c>-byte state block, its <c>+0x7a</c> (docs/es1/simulation/cockpit-gunsight-hud.md#the-state-block),
/// which <c>Gunsight_GetStateBlock</c> (<c>ES1DBSIM 00035180</c>) hands out and <c>Gunsight_SetValues</c> copies over;
/// the target box and the unused child keep a pointer to the gunsight's own.
/// </summary>
public sealed class Es1GunsightState {
	/// <summary>Offset 0, the machine's heading <c>+0xa</c>.</summary>
	public short Heading { get; set; }

	/// <summary>Offset 2, the machine's turret twist <c>+0x22e</c>.</summary>
	public short Twist { get; set; }

	/// <summary>Offset 4, the machine's turret pitch <c>+0x230</c>.</summary>
	public short Pitch { get; set; }

	/// <summary>Offset 6, the selected object, or null.</summary>
	public IEs1SimObject? Selection { get; set; }

	/// <summary>Offsets <c>0xa</c>, <c>0xe</c> and <c>0x12</c>, the selection's position, its <c>+0x20</c>–<c>+0x2b</c>.</summary>
	public Vec3i SelectionPosition { get; set; }

	/// <summary>Offset <c>0x16</c>, the selection's heading <c>+0xa</c>.</summary>
	public short SelectionHeading { get; set; }

	/// <summary>Offset <c>0x1c</c>, the selection's shape's radius.</summary>
	public int SelectionRadius { get; set; }

	/// <summary>Offset <c>0x20</c>, the literal 2000 the update stores with a selection.</summary>
	public int Literal2000 { get; set; }

	/// <summary>Offset <c>0x24</c>, set once a selection reaches the gunsight.</summary>
	public bool SelectionArmed { get; set; }

	/// <summary>Offset <c>0x25</c>, the machine's lock lamp <c>+0x95</c>.</summary>
	public bool LockLamp { get; set; }

	/// <summary>A copy of the block, as <c>Player_PerFrameCockpitUpdate</c> takes one before it writes.</summary>
	public Es1GunsightState Copy() => (Es1GunsightState)MemberwiseClone();

	/// <summary>The <c>0x26</c> bytes of <paramref name="other"/> copied over these.</summary>
	internal void CopyFrom(Es1GunsightState other) {
		Heading = other.Heading;
		Twist = other.Twist;
		Pitch = other.Pitch;
		Selection = other.Selection;
		SelectionPosition = other.SelectionPosition;
		SelectionHeading = other.SelectionHeading;
		SelectionRadius = other.SelectionRadius;
		Literal2000 = other.Literal2000;
		SelectionArmed = other.SelectionArmed;
		LockLamp = other.LockLamp;
	}
}
