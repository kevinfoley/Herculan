# GO.EXE — the shell's value tables

ES1's shell prices repairs and scrap from a component and weapon value table, as ES2's does ([ES2's `gam\damage.dat`](../../retail/formats/herc-catalogs.md#gamdamagedat--the-component-and-weapon-value-table)). ES1's sits in `cfg\`, holds ten internal percentages where ES2's holds nine, and has a fixed run of 18 weapon values with no count. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are VSHELL's.

## `cfg\damage.dat`

One 72-byte file in `SHELL0.VOL`:

```
int16        chassis scale -- 800
6 x int16    external component group percentages -- 100 50 50 50 100 100
10 x int16   internal component percentages -- 100 100 50 50 25 100 25 25 25 0
int16        weapon scale -- 1000
18 x int16   per-weapon values -- 1000 1500 2000 3000 3000 0 1000 1500 2000 1500 2000 2500 3000 1500 0 0 0 0
```

The first nine internal percentages are ES2's.

### The load

`LoadDamageDat_Shell` (`000387a8`) opens `cfg\damage.dat`, reads the five runs in file order (2, 12, 20, 2 and 36 bytes) and closes the stream. It expands them as ES2's loader does, through `Math_Q10Multiply` (`0005a298`), which rounds to nearest:

```
for each chassis s in 0..7, price = word[000808b8 + 2s] * 1000:
  word[000a0580 + s * 0x42 + 2g]        = Q10(Q10(price, external[g]), chassis scale)   for g in 0..5
  word[000a0580 + s * 0x42 + 0x1a + 2i] = Q10(Q10(price, internal[i]), chassis scale)   for i in 0..9
for each w in 0..17:
  word[000ab332 + 2w] = Q10(weaponValue[w], weapon scale)
```

The rows are 0x42 bytes, 33 words, with the internals from the row's fourteenth word, as in ES2's table. The static initializer `DamageValues_StaticInit` (`00038e2f`), the static constructor table's entry `0009a264` ([static construction](compiler.md#static-construction-and-exit)), builds 16 of them at `000a0580` with `__vec_ctor` of `HercStatus_Ctor`, which sets every word to 100 but word 22, the tenth internal ([the HERC record](car-hercs-and-stock.md#the-herc-record)); the load then writes words 0 to 5 and 13 to 22 of rows 0 to 7, so words 6 to 12 of those rows and rows 8 to 15 keep their 100s. The prices are `Chassis_PriceTable` (`000808b8`), eight words in the image indexed by the chassis word, where ES2's come from `herc_inf.dat`: BUILD's panel names chassis *c* with `eng\watext.bin` line `0x1b` + *c* beside price *c*, from ROADRUNNER's 50 tons to COLOSSUS's 200 (the table in [BUILD](../shell/repair-and-build.md#build--screen-2000)). `es2_xref.py` finds one call, at `000208d8` in `Shell_LoadData` (`00020630`).

The after-mission repair prices a HERC from the row of its chassis word and `000ab332` by each mount's weapon ([repairs](../shell/between-missions.md#repairs)), scrapping a HERC turns its worn weapons into salvage at `000ab332`'s values and its frame into salvage at the row's values, `Herc_FrameValue` (`00038d6c`) ([lost squadmates](../shell/between-missions.md#lost-squadmates)), and the hangar's marked HERC reads its chassis' price ([the marked HERC](../shell/between-missions.md#the-marked-herc)). The repair's `Repair_HercCost` and the frame's `00038d6c` read the row's internal words 0 to 8, not the tenth.

## Open

- **Open:** which weapon each of the 18 values prices, and what reads the rows' tenth internal, whose percentage is 0, which neither `Repair_HercCost` nor `Herc_FrameValue` (`00038d6c`) reads.
