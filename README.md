# Ravenfield Split Screen 0.1.8

A Windows BepInEx plugin for two local players in one Ravenfield process. This build was compiled against the installed Steam build **26324504**, which uses **Unity 2020.3.49f1, Mono, and x64**. Playtests confirmed independent movement and firing input, Player 2 respawn, and enemy health damage from Player 2 shots. Version 0.1.8 hides the first-person mesh while aiming through the split-screen equipment sight. This rendering change needs an in-game retest.

## Install

**A legitimate copy of Ravenfield installed through Steam is required to build and play this mod.** This is an unofficial community mod. The repository contains the mod's own source and build scripts. The build references game and Unity assemblies from your local installation. Ravenfield code, decompilations, binaries, and assets are excluded from distribution.

1. In Steam, open Ravenfield's **Manage → Browse local files** to locate your installation.
2. Extract **BepInEx 5.4.23.2 for Windows x64** into that folder. The game root should contain `ravenfield.exe`, `winhttp.dll`, and `BepInEx\core`. [BepInEx release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
3. Build the package using the instructions below. Extract its `BepInEx` folder into the same game root and merge folders. Keep the plugin DLL and its `native` folder together. When updating an existing installation, keep your current configuration file.
4. Connect both controllers and launch Ravenfield normally. Start Instant Action in **Point Match**, **Battle**, or **Domination**. Use keyboard and mouse for the initial menus and deploy Player 1. Once Player 1 is alive, press **F7**. You can also press **Start** on Player 2's controller. F7 reports the assigned devices or shows why joining was blocked.
5. Check `BepInEx\LogOutput.log` for the plugin's startup message and **Player 2 joined team**. An unsupported build warning means the game assembly changed and its patches need reviewing.

Remove the plugin folder and `BepInEx\config\local.ravenfield.splitscreen.cfg` to uninstall the mod. Keep BepInEx installed if other plugins use it.

## Controller setup

The plugin tries XInput first and uses bundled SDL2 direct gamepad input when it needs another way to detect both pads. It prefers slots **1** and **2** and can use other occupied slots. With a single detected pad, F7 can assign keyboard/mouse to P1 and the pad to P2. [SDL2 game controller API](https://wiki.libsdl.org/SDL2/CategoryGameController)

For two DualSense controllers, the Steam layout **ravenfield ps5** maps both pads to shared keyboard and mouse inputs. For the bundled direct input, disable Steam Input for Ravenfield in **Steam Library → Ravenfield → Properties → Controller**. To use Steam Input instead, apply **Templates → Gamepad** to both pads so Steam emits separate XInput gamepads. [Steam Input legacy bindings](https://partner.steamgames.com/doc/features/steam_controller/legacy_mode)

| Action | Controller |
| --- | --- |
| Move and aim | Left and right sticks |
| Shoot and aim down sights | RT and LT |
| Jump and crouch | A and hold B |
| Reload and use / enter / exit | X and Y |
| Sprint | Hold left stick click |
| Cycle carried or mounted weapons | LB and RB |
| Change fire mode | D-pad left |
| Countermeasures | D-pad up |
| Next empty vehicle seat | P2 D-pad right |
| Toggle vehicle camera | D-pad down. P2 can switch between cockpit and chase view in ground vehicles and boats. |
| Kick | P1 right stick click |
| Pause | P1 Start |
| Open personal loadout | Back on either pad. On a DualSense, press Create. |
| Open squad orders | D-pad right on foot, or fire with the Squad Leader Kit equipped. |
| Choose a redeploy location | After death, D-pad left/right selects. A/Cross confirms. |

Edit `BepInEx\config\local.ravenfield.splitscreen.cfg` while Ravenfield is closed. Top/bottom is the default. Set `SideBySide = true` for left/right. For keyboard/mouse plus one controller, set `PlayerOneKeyboardMouse = true` and `PlayerTwoSlot = 1`. Player 1's configured keyboard and mouse bindings are retained in that mode. Each controller has its own look adjustment and Y inversion.

Once split screen is active, either player presses **Back/Create** to open their own picker. Use **D-pad up/down** to choose a slot and **left/right** to cycle items, then press **A/Cross** to save or **B/Circle** to cancel. Saved equipment applies on that player's next spawn and persists across matches. Each picker leaves the other player free to play. P1 can also use Ravenfield's configured loadout key, the arrow keys, Enter, and Backspace in keyboard/mouse mode. Before P2 joins, P1 deploys through Ravenfield's normal menu. The picker follows Ravenfield's weapon availability and reserves both gear slots for large gear. Until P2 saves a loadout, blank config slots copy P1's selection at join time.

Ground vehicles and boats use left-stick steering and throttle. Each stick axis has its own deadzone, and steering ramps in near the center of the stick. For P2, the right stick looks around the cockpit on supported seats. Release it to recenter. In aircraft, the right stick controls roll and pitch, and the left stick controls yaw and throttle.

## Equipment and redeployment

Equipment using Ravenfield's native target tracker shows target brackets and acquisition, lock, or point-target status in the owner's pane. Aim and fire use the weapon's existing lock and firing rules.

While aiming with a handheld target-tracking weapon or the Squad Leader Kit, the aiming player's pane shows the split-screen sight with the first-person equipment mesh hidden. The other player can still see the held equipment on the soldier.

The Squad Leader Kit shows range and opens the squad menu when fired. Aim at the destination or ally before opening it. Use D-pad up/down and A/Cross to move the shared squad, regroup it, or recruit the aimed ally. B/Circle closes the menu. Both players command the same AI squad.

After dying, choose a friendly point or **Other player** and confirm. P1 still deploys on the native spawn wave. P2 still has the configured respawn delay. The game checks for clear ground next to a living teammate on foot at deployment time. If they are unavailable, or if a selected point changed hands, deployment uses a friendly point when one is available. Keyboard/mouse P1 can use the arrow keys and Enter.

## Feature status

| Feature | Implemented in the compiled plugin | In-game verification |
| --- | --- | --- |
| BepInEx entry point and Harmony patches | Build guard and patch setup | Loaded in 0.1.6. The 0.1.7 build passed offline checks. |
| Same-team Player 2 and shared battle | Native actor registration, damage, hitboxes, scoring, and capture participation | Pending |
| Split-screen views | Left/right and top/bottom, separate world and first-person weapon cameras, death view and seat-camera poses | Left/right and P2 first-person weapon confirmed. New aim zoom and other layouts need testing. |
| Controls | Separate XInput or direct gamepad devices, optional keyboard/mouse for P1, movement, aim, jump, crouch, sprint, and weapon controls | Two direct gamepads controlled movement and firing independently in 0.1.2. |
| Combat | Native weapon instances, ammo and reloads, separate inventories, recoil and health | Confirmed P2 enemy damage in the game log. |
| Death and respawn | Independent destination selection for both players, with friendly points and teammate deployment | Earlier P2 death and respawn confirmed. Destination selection needs testing. |
| Vehicles | Native seat entry, exit, mounted weapons, separate vehicle HUDs, smoothed controller steering, and P2 ground-vehicle chase view. | Vehicle use reported. The revised vehicle behavior needs retesting. |
| HUD | Per-pane health, ammo, vehicle status, equipment targeting, squad orders, loadout, and redeploy menus | Equipment overlays, squad orders, and redeployment need testing. |

The offline verification checks plugin metadata, Harmony target existence, the weapon and projectile patch sites, and compiled references against the actual game, Unity, and BepInEx assemblies.

## Known limits

- The game still has a single `LocalPlayer` reference for P1. Workshop scripts, custom trackers, custom scopes, and scripted camera transitions may behave differently for P2. The mod's targeting overlay uses the native `TargetTracker` interface.
- P2 uses an independent movement capsule and the game's actor, seat, and weapon mechanics. First deployment can add P2 when the bot roster is full. Later respawns use the mode's active-soldier cap and need a friendly point.
- The default Instant Action modes listed above are enabled. Skirmish, Spec Ops, Haunted, campaign, spectator mode, and other game versions are disabled. Start testing with Workshop mutators and RavenM disabled.
- The initial deployment menu and shared pause menu use the full screen. Some match-wide UI still spans both panes. The game uses a single audio listener, and split view disables Ravenfield's global full-screen post-processing camera.

## Build and verify

The PowerShell build uses the Visual Studio Build Tools C# compiler and local game assemblies. It can reference an extracted copy of BepInEx, so the loader need never be installed into the game directory to compile.

```powershell
$game = 'C:\path\to\SteamLibrary\steamapps\common\Ravenfield'
$core = Join-Path $game 'BepInEx\core'
./scripts/Build.ps1 -RavenfieldDir $game -BepInExCore $core -Package
./scripts/Verify.ps1 -RavenfieldDir $game -BepInExCore $core
```

The source project also supports `dotnet build RavenfieldSplit.csproj -c Release -p:RavenfieldDir="$game" -p:BepInExCore="$core"` with the .NET SDK and .NET Framework 4.7.2 targeting pack.

Packaging expects the official Windows x64 SDL2 2.32.10 `SDL2.dll` and the upstream license saved as `LICENSE-SDL2.txt` in `.tools/SDL2`. Use `-SdlRuntimeDir` to point to another directory. [SDL2 release](https://github.com/libsdl-org/SDL/releases/tag/release-2.32.10) · [SDL2 license](https://github.com/libsdl-org/SDL/blob/release-2.32.10/LICENSE.txt)

## First in-game test

On a stock Point Match map, join P2 and confirm that moving and firing on one pad leaves the other soldier idle. Shoot and reload on both panes, stand together on a capture point, and let AI kill each player separately. Confirm the survivor can keep playing and the other respawns. Enter two seats in the same vehicle, fire a mounted weapon, and exit both seats. Repeat with the other layout, then return to the menu and start a fresh match. Capture the BepInEx log if anything fails.

## Contributing and licenses

See [CONTRIBUTING.md](CONTRIBUTING.md) for bug reports, patches, and repository boundaries.

The mod code is licensed under the [MIT License](LICENSE). The original documentation is licensed under [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/). These licenses cover the project's own work. Ravenfield, Unity, BepInEx, and SDL2 retain their respective licenses.
