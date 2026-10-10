namespace Herculan.Engine.Sim;

/// <summary>
/// The player's flown missile as the simulation and the cockpit share it: whether one is being flown, the steer
/// input, the trigger byte its flight clears, the round the MFD's missile camera rides, and the press-once latch a
/// flown round asks for as it ends. DBSIM's globals; <see cref="SimWorld.PlayerMissile"/> holds them.
/// </summary>
public sealed class PlayerMissileState {
	/// <summary>
	/// <c>DAT_004d25aa</c> — an electro-optical missile is being flown. <see cref="SimWorld.Tick(short, double)"/> clears it
	/// just before it walks the rounds, and a player-flown round raises it again every tick it is
	/// steered (<see cref="Rocket"/>); <see cref="WeaponMounts.FireTick"/> raises it on the tick that
	/// launches one. While it is up the controls go to the round rather than to the machine, and the
	/// weapon chain holds still. See docs/retail/simulation/rockets.md#the-missile-camera.
	/// </summary>
	public bool Flown { get; internal set; }

	/// <summary>
	/// The two axes and the trigger <c>Rocket_PlayerSteer</c> reads out of the player input block —
	/// what the host built from the controls this frame. It is read, never consumed: the original
	/// rebuilds the block every tick, so zeroing it would cost a second tick in the same host frame its
	/// input. What the steer's own clearing of the trigger does is <see cref="TriggerCleared"/>.
	/// </summary>
	public MissileSteerInput Steer { get; set; }

	/// <summary>
	/// The trigger byte <c>DAT_004d2357</c> cleared part-way through a tick — by the steer, and when a
	/// flown round ends — so the machine's own fire path, which reads it later in the same tick, sees it
	/// released. Reset at the top of every tick, as the original rebuilds the byte.
	/// </summary>
	internal bool TriggerCleared { get; set; }

	/// <summary>
	/// <c>DAT_0049c394</c> — the last round the locally piloted machine launched, whatever its
	/// subtype, which the MFD's missile camera rides. <see cref="SimWorld.FireRocket"/> sets it; the camera
	/// screen clears it the first time it paints and finds the round gone from
	/// <see cref="SimWorld.RocketsInFlight"/>.
	/// </summary>
	public Rocket? Round { get; set; }

	/// <summary>
	/// <c>DAT_0049c398</c> — <see cref="Round"/> ended before its lifetime ran out, which is to
	/// say it struck something. The camera screen flashes on it and clears it.
	/// </summary>
	public bool Struck { get; set; }

	/// <summary>
	/// The <c>Input_LatchButton(1, 1)</c> a flown round makes as it ends: the input layer's press-once
	/// latch on the first button row, which is the host's to hold. Read and cleared by
	/// <see cref="TakeFireRowLatch"/>.
	/// </summary>
	private bool _fireRowLatchRequested;

	/// <summary>
	/// Whether a flown round asked for the first button row to be latched since the last call — see
	/// docs/retail/simulation/rockets.md#flight--rocket_tickupdate-0040a538.
	/// </summary>
	public bool TakeFireRowLatch() {
		bool requested = _fireRowLatchRequested;
		_fireRowLatchRequested = false;
		return requested;
	}

	/// <summary>A flown round has ended: the trigger byte is cleared and row 0 latched.</summary>
	internal void EndFlownRound() {
		TriggerCleared = true;
		_fireRowLatchRequested = true;
	}
}
