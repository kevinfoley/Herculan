using Herculan.Engine.Content;
using Herculan.Engine.Install;

namespace Herculan.Engine.Host.Install;

/// <summary><c>--install</c>: the Settings menu's install window run from the command line, reporting to the console.</summary>
static class DiscInstall {
	/// <summary>
	/// <see cref="RetailInstaller"/> from a disc folder or image, reporting each file as it starts. Returns 0 once
	/// the install is complete, 1 when it was refused or failed, in which case what it copied has been removed.
	/// </summary>
	public static int Run(string source, string destination, RetailInstaller.Size size, RetailInstaller.Language language) {
		GameDisc disc;
		try {
			disc = File.Exists(source) ? GameDisc.OpenImage(source)
				: Directory.Exists(source) ? GameDisc.OpenFolder(source)
				: throw new FileNotFoundException($"{source} does not exist.");
		} catch (Exception ex) when (ex is HercWorks.Disc.DiscFormatException or IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Cannot read {source}: {ex.Message}");
			return 1;
		}

		using (disc) {
			if (RetailInstaller.Identify(disc, out var problem, out string? version) is not { } installer) {
				Console.Error.WriteLine(problem == RetailInstaller.Problem.NoScript
					? $"{source} is not an Earthsiege 2 disc: it has no {RetailInstaller.ScriptFileName}."
					: $"{source} holds a version of Earthsiege 2 this installer does not know ({version}).");
				return 1;
			}

			Console.WriteLine($"Installing Earthsiege 2 {installer.BuildName} from {disc.Location} into {Path.GetFullPath(destination)}: "
				+ $"{size}, {language}.");
			int lastFile = -1;
			try {
				installer.Install(destination, size, language, new ActionProgress<InstallProgress>(report => {
					if (report.FileIndex != lastFile && report.FileIndex < report.FileCount) {
						lastFile = report.FileIndex;
						Console.WriteLine($"  {report.FileIndex + 1}/{report.FileCount} {report.File}");
					}
				}));
			} catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
					or HercWorks.Disc.DiscFormatException) {
				Console.Error.WriteLine($"The install failed: {ex.Message}");
				return 1;
			}

			Console.WriteLine("Done. The original game's programs are copied too, but they will not run from this install: "
				+ "it has none of the original installer's system setup, and from an image no disc the original game can read.");
			return 0;
		}
	}
}
