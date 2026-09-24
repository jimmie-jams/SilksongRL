# HOW TO TRAIN

Here I will give an overview of how to actually run the training. This assumes you have followed the set up instructions in the README.


## START EVERYTHING

From `python-client`, one command starts the training server and the game:

```bash
python launch.py --boss Lace_1
```

`--boss` writes `TargetBoss` into the mod's config before launching, so you no longer need to edit
`silksongrl.cfg` by hand to change encounter. Accepted names are `Lace_1`, `Lace_2` and
`Savage_Beastfly` (spellings like `lace2` or `beastfly` work too).

The server has to be listening before the game starts, because the mod connects out to it and only
retries a few times. The launcher handles that ordering for you - it waits for the socket to bind
before starting the game.

To see what it would do without starting anything:

```bash
python launch.py --dry-run --boss Lace_2
```

```
[Launcher] Game:      /path/to/Hollow Knight Silksong (Windows build via Proton)
[Launcher] BepInEx:   found
[Launcher] Mod:       .../BepInEx/plugins/SilksongRL/SilksongRL.dll
[Launcher] Savestates: lace_1.json, lace_2.json
[Launcher] Encounter: Lace_1
[Launcher] Transport: socket_sync on localhost:8000
[Launcher] Would launch via: steam
[Launcher] Would set: TargetBoss=Lace_2
```

That also warns you if BepInEx, the mod, or the savestates are missing, which is usually the reason
a run does not start.

### Useful options

| Option | Effect |
|--------|--------|
| `--boss NAME` | Encounter to train on |
| `--eval` / `--train` | Evaluation (inference only) or training mode |
| `--step-interval S` | Seconds between RL steps |
| `--port N` | Server port, set on both sides |
| `--no-game` | Only run the server; start the game yourself |
| `--dry-run` | Report and exit |
| `--launch-mode` | `proton`, `steam` or `direct`, if the automatic choice is wrong |
| `--check-proton` | Verify Proton works without starting the game |

The launcher needs to be told where Silksong is installed, the same way the mod project is told
through `<GameDir>` in `SilksongRL.csproj.user`. Copy the example and fill in `game_dir`:

```bash
cp server_config.json.example server_config.json
```

```json
{
    "game_dir": "/home/you/.steam/steam/steamapps/common/Hollow Knight Silksong",
    "autostart": true,

    "transport": "socket_sync",
    "host": "localhost",
    "port": 8000
}
```

`server_config.json` is gitignored, so your paths and ports stay out of commits.

### Launching a Proton install

If your install runs under Proton, the launcher drives Proton itself, using the build that created
your prefix. Steam does not need to be open. You can check it without starting the game:

```bash
python launch.py --check-proton
# [Launcher] Proton chain: OK - Proton 9.0 (Beta) in .../compatdata/1030300
```

`--launch-mode direct` runs the executable instead, which is the default on Windows. If neither suits
your setup, `--no-game` runs just the server and you start the game however you like.

Under Proton the game's save data lives *inside the Wine prefix*, so both your save file and
DebugMod's savestates do too. `--compat-data-path` selects a different prefix, which is what will keep
parallel instances from trampling each other's saves.

If you would rather start the game yourself, use `--no-game` and the launcher behaves as it used to -
just the server, waiting for the mod to connect.

Closing the game shuts the launcher down. Ctrl-C stops the server and leaves the game running, since
killing it mid-episode while the save file is being written is not worth the risk.

## START THE GAME

Once you start the game, you should see the BepInEx console also open alongside it. If everything so far has gone correctly,
top right should list the mods you have active and in the console you will be able to see whichever encounter you have initialized the system for.
The default starting encounter is Lace 1.

<img width="1627" height="920" alt="image" src="https://github.com/user-attachments/assets/b93667c3-0667-45de-b8fa-effa3ff2c473" />

<br>
<br>

With `autostart` on (the default), the game skips the intro and title screen and boots straight from
the encounter's savestate - you never open a save file. Training starts on its own.

