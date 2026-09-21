# HOW TO TRAIN

Here I will give an overview of how to actually run the training. This assumes you have followed the set up instructions in the README.


## INITIALIZE THE SOCKET

To initialize the socket which enables the communication between Silksong and the training script you need to run the launch.py file from the python-client.

You can run this however you like.
Personally, I just do `start /B "" .venv\Scripts\python.exe launch.py` in the directory where the python-client is to start it as a background process.


<img width="1919" height="1030" alt="image" src="https://github.com/user-attachments/assets/9e28422a-e8ec-47f7-8224-3711fb7bcd86" />


## START THE GAME

Once you start the game, you should see the BepInEx console also open alongside it. If everything so far has gone correctly,
top right should list the mods you have active and in the console you will be able to see whichever encounter you have initialized the system for.
The default starting encounter is Lace 1.

<img width="1627" height="920" alt="image" src="https://github.com/user-attachments/assets/b93667c3-0667-45de-b8fa-effa3ff2c473" />

<br>
<br>

> **Train on a save file you do not care about.** Every episode reset loads a savestate, and loading
> a savestate overwrites the active save file with no undo - this is how DebugMod savestates work and
> has nothing to do with SilksongRL specifically. Over a training run this happens thousands of times.
> Make a throwaway save file for training and keep your real one well away from it.

Once you open a savefile, the DebugMod overlay should appear, which looks like this (if it isn't on by default press F2 to activate).
You do not need to set your masks or equipment up - the savestate the agent resets to already has ten masks and the right loadout baked in.

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

Press F2 to close the DebugMod UI (this is not important for Lace 1, but other encounters, such as Lace 2 use visual state information, so the the UI will mess with their performance)

Unpause and press P! You should see your agent start to move on it's own. It's training now! Wish it luck, because it's certainly going to need it.


## CHANGING ENCOUNTERS

If you wish to try out another boss, head to your game installation, get inside `BepInEx/config`, open the silksongrl.cfg file
and set TargetBoss to the one you want.

<img width="1906" height="889" alt="image" src="https://github.com/user-attachments/assets/a458dafc-31d3-4614-8197-fab1c29a3c28" />


### NOTES:

- You can increase the timescale throught Debug mod and the training will function fine as the steps are executed in Unity's FixedUpdate.

- If you want to load saved model weights, you will need to first run the game once with the desired boss selected to create the folder structure. Then, put checkpoint.zip into the `models/"boss_name"/` folder. It should now load the trained agent when you run it.

- As this system runs in real time rather than assuming full control of the game, there will be slight deviations in the latency with which things run on different machines. This shouldn't cause too big of an issue. That being said, performance may degrade slightly if we try a model that is used to a certain amount of ms on an environment with less or more.

- To play, the agent relies on key presses. This means that if you don't have the default key bindings, it will be pressing the wrong buttons.

- For encounters that have a visual observation, your resolution and video settings matter. On a different aspect ratio or with more/less particles, shadows etc. the agent's input will not be the same. I'm not quite sure how catastrophic this would be for performance but it will most definitely have an effect. 
