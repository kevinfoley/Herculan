namespace Herculan.Es1.Input;

/// <summary>Which pair of the input block's axes the camera takes, its pointers <c>+0x1d</c> and <c>+0x21</c>.</summary>
public enum Es1CameraAxes {
	/// <summary>Steering and throttle, <c>+0xd</c> and <c>+0xf</c>.</summary>
	Movement,

	/// <summary>Twist and pitch, <c>+0x11</c> and <c>+0x13</c>.</summary>
	Turret,
}

/// <summary>
/// <c>PlayerInputBlock</c> (<c>ES1DBSIM 000b17ac</c>, <c>0x2b</c> bytes), what <c>Input_BuildPlayerDevice</c> returns
/// each tick (docs/es1/simulation/input.md#the-input-block).
/// </summary>
public sealed class Es1PlayerInputBlock {
	/// <summary>The axes' indices in <see cref="Axes"/>.</summary>
	public const int Steering = 0, Throttle = 1, Twist = 2, Pitch = 3;

	/// <summary><c>+0</c>: the command word, masked with <c>SimCommandMask</c>.</summary>
	public ushort CommandWord { get; set; }

	/// <summary><c>+2</c>, <c>+6</c>: the pointer's x and y.</summary>
	public int PointerX { get; set; }

	public int PointerY { get; set; }

	/// <summary><c>+0xa</c>, <c>+0xb</c>, <c>+0xc</c>: the mouse's button bytes.</summary>
	public byte[] MouseButtons { get; } = new byte[3];

	/// <summary><c>+0xd</c>, <c>+0xf</c>, <c>+0x11</c>, <c>+0x13</c>: steering, throttle, twist, pitch.</summary>
	public short[] Axes { get; } = new short[4];

	/// <summary><c>+0x15</c>–<c>+0x1c</c>: eight buttons; button 0 is the trigger.</summary>
	public byte[] Buttons { get; } = new byte[8];

	/// <summary><c>+0x1d</c>, <c>+0x21</c>: the pair of <see cref="Axes"/> the camera's two pointers address.</summary>
	public Es1CameraAxes CameraAxes { get; set; }

	/// <summary><c>+0x29</c>: a replay's tick delta, 0 outside one.</summary>
	public short ReplayTickDelta { get; set; }

	public short SteeringAxis => Axes[Steering];

	public short ThrottleAxis => Axes[Throttle];

	public short TwistAxis => Axes[Twist];

	public short PitchAxis => Axes[Pitch];

	/// <summary>The camera's two axes, through <see cref="CameraAxes"/>.</summary>
	public (short X, short Y) CameraAxisValues =>
		CameraAxes == Es1CameraAxes.Turret ? (Axes[Twist], Axes[Pitch]) : (Axes[Steering], Axes[Throttle]);

	/// <summary>The two axes the camera's pointers address zeroed through them, as a flown missile's steer zeroes them.</summary>
	public void ClearCameraAxes() {
		int first = CameraAxes == Es1CameraAxes.Turret ? Twist : Steering;
		Axes[first] = 0;
		Axes[first + 1] = 0;
	}
}
