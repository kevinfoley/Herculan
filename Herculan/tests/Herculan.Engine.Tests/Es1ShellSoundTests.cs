using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Shell;
using Herculan.Es1.Shell.MessageBox;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The shell's sound (docs/es1/formats/sound-samples.md#the-shells-sound-manager): the song's change of track, the
/// <c>.TTM</c> scripts' sound commands and the talking head's line, over a manager whose every name opens as ten bytes.
/// </summary>
public class Es1ShellSoundTests {
	/// <summary>
	/// A change of track fades the song made before over its volume's 100 ticks and leaves one song object, the new
	/// track's; with no manager it waits none. Catches the fade skipped or miscounted and the old object not destroyed.
	/// </summary>
	[Fact]
	public void AChangeOfTrackFadesTheOldSongAndLeavesOnlyTheNewOne() {
		int ticks = 0;
		WithShellSound(waits => {
			ticks += waits;
			return Enumerable.Repeat(Es1ShellWait.NextHostFrame, waits);
		}, (sound, _) => {
			Drain(sound.PlayMusic(Es1ShellSound.Intro1Track));
			Assert.Equal(0, ticks);

			Drain(sound.PlayMusic(Es1ShellSound.LosingTrack));

			Assert.Equal(100, ticks);
			var songs = Enumerable.Range(0, Es1SoundManager.ObjectSlots).Select(sound.Manager!.GetObject).Where(o => o is { Kind: 1 }).ToList();
			Assert.Equal([Es1ShellSound.LosingTrack], songs.Select(o => (short)o!.Index));
			Assert.Same(songs[0], sound.MusicObject);
		});

		ticks = 0;
		var none = new Es1ShellSound(waits => {
			ticks += waits;
			return [];
		});
		none.Init("", true, (_, _) => null, new Es1SoundManagerTests.EndlessBackend());
		Drain(none.PlayMusic(Es1ShellSound.Intro1Track));
		Drain(none.PlayMusic(Es1ShellSound.LosingTrack));
		Assert.Equal(0, ticks);
		Assert.Null(none.MusicObject);
	}

	/// <summary>
	/// Two pages that each last three ticks and run <c>0xc21f</c> then <c>0xc220</c> play one sound on name index <c>0xe</c>
	/// once per page, though each page runs on every tick of its wait. Catches the page's sound flag left unset or not
	/// cleared by the step, and the sound put on another index.
	/// </summary>
	[Fact]
	public void ATtmPagesSoundCommandsPlayOneSoundOncePerPage() {
		WithShellSound(Ticks, (sound, backend) => {
			ushort[] page = [0x1021, 3, 0xc21f, 0x2e41, 0x4152, 0x0057, 0xc220, 0x0ff0];
			ushort[] words = [0x1111, 1, .. page, 0x1111, 2, .. page];
			var scripts = Es1TtmManager.ForShell(new Es1VideoMemory(), (_, name) => name == "TEST.TTM" ? Es1TtmPlayerTests.File(words, pages: 2) : null,
				new Es1DirtyList(new Es1VideoMemory()), _ => 0);
			scripts.Sound = sound;
			scripts.SoundOption = () => 1;
			scripts.Configure(2, null, 1);
			scripts.AllocHandles(1, new Es1SimClock());
			var script = scripts.Script(scripts.Open(Es1TtmPlayer.Playing, 0, null, "TEST.TTM"))!;

			for (int tick = 0; tick < 6; tick++) {
				script.Run(1);
			}

			Assert.Equal(2, backend.Starts);
			Assert.Equal(0xe, script.Sound!.Index);
		});
	}

	/// <summary>
	/// A talking head's line with RADIO 0 makes no sound; with RADIO 1 its sound is made on name index 0 and plays, and the
	/// line's stop destroys it, freeing its manager slot. Catches the RADIO test dropped from the open or the play, the
	/// sound put on another index, and the release not destroying the sound.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void ATalkingHeadsLineSoundsOnlyWithRadioOn(short radio) {
		WithShellSound(Ticks, (sound, backend) => {
			byte[] list = new Es1LipSyncListTransformer().Write(new Es1LipSyncList {
				Script = "FACE.TTM", Lines = [new Es1NameList { Names = ["L0.SNC", "L0.RAW"] }],
			})!;
			byte[] face = Es1TtmPlayerTests.File([0x1111, 1, 0x0ff0, 0x1111, 2, 0x0ff0], pages: 2);
			Func<string, string, byte[]?> read = (folder, name) => (folder, name) switch {
				("SNC", "G0_FILES.DAT") => list,
				("", "FACE.TTM") => face,
				_ => null,
			};
			var display = new Es1VideoMemory();
			var scripts = Es1TtmManager.ForShell(display, read, new Es1DirtyList(display), _ => 0);
			scripts.Configure(2, null, 2);
			scripts.AllocHandles(1, new Es1SimClock());
			var player = new Es1LipSyncPlayer(display, read, new Es1MessageBoxRecord()) { Scripts = scripts, Sound = sound };
			player.Init(new Es1SimClock());
			Drain(player.PlayEntry(0, 3, 0));

			player.StartSound(0, radio);

			if (radio == 0) {
				Assert.Null(player.LineSound);
				Assert.Equal(0, backend.Starts);
				return;
			}

			var line = player.LineSound!;
			Assert.Equal(0, line.Index);
			Assert.True(line.IsPlaying());
			Drain(player.Stop());
			Assert.Null(sound.Manager!.GetObject(line.Slot));
		});
	}

	/// <summary>
	/// <paramref name="test"/> given a shell sound made by <see cref="Es1ShellSound.Init"/> from a <c>resource.cfg</c> that
	/// names a digital device, whose reader opens every name as ten bytes, and its backend.
	/// </summary>
	private static void WithShellSound(Func<short, IEnumerable<Es1ShellWait>> waitTicks,
			Action<Es1ShellSound, Es1SoundManagerTests.EndlessBackend> test) {
		var root = Directory.CreateTempSubdirectory("es1shellsound");
		try {
			File.WriteAllText(Path.Combine(root.FullName, Es1ResourceCfg.FileName),
				$"[{Es1ResourceCfg.SoundFx}]\r\n{Es1ResourceCfg.Driver}=E016\r\n");
			var backend = new Es1SoundManagerTests.EndlessBackend();
			var sound = new Es1ShellSound(waitTicks);
			sound.Init(root.FullName, false, (_, _) => new byte[10], backend);
			Assert.NotNull(sound.Manager);
			test(sound, backend);
		} finally {
			root.Delete(true);
		}
	}

	private static IEnumerable<Es1ShellWait> Ticks(short count) => Enumerable.Repeat(Es1ShellWait.NextHostFrame, count);

	private static void Drain(IEnumerable<Es1ShellWait> waits) {
		foreach (var _ in waits) {
		}
	}
}
