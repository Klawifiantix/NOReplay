# Nuclear explosions

Confirmed from the decompiled scripts in `Assembly-CSharp`. The effect prefabs
themselves are not in that dump, so authored mesh shapes, curve key times, and
the per-warhead numbers on those prefabs are marked unconfirmed.

## How a detonation is built

`Missile.Warhead.Detonate` instantiates one effect prefab and parents it to
`Datum.origin`:

- underwater, when the burst is at or below sea level
- `terrainEffect` when the hit is terrain, otherwise `armorEffect` when the hit is armor
- `airEffect` when neither of those produced an object
- `waterSurfaceEffect` as an extra, destroyed after 30 seconds, for a near-surface air burst
- `fizzleEffect` when the missile is not armed

`blastYield` is a serialized field on `Missile`, in kilograms of TNT
(`ExplosionTester` labels the same quantity "kg TNT"). `GetYield()` returns it.
There is one `Missile` class. A tactical warhead and a strategic warhead are
different missile prefabs, not different classes.

When `blastYield > 200` (0.0002 kt), `Detonate` finds the `Shockwave` on the
effect and calls `SetOwner(ownerId, blastYield * 1e-6)`. That call happens
after `Instantiate`, and `Instantiate` of an active prefab has already run
`Shockwave.Start`. `Start` is the only place that computes the visible size:

- `blastPower = Pow(yieldKilotons * 1_000_000, 0.3333)`
- `blastRadius = blastPower * 13`
- `blastPropagation = blastPower * 0.5`

`SetOwner` overwrites `yieldKilotons` but does not recompute those three.
The damage loop later passes `yieldKilotons * 1e6` into `HasShockwaveReached`,
so the damage number follows the missile and the visible radius follows
whatever `yieldKilotons` was serialized on the effect prefab. `MushroomCloud.yield`
is never written by `SetOwner`. Its `Start` does:

- `scaleAdjust = Pow(yield / effectReferenceYield, 1/3)`
- `blastScale = Pow(yield / 1_000_000, 1/3)`
- a downward raycast of `blastScale * 300` on `PhysicsLayers.StaticsMask`
- `Cloud.Initialize`, then each optional piece (`baseCloud`, `updraft`, `stem`, `waterEffect`, `CloudRing`)

So a shared effect prefab cannot show two yields. Different sizes require
either a different effect prefab or the yield fields being set before `Start`.

## What each frame does

`MushroomCloud.Update` adds `Time.deltaTime` to a private `time` and then:

- `Fireball`: uniform scale from `sizeOverTime`, `_FireballColor`, `_EmissiveStrength`. The renderer is disabled after the last emit key and stays off.
- `Cloud`: shader `_EmissiveStrength`, `_EmissiveRoughness`, `_CloudColor`, `_ScrollPosition`. Then the cap moves by `(up * riseRate + LevelInfo.GetWind(position)) * Time.deltaTime`. The scale line is after the wind line, so a missing `LevelInfo` throws and Unity stops calling `Update`. Scale is `(horizontal, horizontal * relativeHeight, horizontal)`. A `relativeHeight` above 1 is a vertical ellipsoid on purpose. This is the mushroom cap, not the shockwave.
- `BaseCloud`, `Stem`, `Updraft`: particle rate, color, and the updraft force, on a one-second slow tick that reads `Time.timeSinceLevelLoad`.
- `CloudRing`: radius `sqrt((blastScale * 50 + 340 * time)^2 - heightOffset^2)` once the wave reaches that ring, then `localScale = Vector3.one * radius`.
- Lens flare intensity from a curve. The flare object is destroyed at intensity 0.

`Shockwave.Update` adds `340 * Time.deltaTime` to `blastPropagation` and
`Time.deltaTime` to `blastTime`, then:

1. Ground decal `_shockwaveExpansion = blastRadius / blastPropagation`. After the wave passes the radius, `dustOpacity` fades at 0.1 per second and the decal and the `Shockwave` component are destroyed.
2. The damage loop. Overpressure is `25000 / max(propagation / power, 1)^3`. Above 0.5 it calls `HasShockwaveReached`, which calls `TakeDamage` and `AddForce`.
3. Only then the vapor. `LookAt(CameraStateManager.i.transform.position)` runs before the scale. A null camera singleton throws, and the scale line never runs. The scale is `localScale = Vector3.one * blastPropagation` with no parent correction. `_ShockwaveAlpha` comes from `vaporCloudAlpha` at `blastTime`. `_Size = blastPropagation / vaporCloudDetailScale`. `_ShockwaveSoftness = 4 / localScale.x`. Alpha at or below 0 destroys the vapor object.

The vapor mesh is a uniform sphere if the mesh and the parent scale are
uniform. Whether the authored mesh is a sphere is unconfirmed: the prefab is
not in the dump. `LookAt` does not change a sphere's silhouette.

Wind is `LevelInfo.GetWind(GlobalPosition)`. Zero wind is a normal game state.
The replay forces that lookup to return zero while a replay cloud is updating,
so the cap rises only by `riseRate` and the scale line is reached.

## Warheads

No script contains a list of nuclear munitions or their yields. The numbers
live on the prefabs.

