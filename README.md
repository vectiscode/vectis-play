# Vectis Play

A Windows launcher and settings workbench for [chiaki-ng](https://github.com/chiaki-ng/chiaki-ng) PS5 Remote Play. Vectis Play is a small .NET Framework WPF app that starts a bundled chiaki-ng player and exposes the stream, picture and sound settings that matter for a short button-to-picture path. The video stream never passes through the launcher.

The runtime is the official chiaki-ng v1.10.1 Windows portable archive ([release](https://github.com/chiaki-ng/chiaki-ng/releases/tag/v1.10.1), [source at commit f30da0aaeb96fc0026cc03683153f682352e7d7c](https://github.com/chiaki-ng/chiaki-ng/tree/f30da0aaeb96fc0026cc03683153f682352e7d7c)), unmodified except for one shared organization and application name literal in `chiaki.exe` that moves its settings to `HKCU\Software\Vectis\Vectis`. `namespace.patch` is the equivalent source change for `gui/src/main.cpp`. Not endorsed or certified by Sony Interactive Entertainment. chiaki-ng and Chiaki are by their authors and contributors; see upstream `docs/index.md` for attribution.

## Layout

- `VectisPlay.cs`: controller. Loads the embedded XAML, binds named parts by type, reads and writes settings, owns the child process.
- `Workbench.xaml`: window layout, embedded as `Vectis.Workbench.xaml`. Four tabs: Session, Stream, Picture, Sound.
- `VectisPlay.manifest`, `assets/VectisPlay.ico`: manifest and icon. The mark follows the vectiscode site favicon under MIT, see `LICENSES/Vectis-brand-MIT.txt`.
- `build.py`: verifies and stages the runtime, patches the exe literal, compiles the launcher.
- `runtime/`: build output, gitignored. `chiaki-ng-Win/chiaki.exe` is SHA-256 checked before every launch and every settings write.
- `namespace.patch`: source form of the exe rebrand.
- `motion_lab.py`: optional offline experiment, see below.
- `COPYING`, `LICENSES/`: AGPL-3.0 with the OpenSSL exception for chiaki-ng and the launcher, MIT for the brand mark.

## Build

Requires Windows with the inbox .NET Framework 4.x compiler and WPF, Python 3 and git for the patch check. The release zip is expected at the repo root; if absent `build.py` downloads the pinned v1.10.1 asset and verifies its SHA-256 before extracting.

```
python build.py
```

An existing `runtime/` is never deleted or modified by the build. Remove it by hand to rebuild it.

## First run

1. On the PS5 open Settings > System > Remote Play and enable Remote Play. Keep the console awake and on the same LAN as this PC.
2. Launch `VectisPlay.exe`. With no console registered it shows "Pair your PS5".
3. Click "Pair PS5". In the chiaki-ng window click the detected console, choose manual PIN pairing and enter your PSN account ID if the registration asks for it. Keep the default profile. Close the window or use "Close setup" when registration finishes.
4. The Session tab then shows the console name with "Connect". With several PS5s registered a list picks the target. "Reconnect" appears after a player session ends. Ctrl+Q disconnects inside the player.

The Session tab also has "Pair / manage" for adding or removing registrations and "Native settings", an explicit button that opens chiaki-ng without auto-connect so its own settings icon can reach controller mapping and every other native option. Neither opens on startup.

## Stream, Picture and Sound settings

Each control edits one native setting. Changing one never changes another except where noted. Values are saved with Apply and used from the next session. Revert reloads what is saved. Unsaved edits block Connect, Pair and Native settings until you Apply or Revert. Closing with unsaved edits asks before discarding. Settings are locked while a child window is open.

| Control | Native setting | Values |
| --- | --- | --- |
| Resolution | `resolution_local_ps5` | 360p 540p 720p 1080p |
| Frame rate | `fps_local_ps5` | 30 or 60 |
| Bitrate | `bitrate_local_ps5` | 0 for upstream auto or 1 to 100000 kbps, saved as typed |
| Codec | `codec_local_ps5` | H.264 H.265 H.265 HDR |
| Decoder | `hw_decoder` | automatic Vulkan Video D3D11VA CUDA software |
| Renderer | `render_backend` | Vulkan OpenGL |
| Window mode | `window_type` | selected resolution adjust manually fullscreen zoom stretch |
| VSync | `vsync` | on or off |
| Volume | `audio_volume` | 0 to 100 percent, native 0 to 128 |
| Audio buffer | `audio_buffer_size` | 0 for auto or 10 to 100 ms, native 192 bytes per ms |
| Upscaling | `placebo_preset` and `placebo_settings/upscaler` | Direct (fast) or one named scaler from nearest to FSRCNNX 16 |
| Frame mixer | `placebo_frame_mixer` and `direct_frame_mapping` | Direct, queue without blend, oversample, hermite, linear, cubic |

Apply validates every changed field before writing anything and writes only the fields you changed. A native value the workbench cannot represent is shown as `Native: <value>` and left alone until you pick something else. OpenGL cannot display H.265 HDR, so that pair is refused with a message instead of being rewritten. The first named scaler also initializes the missing upstream placebo defaults in the private group, because chiaki-ng would otherwise reset the scaler on its next start. Frame mixers are native temporal blending of decoded frames. They are not frame generation and they cannot make input arrive earlier.

## Image quality and latency

chiaki-ng decodes video with FFmpeg and renders it natively. On Windows the automatic path can use Vulkan Video, CUDA or D3D11VA depending on the driver and renderer. Software decode is a fallback. The active path has not been measured on this PC.

Lowering resolution or bitrate sends less data, which can help when Wi-Fi airtime is congested. A wired console and PC avoid the problem. On Wi-Fi stay close to the access point on 5 or 6 GHz. That is a rationale and not a measurement: no Wi-Fi latency was measured here.

Game DLSS does not apply: a decoded stream carries no engine motion vectors or depth buffer. Image-only GPU upscaling can improve display scaling. Optical-flow interpolation adds buffering and artifacts and does not make input earlier. For reference, Veyra is a separate project with GPU frame generation providers that use motion guidance: see its [professional page](https://github.com/Likely7/Veyra-NRVideo/blob/fe8df5328a62849eb2d79053564b9574ab68aecd/qml/Veyra/ProPage.qml) and [`FrameGeneration.cpp`](https://github.com/Likely7/Veyra-NRVideo/blob/fe8df5328a62849eb2d79053564b9574ab68aecd/src/ui/bridge/FrameGeneration.cpp) at commit fe8df5328a62849eb2d79053564b9574ab68aecd. Nothing from it is copied or redistributed here and Vectis Play has no live frame generation.

## motion_lab.py

An optional offline experiment, separate from the app. `python motion_lab.py INPUT OUTPUT.mp4` runs FFmpeg `minterpolate` (CPU motion estimation from pixels, bidirectional, overlapped block compensation) to turn a local 30 fps recording into 60 fps. It needs `ffmpeg` on PATH, refuses to overwrite an existing output and never touches a live session. It is not GPU optical flow and not DLSS.

## State and data

All launcher data lives in `HKCU\Software\Vectis`. The stock Chiaki keys and any earlier launcher keys are never read or written. The launcher reads only `registered_hosts` metadata (`target`, `server_nickname`, `server_mac`) and the settings listed above. Connect copies the raw `server_mac` value into `settings/auto_connect_mac` and Pair, Manage and Native settings clear it. First-use defaults are written on a user action and only where absent: `placebo_preset=fast`, `placebo_frame_mixer=none`, `disconnect_action=nothing`, `remote_play_ask=false`, `add_steam_shortcut_ask=false`.

## Status

The launcher builds with `python build.py` against .NET Framework 4 WPF. The new `runtime/` was staged and its patched `chiaki.exe` hash matched, but that runtime has not been launched. The earlier offline `list` check ran on the previous runtime only. The embedded workbench was loaded by an offscreen host with the registry redirected to a throwaway key: the Session, Stream, Picture and Sound pages rendered at 980x680, a bitrate of 11677 saved and reloaded exactly with resolution unchanged, an invalid volume was refused without touching the saved bitrate and an FSR scaler with a Linear mixer wrote the expected native values. The compiled entry point also opened and closed from a copy without a player runtime. No console was paired. No stream was run and no setting was saved to the real Vectis registry keys. Pairing, reconnect, video pacing, audio, DualSense input and input-to-display latency still need a user-selected live slot. The launcher uses same-LAN discovery rather than asking for an IP.

## License

Launcher code is provided under the same terms as chiaki-ng: GNU AGPL v3 with the OpenSSL exception (`COPYING`, `LICENSES/AGPL-3.0-only-OpenSSL.txt`). The brand mark is MIT (`LICENSES/Vectis-brand-MIT.txt`).
