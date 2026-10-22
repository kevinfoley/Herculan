using Herculan.Engine.Host.Settings;
using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Host.Tests;

// The Keys window lists the map through a table of its own, and names everything through .lang entries, so an action
// added to the map without a row would be missing from the window, and an entry missing from en.lang would show as its
// raw key name.
public class KeysWindowTests {
	[Fact]
	public void EveryActionInTheMapHasARow() {
		var shown = KeyRows.All.SelectMany(row => row.Keys).SelectMany(entry => entry.Actions).ToHashSet();
		var missing = KeyMap.Retail.Bindings.Select(binding => binding.Action).Where(action => !shown.Contains(action)).ToList();
		Assert.Empty(missing);
	}

	[Fact]
	public void EveryEntryTheWindowShowsIsInEnglish() {
		// The host's own copy, which the build puts beside the tests as it does beside the game.
		string path = Path.Combine(AppContext.BaseDirectory, "Localization", "en.lang");
		var keys = File.ReadLines(path).Select(line => line.IndexOf(' ') is > 0 and var space ? line[..space] : null).ToHashSet();
		var missing = KeysWindow.Entries(KeyMap.Retail).Where(entry => !keys.Contains(entry)).ToList();
		Assert.Empty(missing);
	}
}
