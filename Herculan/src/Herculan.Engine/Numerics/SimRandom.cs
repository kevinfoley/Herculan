namespace Herculan.Engine.Numerics;

/// <summary>
/// Port of DBSIM's pseudo-random generator (<c>Math_RandomNext</c> (<c>00492dd4</c>), state block at <c>0x4d261d</c>) —
/// an additive lagged Fibonacci generator over a 56-entry table of <see cref="short"/>s with two
/// rotating cursors:
/// <code>
///   table[i] += table[j];  result = table[i];  i = (i + 1) % 56;  j = (j + 1) % 56;
/// </code>
/// The original returns the new <c>table[i]</c> in the low 16 bits; every caller found so far masks
/// it down further (<c>&amp; 0xfff</c> for the terrain material roll and the explosion's
/// per-component ~51% roll), so <see cref="NextMasked"/> is the shape simulation code actually uses.
///
/// <para><b>The seed state is vanilla; the call history is not.</b> <see cref="SimRandom()"/> starts
/// from DBSIM's own initial state, so the two generators produce identical streams from the same
/// starting point. That is only half of replay parity: a given roll's result also depends on how
/// many times the generator was advanced before it, and this engine does not yet make the same
/// number of draws in the same order as the original. Treat a specific roll as replay-faithful only
/// once the call history is matched too — see docs/retail/simulation/random-generator.md.</para>
/// </summary>
public sealed class SimRandom {
	/// <summary>Table length, from the original's <c>== '8'</c> (0x38) cursor wrap test.</summary>
	private const int TableLength = 0x38;

	/// <summary>
	/// DBSIM's own starting table, the 112 bytes at <c>004a6958</c> that <c>Math_RandomSeed</c> (<c>00492d7c</c>)
	/// <c>memmove</c>s into the state block. Static initialised data, not built at runtime: the
	/// generator has no clock or entropy input anywhere, which is what makes the original's
	/// simulation replay identically on every run.
	/// </summary>
	private static readonly short[] VanillaTable = {
		-8925, -27341, -3123, 19394, -2078, -23841, -21904,
		24746, 5195, -30392, 6442, 27405, -25077, -1039,
		-31225, 18013, -5388, 27133, 27716, 6951, -9359,
		-7281, -21580, -9145, 4062, 19854, -899, -29384,
		22856, -7313, -21982, 12070, 17402, -19035, 8427,
		-4731, -2621, -32353, 11676, -19859, -5845, -26667,
		15777, -30227, -7180, 29877, 44, -11425, 15112,
		3573, -3458, -15760, 9609, 11915, 25426, -9683
	};

	/// <summary>
	/// The destination cursor's start, <c>state+0x71</c>'s literal <c>0x37</c>. This is the entry the
	/// step writes and returns.
	/// </summary>
	private const int VanillaCursorI = 0x37;

	/// <summary>The addend cursor's start, <c>state+0x70</c>'s literal <c>0x18</c>.</summary>
	private const int VanillaCursorJ = 0x18;

	private readonly short[] _table = new short[TableLength];
	private int _cursorI;
	private int _cursorJ;

	/// <summary>
	/// DBSIM's generator as it stands the moment <c>Math_RandomSeed</c> (<c>00492d7c</c>) has seeded it — the same table
	/// and the same two cursors, so this and the original step in lockstep from here.
	/// </summary>
	public SimRandom() {
		VanillaTable.CopyTo(_table, 0);
		_cursorI = VanillaCursorI;
		_cursorJ = VanillaCursorJ;
	}

	/// <summary>
	/// <b>Not a ported mechanic.</b> An independently-seeded stream, for the places this engine wants
	/// variation the original gets from sharing one global generator across everything. The cursors
	/// start where the original's do; only the table differs.
	/// </summary>
	public SimRandom(int seed) {
		var seeder = new Random(seed);
		for (int i = 0; i < TableLength; i++) {
			_table[i] = (short)seeder.Next(short.MinValue, short.MaxValue + 1);
		}
		_cursorI = VanillaCursorI;
		_cursorJ = VanillaCursorJ;
	}

	/// <summary>
	/// One step of the generator, returning the low 16 bits the original returns. Signed
	/// <see cref="short"/> arithmetic wraps exactly as the original's 16-bit add does.
	/// </summary>
	public short Next() {
		_table[_cursorI] = (short)(_table[_cursorI] + _table[_cursorJ]);
		short result = _table[_cursorI];

		_cursorI++;
		if (_cursorI == TableLength) {
			_cursorI = 0;
		}

		_cursorJ++;
		if (_cursorJ == TableLength) {
			_cursorJ = 0;
		}

		return result;
	}

	/// <summary>
	/// <c>Next() &amp; mask</c> — the form every located caller uses (always with a power-of-two-minus-one
	/// mask, e.g. <c>0xfff</c> for the terrain material roll and the AoE component roll).
	/// </summary>
	public int NextMasked(int mask) => Next() & mask;

	/// <summary>
	/// <c>Math_RandomBelow</c> (<c>00492e18</c>) — a draw in <c>[0, bound)</c>, as <c>(Next() &amp; 0x7fff) % bound</c>. The
	/// mask before the modulo is the original's own: it drops the sign bit rather than taking an
	/// absolute value, so the distribution is the low fifteen bits' and not the full sixteen.
	///
	/// <para>A bound of zero would divide by zero in the original; nothing reaches it there and
	/// nothing does here either, so it is answered with zero rather than guarded upstream.</para>
	/// </summary>
	public int NextBelow(short bound) => bound == 0 ? 0 : (Next() & 0x7fff) % bound;
}
