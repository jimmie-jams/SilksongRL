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

Once you open a savefile, the DebugMod overlay should appear, which looks like this (if it isn't on by default press F2 to activate).
Make sure to give yourself ten masks, as the health normalization is set up to work with that many. It's not going to break if you don't do that but I would advise against it. Extra health means longer episodes means more learning.

<br>


<img width="1614" height="910" alt="image" src="https://github.com/user-attachments/assets/5e0f7e9b-39e0-43a4-97a6-18af922876d8" />

<br>
<br>

<img width="1617" height="914" alt="image" src="https://github.com/user-attachments/assets/f04117ca-20db-48b3-8bcc-10ae44b7bf26" />


## NAVIGATE TO THE DESIRED ENCOUNTER

Unfortunately, you'll have to get to the boss you want to fight manually. Only the first time, though! Promise. The DebugMod has a very helpful noclip mode that you can use to phase through walls and get to where you want quickly.
(In the future, I might look into providing ready SaveState information so you can open it straight up from there but if you're reading this it's not available yet)


Once you have reached to the boss you need to get into a position that triggers the fight, pause and set your SaveState by pressing "Write", as shown below. I am using Lace 2 as an example here but this is the same for every boss.

<img width="1611" height="905" alt="image" src="https://github.com/user-attachments/assets/e0ee151a-b4c6-4fb5-8499-7ebd5d27716c" />

<br>
<br>

Congratulations, you now have a SaveState! These persist across runs, so any time you want to return to this encounter you can do it with just a few clicks.
If you now press "Read", it sets the selected SaveStatestate into your Quickslot.

<img width="1607" height="896" alt="image" src="https://github.com/user-attachments/assets/0ae8f443-b798-48b5-931f-26e1a2e53b67" />

<br>
<br>

Enable the "Load Quickslot on Death" setting by pressing the highlighted button. 

<img width="1608" height="907" alt="image" src="https://github.com/user-attachments/assets/bb6e86ae-151e-4c56-81b2-e0969ee6f587" />

<br>
<br>

Bind Quickslot (Load) to F5. This is how we reset when the agent wins.

<img width="1616" height="913" alt="image" src="https://github.com/user-attachments/assets/5dcba72d-99ef-48ac-a3b2-1c54bfc77185" />

## AND... TRAIN!

Press F2 to close the DebugMod UI (this is not important for Lace 1, but other encounters, such as Lace 2 use visual state information, so the the UI will mess with their performance)

Unpause and press P! You should see your agent start to move on it's own. It's training now! Wish it luck, because it's certainly going to need it.



