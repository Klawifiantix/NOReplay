# HUD and MFD in replay

Research only. No replay code in this change. Confirmed lines are from the decompiled `Assembly-CSharp` scripts and from `NOReplay/RecordAircraft.cs`. Prefab layout (which gauge sits on which airframe, canvas render mode, tac-screen camera culling) is not in the scripts and is marked unconfirmed.

Kevin's recording constraint: extra fields are allowed, the file must not grow much, and recording frame rate must not drop. New data is written only when the value changes, on the aircraft sample that already runs every 0.2 s, or from a Harmony hook on the change itself. Nothing new runs every frame.

## Budget

An aircraft pose line is about 70 bytes and is written about 5 times a second (`Aircraft Update Rate` default 0.2 s). That is about **21 KB per minute per aircraft**, before other units. That pose stream is the bulk of an aircraft track.

Proposed cap for everything in this document: **under 1 KB per minute for the followed aircraft, and under 5% of that aircraft's track**. A ten-minute solo flight then gains less than about 10 KB.

Per-frame sampling is the thing that misses the frame-rate limit. A 40-byte field at 60 Hz is about 144 KB per minute and does the work on the recording frame. The same fact, written only when it changes, is a few hundred bytes and a couple of reads inside the existing 0.2 s tick.

| Kind | Bytes per minute | Recording CPU | Use |
| --- | --- | --- | --- |
| Cheap, event | under about 0.3 KB | one compare on the 0.2 s tick, or a Harmony prefix on the click | weapon station, safety, MFD page, warning on/off, gear (already recorded) |
| Cheap, slow value | under about 1 KB while it is moving | one float read on the 0.2 s tick, write if the shown number changed | fuel percent, ammo count, throttle (already recorded) |
| Derived, record nothing | 0 | 0 while recording | pitch ladder, G, heading, CCIP pipper, gun lead, seeker circle |
| Expensive, do not record raw | tens of KB | scans or per-frame math | radar plot of every contact, RWR bearing every frame, targeting-pod video, pipper world point |

## 1. Cockpit HUD

Confirmed. The flight and combat HUD is a **screen-space overlay** for the aircraft the local player is sitting in. It is not a world-space mesh on the glass.

`FlightHud` (`SceneSingleton`) holds the compass, pitch ladder (`pitchCompass` plus `uvRect` from cockpit pitch), velocity vector, waterline, and HUD center. Each `Update` projects the cockpit forward and the velocity vector with `CameraStateManager.mainCamera.WorldToScreenPoint`. The ladder roll comes from the camera and cockpit euler angles. Inputs are the cockpit `Rigidbody` pose and velocity. `SetAircraft` is called from `CameraCockpitState.EnterState`. `FlightHud.EnableCanvas` is what shows or hides this canvas; orbit camera code turns it off.

`CombatHUD` (`SceneSingleton`) is the tactical layer on that same screen: unit markers, target designator, target arrow and text, hit markers, countermeasure name and ammo, threat list, weapon status, and the weapon reticle. `Aircraft` calls `CombatHUD.SetAircraft` only when `Player.IsLocalPlayer` during spawn. So a remote aircraft does not get this HUD. `LateUpdate` moves markers from `FactionHQ` tracking, and calls `weaponState.UpdateWeaponDisplay`. `FixedUpdate` calls `HUDFixedUpdate`.

`ShowWeaponStation` destroys the previous reticle and instantiates one child of `FlightHud`'s HUD center (or `HMDCenter` for cargo):

| Station flag | Prefab field | Class |
| --- | --- | --- |
| none | `NoWeaponUI` | `HUDNoWeaponState` |
| `HasTurret()` | `TurretUI` | `HUDTurretState` |
| `WeaponInfo.boresight` | `BoresightUI` | `HUDBoresightState` |
| `WeaponInfo.bomb` | `BombingUI` | `HUDBombingState` |
| cargo or troops | `CargoUI` | `HUDCargoState` |
| `WeaponInfo.sling` | `SlingUI` | `HUDSlingState` |
| `WeaponInfo.laserGuided` | `LaserGuidedUI` | `HUDLaserGuidedState` |
| otherwise | `MissileUI` | `HUDMissileState` |

`HUDBoresightState` is the gun sight. It projects `gunDirectionRelative` 3000 m ahead, and with a target it shows lead (`ShowingLead` / `Shoot`). It reads ammo, the first target, target position and velocity, and gear (gear hides the sight and shows the waterline).

