# DBSIM.EXE and GO.EXE — asserts

ES1's simulator reports a failed check through the same assert logger ES2 has, a `this` object at `000b5b54` whose `Assert_Report` takes a severity, a condition, a file, a line and a message. Addresses are in `ES1DBSIM` unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2 names and addresses are DBSIM's.

## Severities

`Assert_Report` (`00057298`) passes every assert to `Assert_Emit` (`00056ff0`), which acts on the severity:

| Severity | `Assert_Emit` |
|---|---|
| 1 | prints `Assert: %d warning%s logged.` to stderr (`0009e87c`, the third FILE of `_iob`) when any warning has been counted |
| 2 | passes the message to `00056f30` |
| 3 | counts a warning (`000b5b50`) and passes the formatted line to `00056f30` |
| 4 | passes the formatted line to `00056f30` and writes it to `0009e87c` |
| 5 | writes `Internal Error:`, a request to call Sierra customer support, and the message to `0009e87c` |
| 6 | passes `Assert::break: Ctrl-C / Ctrl-Break` to `00056f30` and writes it to `0009e87c` |

Severities 4, 5 and 6 are fatal. `Assert_Report` executes `INT 3` and returns when `000b5bcc` is set, which `-B` does ([the command line](main-loop.md#the-command-line)); otherwise it calls each hook on the list at `000b5b58` with the severity (`Assert_RunHooks`, `00057270`), emits the assert, emits severity 1 for the warning count, and ends the program through `__exit(1)`. `Assert_AddHook` (`0005730c`), (hook), pushes an 8-byte node {next, hook} from `operator_new` at the list's head, so the newest hook runs first.

## A fatal assert in a mission

**When a check fails fatally once the mission has started, the simulator leaves 0 in `data\exit.cfg` for the shell and ends with status 1.** `Sim_StartMission` adds `Sim_AssertCleanupHook` (`0005a7b8`) first, whose last act is to write that 0 ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 1); before that, the 0 `Sim_Main` writes at its start stands ([starting](main-loop.md#starting--sim_main-00059350), step 4). So a `data\results.dat` or `data\mission.var` that does not open at the mission's end, a severity-5 assert ([writing the results](world.md#writing-the-results--mission_writeresults-0001d698)), ends the program before [`Sim_Shutdown`](main-loop.md#the-end--sim_shutdown-0005b568) writes its code, and `exit.cfg` holds 0. `es2_xref.py` finds `Assert_AddHook` called from `Sim_StartMission` and from five library constructors: `GLDisplay_Ctor`, `GLDisplay_CtorAndOpen`, `DVMouse_Ctor`, `Keyboard_Ctor` and `DVTimer_Ctor` ([Open](#open)).

`GO.EXE` has the same logger at `000a0bd4`, its hooks at `000a0bd8`. Its `Assert_Report` (`ES1GO 0004f498`) has no debugger break: a fatal severity always runs the hooks and exits. It emits through `ES1GO 00000486`.

ES2's logger (`Assert_Report`, `00479b6c`) has the same shape; its `Assert_Emit` shows the formatted line in a message box where ES1's writes to a stream.

## Open

- **Open:** what `00056f30` and `00056e84` do with the line.
- **Open:** the hooks the five library constructors add with `Assert_AddHook`; one added after `Sim_AssertCleanupHook` runs before it.
- **Open:** `ES1GO 00000486`, the shell's emitter, against `Assert_Emit`.
