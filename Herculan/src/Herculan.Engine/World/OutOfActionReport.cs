namespace Herculan.Engine.World;

/// <summary>
/// The ten mission-counter writes an object or a group makes when it goes out of the fight — an
/// object's <c>+0x1ba</c>/<c>+0x1ce</c> and a group's <c>+0x1c</c>/<c>+0x30</c>, both read from its
/// own <c>script.dat</c> record. See docs/retail/simulation/mission-deployment.md#the-out-of-action-report;
/// <see cref="Sim.SimObject.ReportOutOfAction"/> runs them.
/// </summary>
/// <param name="CounterRefs">Which counter each slot writes, <c>-1</c> for a slot that writes none.</param>
/// <param name="CounterOps">What it writes, one operation per slot.</param>
public sealed record OutOfActionReport(IReadOnlyList<short> CounterRefs, IReadOnlyList<short> CounterOps) {
	/// <summary>Slots in each array.</summary>
	public const int Slots = 10;

	/// <summary>Operation 1: the counter is zeroed.</summary>
	public const short OpClear = 1;

	/// <summary>Operation 2: the counter goes up by one.</summary>
	public const short OpIncrement = 2;

	/// <summary>
	/// Operations 0x0d to 0x10 store 1 to 4 — the operation less <see cref="SetBias"/>. Every other
	/// value writes nothing.
	/// </summary>
	public const short OpSetFirst = 0x0d;

	/// <inheritdoc cref="OpSetFirst"/>
	public const short OpSetLast = 0x10;

	/// <inheritdoc cref="OpSetFirst"/>
	public const short SetBias = 0x0c;

	/// <summary>A report that writes nothing.</summary>
	public static readonly OutOfActionReport None = new(Array.Empty<short>(), Array.Empty<short>());
}