`HUDBombingState` is CCIP and CCRP. CCIP is integrated in `CCIPTrajectory` at most every 0.1 s from weapon drag, mass, fin area, aircraft velocity, and wind. CCRP uses the average known position of the target list. The designator is hidden while the gear is down.

`HUDMissileState` draws range cues, no-escape range, and a shoot hint from `WeaponInfo.targetRequirements` and the target list's known positions. It recalculates range on its own timer.

`HUDApp` gauges are also screen HUD, HMD, or MFD (`AppType`). `HUDAppManager.Update` calls `Refresh` on each app every frame, but only after `CombatHUD` has an aircraft, and the manager destroys itself when that aircraft is disabled. Confirmed apps and what they read:

| App | Reads |
| --- | --- |
| `SpeedGauge`, `Altitude`, `MachIndicator`, `Bearing`, `Climbrate`, `ArtificialHorizon`, `BasicFlightInstruments` | aircraft transform, speed, altitude. Not re-read in full here; they are HUD apps bound in `Initialize(Aircraft)` |
| `GIndicators` | `pilots[0].GetAccel()` plus up, dotted with aircraft up. Every refresh |
| `ThrottleGauge` | throttle input, with named regions on the gauge |
| `FuelGauge` | `GetFuelLevel()` at most once a second |
| `GearIndicator` | gear state, speed limit, brake. Plays a voice line |
| `WeaponIndicator` | `weaponManager.currentWeaponStation` name, ammo, safety |
| `PylonIndicator` | stations. Not read line by line |
| `CountermeasureIndicator`, `MissileWarningLight`, `RadarWarning` | countermeasures and warning events |
| `AoADisplay`, `AoAIndexer`, `AoABracket` | angle of attack |
| `RPMGauge`, `NozzleGauge`, `PropGauge`, `StatusGauges`, `SystemStatusDisplay` | engines and system flags |
| `AutopilotIndicator`, `TurretAutoIndicator`, `HoverIndicator`, `WingAngleGauge` | those systems |
| `VRSWarning` | vortex ring state |

`HUDOptions` toggles groups when the station or the safety changes.

There is no flaps HUD class. Rotor `flapAngle` in `SwashRotor` is a blade degree of freedom, not a wing flap lever. A flaps tape is unconfirmed and not required by the scripts that exist.

The HUD is active for the **local player's aircraft in cockpit camera mode**. Following someone else in orbit or TV turns the flight canvas off (`CameraOrbitState` calls `EnableCanvas(false)`). Unconfirmed: whether a spectator who forces cockpit view on a remote aircraft still gets a useful `SetAircraft`. The spawn path only calls it for `IsLocalPlayer`. Cockpit enter does call `FlightHud.SetAircraft` on the followed aircraft, so the flight instruments can attach to whoever you sit on. `CombatHUD.SetAircraft` is still only the local spawn path. That split is confirmed.

## 2. MFDs

Two different surfaces. Both confirmed.

**Screen MFD, external view.** `VirtualMFD` is a screen UI. Its hide position is `Screen.width` to the right. It listens to `DynamicMap` maximize and minimize. While the map is maximized and `CombatHUD.aircraft` is set, it shows speed, radar altitude, and a small horizon from that aircraft's euler angles. The buttons show `MFDScreen` panels. Each screen is a `displayPanel` plus a short name. Which pages exist is serialized on the prefab (`leftScreens` / `rightScreens`). Page contents are unconfirmed beyond the script: the class only shows and hides the panel.

**In-cockpit tac screen.** `TacScreen.Initialize(Aircraft, Cockpit)` builds a **render texture**. It has its own `Camera`, `RenderTexture`, and `screenMaterial`. It subscribes to `aircraft.radar.onScan` or, if there is no radar, a child `TargetDetector.onScan`. It draws icons, pings, missile-warning blips, and a scan line. It also subscribes to `aircraft.onRadarWarning` and `targetCam.onCamToggle`. `TargetCam.SetTargetCam` is called by `CombatHUD` when the target list is not empty. That camera is a real scene camera (FOV, IR mode, parented to a mount on the aircraft) whose picture is shown on `targetCamDisplay`. Landing camera is a second mode. `TargetScreenUI` has a `GunCCIPDisplay` that updates from `CombatHUD.aircraft` and the target list.

