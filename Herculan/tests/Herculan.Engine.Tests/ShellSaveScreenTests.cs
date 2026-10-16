using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The save screen's geometry, gating, chrome and slot rename.
///
/// <para>These are the checks that a rect read correctly out of the decompilation has also been
/// <i>placed</i> correctly — the error a parse landing on EOF cannot catch. The rects in
/// <see cref="ShellSaveScreen"/> are parent-relative, as the builder writes them, so what is pinned
/// here is the composition: the absolutes below were added up by hand from the widget tree, and a
/// wrong parent or a dropped offset moves them.</para>
/// </summary>
public class ShellSaveScreenTests {
	/// <summary>
	/// The content panel is centred on the canvas's midpoint and clear of the tab strip's bottom edge.
	/// Its width is odd, so it cannot sit symmetrically in an even canvas: what is centred is the
	/// panel's midpoint on x=320, not the two margins on each other.
	/// </summary>
	[Fact]
	public void TheContentPanelIsCentredBelowTheTabs() {
		var panel = ShellSaveScreen.PanelRect;

		Assert.Equal(ShellLayout.CanvasWidth / 2, (panel.X0 + panel.X1) / 2);
		Assert.True(panel.Y0 > ShellLayout.TabBottom);
	}

	/// <summary>
	/// The ten slot rows: 13 tall on a 12-pixel pitch, so each overlaps its neighbour by a row, and the
	/// first and last inset two pixels further from the left than the eight between them.
	/// </summary>
	[Fact]
	public void PlacesTheSlotRows() {
		var screen = new ShellSaveScreen();

		var first = screen.RowRect(0);
		var second = screen.RowRect(1);
		var last = screen.RowRect(ShellSaveScreen.RowCount - 1);

		Assert.Equal(new ShellRect(163, 174, 488, 186), first);
		Assert.Equal(new ShellRect(161, 186, 488, 198), second);
		Assert.Equal(new ShellRect(163, 282, 488, 294), last);

		Assert.Equal(13, first.Height);
		Assert.Equal(12, second.Y0 - first.Y0);

		// The overlap is the original's own: row 1 starts on the row row 0 ends on.
		Assert.Equal(first.Y1, second.Y0);

		// Every row shares a right edge, two pixels inside the list panel's own.
		for (int slot = 0; slot < ShellSaveScreen.RowCount; slot++) {
			Assert.Equal(first.X1, screen.RowRect(slot).X1);
		}
	}

	/// <summary>
	/// The five buttons. Two are children of the slot list and three of the content panel, so getting
	/// the parent wrong moves a pair of them by the list's own offset and nothing else.
	/// </summary>
	[Fact]
	public void PlacesTheButtons() {
		var screen = new ShellSaveScreen();

		Assert.Equal(new ShellRect(218, 302, 316, 317), screen.ButtonRect(ShellSaveButton.Cancel));
		Assert.Equal(new ShellRect(327, 302, 425, 317), screen.ButtonRect(ShellSaveButton.Accept));
		Assert.Equal(new ShellRect(151, 356, 249, 371), screen.ButtonRect(ShellSaveButton.Save));
		Assert.Equal(new ShellRect(151, 378, 249, 393), screen.ButtonRect(ShellSaveButton.Restore));
		Assert.Equal(new ShellRect(151, 400, 249, 415), screen.ButtonRect(ShellSaveButton.Exit));

		// The three in the column are the same size on a fixed pitch.
		Assert.Equal(99, screen.ButtonRect(ShellSaveButton.Save).Width);
		Assert.Equal(16, screen.ButtonRect(ShellSaveButton.Save).Height);
		Assert.Equal(22, screen.ButtonRect(ShellSaveButton.Restore).Y0
			- screen.ButtonRect(ShellSaveButton.Save).Y0);
	}

	/// <summary>
	/// SAVE needs a row and a game to write; RESTORE needs a row that holds a save; EXIT is never gated.
	/// The selection parked past the last row — which is where the original leaves it — kills both.
	/// </summary>
	[Fact]
	public void GatesSaveAndRestoreOnTheSelection() {
		var screen = new ShellSaveScreen(new[] {
			new ShellSaveSlot("GAME_0.SAV", " 1. KEVIN", true, null),
			new ShellSaveSlot("GAME_1.SAV", " 2. EMPTY", false, null),
		});

		screen.SelectSlot(0);
		Assert.True(screen.IsEnabled(ShellSaveButton.Save));
		Assert.True(screen.IsEnabled(ShellSaveButton.Restore));

		// An empty slot can be saved into and cannot be restored from.
		screen.SelectSlot(1);
		Assert.True(screen.IsEnabled(ShellSaveButton.Save));
		Assert.False(screen.IsEnabled(ShellSaveButton.Restore));

		// Parked on the autosave, past the ten rows.
		screen.SelectSlot(10);
		Assert.False(screen.IsEnabled(ShellSaveButton.Save));
		Assert.False(screen.IsEnabled(ShellSaveButton.Restore));
		Assert.True(screen.IsEnabled(ShellSaveButton.Exit));

		// With no game in progress there is nothing to write, whatever is selected.
		screen.SelectSlot(0);
		screen.CanSave = false;
		Assert.False(screen.IsEnabled(ShellSaveButton.Save));
		Assert.True(screen.IsEnabled(ShellSaveButton.Restore));
	}

