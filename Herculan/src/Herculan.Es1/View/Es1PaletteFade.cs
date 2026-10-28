using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Es1.View;

/// <summary>
/// The palette module's cross fade (docs/es1/rendering/palette.md#differences-from-es2,
/// docs/retail/rendering/cockpit-canopy-palette.md#interpolating-colors): <c>Palette_BeginCrossFade</c>
/// (<c>ES1DBSIM 00028528</c>) and <c>Palette_StepCrossFade</c> (<c>000287a8</c>) on the globals <c>PaletteFade_From</c>
/// (<c>000bc608</c>), <c>PaletteFade_EntryCount</c> (<c>000bc60e</c>), <c>PaletteFade_Duration</c> (<c>000bc610</c>), the
/// three delta tables (<c>000bc614</c>, <c>000bc814</c>, <c>000bca14</c>), <c>PaletteFade_Mode</c> (<c>000bcc14</c>),
/// <c>PaletteFade_EndTime</c> (<c>00092c38</c>) and <c>PaletteFade_Armed</c> (<c>00092c3c</c>). A palette is its entries
/// as retail's palette dwords, six-bit red, green and blue from the low byte up (<see cref="Entries"/>).
/// </summary>
public sealed class Es1PaletteFade {
	/// <summary>
	/// <c>PaletteFade_FirstEntry</c> (<c>000bc60c</c>), the entry the fade writes from: 0, the address grep of the
	/// disassembly finding no store to it (docs/es1/rendering/palette.md#open).
	/// </summary>
	public const int FirstEntry = 0;

	private short[] _red = [], _green = [], _blue = [];
	private uint[]? _from;
	private int _mode;

	/// <summary><c>PaletteFade_EntryCount</c>, the from palette's entry count, or the to palette's with no from.</summary>
	public int EntryCount { get; private set; }

	/// <summary><c>PaletteFade_Duration</c>, in counts of the clock <c>000981c4</c>.</summary>
	public int Duration { get; private set; }

	/// <summary><c>PaletteFade_EndTime</c>, which the first step after <see cref="Begin"/> arms.</summary>
	public uint EndTime { get; private set; }

	/// <summary><c>PaletteFade_Armed</c>.</summary>
	public bool Armed { get; private set; }

	/// <summary>
	/// <c>Palette_BeginCrossFade</c>(<paramref name="duration"/>, <paramref name="to"/>, <paramref name="from"/>): the
	/// from palette and the duration kept and the end disarmed, and per entry of the from palette (of the to palette with
	/// no from) the three 8.8 deltas, each truncated to an <c>int16</c>: with no from, the to channel × 256 / duration,
	/// mode 0; with no to, the from channel × 256 / −duration, mode 1; with both, (to − from) × 256 / duration, mode 2.
	/// With neither retail leaves the mode and the tables as they were and stores an unset count; here the count is 0,
	/// an engine guard.
	/// </summary>
	public void Begin(int duration, uint[]? to, uint[]? from) {
		_from = from;
		Duration = duration;
		Armed = false;
		int count = from?.Length ?? to?.Length ?? 0;
		if (from == null && to == null) {
			EntryCount = 0;
			return;
		}

		_red = new short[count];
		_green = new short[count];
		_blue = new short[count];
		int divisor = from != null && to == null ? -duration : duration;
		for (int i = 0; i < count; i++) {
			// Retail reads the to palette as far as the from palette's count; an entry past it reads as black here.
			uint t = to == null ? 0 : i < to.Length ? to[i] : 0;
			uint f = from == null ? 0 : from[i];
			_red[i] = Delta(t & 0xff, f & 0xff, to, from, divisor);
			_green[i] = Delta(t >> 8 & 0xff, f >> 8 & 0xff, to, from, divisor);
			_blue[i] = Delta(t >> 16 & 0xff, f >> 16 & 0xff, to, from, divisor);
		}

		_mode = from == null ? 0 : to == null ? 1 : 2;
		EntryCount = count;
	}

	/// <summary>One channel's delta, by the mode the palettes given pick.</summary>
	private static short Delta(uint to, uint from, uint[]? toPalette, uint[]? fromPalette, int divisor) =>
		(short)((fromPalette == null ? (int)to * 256 : toPalette == null ? (int)from * 256 : ((int)to - (int)from) * 256) / divisor);

	/// <summary>
	/// <c>Palette_StepCrossFade</c> at the clock <paramref name="now"/>: the first step after <see cref="Begin"/> arms the
	/// end at now + the duration. Past the end, the flag cleared and 0, nothing written. Otherwise, with <i>e</i> the
	/// counts since the start, each entry from <see cref="FirstEntry"/>: mode 0 each channel byte (delta × <i>e</i>) &gt;&gt;
	/// 8, the delta read unsigned; modes 1 and 2 the from entry with each channel byte plus that, wrapping; the entries
	/// installed in the live palette through <paramref name="install"/> (<c>Palette_InstallRange</c>'s widening of the
	/// dirty range is the same); and the counts to the end returned. The compares are unsigned.
	/// </summary>
	public uint Step(uint now, Action<int, IReadOnlyList<uint>>? install) {
		if (!Armed) {
			EndTime = now + (uint)Duration;
			Armed = true;
		}

		if (now > EndTime) {
			Armed = false;
			return 0;
		}

		uint elapsed = (uint)(Duration - (int)(EndTime - now));
		var entries = new uint[EntryCount];
		for (int i = 0; i < EntryCount; i++) {
			uint r = Scaled(_red[i], elapsed), g = Scaled(_green[i], elapsed), b = Scaled(_blue[i], elapsed);
			if (_mode == 0) {
				entries[i] = (r & 0xff) | (g & 0xff) << 8 | (b & 0xff) << 16;
			} else {
				uint f = _from![FirstEntry + i];
				entries[i] = (f & 0xff000000) | ((f + r) & 0xff) | ((f >> 8) + g & 0xff) << 8 | ((f >> 16) + b & 0xff) << 16;
			}
		}

		if (EntryCount > 0) {
			install?.Invoke(FirstEntry, entries);
		}

		return EndTime - now;
	}

	/// <summary>A delta read as an unsigned word, by the counts elapsed, shifted down 8.</summary>
	private static uint Scaled(short delta, uint elapsed) => (ushort)delta * elapsed >> 8;

	/// <summary>
	/// The palette's entries as retail's palette dwords: each color's red, green and blue at the six bits the palette reader
	/// scaled up by <see cref="DynamixPalette.Scalar"/>, from the low byte up; a missing entry is 0.
	/// </summary>
	public static uint[] Entries(DynamixPalette palette) {
		int scale = Math.Max(palette.Scalar, 1);
		var entries = new uint[palette.ColorCount];
		for (int i = 0; i < entries.Length; i++) {
			if (palette.Colors.TryGetValue(i, out var color)) {
				var bytes = color.Array;
				entries[i] = (uint)(bytes[0] / scale) | (uint)(bytes[1] / scale) << 8 | (uint)(bytes[2] / scale) << 16;
			}
		}

		return entries;
	}
}
