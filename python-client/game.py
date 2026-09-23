"""Finding and launching Silksong.

The mod connects out to our socket server, so the server has to be listening before the game starts.

For a Proton install we invoke Proton ourselves rather than going through `steam -applaunch`, so we
own the process and can point it at a prefix of our choosing.

The Wine prefix holds the save file and DebugMod's savestates, so per-instance isolation means a
per-instance STEAM_COMPAT_DATA_PATH - hence the compat_data_path argument on launch().
"""

import os
import platform
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import List, Optional, Tuple

STEAM_APP_ID = "1030300"
GAME_FOLDER_NAME = "Hollow Knight Silksong"
WINDOWS_EXE_NAME = "Hollow Knight Silksong.exe"

IS_WINDOWS = platform.system() == "Windows"
IS_LINUX = platform.system() == "Linux"


class GameNotFound(Exception):
    pass


# --------------------------------------------------------------------------------------
# Resolving the install directory
# --------------------------------------------------------------------------------------

def resolve_game_dir(configured: Optional[str]) -> Path:
    """Install directory from game_dir."""
    if not configured:
        raise GameNotFound(
            "game_dir is not set. Put your Silksong install directory in "
            "server_config.json (copy server_config.json.example):\n"
            '  {"game_dir": "/path/to/Hollow Knight Silksong"}\n'
            "  Linux (Steam):    ~/.steam/steam/steamapps/common/Hollow Knight Silksong\n"
            "  Windows (Steam):  C:\\Program Files (x86)\\Steam\\steamapps\\common\\Hollow Knight Silksong"
        )

    path = Path(configured).expanduser()
    if not path.is_dir():
        raise GameNotFound(f"game_dir is set to {path}, which is not a directory")
    return path


def steam_root_for(game_dir: Path) -> Optional[Path]:
    """Steam root owning this install: the nearest parent containing steamapps/."""
    for parent in game_dir.parents:
        if (parent / "steamapps").is_dir():
            return parent
    return None


def _proton_from_config_info(compat_data_path: Path) -> Optional[Path]:
    """
    Proton build that created this prefix, from its config_info (path lines are
    "<proton dir>/files/...").  A prefix is not always safe to reuse with a different build.
    """
    config_info = compat_data_path / "config_info"
    if not config_info.is_file():
        return None
    try:
        for line in config_info.read_text(encoding="utf-8", errors="replace").splitlines():
            match = re.match(r"^(.*)/files/", line.strip())
            if match:
                candidate = Path(match.group(1))
                if (candidate / "proton").is_file():
                    return candidate
    except OSError:
        pass
    return None


def find_proton(steam_root: Path, compat_data_path: Optional[Path] = None,
                explicit: Optional[str] = None) -> Path:
    """Locate a Proton build: an explicit path, the one that built this prefix, else the newest."""
    if explicit:
        path = Path(explicit).expanduser()
        if (path / "proton").is_file():
            return path
        raise GameNotFound(f"--proton points at {path}, which has no 'proton' script")

    if compat_data_path is not None:
        recorded = _proton_from_config_info(compat_data_path)
        if recorded is not None:
            return recorded

    candidates = [d for d in (steam_root / "steamapps/common").glob("Proton*")
                  if (d / "proton").is_file()]
    candidates += [d for d in (steam_root / "compatibilitytools.d").glob("*")
                   if (d / "proton").is_file()]
    if not candidates:
        raise GameNotFound(
            f"No Proton build found under {steam_root}. Install Proton through Steam, "
            "or pass --proton."
        )
    return max(candidates, key=lambda d: d.stat().st_mtime)


def find_runtime_entry_point(steam_root: Path) -> Optional[Path]:
    """Steam Linux Runtime entry point, the container Steam runs Proton inside."""
    for name in ("SteamLinuxRuntime_sniper", "SteamLinuxRuntime_soldier"):
        entry = steam_root / "steamapps/common" / name / "_v2-entry-point"
        if entry.is_file():
            return entry
    return None


def default_compat_data_path(steam_root: Path) -> Path:
    """The prefix Steam itself uses for Silksong."""
    return steam_root / "steamapps/compatdata" / STEAM_APP_ID


# --------------------------------------------------------------------------------------
# Describing the install
# --------------------------------------------------------------------------------------