Your save files are safe either way. Once SilksongRL has loaded a savestate, the game's in-memory state
is training state, so the mod stops the game saving until it closes rather than let an autosave write
that over a real save slot.

The DebugMod overlay looks like this (press F2 to toggle it). You do not need to set up masks or
equipment - the savestate already has ten masks and the right loadout.

<br>


<img width="1614" height="910" alt="image" src="https://github.com/user-attachments/assets/5e0f7e9b-39e0-43a4-97a6-18af922876d8" />

<br>
<br>

<img width="1617" height="914" alt="image" src="https://github.com/user-attachments/assets/f04117ca-20db-48b3-8bcc-10ae44b7bf26" />


## SAVESTATES

Episode resets load a savestate. SilksongRL ships its own, as JSON in a `savestates` folder that sits
next to `SilksongRL.dll`:

```
BepInEx/plugins/SilksongRL/SilksongRL.dll
BepInEx/plugins/SilksongRL/savestates/lace_1.json
```

These are separate from DebugMod's savestate slots, so your own savestates, quickslot and DebugMod
settings are never touched and you can keep using DebugMod normally while training runs.

There is nothing to set up by hand. You do **not** need to press "Read", bind Quickslot (Load) to F5,
or enable "Load Quickslot on Death" - the mod loads the savestate itself for agent deaths, wins and
stuck resets alike. On startup the console confirms which one it picked up:

```
[Info   : SilksongRL] [SaveStates] Hooked DebugMod savestate loader (version 1.1.2)
[Info   : SilksongRL] [RL] Resetting to "Lace_1_RL" from .../savestates/lace_1.json
```

If you see an error instead, training will refuse to start - episodes could never reset without a
savestate. The error says whether the file was missing or DebugMod's loader could not be reached. To
use a savestate from somewhere else, point `SavestateFile` in `BepInEx/config/silksongrl.cfg` at it.

## AND... TRAIN!

With `autostart` on there is nothing to press - the agent starts moving on its own. It's training now!
Wish it luck, because it's certainly going to need it.

Press F2 to close the DebugMod UI. This matters for encounters with visual observations, such as
Lace 2, where the UI would end up in what the agent sees.

With `"autostart": false`, load any save and press P. P arms the run: agent control starts once the
game is ready, so pressing it on a loading screen or during a cutscene is fine - it waits. Press P again
to stop.


## CHANGING ENCOUNTERS

Pass a different `--boss` to the launcher:

```bash
python launch.py --boss Savage_Beastfly
```

That writes `TargetBoss` into `BepInEx/config/silksongrl.cfg` for you. You can still edit that file
by hand if you prefer - the launcher only touches the settings you pass it on the command line, and
leaves the rest of the file, comments included, alone.


### NOTES:

- You can increase the timescale throught Debug mod and the training will function fine as the steps are executed in Unity's FixedUpdate.

- If you want to load saved model weights, put the checkpoint in `python-client/models/<boss>/`, named `<boss>_<attempts>.zip`, where `<boss>` is what you pass to `--boss` (e.g. `models/Lace_1/Lace_1_2400.zip`). If there are several, the one with the most attempts is loaded. Each new save replaces the previous checkpoint, except every 1000 attempts (`Lace_1_1000.zip`, `Lace_1_2000.zip`, ...), which are kept.

- As this system runs in real time rather than assuming full control of the game, there will be slight deviations in the latency with which things run on different machines. This shouldn't cause too big of an issue. That being said, performance may degrade slightly if we try a model that is used to a certain amount of ms on an environment with less or more.

- To play, the agent relies on key presses. This means that if you don't have the default key bindings, it will be pressing the wrong buttons.

- For encounters that have a visual observation, your resolution and video settings matter. On a different aspect ratio or with more/less particles, shadows etc. the agent's input will not be the same. I'm not quite sure how catastrophic this would be for performance but it will most definitely have an effect. 
