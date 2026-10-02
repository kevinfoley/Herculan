# On-line manual

How HERCULAN shows the game's on-line manual. The help file itself is [`formats/winhelp.md`](../formats/winhelp.md); retail opens it with `WinHelpA`, and current Windows has no WinHelp.

## What opens it

- The main menu's `ONLINE MANUAL` (`ShellHost.OpenOnlineManual`) does what retail's `004317ea` does before its `WinHelpA` call — leaves full screen, writes option 6 to match, commits and saves the options, stops the shell's sound — and then opens the manual.
- `/` in a mission (`ReadManualKey` in the simulator host), which is `?` without Shift, as the dispatcher sees it. Not while a modal panel holds the input.

## Conversion

`OnlineManual` picks the language folder from the first byte of `DATA\LANGUAGE.CFG` as `Language_GetFolderName` does, reads `<folder>\ES2GUIDE.HLP` and `<folder>\README.WRI`, and has `HercWorks.Help` parse them (`HelpFile`, `WriteDocument`) and write them out as one HTML page (`HelpHtmlWriter`). The page goes to `%LOCALAPPDATA%\HERCULAN\manual\<folder>.html` and is handed to the default browser. The first open in a run rewrites the page, so nothing left in that folder by anything else is what opens; later opens reuse it. Conversion takes about a fifth of a second and runs off the frame loop.

`HercWorks.Help` reads only the WinHelp features the three retail files use, and rejects a file that uses anything else with the reason. It is not a general WinHelp viewer.

## The page

One self-contained file: every topic is a `<template>`, every picture a PNG `data:` URI, and one fixed script shows topics on demand. It needs no web server — it opens from disk — and makes no network request.

