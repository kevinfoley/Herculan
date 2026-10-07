using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The cockpit's own nav marker — a point the player drops under themselves and is then steered back
/// to. It is the second thing the front-window HUD's waypoint indicators can be pointing at, and the
/// only one the player puts there.
///
/// <para>Three fields of the cockpit view: the position at <c>view+0x25e</c>, a "set" flag at
/// <c>+0x26a</c> and an "armed" flag at <c>+0x26b</c>. <c>NavMarker_DropAtPlayer</c> (<c>00434974</c>) drops one, copying the
/// player machine's current position wholesale, and <c>NavMarker_Tick</c> (<c>004349ac</c>) — called from the cockpit's
/// own paint, once a frame — is the whole of its lifecycle. The marker cannot be cleared by walking
/// away and back in one step: leaving <see cref="ClearRange"/> is what arms it, and only an armed
/// marker clears on return.</para>
///
/// <para>The host holds it beside the mission clock rather than on the machine, because it is
/// cockpit-view state, not the machine's.</para>
///
/// <para>It is dropped by <c>[Alt+D]</c>. Derivation, and what it is for, in
/// docs/retail/simulation/player-waypoints.md.</para>
/// </summary>
public sealed class NavMarker {
	/// <summary>
	/// Ground range the marker is armed at and cleared inside, the same 10000 units — 60 metres — an
	/// AI machine calls a waypoint reached at.
	/// </summary>
	public const int ClearRange = 10000;

	/// <summary>Where the marker is, meaningful only while <see cref="IsSet"/>.</summary>
	public Vec3i Position { get; private set; }

	/// <summary><c>view+0x26a</c> — whether a marker is down at all.</summary>
	public bool IsSet { get; private set; }

	/// <summary>
	/// <c>view+0x26b</c> — whether the player has since left <see cref="ClearRange"/> of it. A marker
	/// dropped where the player stands is unarmed, which is what stops it clearing on the same frame.
	/// </summary>
	public bool Armed { get; private set; }

	/// <summary>
	/// <c>NavMarker_DropAtPlayer</c> (<c>00434974</c>) — drop a marker on the player machine's own position. A marker already down
	/// is replaced, and the new one starts unarmed however far the old one had been left behind.
	/// </summary>
	public void Drop(Vec3i playerPosition) {
		Position = playerPosition;
		IsSet = true;
		Armed = false;
	}

	/// <summary>
	/// <c>NavMarker_Tick</c> (<c>004349ac</c>) — one frame of the marker. Arms on leaving, clears and announces on
	/// returning; the message is <see cref="SystemMessages.WaypointReached"/>, the same line a route
	/// waypoint posts.
	/// </summary>
	public void Tick(Vec3i playerPosition, MessagePort? messages) {
		if (!IsSet) {
			return;
		}

		int range = SimMath.FastMagnitude2D(
			playerPosition.X - Position.X, playerPosition.Y - Position.Y);

		if (!Armed) {
			if (range > ClearRange) {
				Armed = true;
			}

			return;
		}

		if (range < ClearRange) {
			IsSet = false;
			messages?.Post(SystemMessages.WaypointReached);
		}
	}
}
