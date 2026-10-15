# Key bindings: Herculan.Engine.Host

The keys the engine adds to retail's. Every retail key works as [`../retail/key-bindings.md`](../retail/key-bindings.md) describes, alongside these:

| Key | What it does |
|---|---|
| `Shift+Esc` | Raise the menu bar: in a mission with the Debug, Settings, Controller, Tweaks and About panels, in the front end with Settings, Controller, Tweaks and About. Press again to back out. It works the same over a mission's pause, objectives and other panels, a movie and the briefing map's opening, and nothing of retail's sees the press: it closes no panel and skips nothing. Retail takes `Shift+Esc` as `Esc`.
| `Esc` | While the menu bar or one of its panels is up, or the welcome the front end shows on its first start, or its notice of a newer release, back out of it, unless a mission's pause, objectives or other panel is up, which takes it as retail's does. Otherwise `Esc` is retail's everywhere.
| Gamepad Back | Raise the menu bar or back out of it, as `Shift+Esc` does, in a mission and in the front end. While the bar stays up, the highlight goes on its first item ([Gamepad in the menus](#gamepad-in-the-menus)). |
| Gamepad Start | In a mission, pause and raise the pause menu, or resume from it ([The pause menu](#the-pause-menu)). Retail's own pause panel stays on `P`. |
| Gamepad left stick click | In a mission, hold for the squad orders ([Squad orders on a gamepad](#squad-orders-on-a-gamepad)). |
| Gamepad right stick click | In a mission, hold for the cockpit's screens ([Screens on a gamepad](#screens-on-a-gamepad)). |
| Gamepad left stick, right trigger | In the front end, move the pointer and click; any button but Back skips a movie ([Gamepad in the front end](#gamepad-in-the-front-end)). |
| Pointer at the top left | In the front end, raise the menu bar by moving the pointer to the window's top edge, within 300 pixels of its left edge (both scaled with the display, as the bar is). A click outside the bar hides it again. Not while a movie plays, nor while the window is out of focus. |
| Left mouse drag | With the Mouse-controlled outside view tweak, swing the outside view round the HERC. |
| `PrtScn` | While full screen, copy the frame to the clipboard in place of Windows' own capture, which can show a stale frame of a full-screen game; with `Alt`, `Ctrl`, `Shift` or `Win` held the key is Windows'. `--save-prtscn` also saves it ([`herculan-command-line.md`](herculan-command-line.md#screenshots-and-staged-state)). |

`Alt+Enter` toggles full screen in a mission while a panel such as pause is up. `Alt+Tab` leaves the game full screen behind the window switched to, where retail leaves full screen.

The tweak replaces the outside view's controls: the mouse swings the camera, the HERC stays under your control throughout, and the cockpit's keys keep working. `Esc` still returns to the cockpit.

During a tape replay, the menu bar's keys and `Ctrl+E` stay with the player's own keyboard and every other key comes from the tape; under `--demo` any key ends the demo.

## Gamepad in the menus

The menu bar and its windows work with a gamepad alone, in a mission and in the front end, through Dear ImGui's own gamepad navigation:

| Button | What it does |
|---|---|
| D-pad | Move between items. Left and right also step a slider. |
| A | Press the item, open a list, tick a box. |
| B | Close a list, leave the menu bar for the window under it, or step out of a scrolling area. |
| X, held | With the bumpers, bring another window to the front; with the left stick, move the window. |
| Bumpers | Held while stepping a slider: slower (left) or faster (right). |
| Left stick | Scroll the window. |
| Y | Type into a text field, which needs a keyboard, or Steam's on-screen keyboard on a Steam Deck. |

Closing a window with one of its own buttons also puts the highlight back on the bar's first item. While the menu bar or one of its windows is up, a mission's stick drives nothing, and after they close it stays quiet until every button is let go, so a button pressed to choose an item does not also fire its action. While the Controller window's Detect is listening, the pad moves nothing in the window, so the control moved to bind it is only bound.

## Gamepad in the front end

In the front end a gamepad works as the mouse: the left stick moves the pointer, faster the further it is pushed, and the right trigger is the left button, pressed at half its pull and released below a quarter. Anything a left click does, the trigger does. Any button but Back also skips a movie or the briefing map's opening, as `Esc` and `Space` do; Back skips nothing, as `Shift+Esc` skips nothing. The mouse keeps working alongside it. The first connected gamepad counts; a joystick that is not a gamepad does not. While the menu bar or one of its windows is up the stick, trigger and buttons leave the shell alone and the pad works the menus instead ([Gamepad in the menus](#gamepad-in-the-menus)), and after the menus close the pad stays quiet until every button and the trigger are let go.

## Gamepad in the editor

The mission editor takes a gamepad as the mouse too, and nothing else of it: the left stick moves the pointer as in the front end, the right trigger is the left button and the left trigger the right. So the right trigger selects in the view and works the editor's menus and panels, and holding the left trigger looks round, the stick then turning the view. It works only while the editor's window has the focus.

## The pause menu

A gamepad's Start pauses a mission and puts up a menu over it: **Resume**, and **Objectives**, **Preferences** and **Quit**, which raise retail's own objectives panel (`F11`), preferences panel (`F12`) and mission status (`Q`). Each of those three is greyed where its key would do nothing, as Objectives is in the outside view, and when its panel comes down the menu is back. Up and down on the d-pad or the left stick move, A chooses, and B or Start resumes; the pointer works it too. While the menu is up the mission holds as it does while the window is out of focus, with every sound stopped. The menu bar still comes up over it, with the highlight on the bar; B on the bar hands the pad back to the pause menu.

## Squad orders on a gamepad

Holding the left stick's click puts FLASH COMM's six orders round the middle of the screen, ATTACK MY TARGET at the top and the rest clockwise in FLASH COMM's order, in the game's own words. Leaning the stick picks the nearest order and centring it picks none. Letting go of the click on an order transmits it as that order's `Alt` key does from any screen ([`../retail/key-bindings.md`](../retail/key-bindings.md#displays-and-views)), and a recording takes it as that key; letting go with none picked sends nothing. The fifth and sixth show whichever of SCAN FOR HOSTILES and EMCON, and of FIRE AT WILL and HOLD YOUR FIRE, FLASH COMM is showing. While the orders are up the left stick does not steer. They do not come up in the outside view, where the `Alt` order keys do nothing, nor over a panel, the pause menu or the menu bar.

## Screens on a gamepad

Holding the right stick's click puts the cockpit's eight screens round the middle of the screen in the same way, each labelled with its display, its key and its title in the game's own words: the MFD's six, `F1` STATUS at the top, then the Heads-Down Display's two, `F7` MAP and `F8` DAMAGE. Leaning the right stick picks one, and letting go of the click on it brings it up as its key does, a recording taking it as that key. While the screens are up the right stick does not aim. They come up where the squad orders do, and not while those are up.

## Developer keys

`--developer` turns on retail's [developer keys](../retail/key-bindings.md#developer-keys), with one difference: the move and turn keys only move and turn. Under `Alt` or `Ctrl` the arrows neither steer nor move the throttle, so a sideways move looks like one rather than being hidden in a turn.
