namespace Herculan.Engine.Content;

/// <summary>
/// One <c>snc\P?_?????.SNC</c> — the frame timeline that animates a talking pilot's portrait while
/// the matching <c>.WAV</c> plays. It carries no audio; see docs/formats/heads-down-display.md,
/// "<c>.SNC</c> — portrait lip-sync scripts".
///
/// <para>Layout after the VOL entry prefix (which <see cref="GameContent.Read"/> has already
/// stripped): an <c>int32</c> length, then <c>length / 2</c> pairs of <c>{ frame, delta }</c>, the
/// delta being coarse ticks until the <i>next</i> event. The <c>0xff</c> terminator
/// <c>Snc_Load</c> (<c>00463270</c>) appends is not in the file, so a script that runs out is simply
/// finished — which is what <see cref="Frame"/> reports as -1.</para>
///
/// <para>Each speaker has its own scripts: the per-speaker copies of a message differ, so the
/// speaker letter in the filename selects real data.</para>
/// </summary>
public sealed class SncScript {
	/// <summary>The resource folder the scripts live in.</summary>
	public const string ResourceFolder = "snc";

	/// <summary>What <see cref="Frame"/> answers once the script has run out — <c>Snc_Advance</c>'s own -1.</summary>
	public const int Finished = -1;

	private readonly (byte Frame, byte Delta)[] _events;

	private SncScript((byte Frame, byte Delta)[] events) {
		_events = events;
	}

	/// <summary>The script's events, in order.</summary>
	public IReadOnlyList<(byte Frame, byte Delta)> Events => _events;

	/// <summary>Coarse ticks the whole script runs for — every delta summed.</summary>
	public int Duration {
		get {
			int total = 0;
			foreach (var (_, delta) in _events) {
				total += delta;
			}

			return total;
		}
	}

	/// <summary>
	/// The frame to show <paramref name="elapsed"/> coarse ticks after the script started, or
	/// <see cref="Finished"/> past its end. <c>Snc_Advance</c> (<c>004633ac</c>) walks pairs until the
	/// accumulated time passes now and publishes the frame it stopped on; this is the same walk
	/// expressed as a function of elapsed time, so nothing has to be stepped.
	///
	/// <para>An empty script is finished immediately, matching the original's buffer of just the
	/// appended terminator — "the voice plays with the portrait held".</para>
	/// </summary>
	public int Frame(long elapsed) {
		long at = 0;
		foreach (var (frame, delta) in _events) {
			at += delta;
			if (elapsed < at) {
				return frame;
			}
		}

		return Finished;
	}

	/// <summary>
	/// The resource name for one speaker and one message — <c>CommBox_BeginMessage</c>'s
	/// <c>"P" + ('A' + portrait) + "_" + 2-digit id + 3-digit variant</c>.
	/// </summary>
	public static string ResourceName(int portrait, int messageId, int variant) =>
		$"P{(char)('A' + portrait)}_{messageId:00}{variant:000}.SNC";

	/// <summary>
	/// Reads one script out of the mounted archives, or null when it is absent or truncated. A
	/// missing script is not fatal in the original either: the slot keeps its bare terminator.
	/// </summary>
	public static SncScript? Load(GameContent content, int portrait, int messageId, int variant) =>
		content.Read(ResourceFolder, ResourceName(portrait, messageId, variant)) is { } bytes
			? Parse(bytes)
			: null;

	/// <summary>Walks the layout above. Returns null when the declared length runs past the file.</summary>
	public static SncScript? Parse(byte[] bytes) {
		if (bytes.Length < 4) {
			return null;
		}

		int length = BitConverter.ToInt32(bytes, 0);
		if (length < 0 || 4 + length > bytes.Length) {
			return null;
		}

		var events = new (byte, byte)[length / 2];
		for (int i = 0; i < events.Length; i++) {
			events[i] = (bytes[4 + i * 2], bytes[5 + i * 2]);
		}

		return new SncScript(events);
	}
}
