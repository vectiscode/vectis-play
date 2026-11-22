# Vectis Play handoff

## State (2026-11-22)

- GitHub: https://github.com/vectiscode/vectis-play is public. Remote `main` is still `ad564f5`. Local `main` is ahead with the link check (`3171e27`), the mouse aim runtime patches (`8f2c853`, `0988b51`) and the Controls tab. Nothing from this round is pushed to `main` yet. `vectiscode/vectiscode-cli` was made private.
- Runtime release: https://github.com/vectiscode/vectis-play/releases/tag/runtime-1.10.1-vectis.1, built by run 44983891530 from `0988b51` (chiaki-ng f30da0a + `patches/0001-0003`).
  - zip SHA-256 `b7af939b641ad4f35e17d747fabc0594db806d4ec86f33d4ce25a5046f1e567f`
  - chiaki.exe SHA-256 `61c0525ff9ac80a290a9534d04dbe1e9b85177523826d4ad7cbb5c580642390d`
  - Smoke checked locally with `--version` only: DLLs and Qt load. No console contact.
- Site (`C:/Users/jojo/Documents/Projects/vectiscode/site`): the link check section, GitHub links and the new session screenshot are local and uncommitted. Live vectiscode.com is still the previous deploy.

## Not done yet

1. Controls tab layout: mouse card first, response graph and aim pad visible on first open at 980x680 (two columns from 900 px), pad usable while Mouse aim is off, CheckBox text 14 px.
2. Site: rebuild the source zip from HEAD, add a Controls screenshot and a keyboard and mouse section, commit the site and deploy with `wrangler.cmd pages deploy .\site --project-name vectiscode --branch main`.

Done: build.py stages the release runtime (zip and exe hashes), patches/ are checked in order and the launcher pins the new chiaki.exe hash.

## Evidence

- Research: `.scratch/kbm-research.md`
- Captures: `.scratch/link-20261122-045448/png/`, `.scratch/controls-20261122-052729/png/`
- Still needs a user-picked PS5 slot: pairing, stream, audio, DualSense, keyboard and mouse feel, latency.
