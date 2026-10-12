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

The rows are 0x42 bytes, 33 words, with the internals from the row's fourteenth word, as in ES2's table. The prices are eight words in the image at `000808b8`, 50, 60, 80, 100, 120, 140, 170 and 200, where ES2's come from `herc_inf.dat`. `es2_xref.py` finds one call, at `000208d8` in `00020630`.

## Open

- **Open:** which chassis each of the eight prices belongs to, and what reads the tables at `000a0580` and `000ab332`.
- **Open:** which weapon each of the 18 values prices, and the tenth internal, whose percentage is 0.
