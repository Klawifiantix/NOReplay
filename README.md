First of all, I would like to thank KopterBuzz for creating the NoBlackBoxMod. Without his tremendous achievement, I likely would never have begun developing the ReplayMod.
https://github.com/KopterBuzz | https://github.com/KopterBuzz/NOBlackBox

This mod was created and developed together with Grok.

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
* In a normal match, press F8 to start recording. Rec appears at the bottom right. F8 is the default.
* Press the same key again to stop. The ACMI file is written to the replay folder.
Recording is off in the main menu and during playback.

Open a replay
* In the main menu, click Replay. The button is only on that screen.
* The list shows ACMI files from the replay folder. The folder path is at the top of the window. Set it once. The mod remembers it.
* Click a file. It is converted to NOReplay.json and that mission starts. There is no second confirm button.
* A .acmi that is still a zip opens the same way as a .zip.acmi.
* Leave the mission and the timeline closes. The Replay button is back in the main menu.

Timeline
* The timeline sits at the top center while a replay is running.
* Play runs the track. Pause holds it.
* Drag the marker to scrub. Units jump to that time. Dragging backward shows the reverse motion.
* Speeds: -4x -2x -1x -0.5x -0.25x 0.25x 0.5x 1x 2x 4x.
* F10 hides the timeline. F10 shows it again. F10 is the default.
* Trails button toggles flight paths. The T key does the same. T is the default. The slider next to it sets trail length from 0 to 10 minutes. Dead airframes lose their trail.

Camera
* Click a unit, building, or projectile on the map. It stays selected, the camera jumps to it, and the view orbits. Shift+click, while the camera is already in free flight, selects the unit and leaves the camera where it is, still in free flight. F then looks at that unit. After PLAY or PAUSE the timeline shows || and the type name, plus the pilot or callsign when there is one. With nothing selected, the || and the name are left out.
* WASD leaves that orbit. The camera flies free again, and the selection stays.
* F toggles the look-at lock. F is the default. While it is on, the camera flies free and turns to face the selected target. The camera does not move with the target. [F] shows after the name. F again returns to normal mouse look.
* If the target is destroyed, detonates, despawns, or the time moves past it, the name leaves the timeline, the lock is released, and the camera stays where it is.
* The look-at and trails keys do nothing while a text field is selected.
* The normal spectator path is unchanged.
* Orbit around a unit works. Free camera and fly-by work.
* If the unit you are watching is destroyed, the camera is released before the unit is removed.
* The camera does not jump to a unit on its own.

Shortcuts
* Defaults: F8 records, Space plays or pauses, 1 / 2 / 4 set speed to 1x / 2x / 4x, Left and Right arrow jump 5 seconds, F10 hides or shows the timeline, F toggles look-at lock, T toggles trails, P toggles the carrier approach pattern.
* Look-at lock, trails and the approach pattern do nothing while a text field is selected. During a replay the game's own binding for those keys is muted. They follow the key you set. The pattern starts off.
* WASD still flies the free camera. Those four keys are not in the config.
* Change them in `BepInEx/config/you.noreplay.cfg`, or in ConfigurationManager (F1). Recording is StartStopRecordingKey under Hotkeys. Replay keys are under Replay Hotkeys. A value already saved in the config is kept. Only a missing entry uses the default above.
* If two shortcuts use the same key, or one of them is W, A, S or D, NOReplay.log warns once.

---

