# SilksongRL

Reinforcement learning system for training AI agents to play Hollow Knight: Silksong boss encounters.

## Encounters

### (✅= Defeated, 🔲= Pending)



<br>

✅ [**Lace 1**](https://www.youtube.com/watch?v=TSNdgidVWeY) 

([model checkpoint](https://drive.google.com/drive/folders/1cKgxRb4KAvJV66gcvnV-Ai7aePXuyngR?usp=sharing))

<img width="1920" height="1080" alt="THUMBNAIL" src="https://github.com/user-attachments/assets/5babba46-4ce9-4d57-9888-58e99de16125" />

🔲 **Lace 2**

🔲 **Savage Beastfly**

## Overview

This project combines a Unity mod with a Python-based RL training pipeline to teach agents how to fight bosses using PPO (Proximal Policy Optimization). Still working on extending this to other RL algorithms.

**Components:**
- **unity-mod/** - BepInEx mod that hooks into Silksong, captures game state, and executes agent actions
- **python-client/** - Socket server that runs training for models and provides action predictions
- **savestates/** - The savestates each encounter resets to

## Architecture

The Unity mod communicates with the Python socket server:
1. Game state (observations) is sent from Unity to the Python Client
2. The trained model predicts actions based on the current state
3. Actions are executed in-game and rewards are calculated
4. Training data is collected for model improvement

## Set up Instructions

### Prerequisites to run training:

- **Hollow Knight: Silksong** (game installation)
- **BepInEx 5.4.x** in your Silksong directory (https://thunderstore.io/c/hollow-knight-silksong/p/BepInEx/BepInExPack_Silksong/)
- **Debug Mod** in your BepInEx plugins folder (https://github.com/hk-speedrunning/Silksong.DebugMod)
- **Python 3.11**

### (Optional) If you want to build the mod yourself as well you'll also need:

- **.NET Framework 4.7.2** 
- **Build system that supports MSBuild projects** (e.g. Visual Studio)


### (Optional) Building the Unity Mod

1. **Configure your game directory:**
   ```bash
   cd unity-mod/SilksongRL

   # PowerShell/Unix:
   cp SilksongRL.csproj.user.example SilksongRL.csproj.user
   # CMD:
   copy SilksongRL.csproj.user.example SilksongRL.csproj.user
   ```

2. **Edit `SilksongRL.csproj.user`** and set your game installation path:
   ```xml
   <GameDir>YOUR_PATH_HERE\Hollow Knight Silksong</GameDir>
   ```
   Path Examples:
   - Steam (Windows): `C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight Silksong`
   - Steam (custom drive): `D:\Steam\steamapps\common\Hollow Knight Silksong`
   - GOG: `C:\GOG Games\Hollow Knight Silksong`
   - Epic Games: `C:\Program Files\Epic Games\Hollow Knight Silksong`

3. **Build the project:**
   
   In Visual Studio:
   - Open `unity-mod/SilksongRL.sln`
   - Build Solution (Ctrl+Shift+B)


### Installing the mod

   - Copy the built `SilksongRL.dll` from `unity-mod/SilksongRL/bin/Debug/` (or `bin/Release/` if you built in Release configuration) into a folder of its own under your game's `BepInEx/plugins/` directory (or the release if you downloaded that instead)
   - Copy the `savestates/` folder in next to the DLL. The mod looks for its savestates there:

   ```
   BepInEx/plugins/SilksongRL/SilksongRL.dll
   BepInEx/plugins/SilksongRL/savestates/lace_1.json
   ```


### Setting Up the Python Client

1. **Navigate to the Python Client directory:**
   ```bash
   cd python-client
   ```

2. **Create a virtual environment (recommended):**
   ```bash
   python -m venv venv
   venv\Scripts\activate # On Unix: source venv/bin/activate
   ```

3. **Install dependencies:**
   ```bash
   pip install -r requirements.txt
   ```

4. **Create your config:**
   ```bash
   cp server_config.json.example server_config.json
   ```
   and set `game_dir` to your Silksong install directory. `server_config.json` is gitignored, like
   `SilksongRL.csproj.user` on the mod side.

5. **Start the server and the game:**
   ```bash
   python launch.py --boss Lace_1
   ```
   The launcher points the mod at the encounter you asked for, starts the training server and
   launches the game in the right order. `python launch.py --dry-run` reports
   what it found without starting anything, and `--no-game` runs just the server if you would rather
   start the game yourself.

## Running the System

Please consult [HOW_TO_TRAIN.md](/HOW_TO_TRAIN.md)


## Future plans

- **Action visualization**: Either with a simple custon keyviz-like visualizer or something that looks like NN nodes with the selected actions flashing (Honestly, not sure if this will look too great because of just how often actions are taken but we'll see).

- **Savage Beastfly overhaul**: Savage Beastfly has summons but those are not explicitly accounted for by either the state or reward definition. For the state this may be fine as the agent can still see them through the visual component, but reward should definitely be given on hitting/killing them.

- **Configurable key bindings**: The agent plays by pressing buttons. Many people do not use the default bindings so if they want to use this they'd have to change them to the default and then back so they can play. Either the key bindings should be manually configurable by the player in silksongrl.cfg or, even better, it should automatically detect the user's keybinds and use those. 

- **Game speed setting**: Training usually runs at a raised game speed, which currently has to be set through Debug Mod in every game window. With several games running at once that gets tedious and is easy to get wrong, so the speed should be a setting in silksongrl.cfg that the mod applies when the game starts.

- **Pausing during updates**: While the model trains on a batch of steps, every game keeps playing on its last action until the update finishes (a second or two). The steps around that gap are a bit off, so pausing the games until the server replies would be cleaner.

- **Frame stacking**: The visual observation is a single frame, so the agent sees where things are but not where they're going. The vector part covers the velocities of Hornet and the boss (and, for Lace 2, which attack she's in and how far into it she is), which may well be enough. If it isn't, stacking the last few frames is the usual fix. It's easy to do on the server per game, but it multiplies the memory the images take up.

- **More bosses**: Adding new bosses is always on the menu. Check out [this PR](https://github.com/jimmie-jams/SilksongRL/pull/2) to get an idea of how it's done. The general idea is you simply need to implement the IBossEncounter interface for another boss. To work out a boss's attacks, set `LogBossStates = true` in silksongrl.cfg and play the fight: every state change of the boss's FSMs gets logged (see [HOW_TO_TRAIN.md](/HOW_TO_TRAIN.md)).

- **More algorithms**: Not too high priority for now, but trying out more RL algorithms would be cool.




