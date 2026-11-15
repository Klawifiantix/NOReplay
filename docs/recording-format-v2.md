# Recording format v2

Proposal only. No recorder or playback code changes in this branch.

Kevin's decision: drop Tacview compatibility and make the mod as cheap as possible at record time and at load time. Old `.acmi` files do not need to keep playing. A one-off converter is optional and is cheap, because `AcmiParser` already turns a file into a `ReplayDocument`.

Numbers below are either a **measurement** of a file or a workstation run, or an **estimate** from the source. Each one is marked.

## Files measured

All of these are zip archives with one ACMI text entry. The four older ones were attached as `.acmi` but the bytes start with `PK`.

| File | Mission clock | Text | Zip entry | Zip / text |
|---|---:|---:|---:|---:|
| Free Feldspar, 2026-10-24 | 2169 s (36.2 min) | 7,392,559 | 2,323,415 | 31.4% |
| Point Blank, 2026-10-28 | 619 s (10.3 min) | 5,989,992 | 1,789,110 | 29.9% |
| Carrier landing, 2026-11-02 | 332 s (5.5 min) | 464,543 | 117,485 | 25.3% |
| Free Flight Heartland, 2026-10-27 | clock stayed at 0 | 111,150 | 27,559 | 24.8% |
| Tank Busters, 2026-11-15 | 12.4 s | 441,243 | 101,144 | 22.9% |
| Altercation, 2026-11-15 | 16.3 s | 180,055 | 46,799 | 26.0% |
| BBB, 2026-11-15 | 25.1 s | 39,570 | 7,610 | 19.2% |

The two long sorties are the ones the rates below trust. The November files are spawn dumps: almost every building and vehicle is written once, then the recording stops, so "bytes per minute of file" on those is not a flight rate.

Header of every file is `FileType=text/acmi/tacview`, `FileVersion=2.2`, `DataRecorder=NOBlackBox 0.3.8.5`. That string is written in `RecordWriter` (`NOReplay/RecordWriter.cs` around the `FileType` / `DataRecorder` lines).

## What the recorder does

Recording is still a Tacview 2.2 text file, zipped only when the sortie ends.

1. Each live unit gets its own `GameObject` and a `Record*` component (`RecordEngine.RegisterUnitFromListener`). Buildings turn that component off after the spawn line (`RecordBuilding.Init` sets `enabled = false`). Aircraft, vehicles, ships, missiles, and shells keep `Update`.
2. `Update` waits for a per-type interval, then fills a `Dictionary<string,string>` and calls `RecordEngine.invokeWriterUpdate`.
3. `RecordWriter.UpdateObject` runs on the main thread. If the timestamp changed it writes a `#<seconds>` line, then `id,key=value,key=value`. `StringifyProps` copies a static `StringBuilder` into a new string, and the call site interpolates another string. Both go on `RecordFileWriter`'s queue.
4. A background task writes those strings with `StreamWriter`. Flush is requested only when `AutoSaveInterval` seconds have passed. That setting's minimum is 60 (`RecordConfig`, `DefaultAutoSaveInterval`).
5. `Close` waits for the queue, then `RecordZip.ZipFileAsync` deflates the whole text at `CompressionLevel.Optimal` and deletes the text. A crash before `Close` leaves the raw text, flushed at most once a minute. A crash during the zip leaves no finished archive.

Sample intervals, from `RecordConfig` defaults:

| Kind | Default interval | What the long files actually wrote |
|---|---:|---|
| Aircraft | 0.2 s | Feldspar 4.94 pose samples/s across aircraft life. Point Blank 4.00. **Measurement.** |
| Vehicle, ship | 1 s | Vehicles only write when the rounded pose changes, so the mission average is lower: Feldspar 0.32 pose/s, Point Blank 0.20. Ships ~1/s. **Measurement.** |
| Missile | 0.2 s | Feldspar 4.77/s, Point Blank 2.91/s. **Measurement.** |
| Shell (`RecordMissile`, physical shell) | 0.05 s | Not separated in these files. **Code, not measured.** |
| Shockwave | 0.016 s, discovery scan every 0.5 s | Feldspar has none. Point Blank has one sample. **Code says 60 Hz while a wave lives. These files do not show that cost.** |
| Flare | 1 s | Short. |
| Building, scenery | config says 1 s | The component disables itself. One registration line, then nothing until disable, faction change, fire, or boom. **Code.** |