| Source | What it decides | Where the number is |
| --- | --- | --- |
| `Missile.blastYield` | Kilograms of TNT. Exclusion zone when above 100000 kg: radius `Pow(yield, 1/3) * 13`. Shockwave damage yield `blastYield * 1e-6` kilotons when above 200 kg. | Serialized on each missile prefab. Not in the scripts. |
| `WeaponInfo.nuclear` | The mount is a nuclear warhead. Rank gate is `minRankTacticalWarhead`. | Serialized on the weapon info asset. |
| `WeaponInfo.strategic` | Strategic subset. Extra rank gate `minRankStrategicWarhead`. A nuclear mount with `strategic` false is the tactical one. | Same asset. The split is a flag, not a yield threshold. |
| `Warhead.airEffect`, `armorEffect`, `terrainEffect`, `waterSurfaceEffect`, `underwaterEffect` | Which explosion prefab is instantiated. | Serialized on the missile. The prefab then carries `MushroomCloud` and `Shockwave`. |
| `MushroomCloud.yield` | Cap, stem, and ring scale. Compared with `effectReferenceYield`. | Serialized on the effect. Not copied from the missile. |
| `Shockwave.yieldKilotons` | Visible radius, computed in `Start`. | Serialized on the effect. `SetOwner` overwrites it too late to change the radius. |
| Building wrecks | An ammunition bunker wreck has its own `MushroomCloud` and `Shockwave` on the wreck prefab. It is not a missile and has no `blastYield`. | Serialized on that wreck prefab. |

`RecordMissile` already treats a unit whose name ends in `kt)` as a destroyed
event. That is a naming convention, not a yield table. The names and the
kiloton figures are unconfirmed from this source dump.

## What a recording stored

A missile line stores `Name` (the unit name), type, coalition, and pose.
It did not store `blastYield`, the nuclear flag, or the effect prefab name.
Playback instantiates the unit's own missile prefab, so `GetYield()` and the
warhead effect references are on the ghost when that prefab is the right one.
An older file still plays: the ghost prefab supplies the yield. New recordings
also write `Yield=<kilograms>` on the missile so the size does not depend on
the prefab still matching.

The spawned effect used to disable `Shockwave` and, for wrecks, `MushroomCloud`.
`PlayWarheadBoom` destroyed the whole effect after 12 seconds. `Shockwave.Update`
never applied `localScale = Vector3.one * blastPropagation`. `ReplayBlast`
scaled the vapor by dividing by the parent lossy scale on each axis and set the
shockwave root to `Vector3.one`. That is not what the game does, and a
non-uniform parent or mesh becomes a tall ellipsoid. The mushroom cap was left
at its `Start` size because `Cloud.Update` did not run. This matches the
in-game report: an upright football that does not grow, and a mushroom that
does not animate.

## Playback

`NukeTime` leaves both components enabled.

- Before `Start`, a missile explosion writes `Shockwave.yieldKilotons = blastYield * 1e-6` and `MushroomCloud.yield = blastYield`. `Start` then computes the radius from the missile, which is the value `SetOwner` was meant to apply. A wreck keeps the yield serialized on its prefab.
- A prefix sets `MushroomCloud.time` and `Shockwave.blastTime` so that after `Update` adds `Time.deltaTime` they equal the replay age. `blastPropagation` is set the same way to `power * 0.5 + 340 * age`. Pause, speed, and seek follow the replay clock because the age is absolute.
- `LevelInfo.GetWind` returns zero only while a replay cloud or shockwave update is on the stack. The cap's height is then the integral of `riseRateOverTime` over the replay age, so pause, speed, and seek move it with the clock instead of with `Time.deltaTime`. The game itself samples that curve once per real second; the replay integrates it.
- The shockwave's influenced-object list is cleared in a `Start` postfix and at the start of every `Update`, so `TakeDamage` and `AddForce` are not called.
- While a mushroom update or shockwave update is running, destroys of the vapor, the decal, and the component are ignored. Destroying the watched root still works, which is how a rewind removes a missile burst. A shockwave with no mushroom is allowed to remove itself when its alpha finishes. Renderers the fireball switched off are enabled again when the age jumps backward.
- `BlastManager.AddBlast` is skipped during replay. That call paints a scorch into the live map texture, and a missing blast manager would throw out of `Start` and stop the shockwave. The shockwave mesh and the ground decal still run.
- `ReplayBlast` only registers the object and logs `Nuke age= ...` a few times. It does not scale anything.

Unconfirmed until an in-game log: the vapor mesh's authored shape, the decal projector size on the prefab, and the exact animation-curve times. The kiloton numbers are confirmed by the first recording: `GPO-N (250kt)` is `NuclearBomb1_strategic` / `explosion_250kt` at 250000000 kg, and `GPO-N (1.5kt)` is `NuclearBomb1` / `explosion_1kt` at 1500000 kg. The vapor scales in that log are uniform.

## Camera and the cloud that followed it

`FloatingOrigin.OriginShift` subtracts the camera position, on a 64 m grid, from every root once the camera is more than 1024 m from the Unity origin on any axis. `MushroomCloud` and `Shockwave` do not move the camera, do not change `Time.timeScale`, and do not enable a cinematic mode. `Shockwave.Update` only points the vapor at `CameraStateManager`.

The stuck camera is the TV state. The first spawned object in this file is the 250 kt bomb (`Airframes in eigene Queue: 0`). Follow calls `SetFollowingUnit`, which the ordnance block skips, and then always calls `SwitchState(TVState)` because that field is never null. `SwitchState` assigns `currentState` before `EnterState`. `CameraTVState.EnterState` then dereferences `followingUnit` and throws. The log line is `Follow: Exception has been thrown by the target of an invocation`, once per bomb, before either `Boom`. The camera is left in TV mode: `UpdateState` zeroes `cameraVelocity` and plants the pivot on the ground linecast, so it cannot be flown, and a later successful shot or origin shift moves it to an unrelated place.

The cloud follows the camera because the cap position was saved as a Unity world point and written back every frame. After an origin shift the world and the camera move and that saved point does not, so the cap stays beside the camera. The rise is now stored relative to the datum, and the effect is parented to the datum the way `Missile.Warhead.Detonate` parents it. A bomb is no longer followed, and a TV state with no aircraft target is returned to the free camera.
