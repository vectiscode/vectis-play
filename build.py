#!/usr/bin/env python3
"""Vectis Play build: stage the verified chiaki-ng backend and compile the launcher.

Steps: require the shipped license texts, ensure the pinned release zip
(download only if absent), verify its SHA-256, extract into a disposable
staging dir inside this repo, rebrand the single organization/app data
literal inside chiaki.exe and verify the patched hash, move the payload to
runtime/, check namespace.patch against the upstream checkout when present,
then compile the sources in src/ with the .NET Framework csc.
"""

import hashlib
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ZIP_NAME = "chiaki-ng-win_x64-MSYS2-portable-1.10.1.zip"
ZIP_URL = "https://github.com/chiaki-ng/chiaki-ng/releases/download/v1.10.1/" + ZIP_NAME
ZIP_SHA256 = "6ca3f5e6780608104d79345ab7755cfa77fefc33a41ae10651a874c43f54ea57"
STOCK_EXE_SHA256 = "c69a1e2dbf7406f74ae8a33c7c98fba226d79431a36aca5bc7143cedc4d19dec"
PATCHED_EXE_SHA256 = "834e7f6c971183664dc9367eae054fbfb4e964e596d37e0aa43f69406bb70805"
OLD = b"ChiakiRegistEventType\x00Chiaki\x001.10.1\x00chiaki-ng\x00"
NEW = OLD.replace(b"\x00Chiaki\x00", b"\x00Vectis\x00")
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
    missing = [rel for rel in ("COPYING", "LICENSES/AGPL-3.0-only-OpenSSL.txt")
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


def stage_backend(zip_path):
    backend = ROOT / "runtime"
    exe = backend / "chiaki-ng-Win" / "chiaki.exe"
    if exe.exists() and sha256_file(exe) == PATCHED_EXE_SHA256:
        print("runtime/ already staged and verified, skipping extraction")
        return
    if backend.exists():
        fail("runtime/ exists but chiaki.exe is missing or fails verification; "
             "refusing to modify it, remove it manually to rebuild")

    with tempfile.TemporaryDirectory(prefix=".vectis-stage-", dir=ROOT) as staging:
        payload = Path(staging) / "payload"
        payload.mkdir()
        print("Extracting " + ZIP_NAME)
        with zipfile.ZipFile(zip_path) as archive:
            archive.extractall(payload)

        staged_exe = payload / "chiaki-ng-Win" / "chiaki.exe"
        data = staged_exe.read_bytes()
        if sha256_bytes(data) != STOCK_EXE_SHA256:
            fail("stock chiaki.exe sha256 mismatch")
        if data.count(OLD) != 1:
            fail("rebrand literal found %d times, expected 1" % data.count(OLD))
        patched = data.replace(OLD, NEW)
        if sha256_bytes(patched) != PATCHED_EXE_SHA256:
            fail("patched chiaki.exe sha256 mismatch")
        staged_exe.write_bytes(patched)

        payload.rename(backend)
        print("runtime/ staged, chiaki.exe rebranded and verified")


def check_patch():
    upstream = ROOT / "upstream"
    patch = ROOT / "namespace.patch"
    if not (upstream / ".git").exists() or not patch.exists():
        return
    result = subprocess.run(
        ["git", "-C", str(upstream), "apply", "--check", str(patch)],
        capture_output=True, text=True)
    if result.returncode != 0:
        fail("namespace.patch does not apply cleanly: " + result.stderr.strip())
    print("namespace.patch applies cleanly to upstream")


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
    zip_path = ensure_zip()
    stage_backend(zip_path)
    check_patch()
    compile_launcher()
    print("build complete")


if __name__ == "__main__":
    main()
