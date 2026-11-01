# Setups: plan (2.5.0)

Room switching today treats every screen as a room. Combinations of screens ("Also turn on", PR #1) were added on top of
that, and every rule needed to make them work (extra screens hidden in one screen's settings, the desk picked up on its
own, arrangements learned silently) made the feature harder to understand. Setups replace rooms with profiles you
create and switch between, plus a panic shortcut that always gets you back.

## Model

- **Known screen** (`KnownScreen`): a monitor Displicare has seen, keyed by model from its device path (`GSM82CD`) as
  rooms are today. Holds what belongs to the screen itself: EDID name, device and adapter paths, EDID ids, the modes
  Windows offered, and its own resolution, refresh rate and HDR (0 / "auto" = native, highest, automatic).
- **Setup** (`Setup`): a name and the screens to turn on, the first being the main one, plus the arrangement saved for
  them (positions, exact modes, HDR), sound, hotkey, controller direction, launcher, hidden, confirmed.
- **Safe setup** (`SafeSetupId`): where the panic shortcut goes; the desk unless the user picks another.

## Behaviour

- **What you see is what you save.** Arrange the screens in Windows, then "Save current layout as a setup" and name it:
  the exact layout is captured (the capture code from PR #1). Setups can also be built from screen chips and edited.
- Every connected screen still gets a one-screen setup by itself, so single-TV use needs no setup work.
- Switching works as rooms do: cards, tray menu, hotkeys, View + D-pad, `--room <name>`. The "Keep this display
  layout?" prompt stays for new or edited setups; switching never asks anything else.
- After a switch, the note on the new main screen shows the setup and its screens ("Desk · AORUS + Dell"), an unplugged
  screen ("Dell not connected") and a newly saved arrangement.
- Nothing changes a setup by itself: no picking up the desk, no learning in the background. Saving a layout is
  something the user does.

## Panic shortcut

- Ctrl+Alt+0 (today's home hotkey) and holding View + Menu + B for 2 s on a controller apply the safe setup.
- It never asks, closes a launcher (Big Picture, Xbox mode), works blind and during a stream.
- If the saved layout can't be applied (a screen unplugged, Windows refuses it): all connected screens on, extended,
  the safe setup's main screen as the main one.

## Upgrade and downgrade

- First start of 2.5.0: each room becomes a known screen plus a one-screen setup with the same name, hotkey, controller
  direction, launcher, sound, hidden and confirmed; a room with "Also turn on" screens (PR #1 test builds) keeps them
  and its learned layout. The home room becomes the safe setup, and the home hotkey the panic hotkey.
- The old `Rooms` list keeps being written from screens and one-screen setups, so going back to 2.4.x still works.

## Steps

1. Hold PR #1; `main` stays releasable on 2.4.x. The `setups` branch starts from it for the layout capture/restore code
   and the test-build pipeline.
2. Model and migration, no behaviour change: `KnownScreen`, `Setup`, rooms → setups, setups → rooms, sanitizing, tests
   with real settings. Not wired in yet.
3. Switching engine on setups: settings load/save, discovery creates screens and one-screen setups, switching applies
   a setup (saved layout, else side by side from the screens' own modes), `--room`, tray, hotkeys, controller.
   PR #1's automatic desk pick-up and background layout learning go.
4. UI: the Rooms tab becomes Setups (cards with screen badges, "Save current layout as a setup"), the editor (screen
   chips, arrangement preview, main-screen labels), the note after switching.
5. Panic shortcut, its controller combo and the last-resort fallback.
6. Hardware checks on DAMEPC at each step through test builds (`test-build` branch pre-releases), then 2.5.0.
