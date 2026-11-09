using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// How the player's <c>herculan-joystick.cfg</c> is seeded from an install's copy (<see cref="JoystickMapFile.LoadForPlayer(string, string?)"/>):
/// only while the player's is missing, and never by writing an empty file that would stop the next game's install seeding it.
/// </summary>
public sealed class JoystickMapFileSeedTests : IDisposable {
	private const string Guid = "030000004f04000008b1000000000000";

	private readonly string _folder = Directory.CreateTempSubdirectory("herculan-seed-").FullName;

	private string PlayerPath => Path.Combine(_folder, "player", JoystickMapFile.FileName);

	private string InstallPath => Path.Combine(_folder, JoystickMapFile.FileName);

	public void Dispose() => Directory.Delete(_folder, recursive: true);

	private static string Section(int rudder) => $"[Joystick {Guid}]\nRudder = {rudder}\n";

	[Fact]
	public void AMissingPlayerFileIsSeededFromTheInstall() {
		File.WriteAllText(InstallPath, Section(rudder: 4));

		var file = JoystickMapFile.LoadForPlayer(PlayerPath, InstallPath);

		Assert.Equal(4, file.For(isGamepad: false, Guid)?.RudderAxis);
		Assert.Equal(4, JoystickMapFile.Load(PlayerPath).For(isGamepad: false, Guid)?.RudderAxis);
	}

	[Fact]
	public void AnExistingPlayerFileIsNotReplacedByTheInstall() {
		Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath)!);
		File.WriteAllText(PlayerPath, Section(rudder: 3));
		File.WriteAllText(InstallPath, Section(rudder: 4));

		var file = JoystickMapFile.LoadForPlayer(PlayerPath, InstallPath);

		Assert.Equal(3, file.For(isGamepad: false, Guid)?.RudderAxis);
		Assert.Equal(Section(rudder: 3), File.ReadAllText(PlayerPath));
	}

	[Fact]
	public void NoInstallCopyWritesNoPlayerFile() {
		JoystickMapFile.LoadForPlayer(PlayerPath, InstallPath);

		Assert.False(File.Exists(PlayerPath));
	}
}
