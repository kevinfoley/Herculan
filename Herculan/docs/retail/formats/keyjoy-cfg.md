# `data\keyjoy.cfg` — the axis-inversion switches

The only part of the input configuration outside `prefs.cfg` ([`../simulation/preferences.md`](../simulation/preferences.md#dataprefscfg--the-option-array)). `Keyjoy_LoadConfig` (`0045b78c`) reads it once with four `GetPrivateProfileStringA` calls against section `[Keyjoy]`, each a case-insensitive compare against the word `Reverse` — anything else, the shipped `Default` included, leaves the flag clear. `GetPrivateProfileStringA` is DBSIM's only profile import, so it has no API to write the file with, and none of VSHELL, ES, SETUP or BATCH contains its name; retail ships it with its own explanatory comments for the player to edit. A tape carries a copy, unpacked to `tapes\keyjoy.cfg` and never read ([`tap-input-tape.md`](tap-input-tape.md)). What each switch inverts is [`../simulation/joystick-input.md`](../simulation/joystick-input.md#the-keyjoy-switches). Addresses are DBSIM virtual addresses.

## Keys

| Key | Global |
|---|---|
| `Tilt` | `0049eab8` |
| `Backturn` | `0049eabc` |
| `Missile` | `0049eac0` |
| `Rudder` | `0049eac4` |
