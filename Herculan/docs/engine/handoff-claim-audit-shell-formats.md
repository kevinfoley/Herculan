# Handoff — what is left of the shell/format claim audit

> Scratchpad, not a status record. Drain and delete. Nothing is committed.

The audit of the 17 shell/format docs that `KNOWN_ISSUES.md` cites is finished: every doc, entry, port annotation, `known_symbols` description and new symbol name the audit produced is applied in the working tree, `doc_lint.py` is clean, the engine builds with 0 warnings and all tests pass. What remains is below.

## Ask the user before acting

Nothing open.

Deferred by the user: the unported Alt+F4 QUIT alert (`QuitAlert_Build`, an Unported bullet in `screen-layout.md`); whether `HddDisplay_SetPage` takes occupied comm boxes out of hit-testing after an F7/F8 switch (an Open bullet in `heads-down-display.md`; the engine keeps them clickable).

## Not done, no decision needed

- **Ghidra DB:** the 21 new `known_symbols` entries are not applied to the Ghidra project. `00401e5c`, `00401ef8`, `0045b888` and VSHELL `0040779f` have no Ghidra function; run `ES2DefineFunctionAt` on them in the same headless session as `ES2ApplySymbolNames` (`-readOnly` first; see `tools/ghidra_scripts/README.md`).
- `docs/formats/save-games.md:179` restates what `Repair_SetComponentNames` does (rule 5) and implies the names always match the machine; trim to a link to `shell/screen-layout.md#which-names-a-chassis-shows`.
- `HercWorks.Help/Internal/RecordReader.cs:8` says only retail picture types are accepted, but `ReadPicture` also accepts type `0x03`, which the corpus never uses.
- Not audited (cited only by engine-section entries): `formats/terrain-texturing.md`, `formats/distance-fog-and-sky.md`, `formats/dts-texture-binding.md`, `formats/cockpit-hud-widgets.md`.