What you see
* Aircraft, helicopters, vehicles, ships, buildings and scenery from the ACMI.
* The pilot name from the callsign, in the info panel. State reads Replay. Speed is taken from the recording, metric or imperial as the game is set.
* Loadout and livery from the recording, including empty pylons.
* Landing gear from the ACMI LandingGear flag. Doors follow the gear. Gear stays attached through ship elevator traversal. With the gear down, a ray from each wheel plays the game's rolling clip while the tyre is on a surface and the aircraft is moving. Touchdown squeal and smoke play only while that wheel would still be spinning up, judged from the recorded descent rather than from altitude above the sea, so a carrier deck counts. A carrier's deck colliders stay available to that ray. Pause stops the sound and the smoke. A Tarantula recording stores each nacelle angle as Tilt. Playback sets those angles. An older file leaves the nacelles where the prefab put them.
* Tailhook on an aircraft whose loadout contains TailHook_ (the Ifrit Multirole and the Compass trainer). A recorded Tailhook value is played back. An older file infers it: down between 30 m and 120 m AGL while slowing and descending, up above 100 m/s or below 2 m/s, moving at half a unit per second. Scrubbing, reverse and pause use the same curve.
* Arresting wires on the Hyperion. A recorded trap uses the wire from the file. Otherwise a sharp stop on the deck with the hook down catches the wire the hook crosses, and a touch-and-go does not. The cable follows the hook, then runs back to rest at 4 m/s.
* The optical landing ball on the Hyperion. It follows the selected aircraft when that aircraft is on the approach with the hook down, otherwise the nearest such aircraft. Low is red, high is white.
* The Case I approach pattern on each Hyperion, one green line. P toggles it. It starts off. Initial and downwind are at 1500 ft. The break is a level left 180 over the ship. The final turn comes down the angled deck, and the glideslope ends on the wires. A bolter continues straight ahead. The line moves and turns with the carrier. Red and blue trails are unchanged.
* Rotors, propellers, jet nozzles and engine sound from the unit you are near. Pitch and volume follow the game's own engine curves and the recorded throttle. Aircraft, helicopters, ground vehicles and ships stay in 3D: the prefab distance curve is left alone, and a fly-by or free camera keeps Doppler on. A parked vehicle is quiet. A moving one crossfades from idle to drive the way the game does.
* Helicopter rotor downwash, and the wake behind a moving ship. The wake, the engines, the turrets and the carrier lights stop when that ship is killed.
* Cargo dropped by a Tarantula or an Ibis, and an ejected pilot. The lifting frame is removed when the recording says the parachute rig is gone, or five seconds after the canopy scale collapses, which is the game's landing timer. A new recording stores the cargo ramp hinge, the mounted load sliding along the rail, and hides that mesh once it has left the aircraft. Seeking back before the drop closes the ramp and puts the load inside again. Ejecting blows the canopy glass off along the recorded path and a seek before the ejection puts it back on the aircraft. An ejected pilot is registered on the map the same way an aircraft ghost is, without being treated as an aircraft. The dropped body is the unit the game spawns, a ground vehicle or a container. A new recording stores Chute for each Parachute: the rig, the canopy position, rotation and scale, the line length, and the wrinkle values the cloth shader was given. An ejected pilot is recorded as its own unit, including the PilotState the animator uses, the moment the seat fires, and the seat's position after it detaches. Playback moves those transforms on the replay clock. It does not run parachute physics, the ejection rail, or the cargo push. Pause holds the canopy and the seat. Scrubbing and reverse jump the pose and do not replay the seat clip unless playback walks across the fire. The game enables the canopy at deploy, so playback turns that renderer on even when the ghost's prefab set had it disabled. An older file has no Chute line, so nothing is added. The pilot stays on the ground after landing instead of being removed at one metre. Record a new match to see the canopies.
* Lasers on the Spearhead MBT, the Cursor class LFD, the HLT-HEL trailer and the MSV LADS. All four use the game's one Laser weapon. Each prefab brings its own beam, muzzle particles, impact effect and the three fire clips. A new recording stores each emitter as Lasers, with the beam on or off, the impact point and the power the beam was drawn at. Playback draws that beam and plays those clips. It does not run Laser.Fire or the weapon's damage tick. Pause holds the beam and stops the sound. Scrubbing jumps the impact and does not replay the start or end click unless playback walks across that edge. An older file has no Lasers line, so the mounts stay up and the beams stay off. The turret's own aim is the existing turret channel, not this one.
* Kill feed, chat and mission lines. A new recording stores each kill the client was sent, before the kill-feed filters, with the names and colours resolved at the time. It stores global chat and this client's own faction chat, plus server announcements. It stores mission text that passed the faction filter, and whether that line played the radio click. Playback puts kills through the game's own kill-feed method, so the viewer's line count and type filters still apply, including a line count of zero. Ally chat uses the game's "(ally)" prefix and ally colour. Mission text stays the default message colour. The radio click plays only while playback walks forward across that line. Pause holds the lists. A seek or a reverse rebuilds them for the new time and does not play the click. An older file has none of these lines, so the lists stay empty.
* Ground-vehicle wheels and tank tracks roll with the replay. Forward and reverse follow the nose. A vehicle turning on the spot rolls the two sides in opposite directions when the prefab has a wheel on each side, or two separate track meshes. One combined track mesh keeps the centerline speed, the same way the game scrolls it. Motion freezes while the timeline is paused, including during a scrub that is not moving the vehicle. Vehicles more than about a kilometer from the camera are left still.
* When a ground vehicle is destroyed, a new recording keeps its pose until the game actually spawns the wreck, which is after the unit is already disabled, and then shows that wreck. That gap is the same for every ground vehicle, including an ammo truck. It is not a separate ammo cook-off time from the game. An older file still wrecks at the removal line. It also keeps moving at its last speed for at most a second and a half, and never faster than 45 m/s. Scrubbing back before the loss puts the vehicle back.
* A building that does not move, including a storage tank, an ammo dump or a depot, is hidden at its removal. Its own wreck prefab supplies the flash, the flames and the smoke. The broken pieces are moved on the replay clock from that prefab's ejection speed, and a scrub puts them back. They are not left to the game's live physics. Dust that the wreck emits into the shared particle pool is not fully removed by a scrub. Smoke from a nose striking a building is not in the file. Smoke and fire that belong to a broken aircraft part stay on that part.
* Ground vehicles farther than about 2.5 km are drawn without their engine, guns and particles. Their position and map icon still follow the recording, so driving up to one shows it where it actually is. Static buildings are not these ghosts. Their visibility is still the game's.
* Flares from the left and right ejectors of the airframe, with sound.
* Missiles and bombs in the world and on the map, in the faction of the shooter, with launch and flight effect. Explosion at track end. Glide bombs and cruise missiles unfold their wings after release. Scrubbing back folds them, scrubbing forward shows them open, and pause holds the angle.
* External missiles and bombs leave the wing pylons as they are fired. The ammo line in the bottom spectate panel counts the station down, including a gun that is reloading. Scrubbing backward puts the rounds and the pylon stores back. Older ACMI files do this from the missile tracks and gun bursts already in the recording. Enclosed tube launchers and guns use the same count.
* Wings, tails, stabilizers, rotor blades and other parts that break off are recorded and shown again on playback, including fragments. The piece tumbles away, and scrubbing backward puts the airframe back together. Recordings made before this version have no part-break lines, so those airframes stay intact.
* On an aircraft, a detached part carries the game's own break effects: disintegration, damage fire and smoke, a ruptured fuel tank's leak and flame, and an engine failure flame when that part is one of the engine's critical parts. Fire the game parents to the part trails the falling piece. An explosion flash or smoke column stays in the world and does not spin with the debris. The wound left on the airframe smokes too. Effects freeze while the timeline is paused, disappear when you scrub back before the break, and a falling trail stops when the piece hits the ground or the water. They are not spawned when the camera is more than 5 km away, which is the same cutoff the game uses. Buildings and ground vehicles still explode the way they did before this: the burst stays in the world. A ship does not. A new recording writes Sinking=1 at the kill, keeps the hull's position and attitude about five times a second until the game despawns it, and only then writes Visible=0. Playback follows that motion. An older file, or a Tacview file with no samples after the kill, lowers the hull on a fixed 28 second curve, lists it, plays a splash as the deck goes under, then hides it. Scrubbing, reverse and pause use the same curve. The vehicle explosion is not played for a ship.
* A round that is already in the air keeps flying if the shooter is destroyed. It uses the gun's own lifetime and drag, on the replay clock. Pause holds it. A seek or a reverse clears it. It does not damage anything.
* Gun bursts from the recorded gun, every barrel, with the game's tracers, muzzle flash, barrel spin, recoil, shell casings and the weapon's own fire sound. A tank cannon or any other single-shot gun fires one shell and stops. Automatic guns keep the rate they had in the match. Turrets on aircraft, ships, vehicles and buildings keep the aim they had, including complex-physics mounts and hardpoint mounts such as the Chicane chin turret and gunpods. A replay turret does not look for targets or keep firing on its own.
* Radar dishes keep turning on ships, radar trucks, radar buildings and AWACS. A radar truck folds the dish in while it is moving and unfolds it when it stops. Pause freezes the motion. Scrubbing sets the fold to the pose for that moment.
* Trails behind airframes and ordnance. Airframe trails are thicker, ordnance trails thinner. Faction color. Dead units lose their trail.
* Weather from the recording: time of day, cloud height, weather intensity. Applied when the world loads.

