using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>
/// Opens a mission for editing: writes its handoff into the editor's scratch directory as the game's launch writes it
/// (<see cref="MissionFileLaunch.Open"/>, <see cref="MissionFileLaunch.OpenNew"/>) and holds it as its <c>.MSN</c>. The
/// editor shows a mission loaded in training mode, a practice mission or one played as <c>INSTANT ACTION</c>, on the theater and by the time
/// of day its own row 2 sets (<see cref="OwnWorld"/>) where the launch writes theater 1 and the practice screen's time of
/// day over them (docs/retail/formats/script-dat.md#the-training-fields): a departure from <c>INSTANT ACTION</c> the user chose, which
/// the planned menu for playing user-made missions honours (docs/herculan/handoff-editor-mission-editing.md#mission-level). A campaign mission is
/// shown as the launch loads it, on its stage's theater by its own time of day.
///
/// <para>The launch is unchanged and its handoff on disk keeps the launch's header until
/// <see cref="EditorMission.Open"/> writes the document's generation over it.</para>
/// </summary>
internal static class MissionOpener {
	/// <summary>
	/// The header words the editor shows as the mission sets them, which a training load writes over: the theater (offset
	/// 0) and its variant, the time of day (offset 18), as <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptDat.TheaterIndex"/>
	/// and <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptDat.TheaterVariant"/> place them.
	/// </summary>
	public const int TheaterWord = 0 / 2;

	public const int VariantWord = 18 / 2;

	/// <summary><see cref="MissionFileLaunch.Open"/>, the document then showing the mission's own theater and time of day.</summary>
	public static string? Open(GameContent shellContent, string installRoot, string name, string directory,
			out MissionDocument? document, out MissionSquad? squad, out string? failure) {
		string? script = MissionFileLaunch.Open(shellContent, installRoot, name, directory, out document, out squad, out failure);
		document = OwnWorld(document, squad);
		return script;
	}

	/// <summary><see cref="MissionFileLaunch.OpenNew"/>, the document then showing the mission's own theater and time of day.</summary>
	public static string? OpenNew(GameContent shellContent, string installRoot, string name, byte[] msn, byte[]? text,
			string directory, out MissionDocument? document, out MissionSquad? squad, out string? failure) {
		string? script = MissionFileLaunch.OpenNew(shellContent, installRoot, name, msn, text, directory, out document,
			out squad, out failure);
		document = OwnWorld(document, squad);
		return script;
	}

	/// <summary>
	/// <paramref name="opened"/>, generated again writing nothing over the theater and time of day when it was loaded in
	/// training mode, which <paramref name="squad"/> says: a squad that is not the career's.
	/// </summary>
	private static MissionDocument? OwnWorld(MissionDocument? opened, MissionSquad? squad) =>
		opened != null && squad is { PlayerMachine: not PlayerMachineSource.CareerSquad }
			? new MissionDocument(opened.Mission, opened.Text, opened.Generation.WithoutOverwrite(TheaterWord, VariantWord))
			: opened;
}
