using Herculan.Engine.Numerics;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's generator, <c>Shell_RandomState</c> (<c>ES1GO 0009aec0</c>): the simulator's algorithm, <see cref="SimRandom"/>,
/// on a state of its own (docs/es1/formats/msn-mission-file.md#the-shells-generator).
///
/// <para><see cref="AtStartup"/> leaves it where the shell's start-up does, seeded and then <c>time()</c> % 128 draws on.
/// What the menus draw between start-up and a load (backgrounds, scripts, the mission's path when it is drawn) has no
/// counterpart here, since this engine has no shell: a load made from this state makes the draws a shell makes when it
/// loads the mission straight after starting. The second count comes from the host's clock rather than the DOS clock
/// and its time zone, which changes nothing but which of the 128 states a run starts at.</para>
/// </summary>
public sealed class Es1ShellRandom {
	/// <summary>How many start-up states there are: the skip is <c>time()</c> modulo this.</summary>
	public const int StartStates = 128;

	private readonly SimRandom _state = new();

	private Es1ShellRandom() {
	}

	/// <summary>
	/// <c>Math_RandomSeedAndSkip</c> (<c>ES1GO 000386f4</c>) as <c>00020630</c> calls it: the seed table, then
	/// <paramref name="secondsSince1970"/> % 128 draws discarded.
	/// </summary>
	public static Es1ShellRandom AtStartup(long secondsSince1970) {
		var random = new Es1ShellRandom();
		long skip = ((secondsSince1970 % StartStates) + StartStates) % StartStates;
		for (long i = 0; i < skip; i++) {
			random._state.Next();
		}

		return random;
	}

	/// <summary><c>Math_RandomBelow</c> (<c>ES1GO 00038774</c>): a draw in [0, bound), or 0 with no draw for a bound of 0.</summary>
	public int Below(short bound) => _state.NextBelow(bound);
}
