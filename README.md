Overview

NOReplay is a BepInEx mod for Nuclear Option. It records a match to an ACMI file and plays that file back inside the game, on the real map, with the game's own aircraft, vehicles, ships and buildings.

Playback does not simulate the battle again. The match is already over. The mod loads a generated mission so the map and units exist, then moves those units along the recorded track. Weapons, flares and gun bursts are shown from the recording, not from the AI.

Boscali stays blue, Primeva stays red, neutral stays neutral. A unit appears when the ACMI says it is active and disappears when the ACMI says it is gone.

---

Install

* Install BepInEx for Nuclear Option.
* Build the project, or take the built NOReplay.dll.
* Put NOReplay.dll in BepInEx/plugins.
* Start the game once. The log NOReplay.log is written next to the dll.

---

Controls & Usage

Record
* In a normal match, press F8 to start recording. Rec appears at the bottom right.
* Press F8 again to stop. The ACMI file is written to the replay folder.
Recording is off in the main menu and during playback.

Open a replay
* In the main menu, click Replay. The button is only on that screen.
* The list shows ACMI files from the replay folder. The folder path is at the top of the window. Set it once. The mod remembers it.
* Click a file. It is converted to NOReplay.json and that mission starts. There is no second confirm button.
* Leave the mission and the timeline closes. The Replay button is back in the main menu.

Timeline
* The timeline sits at the top center while a replay is running.
* Play runs the track. Pause holds it.
* Drag the marker to scrub. Units jump to that time. Dragging backward shows the reverse motion.
* Speeds: -4x -2x -1x -0.5x -0.25x 0.25x 0.5x 1x 2x 4x.
* F10 hides the timeline. F10 shows it again.

Camera
* The normal spectator path is unchanged. Pick a faction screen, then spectator, or click a unit.
* Orbit around a unit works. Free camera and fly-by work (45%).
* If the unit you are watching is destroyed, the camera is released before the unit is removed.
* Clicking a missile follows that missile. The camera does not jump to a missile on its own.

---

What you see
* Aircraft, helicopters, vehicles, ships, buildings and scenery from the ACMI.
* The pilot name from the callsign, in the info panel. State reads Replay. Speed is taken from the recording, metric or imperial as the game is set.
* Loadout and livery from the recording, including empty pylons.
* Landing gear from the ACMI LandingGear flag. Doors follow the gear.
* Rotors, propellers and the engine sound of the unit you are near.
* Flares from the left and right ejectors of the airframe.
* Missiles and bombs in the world and on the map, in the faction of the shooter, with launch and flight effect.
* Gun bursts as tracers from the firing unit, with the weapon's loop sound while the burst is active.

---

How it works

Record
* F8 toggles a recorder in the live match. It writes Tacview-style ACMI: unit id, parent, coalition, position samples, loadout, livery, and gun-burst windows (who fired, from when to when, muzzle speed, spread). Shells are not stored one by one. A burst is enough to rebuild the stream on playback.
* The recorder does not run during a replay. Replay patches that mute AI do not run in a normal match, so leaving the aircraft and normal firing stay intact.

Load
* Choosing a file parses the ACMI into a document: one track per object, samples in time, spawn time, death time. Units that exist at the start go into NOReplay.json. Units that appear later stay in a queue. The JSON is a normal Nuclear Option mission, so the game loads the map, buildings and units through its own spawner. Heartland and Igus Archipelago come from the map id in the ACMI.
* After the scene is up, each spawned unit gets a ghost. The ghost is the only thing that moves it.

Playback
* The clock is the timeline. Each frame the ghost reads the sample at the current time and writes position and rotation. Physics, joints and AI flight are held off, otherwise the airframe tears itself apart or taxis away on its own.
* A unit whose spawn time is still ahead is hidden. At that time it is shown, already at the recorded pose. At its death time the ghost releases the camera if needed, hides the map icon, and disables the object. Scrubbing backward enables it again.
* Neutral units are drawn and not driven. No rotor, no sound, no exhaust.

Weapons
* Missiles are spawned through the game's missile spawner when their sample begins, then moved by a ghost. The faction is the parent's faction. At the end of the track the explosion effect is played and the object is removed.
* Flares instantiate the airframe's own flare prefab and call the game's launch on the next ejector point, left then right.
* Gun bursts spawn a short-lived tracer from the shooter toward the recorded aim, at the weapon's rate, for the length of the burst. One loop sound starts with the burst and stops at the end. Only sounds near the listener play.

---

Files
* NOReplay.log next to the dll. BepInEx still writes its own LogOutput.log.
* Replay folder: the path in the Replay window. Default is the game's Replays folder under AppData/LocalLow/Shockfront/NuclearOption/Replays.
* Missions/NOReplay/NOReplay.json is overwritten on every playback start.

---

Limits
* The ACMI time step is about a fifth of a second. Several missiles fired inside that window share one timestamp, so a salvo appears as a short burst of spawns rather than millisecond gaps.
* Shells are rebuilt from the burst, not from every projectile in the file. The first tracer leaves the shooter. The line is a stand-in for the round, not the game's bullet.
* Old ACMI files from before this recorder have no loadout, livery or gun-burst lines. Playback still moves units. Those extras stay empty.
