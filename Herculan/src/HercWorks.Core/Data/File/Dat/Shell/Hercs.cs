using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/HERCS.DAT — the player's starting hangar, loaded by VSHELL's
/// <c>LoadHercsDat</c> when a new career begins: a <c>UINT16</c> count, then per entry a hangar slot
/// (0-7) and one HERC catalog record (<see cref="ShellHercData"/>).
///
/// <para>Retail ships four Outlaws in bays 0-3 and, in bay 4, a Razor at 0% with three missions
/// left to build — the state a freshly ordered chassis is left in, not a damaged one. See
/// docs/retail/formats/herc-catalogs.md#gamhercsdat--the-starting-hangar.</para>
/// </summary>
public class Hercs {
	public Entry[]? Data { get; set; }

	public Hercs() { }

	public Hercs(short total) {
		Data = new Entry[total];
	}

	public class Entry {
		public short BayId { get; set; }
		public ShellHercData? Herc { get; set; }
	}

	public Entry AddEntry() => new();
}
