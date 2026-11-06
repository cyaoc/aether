#!/usr/bin/env python3
"""Makes and checks the delta package that GUI auto-update (#327) downloads instead of a whole release package (#330).

  python3 scripts/delta-package.py make FROM_TAG PREVIOUS NEW DELTA
  python3 scripts/delta-package.py check PREVIOUS NEW DELTA

PREVIOUS and NEW are the same release package (.zip or .tar.gz) of FROM_TAG and of the new tag.
make:  DELTA gets NEW's files that are new or changed since PREVIOUS, laid out like the program directory,
       and manifest.json: {"from": FROM_TAG, "files": {path: sha256}} for every file in NEW.
check: lays DELTA over PREVIOUS the way auto-update does, and fails unless that matches the manifest and NEW file by file.
"""
import hashlib
import io
import json
import sys
import tarfile
import time
import zipfile

MANIFEST = "manifest.json"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def unpack(path):
    """Yields (path, mode, contents) for each file in a .zip or .tar.gz."""
    if path.endswith(".zip"):
        with zipfile.ZipFile(path) as archive:
            for member in archive.infolist():
                if not member.is_dir():
                    yield member.filename, member.external_attr >> 16, archive.read(member)
    else:
        with tarfile.open(path) as archive:
            for member in archive:
                if member.isfile():
                    yield member.name, member.mode, archive.extractfile(member).read()
                elif not member.isdir():
                    sys.exit(f"{path}: {member.name} is not a regular file")


def release_files(path):
    """Like unpack, with paths inside the program directory instead of the package's aether-gui-… directory."""
    for name, mode, data in unpack(path):
        yield name.split("/", 1)[1], mode, data


def digests(files):
    return {name: (sha256(data), mode) for name, mode, data in files}


def pack(path, files):
    now = time.time()
    if path.endswith(".zip"):
        with zipfile.ZipFile(path, "w") as archive:
            for name, mode, data in files:
                info = zipfile.ZipInfo(name, time.localtime(now)[:6])
                info.external_attr = mode << 16
                archive.writestr(info, data, zipfile.ZIP_DEFLATED)
    else:
        with tarfile.open(path, "w:gz") as archive:
            for name, mode, data in files:
                info = tarfile.TarInfo(name)
                info.size, info.mode, info.mtime = len(data), mode, now
                archive.addfile(info, io.BytesIO(data))


def make(tag, previous, new, delta):
    before = digests(release_files(previous))
    manifest, changed = {}, []
    for name, mode, data in release_files(new):
        manifest[name] = sha256(data)
        # A changed mode counts too, or auto-update would keep the old one.
        if before.get(name) != (manifest[name], mode):
            changed.append((name, mode, data))
    pack(delta, changed + [(MANIFEST, 0o644, json.dumps({"from": tag, "files": manifest}).encode())])
    print(f"{delta}: {len(changed)} of {len(manifest)} files")


def check(previous, new, delta):
    manifest = json.loads(next(data for name, _, data in unpack(delta) if name == MANIFEST))["files"]
    shipped = digests(unpack(delta))
    del shipped[MANIFEST]
    after = digests(release_files(new))
    # What auto-update leaves in the program directory: the delta's files over the previous version's.
    installed = digests(release_files(previous)) | shipped
    wrong = [name for name in sorted(after.keys() | manifest.keys() | shipped.keys())
             if name not in after or manifest.get(name) != after[name][0] or installed.get(name) != after[name]]
    if wrong:
        sys.exit(f"{delta} does not rebuild {new}: {', '.join(wrong)}")
    print(f"{delta}: rebuilds all {len(after)} files of {new}")


if __name__ == "__main__":
    command, *arguments = sys.argv[1:]
    {"make": make, "check": check}[command](*arguments)