class GameInstall:
    """A located Silksong install and how it needs to be started."""

    def __init__(self, game_dir: Path):
        self.game_dir = game_dir
        self.windows_exe = game_dir / WINDOWS_EXE_NAME

    @property
    def is_windows_build(self) -> bool:
        return self.windows_exe.is_file()

    @property
    def needs_proton(self) -> bool:
        return IS_LINUX and self.is_windows_build

    @property
    def bepinex_dir(self) -> Path:
        return self.game_dir / "BepInEx"

    @property
    def has_bepinex(self) -> bool:
        return (self.bepinex_dir / "core").is_dir()

    @property
    def plugin_config(self) -> Path:
        return self.bepinex_dir / "config/silksongrl.cfg"

    @property
    def bepinex_log(self) -> Path:
        return self.bepinex_dir / "LogOutput.log"

    @property
    def installed_plugin(self) -> Optional[Path]:
        matches = list(self.bepinex_dir.glob("plugins/**/SilksongRL.dll"))
        return matches[0] if matches else None

    def default_launch_mode(self) -> str:
        """Proton on Linux, the exe on Windows. Raises rather than guessing wrong silently."""
        if self.needs_proton:
            steam_root = steam_root_for(self.game_dir)
            if steam_root is None:
                raise GameNotFound(
                    f"Could not find the Steam root above {self.game_dir}, which is needed to "
                    "locate Proton. Use --no-game and start the game yourself."
                )
            find_proton(steam_root, default_compat_data_path(steam_root))
            return "proton"
        if IS_WINDOWS and self.is_windows_build:
            return "direct"
        raise GameNotFound(
            f"Do not know how to launch {self.game_dir} on {platform.system()}. "
            "Use --no-game and start the game yourself."
        )

    def describe(self) -> str:
        build = "Windows build via Proton" if self.needs_proton else "Windows build"
        return f"{self.game_dir} ({build})"


# --------------------------------------------------------------------------------------
# Launching
# --------------------------------------------------------------------------------------

class GameProcess:
    """A running game. Always our own child process, in both launch modes."""

    def __init__(self, install: GameInstall, process: subprocess.Popen):
        self.install = install
        self.process = process
        self.pid = process.pid

    @property
    def is_running(self) -> bool:
        return self.process.poll() is None

    def wait(self) -> None:
        """Block until the game exits."""
        self.process.wait()

    def stop(self, timeout: float = 15.0) -> None:
        """Ask the game to close. Best effort, never escalates to SIGKILL."""
        if not self.is_running:
            return
        try:
            self.process.terminate()
        except OSError:
            return
        try:
            self.process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            pass


def steam_is_running() -> Optional[bool]:
    """
    Is a Steam client running? None if we could not tell.

    Matters because Silksong stores saves under a folder named after your Steam account id, which it
    can only learn from a running Steam client. Started with no Steam session, the game does not find
    your existing saves.
    """
    try:
        if IS_WINDOWS:
            out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq steam.exe", "/NH"],
                                 capture_output=True, timeout=30).stdout.decode("utf-8", "replace")
            return "steam.exe" in out.lower()
        result = subprocess.run(["pgrep", "-x", "steam"], capture_output=True, timeout=30)
        return result.returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return None


def ensure_steam_appid_file(install: GameInstall) -> bool:
    """
    Write steam_appid.txt next to the exe if it is missing.

    Started outside Steam, the game asks Steam to relaunch it and exits immediately. This file is
    what tells Steamworks to skip that check and run in place. Returns True if we created it.
    """
    marker = install.game_dir / "steam_appid.txt"
    if marker.is_file():
        return False
    try:
        marker.write_text(STEAM_APP_ID + "\n", encoding="ascii")
    except OSError as e:
        print(f"[Game] Could not write {marker}: {e}. "
              "The game may bounce through Steam and exit immediately.")
        return False
    print(f"[Game] Created {marker} so the game does not relaunch itself through Steam. "
          "Delete it to restore the default behaviour.")
    return True


def _launch_direct(install: GameInstall, game_args: List[str]) -> GameProcess:
    """Run the exe. Windows only, where BepInEx's doorstop is picked up without help from us."""
    if not (IS_WINDOWS and install.is_windows_build):
        raise GameNotFound(
            f"Cannot launch {install.game_dir} directly on {platform.system()}. "
            "On Linux use --launch-mode proton, or --no-game."
        )

    ensure_steam_appid_file(install)

    env = os.environ.copy()
    # Same reason as steam_appid.txt: tell Steamworks which app this is so it does not try to
    # relaunch us through Steam.
    env.setdefault("SteamAppId", STEAM_APP_ID)
    env.setdefault("SteamGameId", STEAM_APP_ID)

    cmd = [str(install.windows_exe)] + game_args
    print(f"[Game] Launching: {' '.join(cmd)}")
    process = subprocess.Popen(cmd, cwd=str(install.game_dir), env=env)
    return GameProcess(install, process)


DOORSTOP_DLL_OVERRIDE = "winhttp=n,b"


def _add_doorstop_dll_override(env: dict) -> None:
    """
    Make Wine prefer the game folder's winhttp.dll (BepInEx's doorstop) over its own builtin.
    Without this the game starts fine and completely unmodded. Same override BepInEx's Proton
    instructions put in Steam's launch options.
    """
    existing = env.get("WINEDLLOVERRIDES", "")
    if "winhttp" in existing:
        return
    env["WINEDLLOVERRIDES"] = f"{existing};{DOORSTOP_DLL_OVERRIDE}" if existing else DOORSTOP_DLL_OVERRIDE