	/// <summary>A disabled button does not answer a click; an enabled one does, over its own rect.</summary>
	[Fact]
	public void HitTestsRowsAndLiveButtonsOnly() {
		var screen = new ShellSaveScreen(new[] {
			new ShellSaveSlot("GAME_0.SAV", " 1. KEVIN", true, null),
		});
		screen.SelectSlot(0);

		var row = screen.RowRect(3);
		Assert.Equal(3, screen.RowAt(row.X0, row.Y0 + 1));
		Assert.Null(screen.RowAt(row.X0 - 1, row.Y0 + 1));

		// The rows overlap by their shared border row, so one row of pixels is inside two of them. The
		// original hit-tests siblings newest first, so the row built later — the lower one — takes it.
		Assert.Equal(3, screen.RowAt(row.X0, row.Y0));
		Assert.Equal(4, screen.RowAt(row.X0, row.Y1));

		var exit = screen.ButtonRect(ShellSaveButton.Exit);
		Assert.Equal(ShellSaveButton.Exit, screen.ButtonAt(exit.X0, exit.Y0));

		// CANCEL belongs to the slot rename and is dead outside one, so its rect answers nothing.
		var cancel = screen.ButtonRect(ShellSaveButton.Cancel);
		Assert.Null(screen.ButtonAt(cancel.X0 + 1, cancel.Y0 + 1));
	}

	/// <summary>
	/// SAVE starts a rename on the selected row: its string becomes the <c>"%2d. "</c> prefix, CANCEL
	/// and ACCEPT are the only live buttons, and the selection cannot move off the row.
	/// </summary>
	[Fact]
	public void StartsARenameOnTheSelectedRow() {
		var screen = RenameScreen();
		Assert.Equal(" 1. KEVIN", screen.RowText(0));
		Assert.False(screen.CaretEnabled(0));

		screen.BeginRename();

		Assert.True(screen.Renaming);
		Assert.Equal(" 1. ", screen.RowText(0));
		Assert.True(screen.CaretEnabled(0));
		Assert.False(screen.CaretEnabled(1));
		foreach (var button in Enum.GetValues<ShellSaveButton>()) {
			Assert.Equal(button is ShellSaveButton.Cancel or ShellSaveButton.Accept, screen.IsEnabled(button));
		}

		Assert.False(screen.SelectSlot(1));
		Assert.Equal(0, screen.SelectedSlot);
	}

	/// <summary>
	/// A row takes characters from its permitted set only while its caret is enabled, and Backspace or
	/// the left arrow erases only with the focus as well, never into the prefix.
	/// </summary>
	[Fact]
	public void TypesAndErasesInsideTheRenamedRow() {
		var screen = RenameScreen();

		// Before the rename the row takes nothing.
		Assert.False(screen.Key(0, new ShellKey('A', null), focused: true, font: null));

		screen.BeginRename();
		Assert.True(screen.Key(0, new ShellKey('A', null), true, null));
		Assert.True(screen.Key(0, new ShellKey('7', null), true, null));
		Assert.True(screen.Key(0, new ShellKey(' ', null), true, null));
		Assert.False(screen.Key(0, new ShellKey('!', null), true, null));
		Assert.Equal(" 1. A7 ", screen.RowText(0));

		// Another row's caret was never enabled.
		Assert.False(screen.Key(1, new ShellKey('B', null), true, null));

		// Erasing needs the focus.
		Assert.False(screen.Key(0, new ShellKey(null, ShellKey.Backspace), focused: false, null));
		Assert.True(screen.Key(0, new ShellKey(null, ShellKey.Backspace), true, null));
		Assert.True(screen.Key(0, new ShellKey(null, ShellKey.Left), true, null));
		Assert.True(screen.Key(0, new ShellKey(null, ShellKey.Backspace), true, null));
		Assert.Equal(" 1. ", screen.RowText(0));
		Assert.False(screen.Key(0, new ShellKey(null, ShellKey.Backspace), true, null));
		Assert.Equal(" 1. ", screen.RowText(0));

		// Any other command changes nothing.
		Assert.False(screen.Key(0, new ShellKey(null, ShellKey.Enter), true, null));
	}

