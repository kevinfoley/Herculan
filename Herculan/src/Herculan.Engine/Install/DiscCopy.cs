using Herculan.Engine.Content;

namespace Herculan.Engine.Install;

/// <summary>How far an install has got, for a progress bar.</summary>
public readonly record struct InstallProgress(int FileIndex, int FileCount, string File, long BytesDone, long BytesTotal);

/// <summary>
/// An <see cref="IProgress{T}"/> that runs a callback on the reporting thread itself.
/// <see cref="Progress{T}"/> posts to the creating thread's synchronization context, which neither the console nor
/// the ImGui frame loop has.
/// </summary>
/// <param name="report">The callback.</param>
public sealed class ActionProgress<T>(Action<T> report) : IProgress<T> {
	public void Report(T value) => report(value);
}

/// <summary>
/// What every game's install from a disc (<see cref="GameDisc"/>) shares: the destination's checks, the copy of a plan
/// of (disc path, install path) pairs with progress and cancellation, and the removal of what a failed copy wrote.
/// Each game's installer decides the plan and the files written after it. This engine's own; nothing on the disc is run.
/// </summary>
public static class DiscCopy {
	/// <summary>Why a directory cannot be installed into (<see cref="CheckDestination"/>).</summary>
	public enum DestinationProblem {
		IsAFile,
		NotEmpty,
		InsideDisc,
	}

	/// <summary>
	/// Why a destination cannot be installed into from a source, or null when it can: it
	/// must be a new or empty directory, outside the source when the source is a directory.
	/// </summary>
	/// <param name="source">The disc copied from.</param>
	/// <param name="destination">The directory copied into.</param>
	public static DestinationProblem? CheckDestination(GameDisc source, string destination) {
		string full = Path.GetFullPath(destination);
		if (File.Exists(full)) {
			return DestinationProblem.IsAFile;
		}

		if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) {
			return DestinationProblem.NotEmpty;
		}

		if (source.Image == null) {
			string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Location)) + Path.DirectorySeparatorChar;
			if ((full + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
				return DestinationProblem.InsideDisc;
			}
		}

		return null;
	}

	/// <summary>The bytes free on the drive holding a destination, or null when that cannot be told.</summary>
	/// <param name="destination">The destination directory.</param>
	public static long? FreeSpace(string destination) {
		try {
			return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
		} catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>The bytes a plan copies from a source; a file missing from the disc counts as nothing.</summary>
	/// <param name="source">The disc copied from.</param>
	/// <param name="plan">The files copied, each a source and a destination path.</param>
	public static long PlanBytes(GameDisc source, IReadOnlyList<(string Source, string Destination)> plan) =>
		plan.Sum(file => {
			using var stream = source.OpenRead(file.Source);
			return stream?.Length ?? 0;
		});

	/// <summary>
	/// Copies a plan from a source into a destination, then runs
	/// the write-after callback (install root, the list of files written), which adds each file it writes to the list.
	/// A failure or a cancellation removes what was written and rethrows.
	/// </summary>
	/// <param name="source">The disc copied from.</param>
	/// <param name="destination">The directory copied into.</param>
	/// <param name="plan">The files copied, each a source and a destination path.</param>
	/// <param name="writeAfter">Runs after the copy; adds each file it writes to the list.</param>
	/// <param name="progress">Receives progress reports.</param>
	/// <param name="cancellation">Cancels the copy.</param>
	/// <exception cref="InvalidOperationException">The destination is refused, a file the plan names is not on the
	/// disc, or there is not room for it.</exception>
	public static void Copy(GameDisc source, string destination, IReadOnlyList<(string Source, string Destination)> plan,
			Action<string, List<string>>? writeAfter, IProgress<InstallProgress>? progress, CancellationToken cancellation) {
		string root = Path.GetFullPath(destination);
		if (CheckDestination(source, root) is { } refusal) {
			throw new InvalidOperationException($"{root} cannot be installed into: {refusal}.");
		}

		if (plan.FirstOrDefault(file => !source.FileExists(file.Source)) is { Source: not null } missing) {
			throw new InvalidOperationException($"The disc has no {missing.Source}.");
		}

		long total = PlanBytes(source, plan);
		if (FreeSpace(root) is { } free && free < total) {
			throw new InvalidOperationException($"{root} has {free:N0} bytes free; the install needs {total:N0}.");
		}

		bool created = !Directory.Exists(root);
		var written = new List<string>();
		try {
			Directory.CreateDirectory(root);
			var buffer = new byte[1 << 20];
			long done = 0;
			for (int i = 0; i < plan.Count; i++) {
				var (from, relative) = plan[i];
				string target = Under(root, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				using var input = source.OpenRead(from)
					?? throw new InvalidOperationException($"The disc has no {from}.");
				written.Add(target);
				using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write)) {
					for (int read; (read = input.Read(buffer)) > 0;) {
						cancellation.ThrowIfCancellationRequested();
						output.Write(buffer, 0, read);
						done += read;
						progress?.Report(new InstallProgress(i, plan.Count, relative, done, total));
					}
				}
			}

			writeAfter?.Invoke(root, written);
			progress?.Report(new InstallProgress(plan.Count, plan.Count, "", total, total));
		} catch {
			Undo(root, created, written);
			throw;
		}
	}

	/// <summary>
	/// A relative path under a root. Every destination is an installer's constant or a disc
	/// file's name; this keeps it so should one ever be wrong.
	/// </summary>
	/// <param name="root">The root folder.</param>
	/// <param name="relative">The relative path.</param>
	/// <exception cref="InvalidOperationException">The path leaves the install.</exception>
	public static string Under(string root, string relative) {
		string full = Path.GetFullPath(Path.Combine(root, relative));
		return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			? full
			: throw new InvalidOperationException($"{relative} is outside the install.");
	}

	// Removes what an install that failed wrote: its files, then the directories left empty, then the
	// destination itself when the install made it.
	private static void Undo(string root, bool created, List<string> written) {
		try {
			foreach (string file in written.Where(File.Exists)) {
				File.Delete(file);
			}

			if (!Directory.Exists(root)) {
				return;
			}

			foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
					.OrderByDescending(path => path.Length)) {
				if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
					Directory.Delete(directory);
				}
			}

			if (created && !Directory.EnumerateFileSystemEntries(root).Any()) {
				Directory.Delete(root);
			}
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not remove the partial install in {root}: {ex.Message}");
		}
	}
}
