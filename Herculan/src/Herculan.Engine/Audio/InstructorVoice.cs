using Herculan.Engine.Content;
using Herculan.Engine.Install;

namespace Herculan.Engine.Audio;

/// <summary>
/// Where a training mission's instructor clips are — the one voice the original reads as loose files
/// rather than out of an archive. See docs/retail/formats/audio.md, "File naming".
///
/// <para>The training port's paint (<c>PilotMessagePort_Paint</c>, <c>0043660c</c>) patches the
/// training number and <b>the posted id plus one</b> into <c>TMx_0000</c>, puts the voice folder
/// in front — <c>simvoice</c> with its last letter patched for the language, as
/// <c>Voice_ArchiveName</c> (<c>0045ef68</c>) does for the archive — and, because the digit is never
/// <c>0</c> on this port, prefixes the directory <c>data\drive.cfg</c> names (<c>DriveCfg_PrefixPath</c>, <c>0045ee44</c>,
/// the path <c>Sim_Run</c> (<c>0045f144</c>) reads at startup). One clip per instruction, not per sentence.</para>
/// </summary>
public static class InstructorVoice {
	/// <summary>
	/// The largest clip read. The longest retail clip is 268,058 bytes; this only keeps a damaged or foreign
	/// file from being read into memory whole.
	/// </summary>
	public const long MaxClipBytes = 16 << 20;

	/// <summary>The clip for instruction <paramref name="messageId"/> of training mission <paramref name="trainingMission"/>.</summary>
	public static string ClipName(int trainingMission, int messageId) =>
		$"TM{trainingMission}_{messageId + 1:0000}.WAV";

	/// <summary>
	/// That clip's bytes: <paramref name="voiceFolder"/>\<see cref="ClipName"/> through
	/// <see cref="GameInstall.ReadDiscFile"/>, or null when there is none.
	/// </summary>
	/// <param name="voiceFolder"><c>SIMVOICE</c>, <c>SIMVOICF</c> or <c>SIMVOICG</c>.</param>
	public static byte[]? ReadClip(string installRoot, GameDisc? disc, string voiceFolder, int trainingMission, int messageId) =>
		GameInstall.ReadDiscFile(installRoot, disc, Path.Combine(voiceFolder, ClipName(trainingMission, messageId)), MaxClipBytes);
}