`UpdatePose` (`RecordUnit.cs`) rounds position and euler angles to 0.01 and skips the line when both are unchanged. The quaternion is not rounded. It is four full `float.ToString()` calls. A Feldspar aircraft sample looks like:

```text
C8,T=-16883.92|12.37|-10372.95,Q=-0.005196869|0.2564157|0.003779415|0.9665453,TAS=0.01,Mach=0,AOA=-84,27,...
```

`Q` on Feldspar is 2,364,963 bytes, 32% of the text, 51,068 values, median 8 digits after the decimal. **Measurement.** Position is already at centimetre resolution (`T` median length 24). The recorder then throws that resolution away by storing a full-precision quaternion beside it.

`AOA` is formatted with `ToString("0.##")` and no invariant culture (`RecordAircraft.UpdateAircraft`). The Feldspar line above contains `AOA=-84,27` and a time line `#1,02`. That is a German locale comma. Other fields use `CultureInfo.InvariantCulture`. Binary storage removes this class of bug.

`RepeatPoseIfStill` writes a fresh `T` and `Q` whenever RPM or throttle is recorded while the aircraft is not moving. The comment in `AcmiParser` says a ground start would otherwise be silent. The pose is repeated so a text property can ride along.

## Where the bytes go

Property bytes, **measured** by splitting each line on unescaped commas.

Feldspar (7.39 MB text, 36.2 min):

| Bucket | Bytes | Share |
|---|---:|---:|
| Pose (`T`, `Q`) | 4,512,787 | 68.3% |
| Telemetry | 1,938,659 | 29.4% |
| Identity (name, type, coalition, …) | 95,105 | 1.4% |
| Other | 47,719 | 0.7% |
| Events (`Fire`, `Boom`, `Visible`, `Dying`, `Sinking`, turrets, chutes, …) | 9,122 | 0.1% |
| `#` time lines | 355,187 | 4.8% of the file |
| Removal lines (`-id`) | 5,306 | |

Point Blank is the busy case: pose 47.5%, telemetry 47.1%, events 2.7%, identity 1.3%. Time lines are 126 KB of 5.99 MB.

