# Vectis Play handoff

## State (2026-11-22, later)

- Runtime release `runtime-1.10.1-vectis.3` published: chiaki-ng f30da0a + patches 0001-0008, built by CI run 45514256438 from commit c2a8177. Local runtime staged and hash-verified.
- Launcher `VectisPlay.exe` built and running; Session tab shows console "agent".

## Registration: migrated from Veyra (done)

- Veyra does NOT use the chiaki registry. Its login lives in `%LOCALAPPDATA%\Veyra\remoteplay\<id>.dat`, a DPAPI blob (CryptProtectData, desc "Veyra PS5 pairing"). Format "VRP"+ver: u32 hostSize, u32 w/h/fps/bitrate/codec, then 40 credential bytes (accountId[8], registrationKey[16]=rp_regist_key, sessionKey[16]=rp_key), host string, v2 adds viewOnly+consoleId[12hex]. Decrypted on this machine = the user's own credential, fine to use locally.
- chiaki registry format (`upstream/gui/src/host.cpp`): registered_hosts\N values target(DWord), ap_*(String), server_nickname(String), server_mac(Binary 6B), rp_regist_key(Binary 16B), rp_key_type(DWord, unused at runtime), rp_key(Binary 16B), console_pin(String). `settings/psn_account_id` = base64 of 8-byte accountId; `settings/auto_connect_mac` = 6-byte MAC Binary.
- server_mac was recovered from ARP for the profile's host IP (192.168.0.42 -> d4:f7:d5:68:fa:24; consoleId d4f7d5f3a713 confirms).
- Wrote complete correct registrations into BOTH `HKCU\Software\Vectis\Vectis` and `HKCU\Software\Chiaki\Chiaki` (the old Chiaki values were corrupted "@ByteArray(...)" text - unusable; now overwritten with real binary).
- Scripts used: `.scratch/migrate_veyra.ps1`, `.scratch/write_mac.ps1`, `.scratch/dump_*.ps1`.

## Settings applied for low latency (registry, verified written)

fps_local_ps5=60, resolution=1080p, bitrate=25000kbps, codec=h265, hw_decoder=auto, render_backend=vulkan, window_type=Fullscreen, vsync=false, placebo_preset=fast, frame_mixer=none, direct_frame_mapping=true, keyboard_enabled default true, mouse_aim_enabled=true, mouse sensitivity=2.5, curve=1.0, stop easing=4 ms, walk=Alt/0.45.

## SOLVED 2026-11-25: stream connects - registry format was the bug

ROOT CAUSE: Qt serializes QByteArray to the Windows registry as REG_BINARY containing UTF-16LE text "@ByteArray(<latin1 bytes>)". The migration wrote RAW bytes, which chiaki's LoadFromSettings misreads -> keys zeroed -> empty RP-Registkey header -> console refuses with 0x80108b26 regardless of key content (explains why the garbage-key A/B test was identical!). The Veyra credentials were valid the whole time; no re-pairing was needed.

FIX: .scratch/fix_qt_format.ps1 rewrites server_mac/rp_regist_key/rp_key/auto_connect_mac in Qt format in both stores. VERIFIED: `chiaki.exe stream agent 192.168.0.42` -> session request successful, ctrl connected, valid session id, 1920x1080 profile, libplacebo init, heartbeats. Stream works.

SECOND BUG (fixed same night): auto-connect stuck at "Waiting for console...". chiaki discovery derives host "MAC" from discovery host_id, which on PS5 is the CONSOLE ID (D4F7D5F3A713), not the network MAC. server_mac/auto_connect_mac were the ARP MAC (D4F7D568FA24) -> mismatch -> auto-connect never fired. .scratch/fix_mac.ps1 writes server_mac+auto_connect_mac = console-id bytes D4F7D5F3A713 (qt format). VERIFIED: `chiaki.exe --exit-app-on-stream-exit` (exact launcher path) auto-connects, session request successful. If re-pairing ever happens, server_mac MUST come from the regist response PS5-Mac header, never from ARP.

ALSO BUILT: .scratch/regist.py - working Python port of chiaki regist handshake (aes-cfb crypto verified against live console, got clean protocol errors). Usage: `python regist.py <ip> <pin> <account_id_b64>` - account id b64 is in registry settings/psn_account_id. Writes regist_result.json. Use if re-pairing ever needed: user reads 8-digit PIN from PS5 Settings>System>Remote Play>Pair Device (valid ~5min), run within that window, then write fields in QT FORMAT.

Future writes to registry QByteArray values MUST use Qt @ByteArray UTF-16LE encoding (see fix_qt_format.ps1). Also: PS5-Suite repo (manos555555/PS5-Suite) client-avalonia/RemotePlay.cs is a reference C# port with the same lessons.

## Still open

- Controls tab layout tweak; site updates uncommitted in `C:/Users/jojo/Documents/Projects/vectiscode/site`.

## Done 2026-11-25: latency, mouse response + runtime vectis.3

- User report: mouse and keyboard worked but had massive visible delay; mouse sensitivity needed to be much faster. Gemini xhigh review agreed the dominant cost was decode/render presentation, not the input transport.
- Live registry now: 60 fps, fast placebo preset, mouse aim enabled, sensitivity 2.5. Curve and stop easing are absent, so runtime defaults 1.0 and 4 ms apply.
- Runtime vectis.3 includes 0005 multi-key alternatives, 0006 FFmpeg low-delay decode, 0007 direct-frame presentation latency plus Windows 1 ms timer resolution and 0008 faster/crisper mouse response at 250 Hz maximum stick-state pacing.
- Pins updated in build.py and src/Controller.cs. zip sha 9eab9fbd9fa44930ecf4fe2072333ef385318e5cf09cb0acb3d5b17bed39f1ec, exe sha 19054310d0cb4de3a014b0df1eea98ec0dc7f3b3e4e04a3ae7d4235d36a80261.
- Verified locally: all patches apply cleanly in order, CI build success, artifact sha verified, runtime staged, chiaki.exe hash matches, chiaki.exe stays alive 4 s when launched with no connect args, VectisPlay.exe rebuilds and stays alive 3 s.
- Not live tested: stream latency feel, Esc+C both Circle, Enter=Options, Shift=L3 sprint, mouse aim feel in a real game, color grade shader on a real frame.
