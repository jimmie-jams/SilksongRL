"""Entry point: start the training server and the game.

    python launch.py                 # server + game
    python launch.py --boss Lace_2   # ...training on Lace 2
    python launch.py --games 2       # two games, one server
    python launch.py --no-game       # server only, start the game yourself
    python launch.py --dry-run       # report what it would do and exit
    python launch.py --check-proton  # verify Proton works, without starting the game

The mod connects out to us and only retries a few times, so the server runs on a background thread
and we wait for it to bind before launching the game.
"""

import argparse
import json
import os
import sys
import threading
import time
from pathlib import Path
from typing import List, Optional

import game as game_module
from mod_config import ENCOUNTER_CHOICES, ModConfig, normalize_encounter

# Per-game files: each game's log, and under Proton the Wine prefix of games 2 and up
INSTANCES_DIR = Path(__file__).resolve().parent / "instances"

# First line RLManager.Awake logs
MOD_LOADED_LINE = "SilksongRL Mod loaded"

DEFAULT_CONFIG = {
    "host": "localhost",
    "port": 8000,
    "game_dir": None,            # required; set in server_config.json
    "autostart": True,           # skip the menus and start training straight from the savestate
}


def load_config(path: str = "server_config.json"):
    """
    Settings from server_config.json, which is gitignored - copy server_config.json.example.
    Same arrangement as SilksongRL.csproj.user on the mod side.
    """
    merged = DEFAULT_CONFIG.copy()
    if not os.path.exists(path):
        print(f"[Launcher] {path} not found. Copy server_config.json.example to create it.")
        return merged
    with open(path, "r", encoding="utf-8") as f:
        try:
            merged.update(json.load(f))
        except json.JSONDecodeError as e:
            print(f"[Launcher] Failed to parse {path}: {e}. Using defaults.")
    return merged


def build_server(host: str, port: int):
    """Return (runner, description). runner takes a ready_event and blocks."""
    from socket_server import RLSocketServer

    server = RLSocketServer(host=host, port=port)
    return server.start, f"socket server on {host}:{port}"


def apply_mod_config(install, boss, port, eval_mode, step_interval, autostart):
    """Push launcher options into the mod's config."""
    # Always written, so server_config.json decides rather than whatever the mod's config last held.
    # For the port that is what keeps the mod dialling the port the server is on.
    values = {
        ("Training", "AutoStart"): "true" if autostart else "false",
        ("Connection", "Port"): str(port),
    }
    if boss is not None:
        values[("Training", "TargetBoss")] = boss
    if eval_mode is not None:
        values[("Training", "EvalMode")] = "true" if eval_mode else "false"
    if step_interval is not None:
        values[("Training", "StepInterval")] = str(step_interval)

    if not values:
        return True

    config = ModConfig(install.plugin_config)
    changed = config.set_values(values)
    for (section, key), value in sorted(changed.items()):
        print(f"[Launcher] Set {section}.{key} = {value}")
    if not changed:
        print("[Launcher] Mod config already matched the requested settings")
    return True


def report(install, host, port, autostart):
    print(f"[Launcher] Game:      {install.describe()}")
    print(f"[Launcher] BepInEx:   {'found' if install.has_bepinex else 'NOT FOUND'}")
    plugin = install.installed_plugin
    print(f"[Launcher] Mod:       {plugin if plugin else 'SilksongRL.dll NOT FOUND in BepInEx/plugins'}")
    if plugin is not None:
        savestates = plugin.parent / "savestates"
        found = sorted(p.name for p in savestates.glob("*.json")) if savestates.is_dir() else []
        print(f"[Launcher] Savestates: {', '.join(found) if found else 'none found next to the plugin'}")
    boss = ModConfig(install.plugin_config).read("Training", "TargetBoss")
    print(f"[Launcher] Encounter: {boss if boss else 'not set (mod will use its default)'}")
    print(f"[Launcher] Server:    {host}:{port}")
    print(f"[Launcher] Autostart: {'on' if autostart else 'off (load a save and press P)'}")


