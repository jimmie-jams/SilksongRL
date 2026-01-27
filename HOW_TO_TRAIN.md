# HOW TO TRAIN (WIP)

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

Unfortunately, you'll have to get to the boss you want to fight manually. Only the first time, though! Promise.
(In the future, I might look into providing ready SaveState information so you can open it straight up from there but if you're reading this it's not available yet)


