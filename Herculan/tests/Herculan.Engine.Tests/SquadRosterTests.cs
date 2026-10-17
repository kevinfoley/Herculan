using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="SquadRoster"/> over a small synthetic mission whose group 0 is the player's machine and three squadmates:
/// the squad build reads the squadmates as the unbroken run of member slots from slot 1, so a remove must close the gap
/// and an add must fill the slot just past the run.
/// </summary>
public class SquadRosterTests {
	private const short Hercs = 0;
	private const short MechSubject = 1;

	/// <summary>
	/// Catches a remove that clears the squadmate's member slot rather than moving the later ones up, which leaves a gap
	/// that ends the squad there; an add that names a slot other than the one just past the run; and an add past the cap.
	/// </summary>
	[Fact]
	public void RemoveClosesTheRunAndAddFillsTheSlotPastIt() {
		var document = Open(new SyntheticMsn()
			.Add(12, Mech(0, type: 0))
			.Add(12, Mech(1, type: 1))
			.Add(12, Mech(2, type: 2))
			.Add(12, Mech(3, type: 3))
			.Add(16, Group(9, Hercs, position: -1, condition: -1, 0, 1, 2, 3)));

		SquadRoster.Remove(document, 1);

		Assert.Equal(3, SquadRoster.Positions(document.Script));
		Assert.Equal([0, 2, 3], Enumerable.Range(0, 3).Select(slot => document.GuidOf(7, document.Script.Groups[0].MemberRefs[slot])));
		Assert.DoesNotContain(document.Mission.Mechs!, record => record.GUID == 1);

		SquadRoster.Add(document, SquadRoster.Machine(4, Unset(), Filler()), out short added);

		Assert.Equal(4, SquadRoster.Positions(document.Script));
		Assert.Equal(added, document.GuidOf(7, document.Script.Groups[0].MemberRefs[3]));
		Assert.NotNull(SquadRoster.WhyNoAdd(document));
	}

	/// <summary>Catches a squadmate removed without the roster delete's check, leaving an objective naming nothing.</summary>
	[Fact]
	public void SquadmateAnObjectiveNamesIsRefused() {
		var document = Open(new SyntheticMsn()
			.Add(12, Mech(0, type: 0))
			.Add(12, Mech(1, type: 1))
			.Add(16, Group(9, Hercs, position: -1, condition: -1, 0, 1))
			.Add(17, Objective(condition: -1, kind: MechSubject, subject: 1)));
		byte[] opened = Write(document);

		Assert.Throws<InvalidOperationException>(() => SquadRoster.Remove(document, 1));
		Assert.Equal(opened, Write(document));
	}

	private static short[] Unset() => Enumerable.Repeat((short)-1, 10).ToArray();

	private static short[] Filler() => Enumerable.Repeat((short)5, 10).ToArray();

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
