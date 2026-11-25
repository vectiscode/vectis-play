#!/usr/bin/env python3
"""Vectis Play build: stage the verified Vectis runtime and compile the launcher.

Steps: require the shipped license texts, ensure the pinned runtime zip
(download only if absent), verify its SHA-256, extract into a disposable
staging dir inside this repo, verify chiaki.exe, move the payload to
runtime/, check that patches/ apply to the upstream checkout when present,
then compile the sources in src/ with the .NET Framework csc.

The runtime is chiaki-ng v1.10.1 (f30da0a) with patches/ applied, built by
.github/workflows/runtime.yml and published as a release of this repo.
"""

import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ZIP_NAME = "vectis-runtime-win_x64-1.10.1-c2a8177.zip"
ZIP_URL = ("https://github.com/vectiscode/vectis-play/releases/download/"
           "runtime-1.10.1-vectis.3/" + ZIP_NAME)
ZIP_SHA256 = "9eab9fbd9fa44930ecf4fe2072333ef385318e5cf09cb0acb3d5b17bed39f1ec"
EXE_SHA256 = "19054310d0cb4de3a014b0df1eea98ec0dc7f3b3e4e04a3ae7d4235d36a80261"
UPSTREAM_COMMIT = "f30da0aaeb96fc0026cc03683153f682352e7d7c"
CSC = Path(r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe")


def fail(message):
    print("build.py: error: " + message, file=sys.stderr)
    sys.exit(1)


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_licenses():
    missing = [rel for rel in ("COPYING", "LICENSES/AGPL-3.0-only-OpenSSL.txt", "LICENSES/Vectis-brand-MIT.txt")
               if not (ROOT / rel).exists()]
    if missing:
        fail("missing shipped license file(s): " + ", ".join(missing))


def ensure_zip():
    zip_path = ROOT / ZIP_NAME
    if not zip_path.exists():
        part = ROOT / (ZIP_NAME + ".part")
        print("Downloading " + ZIP_URL)
        urllib.request.urlretrieve(ZIP_URL, str(part))
        part.replace(zip_path)
    digest = sha256_file(zip_path)
    if digest != ZIP_SHA256:
        fail("%s sha256 mismatch: got %s, want %s" % (ZIP_NAME, digest, ZIP_SHA256))
    return zip_path


def stage_backend(zip_path=None):
    backend = ROOT / "runtime"
    exe = backend / "chiaki-ng-Win" / "chiaki.exe"
    if exe.exists() and sha256_file(exe) == EXE_SHA256:
        print("runtime/ already staged and verified, skipping extraction")
        return
    if backend.exists():
        fail("runtime/ exists but chiaki.exe is missing or fails verification; "
             "refusing to modify it, move it aside manually to rebuild")
    if zip_path is None:
        zip_path = ensure_zip()

    with tempfile.TemporaryDirectory(prefix=".vectis-stage-", dir=ROOT) as staging:
        payload = Path(staging) / "payload"
        payload.mkdir()
        print("Extracting " + ZIP_NAME)
        with zipfile.ZipFile(zip_path) as archive:
            archive.extractall(payload)
        if sha256_file(payload / "chiaki-ng-Win" / "chiaki.exe") != EXE_SHA256:
            fail("runtime chiaki.exe sha256 mismatch")
        payload.rename(backend)
        print("runtime/ staged and verified")


def install_shader_overrides():
    # Copy neural upscale shaders next to chiaki.exe. Runtime patch 0010 makes
    # chiaki prefer these over its embedded shaders once a vectis.4 runtime is
    # built; with the current runtime they are simply inert files.
    shaders = ROOT / "assets" / "shaders"
    target_dir = ROOT / "runtime" / "chiaki-ng-Win" / "shaders"
    if not shaders.is_dir() or not target_dir.parent.is_dir():
        return
    target_dir.mkdir(exist_ok=True)
    artcnn = shaders / "ArtCNN_C4F32_DS.glsl"
    if artcnn.exists():
        shutil.copy2(artcnn, target_dir / "FSRCNNX_x2_16-0-4-1.glsl")
        print("shader override staged: ArtCNN C4F32 DS replaces embedded FSRCNNX x2 16")


def check_patches():
    upstream = ROOT / "upstream"
    patches = sorted((ROOT / "patches").glob("*.patch"))
    if not patches:
        return
    if not (upstream / ".git").exists():
        print("upstream/ not present, skipping patch validation")
        return
    upstream_head = subprocess.check_output(["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True).strip()
    if upstream_head != UPSTREAM_COMMIT:
        fail("upstream/ is at " + upstream_head + ", want chiaki-ng " + UPSTREAM_COMMIT)
    # Apply in order to a throwaway index so later patches see earlier ones
    # while the upstream working tree stays untouched.
    with tempfile.TemporaryDirectory(prefix=".vectis-index-", dir=ROOT) as scratch:
        env = dict(os.environ, GIT_INDEX_FILE=str(Path(scratch) / "index"))
        subprocess.run(["git", "-C", str(upstream), "read-tree", "HEAD"], env=env, check=True)
        for patch in patches:
            result = subprocess.run(
                ["git", "-C", str(upstream), "apply", "--cached", str(patch)],
                env=env, capture_output=True, text=True)
            if result.returncode != 0:
                fail(patch.name + " does not apply cleanly: " + result.stderr.strip())
    print("patches/ apply cleanly to upstream in order")


def compile_launcher():
    if not CSC.exists():
        fail("csc.exe not found at " + str(CSC))
    xaml = ROOT / "Workbench.xaml"
    if not xaml.exists():
        fail("Workbench.xaml is missing; the launcher embeds it as Vectis.Workbench.xaml")
    sources = sorted((ROOT / "src").glob("*.cs"))
    if not sources:
        fail("no C# sources found in src/")
    framework = CSC.parent
    wpf = framework / "WPF"
    subprocess.run([
        str(CSC), "/nologo", "/target:winexe", "/platform:x64", "/optimize+",
        "/win32manifest:" + str(ROOT / "VectisPlay.manifest"),
        "/win32icon:" + str(ROOT / "assets" / "VectisPlay.ico"),
        "/r:" + str(wpf / "WindowsBase.dll"),
        "/r:" + str(wpf / "PresentationCore.dll"),
        "/r:" + str(wpf / "PresentationFramework.dll"),
        "/r:" + str(framework / "System.Xaml.dll"),
        "/r:" + str(framework / "System.Xml.dll"),
        "/resource:" + str(xaml) + ",Vectis.Workbench.xaml",
        "/out:" + str(ROOT / "VectisPlay.exe"),
    ] + [str(path) for path in sources], check=True)
    print("VectisPlay.exe compiled from " + str(len(sources)) + " sources")


def main():
    require_licenses()
    stage_backend()
    install_shader_overrides()
    check_patches()
    compile_launcher()
    print("build complete")


if __name__ == "__main__":
    main()