---

Carrier ops

The hook and the wires are not simulated. Physics on the ghosts stays off, and TailHook, ArrestorGear and OpticalLandingSystem stay disabled. Each frame is a function of the replay clock, so a seek lands on the pose for that time.

A new recording writes Tailhook when the hook moves by more than about 0.05, and Event=Trap|aircraft|carrier|wire when a wire catches, with Event=Untrap when it lets go. Older files have neither line. The hook is inferred and the trap is detected from the track.

The meatball uses the carrier landing runway's 3.4° glideslope. It does not run the game's wave-off timer. That timer waits on the real clock and drops the aircraft from a landing list, which a scrub cannot undo. The ball and the datum lights are moved directly.

The green pattern is that same glideslope, drawn once in the carrier's own coordinates. Initial is 2 NM astern and 0.25 NM to starboard, parallel to the hull, at 1500 ft. The break's nose is 1 NM ahead of the ship. Downwind is 1.75 NM to port. The final turn descends to the ball, 0.75 NM astern on the angled deck, then the 0.06 glideslope (about 3.4°) down to the wires. The bolter runs 1 NM along that deck. Pause, scrub, reverse and speed changes leave the shape alone. The line follows the ship's pose, including heading, so it stays on the deck. It is removed when the replay ends, or when that carrier is killed or gone.