`MFDAppManager` is the same `HUDApp.Refresh` loop, tagged `AppType.MFD`, and it also requires `CombatHUD.aircraft` at `Start`.

What drives an MFD, confirmed:

| Page or feed | State |
| --- | --- |
| Speed, altitude, horizon on the external MFD | local `CombatHUD.aircraft` speed, `radarAlt`, euler angles |
| Radar tac screen | `Radar.onScan`, scan cone, aircraft heading at scan |
| Optical tac screen | child `TargetDetector.onScan` when `aircraft.radar` is null |
| Target camera | `WeaponManager` target list, `TargetCam` mount, IR if night, far, or `PlayerSettings.tacScreenIR` |
| Missile warning blips | `GetMissileWarningSystem().knownMissiles` |
| RWR | `aircraft.onRadarWarning` |
| Weapon page | `WeaponIndicator` reads `currentWeaponStation` |
| Fuel, engines | `FuelGauge`, RPM and nozzle apps |

Datalink as a named page was not found. Friendly and hostile markers on the HUD come from `FactionHQ.factionUnits` and `trackingDatabase`. That is the shared picture, not a separate datalink message.

## 3. What a recording already has

`RecordAircraft` samples every 0.2 s and writes a key only when the stored value changed. Pose (`T`, `Q`) is every sample. Confirmed keys:

| Recorded | HUD use | Gap |
| --- | --- | --- |
| `T`, `Q` | attitude, heading, pitch ladder, velocity-vector direction if we also have velocity | velocity is not a vector. `TAS` is speed along the track only |
| `TAS`, `Mach` | speed tape | on change, so a steady speed is one sample |
| `AOA` | AoA indexer | from cockpit velocity |
| `AGL` | altitude | radar altitude, not barometric |
| `LandingGear` | gear light | boolean only, not the travel animation |
| `RadarMode` | radar on/off | `radar.activated`, not scan angle or contacts |
| `Throttle`, stick axes, `EngineRPM`, `RpmRatio`, `JetRpm`, `TurbineRpm` | throttle and engine gauges | behind `RecordExtraTelemetry` (default on). Exact float compare, so a noisy stick can write almost every 0.2 s sample: about 4 KB/min if all four axes chatter |
| `Tailhook` | hook indicator | quantized to 0.05 |
| `Tilt` | nacelle angle | up to four joints, on change |
| `Loadout` | which weapons are hung | once, not which one is selected |
| `LockedTarget`, `LockedTarget1`… | target box, missile reticle, target camera | up to 10 ids, written when the list membership changes. Empty list writes nothing, so "unlocked" is the absence of a later update |
| `PilotHeadPitch`, `PilotHeadYaw` | where the pilot looks | local aircraft, cockpit camera only |
| Lasers, turrets, bays, gear travel | already their own channels | not HUD text |
| Per-missile `LockedTarget` | the weapon's own lock after launch | not the aircraft's selected station |

Not recorded, and needed for the reticle Kevin described:

| Missing | How often it changes | If we recorded it wrong |
| --- | --- | --- |
| Selected station index and weapon code | a handful of times a minute | per frame would still be small, but the read must not be a per-frame search |
| Safety / master arm | rare | |
| Ammo on the selected station | every round while firing | per shot at gun rate would be a stream. See the cheap row above |
| Countermeasure type and count | on dispense | |
| Fuel fraction | the gauge itself updates at 1 Hz | `fuelLevel` is a `SyncVar` |
| MFD page and target-camera mode | on button or on lock | |
| Radar contacts and RWR bearings | every scan | the expensive row |
| CCIP impact point, gun lead, seeker circle | every frame | do not record; recompute |
| Warning lights (jam, missile, gear voice) | edges | |
| Flaps | no HUD field found | skip until a control is confirmed |

`currentWeaponStation` is a normal field on `WeaponManager`, not a `SyncVar`. Cycling weapons calls `SetStationTargets(stationIndex, ids)`, and that method sends `Cmd`/`Rpc` with the station index and up to 128 target ids. So a weapon change that goes through that method is on the network. Confirmed. The RPC handler body was not found under a stable name, so it is unconfirmed whether a **remote** client's `currentWeaponStation` is updated for the HUD. Recording our own aircraft from `weaponManager.currentWeaponStation` is confirmed-local. Recording another player's selected weapon from the ghost alone is not safe to assume.

## 4. Feasibility

### (a) Drive the real HUD on a ghost