def _launch_via_proton(install: GameInstall, game_args: List[str],
                       compat_data_path: Optional[Path] = None,
                       proton: Optional[str] = None) -> GameProcess:
    """
    Run the Windows build through Proton in a prefix we choose, mirroring what Steam runs:

        <runtime>/_v2-entry-point --verb=waitforexitandrun -- \
            <proton>/proton waitforexitandrun <game>.exe [args]
    """
    steam_root = steam_root_for(install.game_dir)
    if steam_root is None:
        raise GameNotFound(
            f"Could not find the Steam root above {install.game_dir}, which is needed to locate "
            "Proton. Use --no-game and start the game yourself."
        )

    prefix = Path(compat_data_path) if compat_data_path else default_compat_data_path(steam_root)
    proton_dir = find_proton(steam_root, prefix, proton)
    runtime = find_runtime_entry_point(steam_root)

    if not (prefix / "pfx").is_dir():
        # Proton can build one, but Steam's first run also does Windows-side setup the game expects.
        print(f"[Game] Note: {prefix} has no Wine prefix yet. Proton will create one; if the game "
              "misbehaves, launch it once through Steam first.")

    cmd: List[str] = []
    if runtime is not None:
        cmd += [str(runtime), "--verb=waitforexitandrun", "--"]
    else:
        print("[Game] Note: no Steam Linux Runtime found; running Proton outside its container.")
    cmd += [str(proton_dir / "proton"), "waitforexitandrun", str(install.windows_exe)]
    cmd += game_args

    env = os.environ.copy()
    env["STEAM_COMPAT_DATA_PATH"] = str(prefix)
    env["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = str(steam_root)
    # Steamworks needs the app id when started outside Steam, or its init can fail.
    env.setdefault("SteamAppId", STEAM_APP_ID)
    env.setdefault("SteamGameId", STEAM_APP_ID)
    env["STEAM_COMPAT_APP_ID"] = STEAM_APP_ID
    _add_doorstop_dll_override(env)

    ensure_steam_appid_file(install)

    print(f"[Game] Proton:  {proton_dir.name}")
    print(f"[Game] Prefix:  {prefix}")
    if game_args:
        print(f"[Game] Args:    {' '.join(game_args)}")

    process = subprocess.Popen(cmd, cwd=str(install.game_dir), env=env)
    return GameProcess(install, process)


def proton_smoke_test(install: GameInstall, compat_data_path: Optional[Path] = None,
                      proton: Optional[str] = None, timeout: float = 120.0) -> Tuple[bool, str]:
    """
    Check the runtime -> Proton -> prefix chain by running cmd.exe, not the game. Separates
    "Proton is misconfigured" from "the game failed".
    """
    steam_root = steam_root_for(install.game_dir)
    if steam_root is None:
        return False, f"no Steam root above {install.game_dir}"

    prefix = Path(compat_data_path) if compat_data_path else default_compat_data_path(steam_root)
    try:
        proton_dir = find_proton(steam_root, prefix, proton)
    except GameNotFound as e:
        return False, str(e)
    runtime = find_runtime_entry_point(steam_root)

    # `proton run` does not pipe the Windows process's stdout back, but it does propagate the exit
    # code, so a known value is the unambiguous success signal.
    expected = 42
    cmd: List[str] = []
    if runtime is not None:
        cmd += [str(runtime), "--verb=waitforexitandrun", "--"]
    cmd += [str(proton_dir / "proton"), "run", "cmd.exe", "/c", f"exit {expected}"]

    env = os.environ.copy()
    env["STEAM_COMPAT_DATA_PATH"] = str(prefix)
    env["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = str(steam_root)
    env.setdefault("SteamAppId", STEAM_APP_ID)
    env["STEAM_COMPAT_APP_ID"] = STEAM_APP_ID
    _add_doorstop_dll_override(env)

    try:
        result = subprocess.run(cmd, cwd=str(install.game_dir), env=env, timeout=timeout,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    except subprocess.TimeoutExpired:
        return False, f"cmd.exe did not finish within {timeout:.0f}s"
    except OSError as e:
        return False, str(e)

    if result.returncode == expected:
        return True, f"{proton_dir.name} in {prefix}"
    output = result.stdout.decode("utf-8", "replace")
    tail = "\n".join(line for line in output.splitlines()[-6:] if line.strip())
    return False, (f"expected exit {expected}, got {result.returncode}"
                   + (f". Last output:\n{tail}" if tail else ""))


def launch(install: GameInstall, mode: str = "auto", game_args: Optional[List[str]] = None,
           compat_data_path: Optional[Path] = None, proton: Optional[str] = None) -> GameProcess:
    """
    Start the game. mode is "auto", "proton" or "direct". compat_data_path picks the Wine prefix,
    which is what gives separate instances separate saves and savestates.
    """
    game_args = game_args or []
    if mode == "auto":
        mode = install.default_launch_mode()

    if mode == "proton":
        return _launch_via_proton(install, game_args, compat_data_path, proton)
    if mode == "direct":
        return _launch_direct(install, game_args)
    raise ValueError(f"Unknown launch mode '{mode}' (expected auto, proton or direct)")
