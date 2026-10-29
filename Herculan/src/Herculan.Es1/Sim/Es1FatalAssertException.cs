namespace Herculan.Es1.Sim;

/// <summary>
/// A fatal assert inside the tick: retail's <c>Assert_Report</c> at severity 4, 5 or 6, which runs the assert hooks and
/// ends the program with <c>__exit(1)</c> (docs/es1/simulation/asserts.md). Thrown where the failed check stands, so
/// nothing after it runs; the host reports <see cref="Exception.Message"/>, runs <see cref="Es1Shutdown.AssertCleanup"/>
/// and ends the run with its status.
/// </summary>
public sealed class Es1FatalAssertException(string message) : Exception(message);
