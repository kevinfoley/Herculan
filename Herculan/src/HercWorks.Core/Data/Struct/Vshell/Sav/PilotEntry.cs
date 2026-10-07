namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// One pilot record of <see cref="File.Sav.PlayerSave"/> — the same shape for each of the 36
/// squadmates and for the player. Variable-length on disk, 31 bytes plus the name; VSHELL reads it
/// with <c>Pilot_Read</c> (<c>0040fefc</c>) and writes it with <c>Pilot_Write</c> (<c>0040fd5f</c>).
/// The offsets below are the 59-byte in-memory record's. See
/// <c>docs/retail/formats/save-games.md#pilot-record--59-bytes-0x3b-in-memory</c>, and
/// <c>docs/retail/shell/campaign-loop.md#pilot-progression</c> for how skill and rank advance.
/// </summary>
public class PilotEntry {
	/// <summary><c>+0x00</c>, roster id 0-11: squad index x 4 plus a per-squad shuffle.</summary>
	public short RosterId { get; set; }

	/// <summary>
	/// <c>+0x02</c>, index into <c>esnames.bin</c>, 0-35. Stored alongside the name string rather than
	/// instead of it, and every roster draw uses the index space exactly once so no two pilots in a
	/// run share a name.
	/// </summary>
	public short NameIndex { get; set; }

	/// <summary><c>+0x04</c>, the name, copied from <c>esnames.bin</c>; on disk an <c>int16</c> length (NUL included) and the bytes.</summary>
	public string? Name { get; set; }

	/// <summary><c>+0x22</c>, the assigned hangar bay; <c>-1</c> when unassigned.</summary>
	public short Bay { get; set; }

	/// <summary><c>+0x24</c>, on strength — gates repair billing, results accounting and the <c>player.mec</c> export.</summary>
	public byte OnStrength { get; set; }

	/// <summary><c>+0x25</c>, skill 0-3.</summary>
	public PilotSkill? Skill { get; set; }

	/// <summary>
	/// <c>+0x27</c>, squad position: the crew screen row 1-3 the pilot fills, the player's 0, <c>-1</c>
	/// unassigned. Also selects the pilot's promotion divisor pair.
	/// </summary>
	public short SquadPosition { get; set; }

	/// <summary><c>+0x29</c>, rank 0-3 — a separate ladder from <see cref="Skill"/>.</summary>
	public PilotRank? Rank { get; set; }

	/// <summary><c>+0x2b</c>, condition: 100 at creation, overwritten at debrief from the HERC's overall condition.</summary>
	public short Condition { get; set; }

	/// <summary><c>+0x2d</c>, Herc kills this mission.</summary>
	public short HercKills { get; set; }

	/// <summary><c>+0x2f</c>, Flyer kills this mission.</summary>
	public short FlyerKills { get; set; }

	/// <summary><c>+0x31</c>, Base kills this mission.</summary>
	public short BaseKills { get; set; }

	/// <summary><c>+0x33</c>, Herc kills, career total.</summary>
	public short TotalHercKills { get; set; }

	/// <summary><c>+0x35</c>, Flyer kills, career total.</summary>
	public short TotalFlyerKills { get; set; }

	/// <summary><c>+0x37</c>, Base kills, career total.</summary>
	public short TotalBaseKills { get; set; }

	/// <summary><c>+0x39</c>, missions flown.</summary>
	public short MissionsFlown { get; set; }
}