---

How it works

Record
* The record key (default F8) toggles a recorder in the live match. It writes Tacview-style ACMI: unit id, parent, coalition, position samples, loadout, livery, throttle, gun-burst windows, part breaks, and weather (time of day, conditions, cloud height, wind). Shells are not stored one by one. A burst is enough to rebuild the stream on playback. A single cannon shot is one sample. A part break is one line: which part, and the velocity when it left the airframe. A ship keeps its hull samples after it is killed, until the game removes it. Sinking=1 marks the kill. Tacview ignores that property. Visible=0 is the despawn.
* The recorder does not run during a replay. Replay patches that mute AI do not run in a normal match, so leaving the aircraft and normal firing stay intact.
* Aircraft are registered before hardpoint weapons spawn. Turret recording stays attached and picks up the chin turret or gunpod once it exists. Loadout is written again if it arrives just after spawn.

Load
* Choosing a file parses the ACMI into a document: one track per object, samples in time, spawn time, death time. Units that exist at the start go into NOReplay.json. Units that appear later stay in a queue. The JSON is a normal Nuclear Option mission, so the game loads the map, buildings and units through its own spawner. Heartland and Igus Archipelago come from the map id in the ACMI.
* After the scene is up, each spawned unit gets a ghost. The ghost is the only thing that moves it. Weather is applied from the ACMI header.

