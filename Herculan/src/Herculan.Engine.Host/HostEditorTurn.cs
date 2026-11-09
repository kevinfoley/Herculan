using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Platform;
using Herculan.Es2.Host;
using Herculan.Es2.Install;

namespace Herculan.Engine.Host;

/// <summary>
/// The mission editor's turn, handed to Earthsiege 2's <see cref="Launcher"/> as its <see cref="EditorTurn"/>: the
/// editor's assembly references the game's, so the game cannot call it.
/// </summary>
static class HostEditorTurn {
	/// <inheritdoc cref="EditorTurn"/>
	public static EditorTurnEnd Run(HostSession session, HostWindow window, string? mission) {
		GameContent? shellContent = null;
		var result = EditorHost.Run(new EditorLaunch(session.InstallRoot,
			GameInstall.MountSimulator(session.InstallRoot, session.Disc),
			() => shellContent ??= GameInstall.MountShell(session.InstallRoot, session.Disc),
			session.ImGuiFontPath) {
			Window = window,
			Mission = mission,
			BoldFontPath = Path.Combine(Path.GetDirectoryName(session.ImGuiFontPath)!, "OpenSans-Bold.ttf"),
			ItalicFontPath = session.ImGuiItalicFontPath,
		});

		return result.Exit == EditorExit.Closed ? new EditorTurnEnd.Closed()
			: result.Play is { } play ? new EditorTurnEnd.Play(play.Mission, play.Launch)
			: new EditorTurnEnd.BackToShell();
	}
}
