namespace Herculan.Engine.Cockpit;

/// <summary>
/// What a <c>--screenshot</c> capture waits for beyond the warm-up and whatever <see cref="StagedStart"/> staged —
/// this engine's own flags. Each property is the command-line flag of the same purpose, whose comment in the
/// host's parser says why it exists (docs/herculan/herculan-command-line.md).
/// </summary>
public sealed class StagedScreenshot {
	public bool WaitForEffectLight { get; set; }
	public bool WaitForTransmission { get; set; }
}
