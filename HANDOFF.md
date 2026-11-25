# Vectis Play handoff

Canonical project: `C:\Users\jojo\Documents\Projects\VectisPlay`.
Original scratch archive: `C:\Users\jojo\.t3\scratch\2026-11-22-build-a-lean-local-windows-ac2b09b9`; keep it until the user explicitly says it can go.

## Current state

- Published runtime: `runtime-1.10.1-vectis.3`.
- Runtime asset: `vectis-runtime-win_x64-1.10.1-c2a8177.zip`.
- Zip SHA-256: `9eab9fbd9fa44930ecf4fe2072333ef385318e5cf09cb0acb3d5b17bed39f1ec`.
- `chiaki.exe` SHA-256: `19054310d0cb4de3a014b0df1eea98ec0dc7f3b3e4e04a3ae7d4235d36a80261`.
- Runtime base: chiaki-ng `f30da0a` plus patches `0001` through `0008`.
- Last pushed upstream head before this local round: `977a4c9`.
- This round is local only unless the user asks to push.

## Important behavior

- The launcher reads and writes `HKCU\Software\Vectis` only.
- Some chiaki values are Qt serialized strings, so write them through the launcher or runtime UI rather than raw registry editing.
- `server_mac` must be the console-id-derived registration value, not the network adapter MAC.
- Launcher settings are drafts until Save settings. Runtime settings are read when a stream starts, so changes require reconnecting.
- Mouse sensitivity cannot exceed the game's maximum stick turn speed. In-game look sensitivity still matters.

## Current quality-first experiment

The registry is intentionally set to 1080p, 60 fps, H.265, 45000 kbps, Vulkan decode/render, `high_quality_advanced_spatial` (FSRCNNX x2 16 neural upscale on 1440p fullscreen), no frame mixer, direct frame mapping on, VSync off, 30 ms audio buffer and `packet_loss_reported_max=0.05`. `idr_on_fec_failure` is off. This matches the Cinema preset and needs a real PS5 A/B test. Do not call it verified until the user playtests it.

The Cinema preset uses `preset_high_quality_advanced_spatial` scaling. Balanced stays on custom `ewa_lanczossharp` at 32000 kbps. The spatial hook only engages when upscaling by at least 1.2x, which 1080p on 1440p fullscreen satisfies (1.33x).

Patch `0009-receive-buffer.patch` is written and validated but NOT in the shipped runtime. It raises the Takion UDP socket receive buffer from 100 KB to 4 MB in both the LAN and PSN socket paths while keeping the wire `a_rwnd` unchanged. It requires a vectis.4 CI build (workflow_dispatch in `.github/workflows/runtime.yml`) before it has any effect. Further vectis.4 candidates not yet written: shorter packet reorder stall, controller event coalescing, mouse stop decay rework.

The old quality problem was largely self-inflicted: low bitrate plus the fast placebo preset disabled debanding, dithering, high-quality chroma scaling and color processing. Do not map launcher's quality modes back to `fast`.

The simple launcher presets are Low latency, Balanced and Cinema. Balanced is 32000 kbps and Cinema is 45000 kbps with neural spatial upscaling. Low latency still uses high-quality scaling, not `fast`.

Controller launcher navigation is implemented through XInput in `src/GamepadNav.cs`. Native DualSense launcher navigation is not implemented because XInput does not expose it without Steam Input or DS4Windows translation.

## Validation commands

```powershell
cd C:\Users\jojo\Documents\Projects\VectisPlay
python build.py
```

Expected tail:

- `runtime/ already staged and verified, skipping extraction`
- `patches/ apply cleanly to upstream in order`
- `VectisPlay.exe compiled from 8 sources`
- `build complete`

For live diagnostics use `tools/Start-DebugStream.ps1` only when a test stream is allowed. Logs go to `%TEMP%\VectisPlayLogs`.

## Cautions

- Do not connect to the PS5 unless the user has made the console available for a test.
- Do not print, commit or copy registration secrets.
- Do not delete `runtime/` or `upstream/` casually. `runtime/` is required to run and `upstream/` is required for meaningful local patch validation.
- Do not push or cut a vectis.4 release without explicit user approval.
