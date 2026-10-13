# Handoff: ImGui gamepad navigation

Goal: drive the menu bar and its windows (Settings, Controller, Tweaks, About, Welcome, Debug) with a gamepad alone, for pad-only play and the Steam Deck without the trackpad. Agreed scope and estimate below; not started.

## State of the tree at handoff

Uncommitted, built (Release publish) and tested (679 engine tests pass), not yet tried in play by the user:

1. Gamepad default layout and its own CONTROLS settings kept apart from `prefs.cfg` (`GamepadControls`, `IControlsOverride`, `JoystickDeviceMap.ForGamepad`, CONTROLS panel axis rows fixed while a gamepad is read). Suggested message: `Engine: give gamepads their own default layout and CONTROLS settings, kept apart from prefs.cfg (Claude)`
2. Centre-idle throttle greyed for a gamepad, and a gamepad-specific hint on the Throttle row's Invert. Suggested message: `Engine: grey out Centre-idle throttle for gamepads and explain the throttle row's invert (Claude)`
3. Gamepad Back toggles the menu bar, Start pauses/resumes a mission (`GamepadHostButtons`, `LiveKeys.HeldByHost`). Suggested message: `Engine: gamepad Back toggles the menu bar and Start pauses or resumes a mission (Claude)`

The user may commit these as one or three; ask.

Still unchecked in play: which way the right stick's up/down moves a HERC's turret and a RAZOR's nose (joystick-config.md, `## Open`).

## Facts already established

- ImGui.NET in the build (dated 2024-06) exposes `AddKeyAnalogEvent` and `ImGuiConfigFlags.NavEnableGamepad`, so ImGui's own gamepad navigation is available.
- `ScaledImGui` (`Herculan.Engine/Platform/ScaledImGui.cs`, 113 lines) wraps Silk's `ImGuiController`; `ScaledImGui.Update` calls the controller's update. Silk's controller feeds keyboard and mouse only. Gamepad events must be queued before ImGui's `NewFrame`; confirm where Silk's `ImGuiController.Update` calls it before deciding whether to feed events before or after it.
- Silk.NET 2.23.0's GLFW backend lists an Xbox-style pad (and Steam Input's virtual pad) only in `IInputContext.Gamepads`; `ControllerDevice` (`Herculan.Engine.Host/ControllerDevice.cs`) already reads either list. Standard layout buttons: 0-3 A B X Y, 4-5 bumpers, 6 Back, 7 Start, 8 Guide, 9-10 stick clicks, 11-14 d-pad up/right/down/left; axes 0-1 left stick, 2-3 right stick, 4-5 triggers (rest at -1).
- `JoystickSource.Muted` (`Herculan.Engine.Host/JoystickSource.cs`) mutes the mission's stick and waits for every button to be released after unmuting. Only the Controller window sets it today (`ControllerWindow.Open`/`Cancel`).
- The menu bar is `HostMenuBar` (`Herculan.Engine.Host/Settings/HostMenuBar.cs`); `Up` says whether it or any of its panels is showing. Back calls `BackOut`/`Show` from `SimulatorHost.ReadGamepadButtons` and `FrontEndWindow.BeginFrame`.

## Plan

1. **Feed the pad to ImGui** (bulk, ~80-120 lines). Each frame, before ImGui's frame starts, send `ImGuiKey.Gamepad*` events from the first connected gamepad: A = FaceDown (activate), B = FaceRight (cancel), X = FaceLeft (menu layer / windowing), Y = FaceUp, d-pad, left stick as analog (`AddKeyAnalogEvent` for `GamepadLStick*`), LB/RB for tab/window switching. Set `NavEnableGamepad` and `BackendFlags.HasGamepad`. Keep the reading in the host beside `ControllerDevice`; give `ScaledImGui` a hook rather than a dependency on host types.
2. **Mute the stick while any menu bar window is up**, not only the Controller window: drive `JoystickSource.Muted` from `HostMenuBar.Up` in `SimulatorHost`, keeping the Controller window's own mute consistent with it (one owner of the flag).
3. **Suspend ImGui gamepad navigation while the Controller window's Detect is listening**, so moving a stick or pressing the d-pad to bind it does not also move between items. Detect already ignores a button held when it starts, so pressing A on Detect then A again binds A.
4. **Land the nav focus on the menu bar when Back raises it.** ImGui enters a menu bar through its menu layer (FaceLeft tap), so Back raising the bar should also put nav on it, or open its first item. The fiddly part; expect a play-test round.
5. **Docs:** gamepad navigation rows in `docs/herculan/herculan-key-bindings.md`.

## Known limits

- Settings' folder and disc-image fields need typing or the native picker: reachable with the pad, but typing needs Steam's on-screen keyboard on the Deck, or a keyboard.
- ImGui's nav highlight is plain; styling it is extra.
- Verification is build + tests; the feel, and item 4 in particular, needs the user on a pad.