Playback
* The clock is the timeline. Each frame the ghost reads the sample at the current time and writes position and rotation. Physics, joints and AI flight are held off, otherwise the airframe tears itself apart or taxis away on its own.
* A unit whose spawn time is still ahead is hidden. At that time it is shown, already at the recorded pose. At its death time the ghost releases the camera if needed, hides the map icon, and disables the object. Scrubbing backward enables it again.
* Neutral units are drawn and not driven. No rotor, no sound, no exhaust.
* Radar dishes are turned by the replay clock. The unit's own radar scan stays off, so a ghost does not search for targets. The dish freezes while the timeline is paused and follows the playback speed. Radar trucks use the same deploy threshold as the game: stopped unfolds the dish, moving stows it.
* Trails are LineRenderers rebuilt each frame from the samples within the length window. Dead airframes are excluded. Scrubbing rebuilds the trail for the new time.

Weapons
* Missiles are spawned through the game's missile spawner when their sample begins, then moved by a ghost. The faction is the parent's faction. At the end of the track the explosion effect is played and the object is removed. Sound comes from the prefab's AudioSources. Folding fins stay on that ghost. The missile script stays off, and the fin angle is replay time since launch: folded until the seeker's delay, then opening at the prefab's deploy speed. A reused ghost takes the pose for the new time.
* Flares instantiate the airframe's own flare prefab and call the game's launch on the next ejector point, left then right. Sound is played.
* Gun bursts call the game's bullet simulation in visual-only mode, from every muzzle of the gun that fired. Tracers, impacts, muzzle flash, barrel spin and recoil come from the weapon. The shot does not damage anything. Sound uses the gun's own fire clips. A sustained loop starts only while the burst is still going, and it stops when the burst ends, the unit is lost, the camera is far, the replay pauses, or the time jumps. Rate of fire comes from the gun: a one-round magazine uses the reload time, so a tank cannon does not hose shells. The ammo counter uses that same cadence. Older ACMI files without per-turret angles still aim every turret along the recorded burst direction. Older files that held a single shot open for half a second still fire one shell.
* Engine, rotor, propeller, fan and jet-nozzle audio is driven from the recorded throttle and the component's own volume and pitch fields. The prefab's 3D rolloff is not replaced. Following a unit in the fly-by or free camera keeps the sound in the world. Orbit flattens only the sounds the game itself flattens.
* Lasers use the recorded Lasers channel. The game's own Laser script stays off on every ghost, so a ghost never fires a laser or deals damage on its own. The beam, muzzle particles and sounds are shown only when the recording says the laser was firing. An older file without Lasers keeps every beam off.
---

Files
* NOReplay.log next to the dll. BepInEx still writes its own LogOutput.log.
* Replay folder: the path in the Replay window. Default is the game's Replays folder under AppData/LocalLow/Shockfront/NuclearOption/Replays.
* Missions/NOReplay/NOReplay.json is overwritten on every playback start.

---

Limits
* The ACMI time step is about a fifth of a second. Several missiles fired inside that window share one timestamp, so a salvo appears as a short burst of spawns rather than millisecond gaps.
* Shells are rebuilt from the burst, not stored one by one. Playback fires the recorded gun through the game's own tracer, so a twin-barrel mount shoots from both muzzles. A one-round cannon fires once. Rounds already in the air are not rewound; a pause or a scrub clears them. The ammo counter does rewind: it counts the same shells the gun plays.
* A part that breaks off is one line in the ACMI (the part id, and the velocity when it leaves the airframe). Playback hides that part and anything mounted on it, and spawns a visual copy that falls for a few seconds. The copy keeps each piece's own local position, rotation and scale, including a landing-gear leg whose hinges are scaled differently along each axis. Fire and smoke from that part's damage prefab stay on the piece instead of being cleared with the airframe. On an aircraft, the game's fire and smoke go on the piece and on the wound. Jumping the timeline hides or restores the part without spawning another copy, and it clears that fire. Playing or scrubbing through the break shows it again. Scrubbing back before the break leaves no fire behind. Vehicle and building explosions are not parented to the falling wreck. A ship is not blown up at the kill. Secondary breakup of wreckage that is already off the aircraft is not recorded. Rotor blades and other fragments use the same path.
* Old ACMI files from before this recorder have no loadout, livery, gun-burst or weather lines. Playback still moves units. Those extras stay empty.
