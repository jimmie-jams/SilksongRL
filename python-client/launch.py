"""Entry point: start the training server and the game.

    python launch.py                 # server + game
    python launch.py --boss Lace_2   # ...training on Lace 2
    python launch.py --no-game       # server only, start the game yourself
    python launch.py --dry-run       # report what it would do and exit
    python launch.py --check-proton  # verify Proton works, without starting the game

The mod connects out to us and only retries a few times, so the server runs on a background thread
and we wait for it to bind before launching the game.
"""

import argparse
import asyncio
import json
import os
import sys
import threading
import time

import game as game_module
from mod_config import ENCOUNTER_CHOICES, ModConfig, normalize_encounter

DEFAULT_CONFIG = {
    "transport": "socket_sync",  # "socket_sync", "socket_async"
    "host": "localhost",
    "port": 8000,
    "game_dir": None,            # required; set in server_config.json
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


def build_server(transport: str, host: str, port: int):
    """Return (runner, description). runner takes a ready_event and blocks."""
    if transport == "socket_sync":
        from socket_server import RLSocketServer

        server = RLSocketServer(host=host, port=port)
        return server.start, f"sync socket server on {host}:{port}"

    if transport == "socket_async":
        from socket_server_async import AsyncRLSocketServer

        server = AsyncRLSocketServer(host=host, port=port)
        return (lambda ready_event=None: asyncio.run(server.start(ready_event))), \
               f"async socket server on {host}:{port}"

    print(f"[Launcher] Unknown transport '{transport}'. Expected one of socket_sync|socket_async.")
    sys.exit(1)


def apply_mod_config(install, boss, port, eval_mode, step_interval):
    """Push launcher options into the mod's config."""
    values = {}
    if boss is not None:
        values[("Training", "TargetBoss")] = boss
    if port is not None:
        values[("Connection", "Port")] = str(port)
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


def report(install, transport, host, port):
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
    print(f"[Launcher] Transport: {transport} on {host}:{port}")


def warn_about_setup(install):
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


def wait_for_mod(install, process, timeout: float = 90.0) -> bool:
    """
    Watch BepInEx's log for signs of life. A game that starts unmodded looks exactly like one that
    started correctly, so check rather than assume.
    """
    log = install.bepinex_log
    before = log.stat().st_mtime if log.is_file() else 0.0

    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not process.is_running:
            print("[Launcher] Game exited before the mod loaded.")
            return False
        if log.is_file() and log.stat().st_mtime > before:
            print("[Launcher] BepInEx is writing its log; the mod is loading.")
            return True
        time.sleep(2.0)

    print(f"[Launcher] WARNING: {log} has not been touched in {timeout:.0f}s, so BepInEx does not "
          "look like it loaded and the game is running unmodded.")
    if install.needs_proton:
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
    parser.add_argument("--transport", choices=("socket_sync", "socket_async"),
                        help="Server implementation to use.")
    parser.add_argument("--launch-mode", choices=("auto", "proton", "direct"), default="auto",
                        help="How to start the game. 'proton' invokes Proton, 'direct' runs the "
                             "executable. Defaults to whichever suits the install.")
    parser.add_argument("--proton", metavar="PATH",
                        help="Proton build to use. Defaults to the one that created the prefix.")
    parser.add_argument("--compat-data-path", metavar="PATH",
                        help="Wine prefix for --launch-mode proton. Defaults to the prefix Steam "
                             "uses. The save file and DebugMod's savestates live inside it, so a "
                             "separate prefix is what isolates one instance from another.")
    parser.add_argument("--check-proton", action="store_true",
                        help="Verify the Proton chain works (runs cmd.exe, not the game) and exit.")
    parser.add_argument("--no-game", action="store_true",
                        help="Only run the server; start the game yourself.")
    parser.add_argument("--dry-run", action="store_true",
                        help="Report what would happen, then exit without starting anything.")
    args = parser.parse_args()

    cfg = load_config()
    transport = (args.transport or cfg.get("transport", "socket_sync")).lower()
    host = args.host or cfg.get("host", "localhost")
    port = args.port if args.port is not None else int(cfg.get("port", 8000))

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
        report(install, transport, host, port)

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
        if install is not None:
            try:
                mode = (args.launch_mode if args.launch_mode != "auto"
                        else install.default_launch_mode())
            except game_module.GameNotFound as e:
                mode = None
                print(f"[Launcher] Cannot launch: {e}")
            if mode:
                print(f"[Launcher] Would launch via: {mode}")
            if mode == "proton":
                steam_root = game_module.steam_root_for(install.game_dir)
                prefix = args.compat_data_path or (
                    game_module.default_compat_data_path(steam_root) if steam_root else "?")
                print(f"[Launcher] Would use prefix: {prefix}")
            print("[Launcher] Would set: " + (", ".join(
                filter(None, [
                    f"TargetBoss={boss}" if boss else None,
                    f"Port={port}" if args.port is not None else None,
                    f"EvalMode={args.eval_mode}" if args.eval_mode is not None else None,
                    f"StepInterval={args.step_interval}" if args.step_interval is not None else None,
                ])) or "nothing"))
        return

    if install is not None:
        apply_mod_config(install, boss, port if args.port is not None else None,
                         args.eval_mode, args.step_interval)
        warn_about_setup(install)
    elif boss is not None:
        print("[Launcher] WARNING: could not find the game, so --boss was not applied. "
              "Set TargetBoss in silksongrl.cfg yourself.")

    runner, description = build_server(transport, host, port)

    if args.no_game:
        print(f"[Launcher] Starting {description}")
        print("[Launcher] Start the game yourself; the mod will connect here.")
        try:
            runner()
        except KeyboardInterrupt:
            print("\n[Launcher] Interrupted, shutting down.")
        return

    # Daemon thread so the accept loop does not hold the process open once the game is gone.
    ready = threading.Event()
    thread = threading.Thread(target=runner, kwargs={"ready_event": ready},
                              name="rl-server", daemon=True)
    print(f"[Launcher] Starting {description}")
    thread.start()

    if not ready.wait(timeout=30):
        print("[Launcher] Server did not start within 30s, giving up.")
        sys.exit(1)

    process = None
    try:
        process = game_module.launch(install, mode=args.launch_mode,
                                     compat_data_path=args.compat_data_path,
                                     proton=args.proton)
        wait_for_mod(install, process)
        print("[Launcher] Waiting for the game to exit (Ctrl-C to stop the server).")
        process.wait()
        print("[Launcher] Game exited, shutting down.")
    except game_module.GameNotFound as e:
        print(f"[Launcher] {e}")
        sys.exit(1)
    except KeyboardInterrupt:
        print("\n[Launcher] Interrupted, shutting down.")
        # Leave the game running: killing it mid-episode while the save file is being
        # written is not worth the risk.
        if process is not None and process.is_running:
            print("[Launcher] The game is still running; close it when you are ready.")


if __name__ == "__main__":
    main()