def warn_about_setup(install, autostart):
    # Autostart never reads or writes a save file, so only manual runs care where saves are.
    if not autostart and game_module.steam_is_running() is False:
        print("[Launcher] WARNING: Steam does not appear to be running. Silksong finds your saves "
              "through your Steam account id, so without a Steam session it will not show them and "
              "may create a new save file instead. Start Steam first if you want your usual saves.")
    if not install.has_bepinex:
        print("[Launcher] WARNING: no BepInEx/core in the game directory - the mod will not load.")
    plugin = install.installed_plugin
    if plugin is None:
        print("[Launcher] WARNING: SilksongRL.dll is not in BepInEx/plugins - the mod will not load.")
        return
    savestates = plugin.parent / "savestates"
    if not savestates.is_dir() or not any(savestates.glob("*.json")):
        print(f"[Launcher] WARNING: no savestates found in {savestates}. "
              "Training will refuse to start without one.")


def game_log(game: int) -> Path:
    return INSTANCES_DIR / str(game) / "game.log"


def game_prefixes(install, mode: str, games: int, compat_data_path: Optional[str]) -> List[Optional[Path]]:
    """
    Wine prefix for each game, None when there are no prefixes (Windows). Game 1 uses the usual
    one, every other game its own copy of it: Proton only starts a game once everything else
    running in its prefix has exited.
    """
    if mode != "proton":
        return [None] * games
    if compat_data_path:
        first = Path(compat_data_path).expanduser()
    else:
        steam_root = game_module.steam_root_for(install.game_dir)
        if steam_root is None:
            raise game_module.GameNotFound(
                f"Could not find the Steam root above {install.game_dir}, which is needed to "
                "locate Proton. Use --no-game and start the game yourself.")
        first = game_module.default_compat_data_path(steam_root)
    return [first] + [INSTANCES_DIR / str(n) / "prefix" for n in range(2, games + 1)]


def create_missing_prefixes(prefixes: List[Path]) -> bool:
    """Copy the first game's prefix for every other game that has none yet. False on failure."""
    source = prefixes[0]
    missing = [(n, p) for n, p in enumerate(prefixes, 1) if n > 1 and not p.is_dir()]
    if not missing:
        return True
    if not (source / "pfx").is_dir():
        print(f"[Launcher] {source} has no Wine prefix to copy. Start the game through Steam once first.")
        return False
    # A copy taken while a game runs in the prefix can catch Wine halfway through writing its files
    if game_module.silksong_is_running():
        print(f"[Launcher] Close Silksong first. Game(s) {', '.join(str(n) for n, _ in missing)} "
              f"need a copy of {source}, and it is only copied while no game is running.")
        return False
    for n, dest in missing:
        print(f"[Launcher] Game {n}: copying {source} to {dest} (first time only)...")
        dest.parent.mkdir(parents=True, exist_ok=True)
        try:
            game_module.copy_prefix(source, dest)
        except OSError as e:
            print(f"[Launcher] Could not copy the prefix: {e}")
            return False
    return True


def wait_for_mod(game: int, process, log_file: Path, needs_proton: bool, timeout: float = 90.0) -> bool:
    """
    Watch the game's own log for the mod's first line. A game that starts unmodded looks exactly
    like one that started correctly, so check rather than assume.
    """
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        # Log first: the game may have loaded the mod and exited since the last look
        try:
            if MOD_LOADED_LINE in log_file.read_text(encoding="utf-8", errors="replace"):
                print(f"[Launcher] Game {game}: the mod has loaded.")
                return True
        except OSError:
            pass
        if not process.is_running:
            print(f"[Launcher] Game {game} exited before the mod loaded. If it reappears a moment "
                  "later, it relaunched itself through Steam - check that steam_appid.txt exists "
                  "in the game folder.")
            return False
        time.sleep(2.0)

    print(f"[Launcher] WARNING: game {game} has not logged the mod loading in {timeout:.0f}s "
          f"({log_file}), so it looks like it is running unmodded.")
    if needs_proton:
        print('[Launcher]          On Proton this is usually the winhttp override. We set '
              'WINEDLLOVERRIDES="winhttp=n,b" ourselves, so if it is still failing, check that '
              "BepInEx works when you launch through Steam.")
    return False