Lifetime-weighted rates (**measurement**, sum of bytes divided by sum of each unit's life):

| Kind | Feldspar | Point Blank | What that means |
|---|---:|---:|---|
| Aircraft | 62.6 KiB/min | 46.6 KiB/min | One aircraft that is actually being sampled. Feldspar's four aircraft were up the whole sortie. |
| Missile | 30.3 KiB/min | 18.4 KiB/min | In-flight only. A shot that dies in a few seconds is a few kilobytes, not this rate times the mission. |
| Ordnance (bombs, rockets, torpedoes) | none in this file | 24.7 KiB/min | Same idea. |
| Ship | 4.2 KiB/min | 5.4 KiB/min | About the 1 Hz vehicle clock, and they keep moving. |
| Vehicle | 1.44 KiB/min | 1.14 KiB/min | Includes vehicles that sit still and therefore write nothing. A vehicle whose pose changes every sample is about 4–5 KiB/min (77 bytes/line × 60). |
| Building | ~166 B once | ~165 B once | Feldspar 161 buildings, 26,913 bytes. Tank Busters 931 buildings, 177,558 bytes, median 181 B. |

Whole-file growth, **measured**: Feldspar 200 KiB/min of text and 63 KiB/min zipped. Point Blank 567 KiB/min of text and 169 KiB/min zipped, because it had 21 aircraft rather than 4.

Events are not the file. Fire, boom, gear, yield, kill feed, and chat have to survive exactly, and together they are about 0.1–3% of a long file. Buildings are not the file either, except in the first second of a mission that places hundreds of them.

### Keys playback never reads

Grep of the playback project: `AcmiParser` stores `T`, `Q`, `TAS`, `Throttle`, `RpmRatio`, `JetRpm`, `TurbineRpm`, `AGL`, `LandingGear`, `Tilt`, `Tailhook`, `Fire`, `Boom`, `Visible`, `Dying`, `Sinking`, `Yield`, `Turrets`, `Lasers`, `Chute`, `Doors`, `Glass`, the gun-burst and part-break fields, `Map`, and the name/type/coalition/pilot/loadout/livery strings. `FeedLog.Collect` scans the raw text a second time for `Feed=`. `ReplayGhost.Airframe` and `ReplayGhost.MissileAudio` consume TAS, throttle, RPM, gear, AGL, and tailhook.

Nothing in playback reads `Mach`, `AOA`, `PitchControlInput`, `RollControlInput`, `YawControlInput`, `EngineRPM`, `PilotHeadPitch`, `PilotHeadYaw`, `RadarMode`, `LockedTarget`, `Debug`, `Color`, or `EngagementRange`. `Mach` is `TAS/340` written beside `TAS`.

On Feldspar those unread keys are **1,412,547 bytes, 19.1% of the text**. **Measurement.** They are a Tacview leftover. Deflate already compresses `PitchControlInput=0` well, so deleting them shrinks the zip by less than 19%. The recording cost is real either way: each change allocates a string on the main thread.

## Recording CPU

Not profiled in the game. This is the path, counted from code, with the file as a rate check.

Feldspar writes 103,231 lines in 2169 s, **47.6 lines/s**. Point Blank writes **91 lines/s**. **Measurement.**

Every one of those lines, on the main thread (`RecordWriter.UpdateObject` / `StringifyProps`):

- one `ToString()` of the property builder
- one interpolated `id,props` string
- sometimes a `#time` string
- the `T` and `Q` strings built in `UpdatePose` (the quaternion is four unbounded float conversions)
- a `ConcurrentQueue` node

The background thread only encodes UTF-8 and writes. That part is already off the frame. The GC is the main-thread strings. At Feldspar's 48 lines/s this is hundreds of short-lived strings per second, not thousands. It becomes a frame hitch when many shells take the 0.05 s path at once, or when a shockwave takes the 0.016 s path. **Estimate, from the intervals in `RecordMissile` and `RecordShock`. Not observed in these files.**

`RecordShock.ShockWaveDiscovery` calls `FindObjectsByType<Shockwave>` every 0.5 s even when nothing is exploding. Unit discovery by scan is commented out; units arrive through `RecordListener`.

A parked aircraft still wakes `Update` every frame to add `deltaTime`. Buildings do not. A Tank Busters start with 400 vehicles is 400 `Update` calls per frame that usually return at the interval check.

## Load, memory, seek

`AcmiParser.Load` unzips the entry and `ReadToEnd()`s it into one string, then `Parse` walks every character. `FeedLog.Collect` walks that string again. For each object line the parser allocates the id with `Span.ToString()` (`AcmiParser`, the `objId` assignment). A pose or telemetry line then copies a `ReplaySample` onto `List<ReplaySample>`.

`ReplaySample` is a struct with 7 pose floats, 4 quaternion floats, 9 optional floats each preceded by a `bool`, and 4 tilt floats. With default sequential layout that is **about 136 bytes per sample**. **Estimate** (alignment counted by hand; `Marshal.SizeOf` was not run). The list stores the struct inline, so there is no per-sample object header, but capacity doubling can hold up to twice that.

Feldspar has 63,159 stored samples. 63,159 × 136 ≈ **8.4 MB** of samples after parse. The UTF-16 string of the 7.39 MB file is about **14.4 MB** while `Parse` runs. **Estimate** from `string` being UTF-16.

Workstation check, not the game, .NET 8 Release, one run each: unzip plus `ReadToEnd` of Feldspar took **473 ms** and left **14.9 MB** on the GC heap. Point Blank took **599 ms** and left **12.1 MB**. A span walk that also allocated one id string per object line added **8–9 ms**. The real parser does more per line (property scan, sample copy, dictionaries, `StretchSampleTimes` allocates a `float[]` per object), so in-game load is this string plus that work. The measured part is already "allocate the whole text."

Seek does not touch the file. `ReplayClock.Seek` moves `Time` and bumps `SeekSerial`. Playback of a sample is `SampleIndex.Bracket`: the cursor makes a forward frame a couple of comparisons, and a jump is a binary search over that unit's list (`SampleIndex.cs`). Going backward sets `WentBack`, and `Plugin.Update` calls `ResetParticles`. `ApplyOrdnanceSeeks` calls `OnReplaySeek` on each ghost. That visual reset is the seek cost, and a new file format does not remove it. A seek index matters once the whole sortie is no longer decoded up front, and for opening a crashed file. It is not what makes today's 5-second skip expensive.

## Proposed file

Extension `.norp`. Little-endian. Versioned. No Tacview keys, no text lines, no locale.

### Layout

```text
'NORP' u32
version u16          = 1
header
string table
unit table
chunk...
index                at the end, so a writer can append
```

The index is also the thing a reader seeks to. Each chunk starts with its own length and start time, so a file whose index was never written (crash, kill) can be recovered by scanning chunks that were flushed.

**Header.** Mission name, map id, time of day, conditions, cloud height, wind, turbulence, moon, reference time, duration. Same facts as today's `0,...` global line. Duration is patched when the file closes; a crashed file uses the last chunk's end time.

**String table.** Names, types, coalitions, pilot names, callsigns, loadouts, liveries, map unique names, part paths, chat text. One UTF-8 blob, referenced by `u16` or `u32` index. A building line stops repeating `Ground+Static+Building` as characters. 931 buildings are then an id plus a pose, not ~180 bytes of text each.

**Unit table.** Stable `u32` id for the sortie (not a Tacview hex id). Type enum: aircraft, vehicle, ship, building, scenery, missile, bomb, shell, gun burst, flare, pilot, container, part, other. String ids for name, type text (kept until every prefab lookup uses the enum), coalition, pilot, loadout, livery, map id. Parent id. Spawn time. The hex id and `Color`/`Debug`/`EngagementRange` go away.

**Records inside a chunk.** A chunk covers a time span (1 second is a good start) and is deflated on its own. `System.IO.Compression` already does this for the zip; no new native library for v1. LZ4 is faster to read and worth a later version if the load trace says inflate is the wait. It should not be a dependency in the first format PR.

Each record:

- `u16` time delta in milliseconds from the chunk start (a chunk under 65 s)
- `u16` unit slot (unit table index; a long sortie with more than 65535 units bumps the version)
- `u8` opcode
- payload

Opcodes, covering everything playback uses today:

| Opcode | Payload | Replaces |
|---|---|---|
| Pose | flags, xyz, quat | `T` and `Q` |
| Telemetry | bitset + the channels that changed | `TAS`, `Throttle`, `RpmRatio`, `JetRpm`, `TurbineRpm`, `AGL`, `LandingGear`, `Tilt`, `Tailhook` |
| Fire, Boom | none, or a part id | `Fire=1`, `Boom=1` |
| Visible | `u8` | `Visible=0` |
| Dying, Sinking | none | `Dying=1`, `Sinking=1` |
| Yield | `u32` centigrams, or `f32` if a warhead needs the fraction | `Yield` |
| Turrets, Lasers, Doors, Glass, Chute | the same fields those clocks parse now, as numbers | the `;`-separated strings |
| Gun burst / part | rpm, muzzle, drag, gravity, spread, part, path string, segment, rotor, velocity | today's burst line |
| Feed | kind byte, colors, two string ids | `Feed=` |
| Trap | the untrap/trap fields | `Event=` |
| Remove | none | `-id` |

A telemetry change does **not** repeat the pose. The reader keeps the last pose, which is what `AcmiParser` already does when it clones the previous sample. That deletes `RepeatPoseIfStill`.

### Quantization

Match the resolution the recorder already keeps.

- Position: centimetres, `i32` from the datum. A delta from the last keyframe is `i16` centimetres. 327 m per sample is enough at 5 Hz (that would be 1.6 km/s). Overflow, or one second since the last keyframe, writes a full `i32` keyframe.
- Quaternion: smallest-three, three `i16` and a 2-bit index of the dropped component. The reconstructed value is within about 1/32767, which is finer than the 0.01 degree euler gate that decides whether a sample is written at all.
- TAS, AGL, throttle, RPM, gear, tilt, tailhook: `i16` with a fixed scale (centimetres for AGL, 1/1000 for 0..1 channels, 0.1 m/s for TAS). Channels that did not change are absent from the bitset.

### Sample rates

Keep today's intervals in the first version so a visual change can be blamed on the codec, not on a new rate. Per-unit rates are already the design: an aircraft at 5 Hz and a parked vehicle at "only when it moves." A later pass can drop a straight-line aircraft to 2 Hz and keep 10 Hz through a turn. Shells stay on their own interval. Shockwaves should be one event (origin, max radius, speed), not a 60 Hz radius stream, unless playback still draws the ring from samples. Today's files suggest playback barely uses them.

### Write path

The main thread writes into an `ArrayPool<byte>` buffer. No `Dictionary<string,string>`, no `ToString` of floats. The buffer is enqueued. The background thread deflates a full chunk and appends it. Flush a chunk about once a second, not once a minute, so a crash keeps the sortie. Patch the index and the duration on close. This is the same shape as `RecordFileWriter`, with bytes instead of strings.

### Read path and seek

For v1, read the index, inflate every chunk, and fill the same `ReplayDocument` the ghosts already use. Feldspar's packed samples should land around 1–2 MB instead of an 8 MB `ReplaySample` list plus a 14 MB string. **Estimate**, checked against the prototype below. Ghosts, pause, speed, reverse, and the particle reset stay.

A later read path can keep chunks packed and inflate the one that brackets `ReplayClock.Time`. That is when the index becomes the seek path. Do not do it in the same PR as the switch: the ghosts assume a random-access sample list.

### Prototype size (throwaway encoder, not the game)

A Python packer ran on the real files. It dropped the unread keys, stored position as centimetre integers (int16 delta, int32 keyframe), stored the quaternion as smallest-three int16, stored telemetry as int16, and still wasted a float32 time and a string id on every telemetry value. Then it deflated the blob.

| File | Today's zip | Packed, then deflate | Smaller than today's zip |
|---|---:|---:|---:|
| Feldspar | 2,323,415 | 1,044,960 | 2.2× |
| Point Blank | 1,789,110 | 774,485 | 2.3× |
| Tank Busters | 101,144 | 62,716 | 1.6× |

**Measurement of that packer.** A tighter record (bitset, millisecond delta, no per-value key id) should beat this. It will not beat it by another 10×. The text is already deflated to ~30%, and the remaining bytes are real numbers. Expect about **2× smaller than today's zip**, and about **7× smaller than the text**, for a long sortie. Tank Busters gains less because it is mostly names of buildings that spawn and sit.

The load win is larger than the disk win: no 14 MB string, no per-line id allocation, samples that are a few dozen bytes instead of 136.

## Optimizations that do not need the new file

Do these even if the format slips, and do the first one before the format so the property list is frozen.

1. Stop writing the unread keys listed above. One flight tells us whether engine spool, gear, throttle, and TAS still play. They are the keys playback actually reads.
2. Quantize `Q` to three decimal places while the file is still text, only if (1) is not enough and the format PR is waiting. Half the digits are below the euler gate. Skip this if the binary PR is next; doing both confuses the comparison.
3. Do not write `EngineRPM=NaN`.
4. Format every remaining number with invariant culture. `AOA` is the one that does not.
5. One sampler component that walks the live units, instead of a `MonoBehaviour` per unit. Buildings already opt out. Vehicles and shells should too.
6. Shockwave: record the blast once, or not at all, until something in playback draws `Radius`. The 60 Hz path and the 0.5 s `FindObjectsByType` are the spikiest recording code, and the sample files show almost no payoff.
7. Split telemetry from pose in the text writer only if the binary writer is not the next change. `RepeatPoseIfStill` exists so RPM can share a line with a position the parser requires.

Pooling dictionaries and pooling the `StringBuilder` do not matter. The builder is already static. The garbage is the strings it produces.

## Migration

Each step keeps pause, seek, reverse, speed, and the effects that already play. No step has to read an old `.acmi` during a fight. Old files simply do not appear in the replay list once the extension changes.

A converter is a function from `ReplayDocument` to the new writer. `AcmiParser` is the front half. It is one fixture-driven test plus a small command, not a compatibility layer. Worth having so a known Feldspar file can be packed and played without re-flying 36 minutes. Not required for Kevin's rule.

| Step | What Kevin tests in game | Scope | Risk |
|---|---|---|---|
| 1 | Record the same short flight. Engines, gear, throttle, a missile, a building, the feed. File should shrink. No new extension. | Delete the unread `props.Add` calls. | Low. If some HUD was secretly using `CallSign` only, that stays. The dropped keys have no reader. |
| 2 | Tests only, plus an optional converter run on Feldspar. Not a gameplay change. | Define the opcodes and a reader/writer that fill `ReplayDocument`. Round-trip a synthetic file against `AcmiParser` field by field: pose, gear, RPM, fire, boom, yield, chute, doors, feed, map id. | Low for the game. This is the checklist so step 3 does not forget a clock. |
| 3 | Record a new sortie and play it. Same rates, no quantization yet (store `f32`). Motion, a kill, fire that fades, a ship sink, a building wreck, seek back five seconds, reverse, 4×. | `RecordWriter` emits `.norp`. `AcmiParser.Load` is replaced for that extension. Ghosts stay. | Medium. A missed opcode shows up as a missing effect, not as a corrupt file. The test from step 2 is the net. |
| 4 | Same sortie, or the converted Feldspar. Look at a landing and a hover. | Turn on centimetre positions, smallest-three quats, int16 channels, keyframes. | Medium. A bad quat sign shows up as a flipped aircraft. Keyframe every second limits the drift. |
| 5 | Record, pull the plug or kill the game, open what was flushed. Then play normally and seek. | Chunked deflate, index at end, pooled buffers, flush about once a second. | Medium. An index bug loses the tail, which is the same failure as today's unflushed minute, only shorter. |
| 6 | A big mission start (Tank Busters) and a shell fight. Frame time while recording, not the file. | One sampler. Shockwave recorded once. Shells stay at 0.05 s until a visual pass says they can be slower. | Low if steps 3–5 are already trusted. Do not change rates in step 3 or 4. |

Do not combine step 3 and step 4. A jittery aircraft would otherwise be "the format" when it is the quantizer, or the reverse.

## Gains, honestly

| | Confidence | Why |
|---|---|---|
| About 2× smaller than today's zip on a long sortie | Medium-high | Measured 2.2× and 2.3× with a clumsy packer on Feldspar and Point Blank. A tighter layout improves this. It will not reach 10×. The text is already deflated. |
| About 7× smaller than the raw text | High | Same runs. |
| Load stops allocating a 14 MB string | High | `ReadToEnd` is the loader. Workstation: 473 ms and 14.9 MB for Feldspar, before the real property parse. |
| Sample memory drops from ~136 bytes toward a few dozen | Medium | 136 is a layout estimate. The packed Feldspar blob was 1.73 MB before deflate for 63k poses plus telemetry, versus ~8.4 MB of `ReplaySample`. |
| Recording GC drops on the main thread | High that the strings go away, medium on how many milliseconds that is | The strings are real (`StringifyProps`, `UpdatePose`). At 50–90 lines/s the frame cost is probably small until shells or shockwaves spike. Not profiled in game. |
| Seek and reverse get faster | Low for the current files | Seek is already an in-memory cursor plus a particle reset. The index helps a crashed file and a future streaming reader. |
| Unread-key deletion, before any new format | High | 19% of Feldspar text, and no playback reader. Zip shrinks by less than 19%. |

## Risks

- Quantizing the quaternion badly flips an aircraft. Mitigation: keyframe every second, and a test that a round-trip stays within the euler gate.
- Dropping `PilotHead` and the stick inputs is dropping data a future cockpit camera might want. Playback does not use them now. If that feature is planned, keep them as an optional telemetry bit, off by default.
- Shells at 0.05 s can still dominate a fight after the format change. The format makes each sample cheap; it does not make 20 Hz free. Rate changes are step 6, with a visual check.
- The zip-on-close stall goes away only in step 5. Step 3 still has to finish the file on the way out, but it should not deflate 7 MB of text.
- `ReplaySample` can stay as the in-memory form for step 3 so ghosts do not change. The memory win is then only the load spike. The packed in-memory form is a follow-up, not part of the switch.

## First step

Step 1. Stop writing the keys playback never reads, in the current ACMI writer, and fly one sortie.

It is a small, reversible recording change. It freezes the list of facts the binary file has to carry. It does not depend on a new parser, and it cannot break seek, reverse, fire, or boom. The format work starts at step 2, against that shorter list.