	/// <summary>A character goes on only while the new length stays below <c>0x5a</c>, so a row holds 89 at most.</summary>
	[Fact]
	public void StopsTypingAtTheStringLimit() {
		var screen = RenameScreen();
		screen.BeginRename();

		while (screen.Key(0, new ShellKey('W', null), true, null)) {
		}

		Assert.Equal(0x5a - 1, screen.RowText(0).Length);
	}

	/// <summary>
	/// CANCEL puts the label back, ends the rename and parks the selection on slot 10; ACCEPT ends the
	/// rename with the selection left on the row it wrote.
	/// </summary>
	[Fact]
	public void EndsARenameEitherWay() {
		var screen = RenameScreen();
		screen.BeginRename();
		screen.Key(0, new ShellKey('Z', null), true, null);

		screen.CancelRename();

		Assert.False(screen.Renaming);
		Assert.Equal(" 1. KEVIN", screen.RowText(0));
		Assert.Equal(ShellSaveScreen.RowCount, screen.SelectedSlot);
		Assert.False(screen.IsEnabled(ShellSaveButton.Cancel));

		screen.SelectSlot(1);
		screen.BeginRename();
		screen.Key(1, new ShellKey('Q', null), true, null);
		screen.SetSlot(1, new ShellSaveSlot("GAME_1.SAV", " 2. Q", true, null));
		screen.EndRename();

		Assert.False(screen.Renaming);
		Assert.Equal(1, screen.SelectedSlot);
		Assert.Equal(" 2. Q", screen.RowText(1));
		Assert.True(screen.IsEnabled(ShellSaveButton.Save));
		Assert.True(screen.IsEnabled(ShellSaveButton.Restore));
		Assert.False(screen.IsEnabled(ShellSaveButton.Accept));

		// Entering the screen again reads every row back from its slot.
		screen.Enter();
		Assert.Equal(" 2. Q", screen.RowText(1));
		Assert.Equal(" 3. EMPTY", screen.RowText(2));
	}

	/// <summary>The caret's blink phase flips on each tick while the row's caret is enabled, and is out otherwise.</summary>
	[Fact]
	public void BlinksTheCaretOnlyInTheRenamedRow() {
		var screen = RenameScreen();
		screen.CaretTick(0);
		Assert.False(screen.CaretOn(0));

		screen.BeginRename();
		Assert.True(screen.CaretOn(0));
		screen.CaretTick(0);
		Assert.False(screen.CaretOn(0));
		screen.CaretTick(0);
		Assert.True(screen.CaretOn(0));

		screen.CaretTick(1);
		Assert.False(screen.CaretOn(1));
	}

	private static ShellSaveScreen RenameScreen() {
		var screen = new ShellSaveScreen(new[] {
			new ShellSaveSlot("GAME_0.SAV", " 1. KEVIN", true, null),
			new ShellSaveSlot("GAME_1.SAV", " 2. EMPTY", false, null),
			new ShellSaveSlot("GAME_2.SAV", " 3. EMPTY", false, null),
		});
		screen.SelectSlot(0);
		return screen;
	}

	/// <summary>
	/// The chamfered border: each edge stops a pixel short at both ends, the true corners stay empty,
	/// and the four pixels one step inside them are painted instead.
	/// </summary>
	[Fact]
	public void PaintsAChamferedBorder() {
		var surface = new ShellSurface(20, 10);
		var rect = new ShellRect(0, 0, 19, 9);

		ShellChrome.PaintPanel(surface, rect, borderColor: 0x22, fill: true);

		// The four true corners are never drawn.
		Assert.Equal(ShellSurface.Transparent, surface.At(0, 0));
		Assert.Equal(ShellSurface.Transparent, surface.At(19, 0));
		Assert.Equal(ShellSurface.Transparent, surface.At(19, 9));
		Assert.Equal(ShellSurface.Transparent, surface.At(0, 9));

		// The edges are, between them.
		Assert.Equal(0x22, surface.At(5, 0));
		Assert.Equal(0x22, surface.At(19, 5));
		Assert.Equal(0x22, surface.At(5, 9));
		Assert.Equal(0x22, surface.At(0, 5));

		// And the inset corners carry the border colour over the fill.
		Assert.Equal(0x22, surface.At(1, 1));
		Assert.Equal(0x22, surface.At(18, 8));

		// The interior is the shell's one background colour, which is a literal in every panel paint.
		Assert.Equal(ShellChrome.InteriorColor, surface.At(10, 5));
	}