- **Stage.** The main window at its defined size, 640×480, centred in the browser window with the button bar directly above it at the same width, so text wraps at the width it was written for. It is drawn at a zoom: 200% until the reader changes it with `−` and `+` on the bar, remembered by the browser for the page, and reduced whenever the stage would not fit. At a whole-number zoom the pictures keep hard pixel edges.
- **Main window.** The stage, in the main window's background colour. A jump in it goes through the URL fragment, so the browser's Back button is WinHelp's Back. A topic's non-scrolling region stays at the top while the rest scrolls.
- **Secondary windows.** Window frames over the main window at the rectangle their definition gives: a title bar with the caption, then a button bar with the buttons the window's `|CF` macro creates. `overview` opens from the contents topic's links and closes on its `Close` button or when the contents topic is shown, as the file's macros say. The window a jump lands in comes to the front, as a window activated by a click does, so a link in `overview` brings the main window over it; a secondary window still showing behind it gets a button on the bar, named after it, that brings it back. Whether WinHelp brought the target window forward on such a jump is [Open](#open).
- **Pop-ups.** A box at the pointer in the file's pop-up colour, closed by the next click or Esc. The 13 screenshots with clickable regions are image maps that open them.
- **Button bar.** Contents, Index and Back are this viewer's own, in place of WinHelp's frame, as are the window buttons and the zoom. `<<`, `>>`, `Keys` and `Readme` come from the file's startup macros, and its two Help-menu items sit in a Help menu.
- **Index.** The keyword list. A keyword with several topics opens a list of them, as WinHelp's Topics Found does.
- **Readme.** The `Readme` button and the three readme links in the text open the language folder's `README.WRI` in the main window as one more topic: its text alone, in a monospace font in the light grey of the manual's body text, with its line breaks as stored, and nothing in it a link. Retail's `SH` starts `esreadme.txt`, v1.0's installed copy of that file ([`formats/winhelp.md`](../formats/winhelp.md#macros)); this viewer starts no program and shows the text instead, a deliberate departure from retail. When the file is missing or rejected, they show a note that this viewer starts no programs.

Layout follows the file: fonts by descriptor, text colours as stored, paragraph spacing, indents and alignment, tables at their stored column widths, each column preceded by the second value stored with it as a gap — this viewer's reading of a value the format doc lists as Open, from its being 1 for every first column and 11 for every later one. A column never widens to fit its content, because the heading banners are a 610-pixel picture in a narrow first column, drawn under the heading in the next one. Spacing, indents, tab stops and column widths are taken as half-points, the unit of the font sizes ([the format doc's Open list](../formats/winhelp.md#open) has the evidence). Every tab in the manual is a hanging indent, and is drawn as one. The font descriptors' background colour is not drawn. Text sits 8 pixels in from the edges of the main and secondary windows: the file sets no margin and WinHelp's own is not measured, so that width is this viewer's.

The look is a web page, not WinHelp's frame, menus or fonts at their 1996 metrics.

## Security posture

A copy of the game from an abandonware site may carry a tampered help file or readme, so the decoder treats both as hostile and the page carries nothing from them but text:

- `HercWorks.Help` builds without `AllowUnsafeBlocks` and takes no `PackageReference` and no `ProjectReference`. Nothing in it opens a file, resolves a path or starts a process: it is handed bytes and returns a model and strings.
- Every length and offset is checked against the bytes present. B+ tree walks refuse to revisit a page, the topic chain only moves forward, and `HelpLimits` caps file size, tree pages, topic links, string length and picture size, the last checked as a `long` product so two in-range dimensions cannot overflow. Run-length output is clamped to the bitmap. Malformed input returns null and a reason; nothing throws on bad data.
- No macro runs. The few the manual uses — `JI`, `JK`, `CW`, and the startup macros that build the button bar — are recognised and mapped to fixed actions; any other macro is left as plain text. `SH`, which starts a program, opens the readme topic, or a note when there is no readme.
- The readme is read only as far as its text. `WriteDocument` checks the Write magic, requires the text end the header gives to lie between the end of the 128-byte header and the end of the file, refuses a file over `HelpLimits.MaxReadmeBytes`, and never looks at the formatting, pictures or OLE objects after the text. It decodes the text as Windows-1252, turns CRLF into a line break and drops every other control character but tab. `OnlineManual` checks the file's size before reading it, and a readme that is missing, unreadable or rejected leaves the note without stopping the manual opening.
- No path, URL or file name from the help file reaches the page as a link or source. Links name topics by the numbers the writer assigned, and pictures are PNGs the writer encoded. A help file naming a network share therefore cannot make the browser contact it.
- Text is HTML-encoded, the readme's included, which sits in a `<pre>` block that nothing turns into links. Font names map through a fixed table, and colours, sizes and coordinates are written from integers.
- The page's Content-Security-Policy allows its one script by hash, images only as `data:` URIs, and nothing else: no network, no forms, no other scripts. The script itself is fixed text in `ManualScript` that no help file contributes to.

`HercWorks.Help.Tests` covers these: truncation at every length through the header and a spread beyond it, random byte corruption of the topic and index data and of a picture header, a topic chain pointed back at itself, a 32767×32767 picture, and markup planted in the title and the topic text. For the readme it covers truncation at every length through the header and into the text, a text end past the file or inside the header, a wrong magic, an oversize file, random corruption of the header and text, control characters, and markup planted in the text.

## Open

- **Open:** whether WinHelp brought the main window in front of `overview` when a link in `overview` jumped into it. Both are top-level windows, and `overview` covers the right of the main window, so without that the topic opened mostly hidden. No retail capture of the manual in use has been found.
- **Unported:** the cockpit's right-hand system button, retail's third way in ([`formats/cockpit-input.md`](../formats/cockpit-input.md#the-two-system-buttons)).
- **Open:** what v1.0 shows for the `Readme` action. `SH` starts `esreadme.txt`, a Write document under a `.txt` name ([`formats/winhelp.md`](../formats/winhelp.md#macros)), and no retail capture of it has been found.
