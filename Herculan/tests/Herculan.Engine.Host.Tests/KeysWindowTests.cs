using Herculan.Engine.Menus;
using Herculan.Engine.Input;
using Herculan.Es1.Host;
using Xunit;

namespace Herculan.Engine.Host.Tests;

// The Keys window lists Earthsiege 2's map through a table of its own, and names everything through .lang entries, so
// an action added to the map without a row would be missing from the window, and an entry missing from en.lang, for
// either game's list, would show as its raw key name.
public class KeysWindowTests {
	public static TheoryData<string> Games => ["es2", "es1"];

	[Fact]
	public void EveryActionInTheMapHasARow() {
		var shown = KeyRows.All.SelectMany(row => row.Keys).SelectMany(entry => entry.Actions).ToHashSet();
		var missing = KeyMap.Default.Bindings.Select(binding => binding.Action).Where(action => !shown.Contains(action)).ToList();
		Assert.Empty(missing);
	}

	[Theory]
	[MemberData(nameof(Games))]
	public void EveryEntryTheWindowShowsIsInEnglish(string game) {
		var list = game == "es1" ? Es1KeyList.All : KeyList.Of(KeyMap.Default);

		// The host's own copy, which the build puts beside the tests as it does beside the game.
		string path = Path.Combine(AppContext.BaseDirectory, "Localization", "en.lang");
		var keys = File.ReadLines(path).Select(line => line.IndexOf(' ') is > 0 and var space ? line[..space] : null).ToHashSet();
		var missing = KeysWindow.Entries(list).Where(entry => !keys.Contains(entry)).ToList();
		Assert.Empty(missing);
	}
}
