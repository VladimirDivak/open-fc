"""Far Cry PAK (zip) access for the CGF inspector tool.

PAK archives are standard zip containers (matching Unity SharpZipLib usage in
PakArchive.cs). This module locates files inside FCData/Levels paks and reads
their bytes, mirroring FcFileSystem reverse-mount-order override behaviour.
"""

import glob
import os
import zipfile

DEFAULT_GAME = os.path.expanduser("~/Documents/farcry-game")


def norm(path):
    """Normalize to lower-case forward-slash, no leading/trailing slash."""
    if not path:
        return ""
    path = path.replace("\\", "/").lower().strip("/")
    while "//" in path:
        path = path.replace("//", "/")
    return path


def list_paks(game_path):
    """All pak archives, in mount order (later entries override earlier)."""
    paks = sorted(glob.glob(os.path.join(game_path, "FCData", "*.pak")))
    paks += sorted(glob.glob(os.path.join(game_path, "Levels", "*", "*.pak")))
    return paks


def find_in_paks(pattern, game_path):
    """Return list of (pak_path, entry_name) whose normalized name contains pattern."""
    pat = norm(pattern)
    results = []
    for pak in list_paks(game_path):
        try:
            with zipfile.ZipFile(pak) as zf:
                for name in zf.namelist():
                    if name.endswith("/"):
                        continue
                    if pat in norm(name):
                        results.append((pak, name))
        except (zipfile.BadZipFile, OSError):
            continue
    return results


def read_file(path, game_path):
    """Read file bytes from disk or from paks.

    Returns (data, source_description). Raises FileNotFoundError if not found.
    Disk path wins; otherwise an exact normalized pak match, then a suffix
    match, then a basename match. Later paks override earlier ones.
    """
    if os.path.isfile(path):
        with open(path, "rb") as f:
            return f.read(), path

    target = norm(path)
    base = target.rsplit("/", 1)[-1]
    exact = []
    suffix = []
    basename = []
    for pak in list_paks(game_path):
        try:
            with zipfile.ZipFile(pak) as zf:
                for name in zf.namelist():
                    if name.endswith("/"):
                        continue
                    n = norm(name)
                    if n == target:
                        exact.append((pak, name))
                    elif n.endswith("/" + target) or n.endswith(target):
                        suffix.append((pak, name))
                    elif n.rsplit("/", 1)[-1] == base:
                        basename.append((pak, name))
        except (zipfile.BadZipFile, OSError):
            continue

    hits = exact or suffix or basename
    if not hits:
        raise FileNotFoundError(f"'{path}' not found on disk or in any pak under {game_path}")

    pak, name = hits[-1]  # last pak wins (patch override)
    with zipfile.ZipFile(pak) as zf:
        data = zf.read(name)
    extra = ""
    if len(hits) > 1:
        extra = f" ({len(hits)} matches, used last)"
    return data, f"{name} @ {os.path.basename(pak)}{extra}"
