# SilksongRL

Reinforcement learning system for training AI agents to play Hollow Knight: Silksong boss encounters.

## Encounters

### (✅= Defeated, 🔲= Pending)


<br>

✅ [**Lace 1**](https://www.youtube.com/watch?v=TSNdgidVWeY)

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

### Running the System

0. On first run, the mod will create a config file in `BepInEx/config` with default
target boss Lace 1. Open the file and edit to the desired encounter.
1. Start the Python socket server (as described above)
2. Launch Hollow Knight: Silksong
3. The mod will automatically connect to the server
4. Navigate to selected boss encounter in-game and set your save state in the arena through the Debug mod. Activate Load State on Death option.
5. Press P to hand over control to the agent.

Note:
Boss fight triggers are different from boss to boss so you might need to check {Boss}Encounter.cs to figure out how to actually begin the fight. For now you may need to manually begin the first episode and then hand over control. After that the training should continue on its own. This is a limitation of relying on the Debug mode for resetting.


