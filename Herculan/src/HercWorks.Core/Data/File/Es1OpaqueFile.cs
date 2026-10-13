namespace HercWorks.Core.Data.File;

/// <summary>
/// OPAQUE - an ES1 file whose bytes are not understood, kept whole so it writes back unchanged. No field is modelled:
/// what the bytes are is an Open item in the file's doc. Its uses are <c>dmg\HOVTANK.DMG</c>
/// (docs/es1/formats/dmg-damage-file.md#hovtankdmg), <c>ZONES.VOL</c>'s <c>dat\WORLD0.DAT</c> to <c>WORLD3.DAT</c>
/// (docs/es1/formats/wld-world.md#the-world-tables-in-zonesvol), and <c>SHELL0.VOL</c>'s <c>cfg\CONFIG.DAT</c> and
/// <c>cfg\TEST.DAT</c> (docs/es1/formats/herc-diagrams.md#files-no-loader-names) and <c>snc\CO_FILES.DAT</c>
/// (docs/es1/formats/shell-file-lists.md#the-lip-sync-lists), <c>SIMVOL0.VOL</c>'s <c>bnd\</c> files but <c>CAM.BND</c>
/// (docs/es1/formats/bnd-notes.md#the-simulators-files), <c>SHELL0.VOL</c>'s <c>sos\DONS.BNK</c> and <c>sos\DRUM.DIG</c>
/// (docs/es1/formats/sound-samples.md#the-banks), and <c>SHELL0.VOL</c>'s <c>car\CAREER1.MEC</c>
/// (docs/es1/formats/career-log-and-player-mec.md#the-archives-career1mec). Read by
/// <see cref="Io.Transform.Common.Es1OpaqueFileTransformer"/>.
/// </summary>
public class Es1OpaqueFile {
	/// <summary>The file's content, as read.</summary>
	public byte[] Bytes { get; set; } = [];
}
