# ES2GUIDE.HLP — the on-line manual

The "On-Line Manual" is a Windows Help file, one per language: `ENGLISH\ES2GUIDE.HLP`, `FRENCH\ES2GUIDE.HLP` and `GERMAN\ES2GUIDE.HLP`, 8.6 MiB each; v1.0 and the GoldGames build ship the same three files byte for byte. Both executables open it the same way: DBSIM's `?` key and right-hand system button reach `OnlineManual_Raise` (`0045f054`) and `Help_Show` (`004668c0`), and VSHELL's `ONLINE MANUAL` reaches `OnlineManual_Open` (VSHELL `004317ea`) and `Shell_OpenHelp` (VSHELL `004073a2`) (see [screen-layout.md](../shell/screen-layout.md#the-main-menu)). Each calls `WinHelpA(hwnd, path, HELP_CONTENTS, 0)`, the one `WinHelpA` call in its executable: no context id, no keyword, no macro. Everything below the contents topic is reached from inside the help file.

This doc describes the format as these three files use it. They were compiled by the Windows 95 help compiler (`|SYSTEM` minor version 33) and share one feature set; WinHelp features they do not use are listed under [Not in the corpus](#not-in-the-corpus). The GoldGames build also carries Windows 3.1 versions under `VER31\`, about 3 MB each, from the older compiler (`|SYSTEM` minor version 21, LZ77-compressed topics, 35 of the pictures stored under file names such as `256_OUTL.BMP`, and no `Readme` button); this doc does not describe them.

Multi-byte fields are little-endian. Text is Windows-1252.

## Container

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | magic `0x00035F3F` |
| 4 | 4 | offset of the internal directory's file header |
| 8 | 4 | first free block, `-1` |
| 12 | 4 | file size |

All three files run 72 bytes past the declared size, the same 72 bytes in each; nothing in the format points at them.

The file is a set of named internal files. Each starts with a 9-byte header — reserved space (4), used space (4), flags (1) — and its contents are the next *used space* bytes.

### B+ trees

The internal directory and three of the internal files are B+ trees. The tree starts with a 38-byte header:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | magic `0x293B` |
| 2 | 2 | flags: `0x0402` for the directory, `0x0002` for the others |
| 4 | 2 | page size: 1024 for the directory, 2048 for the others |
| 6 | 16 | structure string, NUL-padded, naming the entry layout |
| 22 | 2 | zero |
| 24 | 2 | page splits |
| 26 | 2 | root page |
| 28 | 2 | `-1` |
| 30 | 2 | page count |
| 32 | 2 | level count |
| 34 | 4 | entry count |

Pages follow, each *page size* bytes, numbered from 0. With one level the root is the only leaf. With more, a page above the leaves starts with free bytes (2), entry count (2) and the page number of its leftmost child (2); following that pointer from the root down to the leaf level finds the first leaf. A leaf page starts with free bytes (2), entry count (2), previous leaf (2) and next leaf (2, `-1` on the last), then its entries, so the whole tree is read in key order by walking the leaf chain.

| Tree | Structure | Entry |
|---|---|---|
| directory | `z4` | file name (NUL-terminated), offset of that file's header (4) |
| `\|CONTEXT` | `L4` | context hash (4), topic offset (4) |
| `\|TTLBTREE` | `Lz` | topic offset (4), topic title (NUL-terminated) |
| `\|KWBTREE` | `F24` | keyword (NUL-terminated), topic count (2), offset into `\|KWDATA` (4) |

The directory has 93 entries: `|SYSTEM`, `|TOPIC`, `|FONT`, `|CONTEXT`, `|CTXOMAP`, `|TTLBTREE`, `|KWBTREE`, `|KWDATA`, `|KWMAP`, `|CF1`, `|CF2`, and the pictures `|bm0` to `|bm81`.

### Compressed integers

Paragraph, table and picture records pack integers into one or two (or two or four) bytes, with the low bit of the first byte choosing the size:

| Name | Low bit 0 | Low bit 1 |
|---|---|---|
| compressed unsigned short | 1 byte, value = byte / 2 | 2 bytes, value = word / 2 |
| compressed signed short | 1 byte, value = byte / 2 − `0x40` | 2 bytes, value = word / 2 − `0x4000` |
| compressed unsigned long | 2 bytes, value = word / 2 | 4 bytes, value = dword / 2 |
| compressed signed long | 2 bytes, value = word / 2 − `0x4000` | 4 bytes, see [Open](#open) |

Every compressed signed long in the corpus takes the 2-byte form.

## |SYSTEM

A 12-byte header — magic `0x036C` (2), minor version 33 (2), major version 1 (2), build time as Unix seconds (4), flags 0 (2) — then records of type (2), size (2) and data. Flags 0 is what makes the rest of this doc simple: the topic text carries no LZ77 compression and the file has no phrase table.

| Type | Content in the corpus |
|---|---|
| 1 | title: `EarthSiege 2 On-Line Manual`, `EarthSiege 2 Manuel En Ligne`, `EarthSiege 2 On-Line Handbuch` |
| 2 | copyright: `EarthSiege 2 (c)1996 Sierra On-Line, Inc.` |
| 3 | topic offset of the contents topic: 0 |
| 4 | a startup macro; six of them, see [Macros](#macros) |
| 6 | a window definition; three of them, below |
| 9 | 10 bytes ending in language id `0x0409` (in all three files) |
| 11 | 8 bytes, `00 00 02 00 00 00 00 00` |

A window definition is 90 bytes:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | flags saying which fields are set |
| 2 | 10 | type, `main` or `secondary` |
| 12 | 9 | name |
| 21 | 51 | caption |
| 72 | 2 × 5 | x, y, width, height, and a fifth word |
| 82 | 4 | background colour of the scrolling region, RGB plus a zero byte |
| 86 | 4 | background colour of the non-scrolling region |

| Name | Type | Caption (English) | x, y, width, height | Backgrounds |
|---|---|---|---|---|
| `main` | main | none | 0, 0, 640, 480 | black, black |
| `useguide` | secondary | `Using the On-Line Manual` | 235, 0, 405, 480 | black, black |
| `overview` | secondary | none | 235, 0, 405, 480 | `#808080`, `#808080` |

The French and German `useguide` sit at 232, 0, 402, 478. The secondary windows cover the right-hand part of the main window.

`|CF1` and `|CF2` hold one macro each, run when secondary windows 1 and 2 open: a `Close` button that closes that window, `CB(`btn_close',`Close',`CW(`useguide')')` and the same for `overview`.

## |TOPIC

The topic text: a chain of records called topic links, packed into 4096-byte blocks.

Each block starts with a 12-byte header — last topic link in the block, first topic link starting in it, last topic header — and the remaining 4084 bytes are a slice of one continuous stream: a link that does not fit runs on into the next block after that block's header. Joining the blocks with their headers removed gives the stream.

### Positions

Two kinds of position point into the topic text.

A **topic position** names a byte: block number × `0x4000` + offset within the block, counting the 12-byte header. Topic links point at each other with these, and so do the block headers and the topic header's region fields. The stream offset is block × 4084 + (offset − 12).

A **topic offset** names a character: block number × `0x8000` + a character count. The block is the one the record starts in, and the count is the sum of the *text length* fields of the paragraph and table records that start earlier in that block; topic headers count zero. `|CONTEXT`, `|TTLBTREE`, `|KWDATA`, the browse fields and `|SYSTEM` type 3 use topic offsets. Every `|TTLBTREE` target and every browse field lands on a topic header computed this way, and so does every `|CONTEXT` target but one: French topic 74's header is the last link in block 43 and its text starts block 44, and `|CONTEXT` points at that first paragraph, `0x160000`, where the header computes to `0x158A7C`. Both offsets fall inside topic 74. Keyword targets land inside topics as well, often in the middle of a record.

### Topic links

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | link size, all of it |
| 4 | 4 | size of the second data part |
| 8 | 4 | previous link, a topic position |
| 12 | 4 | next link, a topic position; `-1` ends the chain |
| 16 | 4 | 21 + size of the first data part |
| 20 | 1 | record type |

The first data part follows the 21 bytes, the second follows that. The chain starts at block 0's first-link field.

The record types are 2, a topic header; `0x20`, a paragraph run; and `0x23`, a table. Each file has 103 topics, and the three together have 1942 paragraph and table records.

### Topic header

The first data part is 28 bytes:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | size in bytes of the topic, header link to the next header link |
| 4 | 4 | browse back, a topic offset; `-1` when the topic is not in the browse sequence |
| 8 | 4 | browse forward, likewise |
| 12 | 4 | topic number, 0 upward in chain order |
| 16 | 4 | start of the non-scrolling region, a topic position; `-1` for none |
| 20 | 4 | start of the scrolling region; `-1` when the non-scrolling region is the whole topic, as on the contents topic |
| 24 | 4 | next topic header |

The second data part is NUL-separated strings: the title, then the topic's entry macros. Only the contents topic has entry macros, `FocusWindow(`main');CW(`overview')`. 62 topics are in the browse sequence. 56 have a non-scrolling region: the records from its start up to the scrolling region's start stay put while the rest scrolls.

### Paragraph runs

The first data part of a `0x20` record:

| Field | Encoding |
|---|---|
| format size: bytes from after the next field to the end of the data part | compressed signed long |
| text length, in characters | compressed unsigned short |
| paragraph format | below |
| commands | below, ending `0xFF` |

The paragraph format is a compressed signed long (always 0 in a paragraph run), the paragraph id (2), a bit set (2), and then one field per set bit:

| Bit | Field | Encoding |
|---|---|---|
| `0x0001` | not in the corpus | compressed signed long |
| `0x0002` | space above | compressed signed short |
| `0x0004` | space below | compressed signed short |
| `0x0008` | line spacing | compressed signed short |
| `0x0010` | left indent | compressed signed short |
| `0x0020` | right indent | compressed signed short |
| `0x0040` | first-line indent | compressed signed short |
| `0x0100` | border, not in the corpus | 3 bytes |
| `0x0200` | tab stops | count as compressed signed short, then each stop as compressed unsigned short; a stop with bit `0x4000` set carries a further compressed unsigned short, and none in the corpus does |
| `0x0400` | right-aligned | none |
| `0x0800` | centred | none |

The format applies to every paragraph the run's commands contain. The values in the corpus: space above 4 or 6, space below 6, 8 or 12, left indent 108 or 504, first-line indent −35, −107 or −503, tab stops at 0, 108 or 504, except that the one paragraph indented 108 / −107 in English and German is indented 180 / −179 with a tab stop at 180 in French. The unit is [Open](#open).

The second data part is the text: NUL-terminated strings, one before each command, the `0xFF` included. A string may be empty.

### Commands

| Byte | Command | Operands |
|---|---|---|
| `0x80` | font change | font descriptor index (2) |
| `0x81` | line break | none |
| `0x82` | end of paragraph | none |
| `0x83` | tab | none |
| `0x86` | picture inline, as a character | below |
| `0x88` | picture at the right margin, text flowing round it | below |
| `0x89` | end of hotspot | none |
| `0xCC` | macro hotspot, no font change | length (2), macro text (NUL-terminated) |
| `0xE7` | jump hotspot, no font change | context hash (4) |
| `0xEF` | jump hotspot into a window, no font change | length (2), then type 1 (1), context hash (4), window number (1) |
| `0xFF` | end of commands | none |

A hotspot runs from its opening command to the next `0x89`. "No font change" means the hotspot is drawn in the font the text already has, and these files style every link through the font: link text is a bold underlined cyan descriptor. The window number in `0xEF` indexes the `|SYSTEM` window definitions in order. The corpus uses 0, `main`, and 2, `overview`; in each file 31 jumps open `overview`, at five untitled topics, and no jump opens `useguide`. Each file has 46 untitled topics: those five, the 39 pop-ups that [picture hotspots](#hotspots) open, and two more.

The picture commands share one layout: a type byte, the picture data's size as a compressed signed long, a hotspot count as a compressed unsigned short when the type is `0x22`, then the picture data. Every picture command in the corpus is type `0x22` or type 5. Type `0x22` data is 4 bytes, a zero word and the picture number: `|bm` plus that number in decimal. Type 5 is an [embedded button](#embedded-buttons).

### Tables

The first data part of a `0x23` record starts like a paragraph run's — format size, text length — then:

| Field | Size |
|---|---|
| column count | 1 |
| table type, 1 in the corpus | 1 |
| per column: width, then a second value | 2 + 2 |

and then cells, each a column number (2), a word and a byte (3), a paragraph format and commands up to `0xFF`. Column number `-1` ends the record. The cell's paragraph format begins with a compressed signed long that is often non-zero, where a paragraph run's is always zero. A column number may repeat: two cells in a row for column 0 are two paragraphs in that column.

A `0x23` record is one row; consecutive records make a taller table. Every row has 2 or 3 columns, and the second value is 1 for the first column and 11 for the others in every row. English widths are 30 + 828 and 31 + 828 (55 rows, holding the heading banners), 174 + 349, 210 + 637, 210 + 313, 318 + 565 (the contents topic) and 271 + 277 + 277. The translations keep the banner, contents and three-column widths and change the others, except that German keeps 210 + 637.

### Embedded buttons

A type 5 picture is a button. Its data is three words — 1, 0, and a value that changes from topic to topic — then a NUL-terminated string: `!`, the label, a comma, and the macro the button runs, for example `!Outlaw,JI(`',`Reference_2_HERC1')`. Every button in the corpus runs `JI`.

## |FONT

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | face name count, 7 |
| 2 | 2 | descriptor count: 82, 88 French |
| 4 | 2 | face name table offset, 8 |
| 6 | 2 | descriptor table offset |

Face names are 32-byte NUL-padded strings: `MS Sans Serif`, `Tms Rmn`, `Symbol`, `Helv`, `Courier`, `Times New Roman`, `Arial`. A descriptor is 11 bytes:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | attributes: `0x01` bold, `0x02` italic, `0x04` underline, `0x20` small capitals |
| 1 | 1 | size in half-points |
| 2 | 1 | family: 2 roman, 3 swiss |
| 3 | 2 | face name index |
| 5 | 3 | text colour, R G B |
| 8 | 3 | background colour, `01 01 00` in every descriptor |

The text uses Arial and Times New Roman only, in white, silver (`#C0C0C0`), cyan for links, and a few others for headings and the HERC data sheets. Text colour `01 01 00` appears on spaces and punctuation, and on one word, `(DONE)` in the English `Controls` topic.

## Context hashes

A jump names its target by a hash of the target's context string, and `|CONTEXT` maps each hash to a topic offset. The hash starts at 0 and, for each character, multiplies by 43 and adds the character's value, wrapping at 32 bits:

| Character | Value |
|---|---|
| `A`–`Z` and `a`–`z`, case-insensitive | 17–42 |
| `1`–`9` | 1–9 |
| `0` | 10 |
| `_` | 13 |

The 64 context strings each file spells out — the targets of `JI` in buttons and startup macros, and the hotspot names in pictures — hash to entries in their file's `|CONTEXT` under this table, which has 102 entries in each file. Other characters do not occur.

## Keywords

`|KWBTREE` lists the index keywords in order, 263 in English. Each entry's offset points into `|KWDATA`, a flat array of topic offsets, at the entry's first target; a keyword with several targets names several topics. `|KWMAP` is a count (2) and then, per `|KWBTREE` leaf page, the index of its first keyword (4) and its page number (2); it holds nothing the tree does not. `|CTXOMAP` is a count of 0 and nothing else.

## Macros

| Macro | Where | Effect |
|---|---|---|
| `BrowseButtons()` | startup | adds the `<<` and `>>` buttons that walk the browse sequence |
| `SPC(8355711)` | startup | pop-up windows get background `#7F7F7F` |
| `AI(`mnu_help',`item_use',`&Using the On-Line Manual',`JI(`',`Using_the_OnLine_Manual')')` | startup | adds a Help menu item |
| `AI(`mnu_help',`item_quick',`&Quick Reference',`JI(`',`quick_reference')')` | startup | adds a Help menu item |
| `CB(`btn_quick',`Keys',`JI(`',`Quick_Reference')')` | startup | adds a `Keys` button |
| `CB(`btn_readme',`Readme',`SH(`Notepad',`esreadme.txt',-1)')` | startup | adds a `Readme` button |
| `FocusWindow(`main');CW(`overview')` | contents topic | brings the main window forward and closes `overview` |
| `CB(`btn_close',`Close',`CW(...)')` | `\|CF1`, `\|CF2` | a `Close` button in each secondary window |
| `JI(`',`context')` | buttons, menu items | jump to a context string in this file |
| `JK(`',`keyword')` | `0xCC` hotspots | jump to the topic a keyword names |
| `SH(`Notepad',`esreadme.txt',-1)` | `0xCC` hotspots, three in each file | the `Readme` action again |

The menu and button labels are translated; the macros' arguments are not. `SH` is `ShortCut`: switch to a running Notepad, else start `esreadme.txt`. The installer is what creates that file: `SIERRA.INF` runs `BATCH.EXE` with the source and install directories and the language letter, and v1.0's `BATCH.EXE` copies `<install>\<language>\README.WRI` byte for byte to `<install>\esreadme.txt`, a Write document under a `.txt` name. v1.0's three `README.WRI` are the same 37,120-byte English document: its text is hard-wrapped at about 72 columns with CRLF line ends, holds no other control character than tab, and is followed by its formatting. The GoldGames build ships the same help files, but its `BATCH.EXE` names the copy `readme.txt`, and nothing else in that build is named `esreadme.txt`; see [KNOWN_ISSUES](../../KNOWN_ISSUES.md).

## Pictures

`|bm0` to `|bm81`, 82 per language. Each starts with magic `0x706C` (2), a picture count of 1 (2), and the picture's offset from the start of the internal file (4). The picture:

| Field | Encoding |
|---|---|
| picture type: 6, a device-independent bitmap | 1 byte |
| packing: 0 none, 1 run-length | 1 byte |
| x and y resolution, 0 | compressed unsigned long × 2 |
| planes, 1 | compressed unsigned short |
| bits per pixel, 24 | compressed unsigned short |
| width, height | compressed unsigned long × 2 |
| colours used, colours important: 0 | compressed unsigned long × 2 |
| pixel data size | compressed unsigned long |
| hotspot data size, 0 for none | compressed unsigned long |
| pixel data offset, from the picture's start | 4 bytes |
| hotspot data offset, likewise | 4 bytes |

24 bits per pixel means no palette. The pixels are a standard bottom-up DIB: rows of blue, green, red, each row padded to a multiple of 4 bytes. The largest is the 344×432 box art on the contents topic; 11 are 320×240 screenshots, and three are the 610×23 heading banners.

Run-length packing is a stream of runs. A byte with bit 7 set is followed by that many (low 7 bits) literal bytes; any other byte is followed by one byte repeated that many times. Of the 74 run-length pictures in English, 67 fill the bitmap exactly and seven end two bytes past it, and one of the 67 stops three bytes short of the end of its data. Of the eight unpacked pictures, three carry two bytes beyond the bitmap. The surplus is padding.

### Hotspots

13 pictures carry hotspot data — the clickable regions of the cockpit and base screenshots. A 7-byte header, 1 (1), hotspot count (2), macro data size 0 (4), then 15 bytes per hotspot:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | `0xE6`: open the target in a pop-up; every target is an untitled `*_popup_*` topic |
| 1 | 2 | `4`, `0` |
| 3 | 2 × 4 | left, top, width, height, in pixels from the picture's top-left |
| 11 | 4 | context hash of the target |

then, per hotspot, two NUL-terminated strings: a name (`Hotspot 1` …) and the target's context string (`HUD_popup_Target_Box`). The hash is that string's.

## Not in the corpus

WinHelp features these files do not use, so nothing above describes them: LZ77-compressed topic blocks, the `|Phrases` and `|PhrIndex`/`|PhrImage` phrase tables, the version 3.0 topic layout, `|FTS` full-text search, `.CNT` contents files, pictures of type 5 (device-dependent bitmaps) or 8 (metafiles), palettes, LZ77-packed pictures, more than one picture per `|bm` file, picture commands of type 3, left-margin pictures (`0x87`), the hotspot commands that change the font (`0xE2`, `0xE3`) or open a pop-up (`0xE2`, `0xE6`) from text, jumps into another help file (`0xEA`, `0xEB`, `0xEE`), macro hotspots with a font change (`0xC8`), non-breaking spaces and hyphens (`0x8B`, `0x8C`), paragraph borders, and decimal or right tab stops.

## Open

- **Open:** the unit of paragraph spacing, indents, tab stops and table column widths. Font sizes are half-points, and in half-points the indents are round inches (108 = 0.75 in, 504 = 3.5 in), and the getting-started overview's 174 + 349 table wraps its entries at the same words as `Earthsiege 2 - On-Line Manual.pdf`, a rendering of this file. Nothing in the file or the executables states the unit.
- **Open:** the second value in each table column (1 for the first column, 11 for the others) and the table type.
- **Open:** a table cell's word and byte after the column number, and the non-zero compressed signed long that opens a cell's paragraph format.
- **Open:** the third word of an embedded button.
- **Open:** a hotspot's bytes 1–2, always 4 and 0.
- **Open:** the fifth word of a window definition, and flag bits `0x0800` (set on `useguide` only) and `0x1000`.
- **Open:** font attribute bits `0x10` (set on two descriptors in each file) and `0x40` (on three).
- **Open:** the font descriptors' background colour, `01 01 00` throughout, and whether the text colour `01 01 00` is a literal colour or a marker for the default.
- **Open:** the 4-byte compressed signed long's bias.
- **Open:** the context hash's values for characters other than letters, digits and `_`.