	/// <summary>
	/// A titled panel with its body fill off dithers the body instead, leaving every other pixel
	/// untouched — which is how the shell's single backdrop bitmap shows through at half strength.
	/// </summary>
	[Fact]
	public void DithersAnUnfilledBodyOverTheBackdrop() {
		var surface = new ShellSurface(40, 60);
		var rect = new ShellRect(0, 0, 39, 59);

		ShellChrome.PaintTitledPanel(surface, rect, borderColor: 0x27, faceColor: 0x25,
			bodyDitherColor: 0x10, headerHeight: 19, headerChrome: true, plateFirst: 8, plateLast: 30,
			fill: false);

		// The header strip is filled — sampled inside the title plate, since outside it the hatch is
		// drawn over the fill. The divider sits on the row the header height names.
		Assert.Equal(0x25, surface.At(20, 10));
		Assert.Equal(0x27, surface.At(20, 19));

		// Below it, alternating pixels and alternating rows — a pixel painted on one row is clear on the
		// next, which is what makes it a checkerboard rather than vertical stripes.
		//
		// The dither starts on the header height's own row and the divider is drawn over it afterwards,
		// so the first row of it that survives is the one below, and by then the phase has flipped once.
		Assert.Equal(ShellSurface.Transparent, surface.At(1, 20));
		Assert.Equal(0x10, surface.At(2, 20));
		Assert.Equal(0x10, surface.At(1, 21));
		Assert.Equal(ShellSurface.Transparent, surface.At(2, 21));
	}

	/// <summary>
	/// The title plate: the header's diagonal hatch punched back out to the face colour across the span
	/// the widget's two fields name, so the caption reads against flat ground.
	/// </summary>
	[Fact]
	public void PunchesTheTitlePlateThroughTheHatch() {
		var surface = new ShellSurface(200, 40);
		var rect = new ShellRect(0, 0, 199, 39);

		ShellChrome.PaintTitledPanel(surface, rect, borderColor: 0x27, faceColor: 0x25,
			bodyDitherColor: 0x10, headerHeight: 19, headerChrome: true, plateFirst: 60, plateLast: 140,
			fill: false);

		// Inside the plate there is no hatch, whatever row is sampled.
		for (int y = 1; y < 19; y++) {
			Assert.Equal(0x25, surface.At(100, y));
		}

		// Outside it the hatch is present somewhere in the strip — the bands are 14 lines on a 28-pixel
		// pitch, so a 40-pixel span either side of the plate cannot miss one.
		bool hatched = false;
		for (int x = 0; x < 60 && !hatched; x++) {
			for (int y = 1; y < 19 && !hatched; y++) {
				hatched = surface.At(x, y) == 0x0d;
			}
		}

		Assert.True(hatched, "the header should carry its diagonal hatch outside the title plate");
	}

	/// <summary>
	/// Nothing a paint draws escapes its own widget.
	///
	/// <para>The title bar's hatch is the case that matters: it lays down 26 bands on a 28-pixel pitch
	/// from five pixels left of the widget, which is over 700 pixels of diagonal for a panel a third
	/// that wide, and only the clip stops the surplus reaching the canvas. Without it the hazard stripe
	/// runs off the panel and across the screen.</para>
	/// </summary>
	[Fact]
	public void ClipsEveryPaintToItsOwnWidget() {
		var surface = new ShellSurface(ShellLayout.CanvasWidth, ShellLayout.CanvasHeight);
		var panel = ShellSaveScreen.PanelRect;

		new ShellSaveScreen().Paint(surface, null, null);

		// Not one pixel outside the content panel, on any of the four sides.
		for (int y = 0; y < ShellLayout.CanvasHeight; y++) {
			for (int x = 0; x < ShellLayout.CanvasWidth; x++) {
				if (!panel.Contains(x, y)) {
					Assert.Equal(ShellSurface.Transparent, surface.At(x, y));
				}
			}
		}

		// And the hatch really did draw, so the check above is not passing on an empty surface.
		Assert.False(surface.IsBlank);
	}

	/// <summary>
	/// Text colour is a remap, not a pen: the glyphs go down in the font's own ink index and that one
	/// index is then replaced. A row's resting and selected colours are the same paint with a different
	/// replacement, which is what makes the selection highlight free.
	/// </summary>
	[Fact]
	public void RecolorsTextByRemappingTheFontsInk() {
		var surface = new ShellSurface(10, 10);
		surface.Fill(2, 2, 6, 6, ShellChrome.FontInkColor);

		surface.Remap(0, 0, 9, 9, ShellChrome.FontInkColor, 0x27);

		Assert.Equal(0x27, surface.At(4, 4));

		// Nothing else moves — the table it installs is the identity apart from the one entry.
		surface.Fill(8, 8, 9, 9, 0x1a);
		surface.Remap(0, 0, 9, 9, ShellChrome.FontInkColor, 0x29);
		Assert.Equal(0x1a, surface.At(8, 8));
		Assert.Equal(0x27, surface.At(4, 4));
	}
}
