# DBSIM.EXE and GO.EXE — asserts

ES1's simulator reports a failed check through the same assert logger ES2 has, a `this` object at `000b5b54` whose `Assert_Report` takes a severity, a condition, a file, a line and a message. Addresses are in `ES1DBSIM` unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2 names and addresses are DBSIM's.

## Severities

`Assert_Report` (`00057298`) passes every assert to `Assert_Emit` (`00056ff0`), which acts on the severity:

| Severity | `Assert_Emit` |
|---|---|
| 1 | prints `Assert: %d warning%s logged.` to the stream at `0009e87c` when any warning has been counted |
| 2 | passes the message to `00056f30` |
| 3 | counts a warning (`000b5b50`) and passes the formatted line to `00056f30` |
| 4 | passes the formatted line to `00056f30` and writes it to `0009e87c` |
| 5 | writes `Internal Error:`, a request to call Sierra customer support, and the message to `0009e87c` |
| 6 | passes `Assert::break: Ctrl-C / Ctrl-Break` to `00056f30` and writes it to `0009e87c` |

Severities 4, 5 and 6 are fatal. `Assert_Report` executes `INT 3` and returns when `000b5bcc` is set; otherwise it calls each hook on the list at `000b5b58` with the severity (`Assert_RunHooks`, `00057270`), emits the assert, emits severity 1 for the warning count, and ends the program through `__exit(1)`.

`GO.EXE` has the same logger at `000a0bd4`, its hooks at `000a0bd8`. Its `Assert_Report` (`ES1GO 0004f498`) has no debugger break: a fatal severity always runs the hooks and exits. It emits through `ES1GO 00000486`.

ES2's logger (`Assert_Report`, `00479b6c`) has the same shape; its `Assert_Emit` shows the formatted line in a message box where ES1's writes to a stream.

## Open

- **Open:** what `00056f30` and `00056e84` do with the line; the stream `0009e87c`.
- **Open:** what sets `000b5bcc`, and the hooks the program adds to `000b5b58`.
- **Open:** `ES1GO 00000486`, the shell's emitter, against `Assert_Emit`.
