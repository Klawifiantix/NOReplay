First of all, I would like to thank KopterBuzz for creating the NoBlackBoxMod. Without his tremendous achievement, I likely would never have begun developing the ReplayMod.
https://github.com/KopterBuzz | https://github.com/KopterBuzz/NOBlackBox

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
* Trails button toggles flight paths. The slider next to it sets trail length from 0 to 10 minutes. Dead airframes lose their trail.

Camera
* The normal spectator path is unchanged. Pick a faction screen, then spectator, or click a unit.
* Orbit around a unit works. Free camera and fly-by work.
* If the unit you are watching is destroyed, the camera is released before the unit is removed.
* Clicking a missile follows that missile. The camera does not jump to a missile on its own.

---

What you see
* Aircraft, helicopters, vehicles, ships, buildings and scenery from the ACMI.
* The pilot name from the callsign, in the info panel. State reads Replay. Speed is taken from the recording, metric or imperial as the game is set.
* Loadout and livery from the recording, including empty pylons.
* Landing gear from the ACMI LandingGear flag. Doors follow the gear. Gear stays attached through ship elevator traversal.
* Rotors, propellers and the engine sound of the unit you are near.
* Flares from the left and right ejectors of the airframe, with sound.
* Missiles and bombs in the world and on the map, in the faction of the shooter, with launch and flight effect. Explosion at track end.
* Gun bursts from the recorded gun, every barrel, with the game's tracers, muzzle flash, barrel spin, recoil, shell casings and the weapon's own fire sound. Turrets on aircraft, ships, vehicles and buildings keep the aim they had in the match.
* Trails behind airframes and ordnance. Airframe trails are thicker, ordnance trails thinner. Faction color. Dead units lose their trail.
* Weather from the recording: time of day, cloud height, weather intensity. Applied when the world loads.

---

How it works

Record
* F8 toggles a recorder in the live match. It writes Tacview-style ACMI: unit id, parent, coalition, position samples, loadout, livery, gun-burst windows, and weather (time of day, conditions, cloud height, wind). Shells are not stored one by one. A burst is enough to rebuild the stream on playback.
* The recorder does not run during a replay. Replay patches that mute AI do not run in a normal match, so leaving the aircraft and normal firing stay intact.

Load
* Choosing a file parses the ACMI into a document: one track per object, samples in time, spawn time, death time. Units that exist at the start go into NOReplay.json. Units that appear later stay in a queue. The JSON is a normal Nuclear Option mission, so the game loads the map, buildings and units through its own spawner. Heartland and Igus Archipelago come from the map id in the ACMI.
* After the scene is up, each spawned unit gets a ghost. The ghost is the only thing that moves it. Weather is applied from the ACMI header.

Playback
* The clock is the timeline. Each frame the ghost reads the sample at the current time and writes position and rotation. Physics, joints and AI flight are held off, otherwise the airframe tears itself apart or taxis away on its own.
* A unit whose spawn time is still ahead is hidden. At that time it is shown, already at the recorded pose. At its death time the ghost releases the camera if needed, hides the map icon, and disables the object. Scrubbing backward enables it again.
* Neutral units are drawn and not driven. No rotor, no sound, no exhaust.
* Trails are LineRenderers rebuilt each frame from the samples within the length window. Dead airframes are excluded. Scrubbing rebuilds the trail for the new time.

Weapons
* Missiles are spawned through the game's missile spawner when their sample begins, then moved by a ghost. The faction is the parent's faction. At the end of the track the explosion effect is played and the object is removed. Sound comes from the prefab's AudioSources.
* Flares instantiate the airframe's own flare prefab and call the game's launch on the next ejector point, left then right. Sound is played.
* Gun bursts call the game's bullet simulation in visual-only mode, from every muzzle of the gun that fired. Tracers, impacts, muzzle flash, barrel spin and recoil come from the weapon. The shot does not damage anything. Sound uses the gun's own fire clips and stops when the burst ends, the replay pauses, or the time jumps. Older ACMI files without per-turret angles still aim every turret along the recorded burst direction.
* Lasers on tanks and ships are disabled. The beam child of the LFD is deactivated.

---

Files
* NOReplay.log next to the dll. BepInEx still writes its own LogOutput.log.
* Replay folder: the path in the Replay window. Default is the game's Replays folder under AppData/LocalLow/Shockfront/NuclearOption/Replays.
* Missions/NOReplay/NOReplay.json is overwritten on every playback start.

---

Limits
* The ACMI time step is about a fifth of a second. Several missiles fired inside that window share one timestamp, so a salvo appears as a short burst of spawns rather than millisecond gaps.
* Shells are rebuilt from the burst, not stored one by one. Playback fires the recorded gun through the game's own tracer, so a twin-barrel mount shoots from both muzzles. Rounds already in the air are not rewound; a pause or a scrub clears them.
* Old ACMI files from before this recorder have no loadout, livery, gun-burst or weather lines. Playback still moves units. Those extras stay empty.