def main():
    parser = argparse.ArgumentParser(
        prog="launch.py",
        description="Start the SilksongRL training server and the game.",
    )
    parser.add_argument("--boss", "--encounter", dest="boss", metavar="NAME",
                        help=f"Encounter to train on ({', '.join(ENCOUNTER_CHOICES)}). "
                             "Written to the mod's config before launching.")
    parser.add_argument("--eval", dest="eval_mode", action="store_true", default=None,
                        help="Run the mod in evaluation mode (inference only, no training).")
    parser.add_argument("--train", dest="eval_mode", action="store_false",
                        help="Force training mode, overriding whatever the mod config says.")
    parser.add_argument("--step-interval", type=float, metavar="SECONDS",
                        help="Seconds between RL steps (mod's StepInterval).")
    parser.add_argument("--host", help="Address to bind the server to.")
    parser.add_argument("--port", type=int, help="Port to bind the server to. Also set on the mod.")
    parser.add_argument("--launch-mode", choices=("auto", "proton", "direct"), default="auto",
                        help="How to start the game. 'proton' invokes Proton, 'direct' runs the "
                             "executable. Defaults to whichever suits the install.")
    parser.add_argument("--proton", metavar="PATH",
                        help="Proton build to use. Defaults to the one that created the prefix.")
    parser.add_argument("--compat-data-path", metavar="PATH",
                        help="Wine prefix for game 1 under Proton. Defaults to the prefix Steam "
                             "uses. Games 2 and up run in copies of it (see --games).")
    parser.add_argument("--games", type=int, default=1, metavar="N",
                        help="Number of games to start, all connecting to the one server. Under "
                             "Proton each game after the first runs in its own copy of the Wine "
                             "prefix, made under instances/ the first time.")
    parser.add_argument("--check-proton", action="store_true",
                        help="Verify the Proton chain works (runs cmd.exe, not the game) and exit.")
    parser.add_argument("--no-game", action="store_true",
                        help="Only run the server; start the game yourself.")
    parser.add_argument("--dry-run", action="store_true",
                        help="Report what would happen, then exit without starting anything.")
    args = parser.parse_args()
    if args.games < 1:
        parser.error("--games must be at least 1")

    cfg = load_config()
    host = args.host or cfg.get("host", "localhost")
    port = args.port if args.port is not None else int(cfg.get("port", 8000))
    autostart = bool(cfg.get("autostart", True))

    boss = normalize_encounter(args.boss) if args.boss else None

    # Everything that touches the game needs the install; --no-game does not.
    install = None
    if not args.no_game or args.dry_run:
        try:
            install = game_module.GameInstall(
                game_module.resolve_game_dir(cfg.get("game_dir")))
        except game_module.GameNotFound as e:
            if args.no_game or args.dry_run:
                print(f"[Launcher] {e}")
            else:
                print(f"[Launcher] {e}")
                sys.exit(1)

    if install is not None:
        report(install, host, port, autostart)

    if args.check_proton:
        if install is None:
            print("[Launcher] Cannot check Proton without finding the game.")
            sys.exit(1)
        print("[Launcher] Checking the Proton chain (this runs cmd.exe, not the game)...")
        ok, detail = game_module.proton_smoke_test(
            install, args.compat_data_path, args.proton)
        print(f"[Launcher] Proton chain: {'OK' if ok else 'FAILED'} - {detail}")
        sys.exit(0 if ok else 1)

    if args.dry_run:
        current_autostart = current_port = None
        if install is not None:
            mod_config = ModConfig(install.plugin_config)
            current = mod_config.read("Training", "AutoStart")
            current_autostart = None if current is None else current.strip().lower() == "true"
            current = mod_config.read("Connection", "Port")
            current_port = None if current is None else current.strip()
        if install is not None:
            try:
                mode = (args.launch_mode if args.launch_mode != "auto"
                        else install.default_launch_mode())
                prefixes = game_prefixes(install, mode, args.games, args.compat_data_path)
            except game_module.GameNotFound as e:
                mode = None
                print(f"[Launcher] Cannot launch: {e}")
            if mode:
                print(f"[Launcher] Would launch {args.games} game(s) via: {mode}")
                for n, prefix in enumerate(prefixes, 1):
                    line = f"[Launcher]   Game {n}: log {game_log(n)}"
                    if prefix is not None:
                        copied = n > 1 and not prefix.is_dir()
                        line += f", prefix {prefix}{' (to be copied from game 1)' if copied else ''}"
                    print(line)
            print("[Launcher] Would set: " + (", ".join(
                filter(None, [
                    f"TargetBoss={boss}" if boss else None,
                    f"Port={port}" if current_port != str(port) else None,
                    f"EvalMode={args.eval_mode}" if args.eval_mode is not None else None,
                    f"StepInterval={args.step_interval}" if args.step_interval is not None else None,
                    f"AutoStart={autostart}" if current_autostart != autostart else None,
                ])) or "nothing"))
        return

    if install is not None:
        apply_mod_config(install, boss, port, args.eval_mode, args.step_interval, autostart)
        warn_about_setup(install, autostart)
    elif boss is not None:
        print("[Launcher] WARNING: could not find the game, so --boss was not applied. "
              "Set TargetBoss in silksongrl.cfg yourself.")

    runner, description = build_server(host, port)

    if args.no_game:
        print(f"[Launcher] Starting {description}")
        print("[Launcher] Start the game yourself; the mod will connect here.")
        try:
            runner()
        except KeyboardInterrupt:
            print("\n[Launcher] Interrupted, shutting down.")
        return

    try:
        mode = args.launch_mode if args.launch_mode != "auto" else install.default_launch_mode()
        prefixes = game_prefixes(install, mode, args.games, args.compat_data_path)
    except game_module.GameNotFound as e:
        print(f"[Launcher] {e}")
        sys.exit(1)
    if mode == "proton" and not create_missing_prefixes(prefixes):
        sys.exit(1)

    # Daemon thread so the accept loop does not hold the process open once the games are gone.
    ready = threading.Event()
    thread = threading.Thread(target=runner, kwargs={"ready_event": ready},
                              name="rl-server", daemon=True)
    print(f"[Launcher] Starting {description}")
    thread.start()

    if not ready.wait(timeout=30):
        print("[Launcher] Server did not start within 30s, giving up.")
        sys.exit(1)

    processes = {}
    try:
        for n, prefix in enumerate(prefixes, 1):
            log_file = game_log(n)
            log_file.parent.mkdir(parents=True, exist_ok=True)
            # Last run's log would read as this game having loaded already
            log_file.unlink(missing_ok=True)
            print(f"[Launcher] Starting game {n} of {args.games}, logging to {log_file}")
            processes[n] = game_module.launch(install, mode=mode, compat_data_path=prefix,
                                              proton=args.proton, log_file=log_file)
            # Waiting also staggers the games, which share BepInEx's startup cache
            loaded = wait_for_mod(n, processes[n], log_file, install.needs_proton)
            if not loaded and not processes[n].is_running:
                del processes[n]  # wait_for_mod has reported it

        if processes:
            print("[Launcher] Waiting for the games to exit (Ctrl-C to stop the server).")
        while processes:
            for n in [n for n, p in processes.items() if not p.is_running]:
                print(f"[Launcher] Game {n} exited.")
                del processes[n]
            time.sleep(1.0)
        print("[Launcher] No games left running, shutting down.")
    except game_module.GameNotFound as e:
        print(f"[Launcher] {e}")
        sys.exit(1)
    except KeyboardInterrupt:
        print("\n[Launcher] Interrupted, shutting down.")
        # Leave the games running: killing one mid-episode while the save file is being
        # written is not worth the risk.
        if any(p.is_running for p in processes.values()):
            print("[Launcher] The games are still running; close them when you are ready.")


if __name__ == "__main__":
    main()
