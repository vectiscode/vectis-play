# Vectis Play

Vectis Play is a lean Windows launcher for PS5 Remote Play. It wraps a patched chiaki-ng runtime with a small WPF front end focused on fast setup, low latency streaming and keyboard/mouse play.

The project is local first. It does not install drivers, does not replace Sony Remote Play and does not share registration data with other applications. Registration data needed by the runtime remains in the user's registry.

## Quick start

```powershell
cd C:\Users\jojo\Documents\Projects\VectisPlay
python build.py
.\VectisPlay.exe
```

`build.py` verifies the pinned runtime, checks that the runtime patches still apply when `upstream/` is present and compiles `VectisPlay.exe` with the .NET Framework compiler already included in Windows. If `runtime/` is already valid it is used directly. If it is absent the pinned runtime archive is downloaded, hash checked and extracted.

The first screen is the simple view. It exposes only the choices most people need:

- connect, disconnect and pairing state
- performance preset
- resolution
- mouse aim and sensitivity
- volume
- network check and suggested bitrate

Use **All settings** for the full tabs: Session, Stream, Picture, Sound and Controls. Settings are drafts until **Save settings** is pressed. **Reset to saved** reloads the last saved values. Saved settings are used by the next connection, not by a stream that is already running.

## Controls

The default layout is meant to feel like a PC game:

| Input | PlayStation control |
| --- | --- |
| Esc or C | Circle/back |
| Enter | Options/pause |
| Space | Cross |
| R and F | Square and Triangle |
| Q and E | L1 and R1 |
| Shift | L3/sprint |
| V | R3 |
| WASD | left stick |
| Arrow keys | D-pad |
| Tab | touchpad click |
| Backspace | Create |
| Home | PS |
| Mouse movement | right stick aim |
| Left mouse button | R2/fire |
| Right mouse button | L2/aim |
| Alt | walk modifier |

Use **Edit key bindings** to open the runtime's native binding editor. Custom user bindings win over launcher defaults.

Mouse sensitivity changes how quickly the virtual stick reaches full deflection. It cannot turn faster than the game's own maximum stick turn speed. If fast mouse movement is capped, raise the in-game look sensitivity as well.

## Stream quality profiles

The simple view has three stream presets. All use 60 fps stream mode, H.265, Vulkan decode and render, direct frame mapping and no frame interpolation:

- **Low latency**: quality scaling, VSync off, 20 ms audio buffer, 20000 kbps at 1080p or 15000 kbps at 720p
- **Balanced**: tuned EWA Lanczos sharp scaling, debanding, dithering, VSync off, 20 ms audio buffer, 32000 kbps
- **Cinema**: stronger debanding and anti-ringing, VSync off, 30 ms audio buffer, 45000 kbps and clean keyframe recovery after unrecoverable packet loss

The 5% packet-loss reporting ceiling avoids unnecessary resolution and bitrate reduction during small Wi-Fi spikes. On genuinely unstable Wi-Fi this can mean more visible compression artifacts; use the network check and lower the bitrate if needed.

H.265 HDR is available in the advanced Stream page. It sends 10-bit video and can substantially reduce banding on an OLED, but Windows HDR should be enabled first with `Win+Alt+B`.

60 fps streaming does not make a 30 fps game simulate faster. A game capped at 30 fps keeps its own engine and frame cadence latency regardless of the streaming preset.

## Controller play

The launcher can be navigated with an XInput controller: A selects, B goes back, LB/RB change pages and the D-pad or left stick moves focus. Native DualSense launcher navigation is not implemented because DualSense is not an XInput device unless Steam Input or DS4Windows is already translating it.

For streaming with a real DualSense, use USB-C for full HD haptics and adaptive triggers. Configure the controller's Windows playback device as Quadraphonic and do not use it as the default Windows output. Close Steam Input, DS4Windows or DualSenseX while streaming if they mask the controller. Bluetooth play uses adaptive triggers but falls back to motor rumble instead of full HD haptics. The Controls page exposes HD haptic strength, Bluetooth rumble strength and background controller input.

Remote Play still includes console capture, encoding, Wi-Fi, decoding and display delay. It cannot feel identical to a directly connected monitor.

## Diagnostic tool

`tools/Start-DebugStream.ps1` starts the runtime with verbose latency logging and writes logs under `%TEMP%\VectisPlayLogs`. It starts a real auto-connect stream, so use it only when the PS5 is free for a test session. Close the player window when done.

Other recovery and maintenance scripts live in `tools/`. They are not part of normal play.

## Project layout

- `src/`: launcher C# source
- `Workbench.xaml`: launcher interface
- `build.py`: local verifier and compiler
- `patches/`: ordered Vectis patches for chiaki-ng
- `assets/`: icon and brand assets
- `tools/`: diagnostics and maintenance tools
- `runtime/`: ignored local runtime payload created by `build.py`
- `upstream/`: ignored optional chiaki-ng checkout used to validate patches
- `tools/motion_lab.py`: optional offline frame interpolation experiment, separate from streaming

Do not hand edit files inside `runtime/` or `upstream/`; both are generated or external dependencies. Patch changes belong in `patches/`.

## Runtime patches

The runtime is chiaki-ng commit `f30da0a` plus ordered patches `0001` through `0008`. They add the Vectis registry namespace, keyboard stick input, mouse aim, color controls, multi-key alternatives, low-delay decode/presentation behavior and input response tuning. `build.py` validates the patch series when the pinned upstream checkout is present.

## License and attribution

Vectis Play preserves chiaki-ng attribution and license notices. chiaki-ng is distributed under AGPL-3.0-only with the OpenSSL exception; see `COPYING` and `LICENSES/AGPL-3.0-only-OpenSSL.txt`. Upstream project: https://github.com/streetpea/chiaki-ng

The Vectis brand icon is under `LICENSES/Vectis-brand-MIT.txt`.