`FlightHud` and the gauges mostly need an `Aircraft`, a cockpit transform, a rigidbody velocity, and the main camera. A ghost can supply pose. It does not supply a live `WeaponManager` target list, a live `Radar.onScan`, or `pilots[0].GetAccel()` unless we fake them.

`CombatHUD.SetAircraft` then walks `NetworkHQ.factionUnits` and the tracking database and builds markers. On a replay those sets are the live mission, not the recording. `ShowWeaponStation` instantiates the real reticle, which then runs CCIP with `aircraft.rb.velocity` and wind. Our ghosts are kinematic and wind is forced to zero for nukes only. A half-fed HUD will show the live war, or throw, or sit empty.

`TacScreen` and `TargetCam` render real cameras. Pointing a second camera at the replay every frame is a real GPU cost. `TargetCam` also sets `displayDetail` on live targets.

Pros: the picture matches the game, including theme and weapon art. Cons: the components assume a local player aircraft with live systems. Patching every read is a large surface, and it can fight the replay camera and the live mission. Risk: high. Not the first step.

### (b) Our own overlay

Draw tapes from pose, `TAS`, `AGL`, `AOA`, gear, throttle, and fuel once we record fuel. Draw the reticle we can identify from a recorded station id. CCIP and gun lead are recomputed from the two tracks (shooter pose and target pose) plus the weapon definition's drag, mass, and muzzle data, which are on the prefab when that station was recorded. No live HUD singleton.

Pros: pause, seek, and reverse are just "evaluate at this time". No live HQ, no extra camera. Cons: we redraw widgets, and prefab art (exact sprites, fonts, CCIP marker offsets) is unconfirmed until we sample one airframe in play mode. Risk: medium, and it stays inside the replay.

### (c) Hybrid

Phase 1 and 2 are our overlay. A later experiment can parent the real `HUDBombingState` under our camera only for the followed aircraft, after the ghost has a stand-in `WeaponManager` and a frozen target list. The tac-screen camera stays off until that experiment is measured. This is the recommended shape. The risk of (a) stays inside one optional phase instead of the whole feature.

Scope stays **the followed aircraft**. Other aircraft keep the external model. Multiplayer: record weapon, safety, ammo, fuel, and MFD page for the local aircraft, because that client has the objects. For everyone else, record only what already arrives (loadout, gear, fuel SyncVar, launched weapons, turret RPCs, and `SetStationTargets` if we hook the RPC we actually receive). Do not invent another player's selected station.

Performance: the overlay should run only for the followed ghost, and only rebuild widget text when the displayed number changes. That matches the playback rule already used for the kill feed.

## 5. Phased plan

**Phase 1. Flight overlay from data we already store.** Speed, altitude, heading, pitch ladder, and velocity vector from `T`/`Q`/`TAS`/`AGL`. Gear light from `LandingGear`. Throttle and RPM from the extra telemetry. G is recomputed from the pose delta, not recorded. No new fields. File size unchanged. Unconfirmed until a playback test: the ladder's texture `uvRect` math (`pitch / 180 + 0.437`, roll from camera) matches the screen, and `TAS` is enough for the speed tape when the velocity vector needs a direction (use the pose delta as the velocity vector).

**Phase 2. Own-aircraft weapon and target, event only.** On the local aircraft, inside the existing 0.2 s tick (not a new `Update`): if `currentWeaponStation.Number` or the weapon code changed, write `Station=<index>|<code>` (about 20 bytes, a few times a minute). If safety changed, one byte. If the displayed ammo integer changed, write it (about 3 KB per minute only while a gun is firing; zero otherwise). Fuel: write when the percent changes (the gauge is already 1 Hz; well under 0.5 KB per minute). Countermeasure count on change. Do not write CCIP, lead, or seeker position. At playback, choose the reticle class from the weapon flags and recompute the pipper. Hook `SetStationTargets` only if we need other players, and store the station index from the RPC we already receive. Confirmed we can read the local station. Unconfirmed: remote station field, and the exact prefab numbers for CCIP drag.

**Phase 3. MFD, still no video.** Record the open `MFDScreen.shortName` and target-camera mode on change (tens of bytes per switch). Draw a schematic radar from units we already record, not from a contact dump. Do not record `onScan` plots. A real `TacScreen` / `TargetCam` render is a separate experiment with a frame-time measurement, and it stays off unless that measurement is fine.

Out of scope until a control is found in source: flaps, a full RWR scope, and datalink text.
