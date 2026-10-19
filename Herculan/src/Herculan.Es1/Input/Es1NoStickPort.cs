namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> a game port where no axis answers, so every joystick object stays disabled and every
/// device's record reads 0 (docs/es1/simulation/joystick.md#building-the-devices).
/// </summary>
public sealed class Es1NoStickPort : IEs1GamePort {
	public static Es1NoStickPort Instance { get; } = new();

	private Es1NoStickPort() {
	}

	public bool AxisAnswers(int axis) => false;

	public int ReadAxis(int axis) => 0;

	public byte Buttons => 0;
}
