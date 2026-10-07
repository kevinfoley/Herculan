namespace Herculan.Engine.Shell;

/// <summary>
/// One pilot record as the shell's screens print it (docs/retail/formats/save-games.md, "The pilot record").
/// The crew screen's assignments change the bay, the squad position and the on-strength byte, always
/// through <see cref="ShellHangar"/>.
/// </summary>
public sealed class ShellBayPilot {
	public ShellBayPilot(string name, int rosterId, int bay, int skill, int squadPosition, bool onStrength,
			int nameIndex = 0) {
		Name = name;
		RosterId = rosterId;
		NameIndex = nameIndex;
		Bay = bay;
		Skill = skill;
		SquadPosition = squadPosition;
		OnStrength = onStrength;
	}

	public string Name { get; }

	/// <summary><c>+0x00</c>, 0-11 — also the pilot's portrait, the frame of <c>dba\c_pilots.dba</c> the crew screen shows.</summary>
	public int RosterId { get; }

	/// <summary><c>+0x02</c>, the pilot's <c>esnames.bin</c> name index, which the <c>player.mec</c> export writes first in each entry.</summary>
	public int NameIndex { get; }

	/// <summary>The hangar bay at <c>+0x22</c>, <c>-1</c> when unassigned.</summary>
	public int Bay { get; internal set; }

	/// <summary>The skill ladder at <c>+0x25</c>, 0-3; the panel prints <c>estext.bin</c> <c>0x35 + skill</c>.</summary>
	public int Skill { get; internal set; }

	/// <summary><c>+0x27</c> — which of the crew screen's three wingman rows, 1-3, the pilot fills, or <c>-1</c>; the player's is 0.</summary>
	public int SquadPosition { get; internal set; }

	/// <summary><c>+0x24</c>, the on-strength byte. Meaningful for the three squad members only.</summary>
	public bool OnStrength { get; internal set; }
}
