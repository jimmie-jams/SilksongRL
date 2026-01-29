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

   - Copy the built `SilksongRL.dll` and from `unity-mod/SilksongRL/bin/Debug/` (or `bin/Release/` if you built in Release configuration) to your game's `BepInEx/plugins/` directory (or the realease if you downloaded that instead)


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

4. **Run the socket server:**
   ```bash
   python launch.py
   ```

## Running the System

Please consult [HOW_TO_TRAIN.md](/HOW_TO_TRAIN.md)


## Future plans

- **Screen Capture overhaul**: Currently, visual observations are captured from the entire screen. This includes UI. I'm sure there's a way to have it ignore the UI entirely. This would be very beneficial, as we can keep Debug UI on and also don't have to worry about whether we're capturing Hornet's masks or anything else.

- **Ping meter**: Just a small (toggleable?) counter on some corner of the screen. Would be helpful to understand the performance on different machines and different TimeScales.

- **Action visualization**: Either with a simple custon keyviz-like visualizer or something that looks like NN nodes with the selected actions flashing (Honestly, not sure if this will look too great because of just how often actions are taken but we'll see).

- **Savage Beastfly overhaul**: Savage Beastfly has summons but those are not explicitly accounted for by either the state or reward definition. For the state this may be fine as the agent can still see them through the visual component, but reward should definitely be given on hitting/killing them.

- **Link in game name to "real" name**: Bosses have an in game name that is usually not the same as the one people might be familiar with. For instance, Savage Beatsfly's name in the code is Bone Flyer Giant. It may be a good idea to link the in game name to the expected name and only show that to people so they don't get confused.

- **Configurable key bindings**: The agent plays by pressing buttons. Many people do not use the default bindings so if they want to use this they'd have to change them to the default and then back so they can play. Either the key bindings should be manually configurable by the player in silksongrl.cfg or, even better, it should automatically detect the user's keybinds and use those. 

- **Untie reward saving from checkpoints**: The rewards a model gets during training (and episode count, times trained count etc.) are saved within the checkpoint itself. I honestly don't remember *why* I did it like that, maybe I wanted to keep things more compact. At any rate, that seems silly to me right now. A separate json to store and load this info would probably be better (?) and would also mean that the monstrosity that is the load function override can be removed.

- **Named checkpoints**: Probably should have the actual checkpoint zip have a name rather than simply being called checkpoint. (Lace1.zip, Lace2.zip etc.)

- **More bosses**: Adding new bosses is always on the menu. Check out [this PR](https://github.com/jimmie-jams/SilksongRL/pull/2) to get an idea of how it's done. The general idea is you simply need to implement the IBossEncounter interface for another boss.

- **More algorithms**: Not too high priority for now, but trying out more RL algorithms would be cool.




