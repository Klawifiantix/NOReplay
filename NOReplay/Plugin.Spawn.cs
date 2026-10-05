using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static bool _spawnLogged;
        static bool _gameSpawnDead;
        static readonly List<ReplayObject> _flareEvents = new List<ReplayObject>();
        static readonly HashSet<ReplayObject> _flareDone = new HashSet<ReplayObject>();
        static float _flareClock = -1f;

        static void CycleFollow(int delta)
        {
            if (_ghosts.Count == 0) return;
            _followIndex = (_followIndex + delta + _ghosts.Count) % _ghosts.Count;
            _ghost = _ghosts[_followIndex];
            _follow = true;
            Log.LogInfo("Follow " + _ghost.name);
            try { TryFollowGhost(_ghost); }
            catch (Exception ex) { Log.LogWarning("Follow: " + ex.Message); }
        }

        static bool _ordnanceWarmed;
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Queue<GameObject>> _pool
            = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Queue<GameObject>>();

        static readonly HashSet<int> _ordnancePrepared = new HashSet<int>();

        static readonly List<(GameObject prefab, int left)> _warmLeft = new List<(GameObject, int)>();

        static readonly Dictionary<int, int> _poolOwner = new Dictionary<int, int>();

        static GameObject? TakePooled(GameObject? prefab, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return null;
            if (!_pool.TryGetValue(prefab.GetInstanceID(), out var q)) return null;
            while (q.Count > 0)
            {
                var go = q.Dequeue();
                if (go == null) continue;
                StripCameras(go);
                go.transform.SetPositionAndRotation(pos, rot);
                go.SetActive(true);
                StripCameras(go);
                ReleaseCameraIfOn(go);
                _poolOwner[go.GetInstanceID()] = prefab.GetInstanceID();
                return go;
            }
            return null;
        }

        internal static void ReturnOrdnance(GameObject go)
        {
            if (go == null) return;
            StripCameras(go);
            ReleaseCameraIfOn(go);
            go.SetActive(false);
            if (!_poolOwner.TryGetValue(go.GetInstanceID(), out var prefabId)) return;
            if (!_pool.TryGetValue(prefabId, out var q)) return;
            q.Enqueue(go);
        }

        void WarmOrdnance()
        {
            if (_warmLeft.Count == 0) return;
            int step = _holdClock ? 4 : 2;
            for (int n = 0; n < step && _warmLeft.Count > 0; n++)
            {
                var job = _warmLeft[0];
                var made = MakePooled(job.prefab);
                if (made != null)
                {
                    if (!_pool.TryGetValue(job.prefab.GetInstanceID(), out var q))
                    {
                        q = new Queue<GameObject>();
                        _pool[job.prefab.GetInstanceID()] = q;
                    }
                    q.Enqueue(made);
                    _poolOwner[made.GetInstanceID()] = job.prefab.GetInstanceID();
                }
                job.left--;
                if (job.left <= 0) _warmLeft.RemoveAt(0);
                else _warmLeft[0] = job;
            }
        }

        static readonly HashSet<int> _warmSeen = new HashSet<int>();

        static void WarmLoadout(ReplayObject obj)
        {
            if (string.IsNullOrEmpty(obj.Loadout)) return;
            int n = 0;
            foreach (var part in obj.Loadout.Split('|'))
            {
                var bits = part.Split(':');
                if (bits.Length < 2 || string.IsNullOrEmpty(bits[1])) continue;
                string key = bits[1];
                if (key.IndexOf("Truck", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (key.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (key.IndexOf("6x6", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (key.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (key.IndexOf("Gunpod", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (key.IndexOf("Hook", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var probe = new ReplayObject { Name = key, Type = "Weapon+Missile" };
                var d = FindAnyDef(probe);
                var prefab = d != null ? GetDefPrefab(d) : null;
                if (prefab == null) prefab = PrefabFromMount(FindWeaponMount(key));
                if (IsAirframePrefab(prefab)) prefab = null;
                if (prefab != null && prefab.name.IndexOf("Tarantula", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (prefab != null && prefab.name.IndexOf("VTOL", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                n += QueuePrefab(prefab, _warmSeen);
            }
            if (n > 0)
                Log.LogInfo("Waffen warm aus Loadout " + obj.Name + " arten=" + n);
        }

        static bool IsAirframePrefab(GameObject? prefab)
        {
            if (prefab == null) return false;
            var ac = FindGameType("Aircraft");
            return ac != null && prefab.GetComponentInChildren(ac, true) != null;
        }

        static int QueuePrefab(GameObject? prefab, HashSet<int> seen)
        {
            if (prefab == null || !seen.Add(prefab.GetInstanceID())) return 0;
            _warmLeft.Add((prefab, 32));
            return 1;
        }

        static GameObject? PrefabFromMount(object? mount)
        {
            if (mount == null) return null;
            if (mount is GameObject go) return go;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in mount.GetType().GetFields(flags))
            {
                if (f.GetValue(mount) is GameObject prefab) return prefab;
            }
            return null;
        }

        static GameObject? MakePooled(GameObject prefab)
        {
            var go = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, -8000f, 0f), Quaternion.identity);
            go.SetActive(false);
            return go;
        }

        static UnityEngine.Object? FindAnyDef(ReplayObject obj)
        {
            string key = (obj.Name ?? "") + "|" + (obj.Type ?? "");
            if (_defCache.TryGetValue(key, out var cached)) return cached;
            string t = obj.Type ?? "";
            UnityEngine.Object? found;
            if (IsFlareType(t, obj.Name))
                found = FindFlarePrefab();
            else if (IsOrdnance(t, obj.Name))
                found = FindDefInTypes(obj, "MissileDefinition", "WeaponDefinition", "BombDefinition", "UnitDefinition");
            else if (IsAircraft(t, obj.Name))
                found = FindDefInTypes(obj, "AircraftDefinition");
            else if (IsScenery(t, obj.Name))
                found = FindDefInTypes(obj, "SceneryDefinition", "BuildingDefinition", "UnitDefinition");
            else if (IsEmplacement(t, obj.Name))
                found = FindDefInTypes(obj, "VehicleDefinition", "BuildingDefinition", "UnitDefinition");
            else if (IsBuilding(t, obj.Name))
                found = FindDefInTypes(obj, "BuildingDefinition", "SceneryDefinition", "UnitDefinition");
            else if (t.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0
                  || t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0)
                found = FindDefInTypes(obj, "ShipDefinition", "UnitDefinition");
            else
                found = FindDefInTypes(obj, "VehicleDefinition", "UnitDefinition");
            _defCache[key] = found;
            if (found != null)
            {
                Log.LogInfo("Def " + obj.Name + " -> " + found.name + " (" + found.GetType().Name + ")");
                if (IsOrdnance(obj.Type, obj.Name))
                    DumpDefPrefabs(found);
            }
            else
                Log.LogWarning("Keine Def für " + obj.Name + " typ=" + obj.Type);
            return found;
        }

        static bool IsFlareType(string? type, string? name)
        {
            string t = type ?? "";
            string n = name ?? "";
            if (n.IndexOf("Stack", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0
                || ((n.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0
                     || n.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0)
                    && t.IndexOf("Misc", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static UnityEngine.Object? _flarePrefab;

        static UnityEngine.Object? FindFlarePrefab()
        {
            if (_flarePrefab != null) return _flarePrefab;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var ej = FindGameType("FlareEjector");
            if (ej != null)
            {
                GameObject? fromLive = GrabFlareFromEjectors(ej, flags, sceneOnly: true);
                if (fromLive != null)
                {
                    _flarePrefab = fromLive;
                    Log.LogInfo("Flare-Prefab (live) " + fromLive.name);
                    return fromLive;
                }
                GameObject? fromAsset = GrabFlareFromEjectors(ej, flags, sceneOnly: false);
                if (fromAsset != null)
                {
                    _flarePrefab = fromAsset;
                    Log.LogInfo("Flare-Prefab (asset) " + fromAsset.name);
                    return fromAsset;
                }
            }
            _flarePrefab = MakeReplayFlare();
            Log.LogInfo("Flare-Prefab Fallback NOReplayFlareTpl");
            return _flarePrefab;
        }

        static GameObject? GrabFlareFromEjectors(Type ej, BindingFlags flags, bool sceneOnly)
        {
            foreach (var c in Resources.FindObjectsOfTypeAll(ej))
            {
                if (c == null) continue;
                var comp = c as Component;
                if (sceneOnly)
                {
                    if (comp == null || !comp.gameObject.scene.IsValid()) continue;
                    if (comp.GetComponentInParent<ReplayGhost>() == null) continue;
                }
                foreach (var f in c.GetType().GetFields(flags))
                {
                    if (!typeof(GameObject).IsAssignableFrom(f.FieldType)) continue;
                    var go = f.GetValue(c) as GameObject;
                    if (go == null) continue;
                    string fn = (f.Name + " " + go.name).ToLowerInvariant();
                    if (fn.IndexOf("stack", StringComparison.Ordinal) >= 0) continue;
                    if (fn.IndexOf("building", StringComparison.Ordinal) >= 0) continue;
                    if (fn.IndexOf("flare", StringComparison.Ordinal) < 0
                        && fn.IndexOf("decoy", StringComparison.Ordinal) < 0
                        && fn.IndexOf("counter", StringComparison.Ordinal) < 0)
                        continue;
                    return go;
                }
            }
            return null;
        }

        static GameObject MakeReplayFlare()
        {
            var root = new GameObject("NOReplayFlareTpl");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "FlareBody";
            ball.transform.SetParent(root.transform, false);
            ball.transform.localScale = new Vector3(2.2f, 2.2f, 2.2f);
            var col = ball.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            try
            {
                var rend = ball.GetComponent<Renderer>();
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                if (rend != null && sh != null)
                {
                    var mat = new Material(sh);
                    mat.color = new Color(1f, 0.72f, 0.15f, 1f);
                    rend.sharedMaterial = mat;
                }
            }
            catch { }
            try
            {
                var light = ball.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.65f, 0.2f);
                light.range = 40f;
                light.intensity = 4f;
            }
            catch { }
            try
            {
                var trail = root.AddComponent<TrailRenderer>();
                trail.time = 1.2f;
                trail.startWidth = 0.25f;
                trail.endWidth = 0.02f;
                trail.minVertexDistance = 0.4f;
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    mat.color = new Color(1f, 0.55f, 0.1f, 0.8f);
                    trail.sharedMaterial = mat;
                }
            }
            catch { }
            root.SetActive(false);
            return root;
        }

        static UnityEngine.Object? FindDefInTypes(ReplayObject obj, params string[] typeNames)
        {
            string name = (obj.Name ?? "").Trim();
            if (name.Length == 0) return null;
            string nlow = name.ToLowerInvariant();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            UnityEngine.Object? contains = null;
            foreach (var tn in typeNames)
            {
                var defType = FindGameType(tn);
                if (defType == null) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(defType))
                {
                    if (o == null) continue;
                    string unitName = defType.GetField("unitName", flags)?.GetValue(o) as string
                                      ?? defType.GetField("Name", flags)?.GetValue(o) as string
                                      ?? o.name;
                    if (string.IsNullOrEmpty(unitName)) continue;
                    string ulow = unitName.ToLowerInvariant();
                    if (ulow == nlow) return o;
                    string ncmp = nlow.Replace(" ", "").Replace("-", "");
                    string ucmp = ulow.Replace(" ", "").Replace("-", "");
                    if (ucmp == ncmp) return o;
                    if ((nlow.Contains(ulow) || ulow.Contains(nlow)) && ulow.Length >= 4 && contains == null)
                        contains = o;
                }
            }
            return contains;
        }

        static void SpawnFirstGhost()
        {
            if (CurrentDoc == null) { Log.LogWarning("Zuerst F8."); return; }
            ClearGhosts();
            int n = 0;
            foreach (var obj in CurrentDoc.Objects)
            {
                if (!IsAircraft(obj.Type, obj.Name)) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "NOReplay_" + obj.Name;
                go.transform.localScale = new Vector3(15f, 4f, 20f);
                var rg = go.AddComponent<ReplayGhost>();
                rg.Track = obj;
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                _ghosts.Add(go);
                n++;
            }
            _ghost = _ghosts.Count > 0 ? _ghosts[0] : null;
            _followIndex = _ghosts.Count > 0 ? 0 : -1;
            Log.LogInfo("Ghosts erzeugt: " + n);
        }

        static void SpawnAllAircraft()
        {
            if (CurrentDoc == null) { Log.LogWarning("Kein Replay."); return; }
            DumpSession("Spawn All");
            if (FindLocalNetworkPlayer() == null)
            {
                Log.LogWarning("Kein Live-Player. Erst leere Mission über das Menü starten.");
                return;
            }
            ClearGhosts();
            int skip = 0;
            foreach (var obj in CurrentDoc.Objects)
            {
                if (!ShouldQueue(obj)) { skip++; continue; }
                if (IsFlareType(obj.Type, obj.Name))
                {
                    _flareEvents.Add(obj);
                    continue;
                }
                _pendingAir.Add(obj);
            }
            Log.LogInfo("Queued units: " + _pendingAir.Count + " skip=" + skip);
        }

        static void ClearGhosts()
        {
            foreach (var g in _ghosts)
            {
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg != null) UnityEngine.Object.Destroy(rg);
            }
            _ghosts.Clear();
            _ghost = null;
            _pendingAir.Clear();
            _flareEvents.Clear();
            _flareDone.Clear();
            _flareClock = -1f;
            _followIndex = -1;
            _follow = false;
            _spawnLogged = false;
            _gameSpawnDead = false;
        }

        static void TickFlareEvents()
        {
            if (_flareEvents.Count == 0 || CurrentDoc == null) return;
            float t = Clock.Time;
            if (_flareClock >= 0f && t + 0.2f < _flareClock)
                _flareDone.Clear();
            _flareClock = t;
            int n = 0;
            for (int i = 0; i < _flareEvents.Count && n < 2; i++)
            {
                var obj = _flareEvents[i];
                float born = TrackBorn(obj);
                if (t + 0.05f < born) continue;
                if (_flareDone.Contains(obj)) continue;
                _flareDone.Add(obj);
                try { PlayAirframeFlare(obj); }
                catch (Exception ex) { Log.LogWarning("Flare-FX: " + ex.Message); }
                n++;
            }
        }

        static bool _aircraftQueued;

        static void QueueOwnAircraft()
        {
            if (_aircraftQueued || CurrentDoc == null || !InGameWorld()) return;
            _aircraftQueued = true;
            int n = 0;
            foreach (var obj in CurrentDoc.Objects)
            {
                if (obj == null || obj.Samples.Count == 0) continue;
                if (!string.IsNullOrEmpty(obj.ParentId)) continue;
                string typ = obj.Type ?? "";
                if (typ.IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (typ.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (typ.IndexOf("Misc", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!IsAircraft(obj.Type, obj.Name)) continue;
                _pendingAir.Add(obj);
                n++;
            }
            Log.LogInfo("Airframes in eigene Queue: " + n);
        }

        static void TickGhostSpawns()
        {
            QueueOwnAircraft();
            TickFlareEvents();
            if (_pendingAir.Count == 0) return;
            if (FindLocalNetworkPlayer() == null)
            {
                if ((Time.frameCount % 180) == 0)
                    Log.LogWarning("Spawn wartet auf Live-Player (Mission im Menü starten).");
                return;
            }
            float t = Clock.Time;
            int budget = 8;
            for (int i = 0; i < _pendingAir.Count && (budget > 0 || IsOrdnance(_pendingAir[i].Type, _pendingAir[i].Name));)
            {
                var obj = _pendingAir[i];
                if (obj.Samples.Count == 0) { _pendingAir.RemoveAt(i); continue; }
                if (t + 0.05f < TrackBorn(obj)) { i++; continue; }
                if (AlreadySpawned(obj)) { _pendingAir.RemoveAt(i); continue; }
                bool follow = false;
                GameObject? go = null;
                if (IsFlareType(obj.Type, obj.Name))
                {
                    _pendingAir.RemoveAt(i);
                    continue;
                }
                try { go = InstantiateAircraft(obj); }
                catch (Exception ex) { Log.LogError("Instantiate " + obj.Name + ": " + ex.Message); }
                if (go == null && IsOrdnance(obj.Type, obj.Name))
                {
                    i++;
                    continue;
                }
                if (go != null)
                {
                    _ghosts.Add(go);
                    if (_ghost == null && !IsOrdnance(obj.Type, obj.Name)) _ghost = go;
                    if (follow)
                    {
                        try { TryFollowGhost(go); }
                        catch (Exception ex) { Log.LogWarning("Follow: " + ex.Message); }
                    }
                    if (_ghosts.Count <= 8 || (_ghosts.Count % 50) == 0)
                        Log.LogInfo("Spawn now " + obj.Name + " t=" + t.ToString("0.0") + " n=" + _ghosts.Count);
                }
                _pendingAir.RemoveAt(i);
                budget--;
            }
        }

        static bool AlreadySpawned(ReplayObject obj)
        {
            string id = obj.Id ?? "";
            string name = obj.Name ?? "";
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var g = _ghosts[i];
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg == null || rg.Track == null) continue;
                if (!string.IsNullOrEmpty(id) && rg.Track.Id == id) return true;
                if (IsAircraft(obj.Type, obj.Name) && rg.Track.Name == name && rg.Track.Id == id) return true;
            }
            return false;
        }

        static int _flareFxLog;

        static void PlayAirframeFlare(ReplayObject obj)
        {
            ReplaySample s;
            if (!obj.TrySample(Clock.Time, out s))
            {
                if (obj.Samples.Count == 0) return;
                s = obj.Samples[0];
            }
            Vector3 pos = GlobalToLocal(s.X, s.Y, s.Z);
            GameObject? host = null;
            float best = 250f * 250f;
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var g = _ghosts[i];
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg == null || rg.Track == null) continue;
                if (!IsAircraft(rg.Track.Type, rg.Track.Name)) continue;
                float d = (g.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; host = g; }
            }
            if (host == null)
            {
                if (_flareFxLog < 8)
                {
                    _flareFxLog++;
                    Log.LogWarning("Flare-FX kein Airframe nahe " + pos);
                }
                return;
            }
            int played = BurstFlareParticles(host);
            if (_flareFxLog < 8)
            {
                _flareFxLog++;
                Log.LogInfo("Flare-FX " + host.name + " ps=" + played + " dist=" + Mathf.Sqrt(best).ToString("0.0"));
            }
        }

        static int BurstFlareParticles(GameObject host)
        {
            int n = 0;
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? FindGameType("ParticleSystem");
            if (pt == null) return 0;
            var play = pt.GetMethod("Play", new[] { typeof(bool) }) ?? pt.GetMethod("Play", Type.EmptyTypes);
            var emit = pt.GetMethod("Emit", new[] { typeof(int) });
            foreach (var c in host.GetComponentsInChildren(pt, true))
            {
                if (c == null) continue;
                string nm = (c.name + " " + c.gameObject.name).ToLowerInvariant();
                bool hit = nm.IndexOf("flare", StringComparison.Ordinal) >= 0
                        || nm.IndexOf("decoy", StringComparison.Ordinal) >= 0
                        || nm.IndexOf("counter", StringComparison.Ordinal) >= 0
                        || nm.IndexOf("cmfx", StringComparison.Ordinal) >= 0
                        || nm.IndexOf("chaff", StringComparison.Ordinal) >= 0;
                if (!hit)
                {
                    var parent = (c as Component)?.transform.parent;
                    if (parent != null)
                    {
                        string pn = parent.name.ToLowerInvariant();
                        hit = pn.IndexOf("flare", StringComparison.Ordinal) >= 0
                           || pn.IndexOf("counter", StringComparison.Ordinal) >= 0;
                    }
                }
                if (!hit) continue;
                try
                {
                    if (play != null && play.GetParameters().Length == 1)
                        play.Invoke(c, new object[] { true });
                    else
                        play?.Invoke(c, null);
                    emit?.Invoke(c, new object[] { 12 });
                    n++;
                }
                catch { }
            }
            if (n == 0 && _flareFxLog < 4)
            {
                _flareFxLog++;
                Log.LogInfo("Flare-FX keine Flare-PS an " + host.name);
            }
            return n;
        }

        static GameObject? InstantiateAircraft(ReplayObject obj)
        {
            ReplaySample s;
            if (!obj.TrySample(Clock.Time, out s))
            {
                if (obj.Samples.Count == 0) return null;
                s = obj.Samples[0];
            }
            Vector3 pos = GlobalToLocal(s.X, s.Y, s.Z);
            Quaternion rot = Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
            var def = FindAnyDef(obj);

            GameObject? go = null;
            bool flare = IsFlareType(obj.Type, obj.Name);
            bool aircraft = IsAircraft(obj.Type ?? "", obj.Name)
                || (def != null && def.GetType().Name.IndexOf("Aircraft", StringComparison.OrdinalIgnoreCase) >= 0);
            bool ordnance = !aircraft && IsOrdnance(obj.Type, obj.Name);
            bool visualOnly = IsScenery(obj.Type, obj.Name) || IsBuilding(obj.Type, obj.Name);
            if (ordnance && !flare)
                go = TryGameSpawnMissile(obj, def, pos, rot);
            else if (aircraft)
                go = InstantiatePrefab(obj, def, pos, rot);
            else if (!visualOnly && !_gameSpawnDead)
                go = TryGameSpawn(obj, def, pos, rot);
            if (go == null)
                go = InstantiatePrefab(obj, def, pos, rot);
            if (go == null) return null;
            go.SetActive(true);

            go.name = "NOReplay_" + (obj.Name ?? obj.Id);
            if (!flare && !ordnance) SilenceAiOnly(go);
            var rg = go.GetComponent<ReplayGhost>();
            if (rg == null) rg = go.AddComponent<ReplayGhost>();
            rg.Adopt(obj);
            if (aircraft)
            {
                TryApplyRecordedLivery(go, obj);
                TryApplyRecordedLoadout(go, obj);
                WarmLoadout(obj);
                ApplyHq(go, obj.Coalition);
                try { TryRegisterGhostOnMap(go, false); } catch { }
            }
            if (ordnance && !flare)
            {
                ApplyHq(go, obj.Coalition);
                StripCameras(go);
            }
            return go;
        }

        static void StripCameras(GameObject go)
        {
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            {
                if (cam == null) continue;
                cam.enabled = false;
                cam.gameObject.tag = "Untagged";
            }
            var listen = Type.GetType("UnityEngine.AudioListener, UnityEngine.AudioModule");
            if (listen == null) return;
            foreach (var c in go.GetComponentsInChildren(listen, true))
            {
                if (c is Behaviour b) b.enabled = false;
            }
        }

        static void PrepareOrdnanceVisual(GameObject go, ReplayObject obj, Vector3 pos)
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? FindGameType("AudioSource");
            if (at != null)
            {
                var stop = at.GetMethod("Stop", Type.EmptyTypes);
                var playAwake = at.GetProperty("playOnAwake");
                foreach (var c in go.GetComponentsInChildren(at, true))
                {
                    if (c == null) continue;
                    try
                    {
                        playAwake?.SetValue(c, false);
                        stop?.Invoke(c, null);
                        at.GetProperty("spatialBlend")?.SetValue(c, 1f);
                        at.GetProperty("mute")?.SetValue(c, false);
                    }
                    catch { }
                }
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true))
                if (l != null) l.enabled = false;
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            {
                if (cam == null) continue;
                cam.enabled = false;
                cam.gameObject.tag = "Untagged";
            }
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                tr.gameObject.layer = 0;
                tr.gameObject.SetActive(true);
            }
            var lodType = FindGameType("LODGroup");
            if (lodType != null)
            {
                foreach (var c in go.GetComponentsInChildren(lodType, true))
                {
                    var beh = c as Behaviour;
                    if (beh != null) beh.enabled = false;
                }
            }
            var rend = go.GetComponentsInChildren<Renderer>(true);
            int on = 0;
            for (int i = 0; i < rend.Length; i++)
            {
                if (rend[i] == null) continue;
                rend[i].enabled = true;
                on++;
            }
            Log.LogInfo("Ordnance " + obj.Name + " rend=" + rend.Length
                        + " pos=" + pos.x.ToString("0") + "," + pos.y.ToString("0") + "," + pos.z.ToString("0"));
        }

        static GameObject? InstantiatePrefab(ReplayObject obj, UnityEngine.Object? def, Vector3 pos, Quaternion rot)
        {
            if (def == null) return null;
            var prefab = GetDefPrefab(def);
            if (prefab == null) return null;
            var pooled = TakePooled(prefab, pos, rot);
            if (pooled != null) return pooled;
            return UnityEngine.Object.Instantiate(prefab, pos, rot);
        }

        static GameObject? GetDefPrefab(UnityEngine.Object def)
        {
            if (def is GameObject direct) return direct;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = def.GetType();
            GameObject? best = t.GetField("unitPrefab", flags)?.GetValue(def) as GameObject
                ?? t.GetField("prefab", flags)?.GetValue(def) as GameObject
                ?? t.GetField("modelPrefab", flags)?.GetValue(def) as GameObject;
            if (best != null) return best;
            GameObject? any = null;
            foreach (var f in t.GetFields(flags))
            {
                if (!typeof(GameObject).IsAssignableFrom(f.FieldType)) continue;
                var go = f.GetValue(def) as GameObject;
                if (go == null) continue;
                string n = (f.Name + " " + go.name).ToLowerInvariant();
                if (n.IndexOf("inflight", StringComparison.Ordinal) >= 0
                    || n.IndexOf("world", StringComparison.Ordinal) >= 0
                    || n.IndexOf("visual", StringComparison.Ordinal) >= 0
                    || n.IndexOf("model", StringComparison.Ordinal) >= 0
                    || n.IndexOf("prefab", StringComparison.Ordinal) >= 0)
                    return go;
                if (any == null) any = go;
            }
            return any;
        }

        static bool _dumpedMissileDef;

        static void DumpDefPrefabs(UnityEngine.Object def)
        {
            if (_dumpedMissileDef) return;
            _dumpedMissileDef = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = def.GetType();
            var sb = new System.Text.StringBuilder("MissileDef-Felder " + t.Name + "/" + def.name + ":");
            foreach (var f in t.GetFields(flags))
            {
                object? v = null;
                try { v = f.GetValue(def); } catch { }
                if (v == null) continue;
                sb.Append(' ').Append(f.Name).Append('=').Append(v is GameObject g ? g.name : v.GetType().Name);
            }
            Log.LogInfo(sb.ToString());
        }

        static void SilenceAiOnly(GameObject go)
        {
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var beh in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null || beh is ReplayGhost) continue;
                string tn = beh.GetType().Name;
                if (tn == "RotorShaft" || tn == "SwashRotor" || tn == "TurbineEngine" || tn == "LandingGear")
                    continue;
                if (tn.IndexOf("LOD", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (tn.IndexOf("Cull", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (tn.IndexOf("Visib", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (tn.IndexOf("Sleep", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (tn.IndexOf("Renderer", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (tn == "Unit" || tn == "NetworkIdentity" || tn == "NetworkBehaviour")
                    continue;
                if (tn == "Aircraft")
                {
                    beh.enabled = false;
                    continue;
                }
                bool mute = tn.IndexOf("AI", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Pilot", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Autopilot", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Task", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Mission", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Eject", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Wreck", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Flight", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Laser", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Designat", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Searchlight", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Warhead", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Fuze", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Explod", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Hardpoint", StringComparison.OrdinalIgnoreCase) >= 0
                            || tn.IndexOf("Seeker", StringComparison.OrdinalIgnoreCase) >= 0;
                if (mute) beh.enabled = false;
            }
            KillLaserBeams(go);
        }

        internal static void KillLaserBeams(GameObject go)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var lt = FindGameType("Laser");
            if (lt != null)
            {
                foreach (var c in go.GetComponentsInChildren(lt, true))
                {
                    if (c == null) continue;
                    try
                    {
                        if (c is Behaviour b) b.enabled = false;
                        var beam = lt.GetField("beamRenderer", flags)?.GetValue(c) as Renderer;
                        if (beam != null) beam.enabled = false;
                        var muz = lt.GetField("muzzleParticles", flags)?.GetValue(c) as Array;
                        if (muz != null)
                        {
                            foreach (var p in muz)
                            {
                                if (p == null) continue;
                                try { p.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(p, null); } catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            var lrType = FindGameType("LineRenderer")
                         ?? Type.GetType("UnityEngine.LineRenderer, UnityEngine.CoreModule");
            if (lrType != null)
            {
                foreach (var c in go.GetComponentsInChildren(lrType, true))
                {
                    if (c == null) continue;
                    if (c is Behaviour b) b.enabled = false;
                    if (c is Renderer r) r.enabled = false;
                    UnityEngine.Object.Destroy(c);
                }
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string n = (r.name ?? "") + " " + (r.sharedMaterial != null ? r.sharedMaterial.name : "");
                if (n.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("beam", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("lase", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("lrf", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("designat", StringComparison.OrdinalIgnoreCase) >= 0)
                    r.enabled = false;
            }
        }

        static object? MakeGlobalPos(ReplayObject obj)
        {
            ReplaySample s;
            if (!obj.TrySample(Clock.Time, out s))
            {
                if (obj.Samples.Count == 0) return null;
                s = obj.Samples[0];
            }
            var gpType = FindGameType("GlobalPosition");
            if (gpType == null) return null;
            try { return Activator.CreateInstance(gpType, s.X, s.Y, s.Z); }
            catch
            {
                try { return Activator.CreateInstance(gpType, new Vector3(s.X, s.Y, s.Z)); }
                catch { return null; }
            }
        }

        static object? MakeLoadout()
        {
            var t = FindGameType("Loadout");
            if (t == null) return null;
            try { return Activator.CreateInstance(t); }
            catch { return null; }
        }

        static object? MakeLivery()
        {
            var t = FindGameType("LiveryKey");
            if (t == null) return null;
            try { return Activator.CreateInstance(t, 0); }
            catch { return null; }
        }

        static void TryApplyRecordedLoadout(GameObject go, ReplayObject? obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.Loadout)) return;
            var act = FindGameType("Aircraft");
            var wmT = FindGameType("WeaponManager");
            var loadT = FindGameType("Loadout");
            if (act == null || wmT == null || loadT == null) return;
            var ac = go.GetComponent(act) ?? go.GetComponentInChildren(act, true);
            var wm = go.GetComponent(wmT) ?? go.GetComponentInChildren(wmT, true);
            if (ac == null || wm == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sets = wmT.GetField("hardpointSets", flags)?.GetValue(wm) as Array;
            if (sets == null || sets.Length == 0) return;
            var slots = new Dictionary<int, string>();
            foreach (var part in obj.Loadout.Split('|'))
            {
                var bits = part.Split(':');
                if (bits.Length < 2 || !int.TryParse(bits[0], out int idx)) continue;
                if (!string.IsNullOrEmpty(bits[1])) slots[idx] = bits[1];
            }
            object? loadout;
            try { loadout = Activator.CreateInstance(loadT); }
            catch { return; }
            var weapons = loadT.GetField("weapons", flags)?.GetValue(loadout);
            var add = weapons?.GetType().GetMethod("Add");
            if (weapons == null || add == null) return;
            int filled = 0;
            for (int i = 0; i < sets.Length; i++)
            {
                object? mount = null;
                if (slots.TryGetValue(i, out var key))
                {
                    mount = FindWeaponMount(key);
                    if (mount != null) filled++;
                }
                add.Invoke(weapons, new object?[] { mount });
            }
            try { act.GetProperty("Networkloadout", flags)?.SetValue(ac, loadout); } catch { }
            try { wmT.GetMethod("SpawnWeapons", flags)?.Invoke(wm, null); } catch { }
            Log.LogInfo("Loadout " + obj.Loadout + " stations=" + sets.Length + " gesetzt=" + filled + " -> " + go.name);
        }

        static object? FindWeaponMount(string key)
        {
            var enc = FindGameType("Encyclopedia");
            if (enc == null) return null;
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var lookup = enc.GetField("WeaponLookup", flags)?.GetValue(null);
            if (lookup == null) return null;
            var tryGet = lookup.GetType().GetMethod("TryGetValue");
            if (tryGet == null) return null;
            var args = new object?[] { key, null };
            try
            {
                if (tryGet.Invoke(lookup, args) is bool ok && ok) return args[1];
            }
            catch { }
            return null;
        }

        static void TryApplyRecordedLivery(GameObject go, ReplayObject? obj)
        {
            var act = FindGameType("Aircraft");
            if (act == null) return;
            var ac = go.GetComponent(act) ?? go.GetComponentInChildren(act, true);
            if (ac == null) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var keyT = FindGameType("LiveryKey");
                if (keyT == null) return;
                object? key = null;
                string raw = obj?.Livery ?? "";
                int c = raw.IndexOf(':');
                string kind = c < 0 ? raw : raw.Substring(0, c);
                string rest = c < 0 ? "" : raw.Substring(c + 1);
                if (kind.Equals("Workshop", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
                {
                    var enumT = keyT.GetNestedType("KeyType");
                    object workshop = Enum.Parse(enumT, "Workshop");
                    key = Activator.CreateInstance(keyT, workshop, 0, rest);
                }
                else if (kind.Equals("AppData", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
                {
                    var enumT = keyT.GetNestedType("KeyType");
                    object app = Enum.Parse(enumT, "AppData");
                    key = Activator.CreateInstance(keyT, app, 0, rest);
                }
                else
                {
                    int idx = 0;
                    if (!int.TryParse(rest, out idx))
                    {
                        try
                        {
                            var def = act.GetField("definition", flags)?.GetValue(ac);
                            var parms = def?.GetType().GetField("aircraftParameters", flags)?.GetValue(def);
                            object? fac = null;
                            var hq = act.GetProperty("NetworkHQ", flags)?.GetValue(ac);
                            fac = hq?.GetType().GetField("faction", flags)?.GetValue(hq);
                            var r = parms?.GetType().GetMethod("GetFirstLiveryForFaction")
                                ?.Invoke(parms, new object?[] { fac });
                            if (r is int i) idx = i;
                        }
                        catch { }
                    }
                    key = Activator.CreateInstance(keyT, idx);
                }
                if (key == null) return;
                try { act.GetMethod("SetLiveryKey")?.Invoke(ac, new object[] { key, true }); } catch { }
                var lbT = FindGameType("LiveryBehaviour");
                if (lbT == null) return;
                var lb = go.GetComponent(lbT) ?? go.GetComponentInChildren(lbT, true)
                         ?? go.AddComponent(lbT);
                try { lbT.GetMethod("Setup")?.Invoke(lb, new object[] { ac, key }); } catch { }
                try { lbT.GetMethod("SetKey")?.Invoke(lb, new object[] { key }); } catch { }
                if (!string.IsNullOrEmpty(raw))
                    Log.LogInfo("Livery " + raw + " -> " + go.name);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Livery: " + ex.Message);
            }
        }

        static void ApplyHq(GameObject go, string? coal)
        {
            if (go == null || string.IsNullOrEmpty(coal)) return;
            var hq = GetHq(coal);
            if (hq == null) return;
            var unit = FindUnitOn(go);
            if (unit == null) return;
            try
            {
                unit.GetType().GetProperty("NetworkHQ", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.SetValue(unit, hq);
                Log.LogInfo("Fraktion " + go.name + " -> " + FactionName(coal));
            }
            catch { }
        }

        static object? GetHq(string? coal)
        {
            var hqType = FindGameType("FactionHQ");
            if (hqType == null) return null;
            string want = FactionName(coal);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var o in Resources.FindObjectsOfTypeAll(hqType))
            {
                if (o == null) continue;
                string fac = "";
                try
                {
                    var f = o.GetType().GetField("faction", flags)?.GetValue(o);
                    fac = f?.ToString() ?? "";
                }
                catch { }
                if (o.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0
                    || fac.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                    return o;
            }
            Log.LogWarning("kein HQ für " + want);
            return null;
        }

        static GameObject? TryGameSpawnMissile(ReplayObject obj, UnityEngine.Object? def, Vector3 pos, Quaternion rot)
        {
            if (def == null) return null;
            var st = FindGameType("Spawner");
            if (st == null) return null;
            object? inst = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(st))
                if (o != null) { inst = o; break; }
            if (inst == null) return null;

            var unitType = FindGameType("Unit");
            var prefab = GetDefPrefab(def);
            var pooled = TakePooled(prefab, pos, rot);
            if (pooled != null)
            {
                ShowMissile(pooled);
                ApplyHq(pooled, obj.Coalition);
                try { TryRegisterGhostOnMap(pooled, false); } catch { }
                Log.LogInfo("Pool " + obj.Name + " " + prefab.name);
                return pooled;
            }
            object? owner = FindLaunchOwner(obj, unitType) ?? FindFactionOwner(obj.Coalition, unitType);
            if (owner == null)
                Log.LogWarning("kein Parent-Besitzer " + obj.Name + " parent=" + (obj.ParentId ?? "-") + " coal=" + (obj.Coalition ?? "-"));
            string coal = owner != null ? "parent" : (obj.Coalition ?? "?");
            Vector3 vel = OrdnanceVelocity(obj);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            AllowOrdnanceSpawn = true;
            try
            {
                if (owner != null)
                    foreach (var m in st.GetMethods(flags))
                    {
                        if (m.Name != "SpawnMissile") continue;
                        var ps = m.GetParameters();
                        if (ps.Length < 6) continue;
                        if (ps[0].ParameterType.Name.IndexOf("Definition", StringComparison.Ordinal) < 0
                            && ps[0].ParameterType != typeof(GameObject))
                            continue;
                        object? arg0 = def;
                        if (ps[0].ParameterType == typeof(GameObject))
                            arg0 = GetDefPrefab(def);
                        if (arg0 == null) continue;
                        var args = new object?[ps.Length];
                        args[0] = arg0;
                        args[1] = pos;
                        args[2] = rot;
                        args[3] = vel;
                        args[4] = null;
                        args[5] = owner;
                        var ret = m.Invoke(inst, args);
                        GameObject? go = (ret as Component)?.gameObject ?? ret as GameObject;
                        if (go != null)
                        {
                            ShowMissile(go);
                            Log.LogInfo("GameSpawn SpawnMissile " + obj.Name + " coal=" + coal + " parent=" + (obj.ParentId ?? "-"));
                            return go;
                        }
                    }
            }
            catch (Exception ex)
            {
                Log.LogWarning("SpawnMissile " + obj.Name + ": " + (ex.InnerException ?? ex).Message);
            }
            finally
            {
                AllowOrdnanceSpawn = false;
            }
            var saved = SpawnSavedMissile(obj, def, pos, rot, vel);
            if (saved != null) return saved;
            return null;
        }

        static GameObject? SpawnSavedMissile(ReplayObject obj, UnityEngine.Object? def, Vector3 pos, Quaternion rot, Vector3 vel)
        {
            var prefab = GetDefPrefab(def);
            if (prefab == null) return null;
            var st = FindGameType("Spawner");
            object? spawner = null;
            if (st != null)
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(st))
                    if (o != null) { spawner = o; break; }
            }
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
            var missile = go.GetComponent(FindGameType("Missile") ?? typeof(Component));
            if (missile != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                void set(string n, object? v)
                {
                    var p = missile.GetType().GetProperty(n, flags);
                    if (p != null && v != null) try { p.SetValue(missile, v); } catch { }
                }
                set("NetworkunitName", obj.Name ?? "");
                set("NetworkstartRotation", rot);
                set("NetworkstartingVelocity", vel);
                var hq = GetHq(obj.Coalition);
                if (hq != null) set("NetworkHQ", hq);
                else Log.LogWarning("kein HQ für " + obj.Name + " coal=" + (obj.Coalition ?? "-"));
            }
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.velocity = vel;
            if (spawner != null)
            {
                var som = spawner.GetType().GetProperty("ServerObjectManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? spawner.GetType().BaseType?.GetProperty("ServerObjectManager",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var mgr = som?.GetValue(spawner);
                var spawn = mgr?.GetType().GetMethod("Spawn", new[] { typeof(GameObject) });
                if (spawn != null)
                {
                    try
                    {
                        spawn.Invoke(mgr, new object[] { go });
                        ShowMissile(go);
                        Log.LogInfo("SOM.Spawn " + obj.Name);
                        return go;
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning("SOM.Spawn " + obj.Name + ": " + (ex.InnerException ?? ex).Message);
                    }
                }
            }
            Log.LogWarning("Missile ohne SOM.Spawn " + obj.Name);
            ShowMissile(go);
            return go;
        }

        static void ShowMissile(GameObject go)
        {
            int rends = 0;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                tr.gameObject.layer = 0;
                if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            }
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                rs[i].enabled = true;
                rends++;
            }
            var mt = FindGameType("Missile");
            var missile = mt != null ? go.GetComponent(mt) : null;
            if (missile != null && mt != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                try { mt.GetMethod("SetTangible", flags)?.Invoke(missile, new object[] { true }); } catch { }
                try
                {
                    var flight = mt.GetField("flightSound", flags)?.GetValue(missile);
                    if (flight != null)
                    {
                        var ft = flight.GetType();
                        ft.GetProperty("mute")?.SetValue(flight, false);
                        ft.GetProperty("loop")?.SetValue(flight, true);
                        ft.GetProperty("volume")?.SetValue(flight, 0.7f);
                        ft.GetProperty("spatialBlend")?.SetValue(flight, 1f);
                        bool playing = ft.GetProperty("isPlaying")?.GetValue(flight) is bool b && b;
                        if (!playing) ft.GetMethod("Play", Type.EmptyTypes)?.Invoke(flight, null);
                    }
                }
                catch { }
                var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                if (motors != null)
                {
                    foreach (var motor in motors)
                    {
                        if (motor == null) continue;
                        try { motor.GetType().GetMethod("Activate", flags)?.Invoke(motor, new[] { missile }); } catch { }
                    }
                }
            }
            Log.LogInfo("Missile-Vis " + go.name + " renderer=" + rends);
        }

        static object? FindLaunchOwner(ReplayObject obj, Type? unitType)
        {
            if (unitType == null || string.IsNullOrEmpty(obj.ParentId) || CurrentDoc == null)
                return null;
            ReplayObject? parent = null;
            foreach (var o in CurrentDoc.Objects)
                if (o.Id == obj.ParentId) { parent = o; break; }
            if (parent == null) return null;
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var go = _ghosts[i];
                if (go == null) continue;
                var rg = go.GetComponent<ReplayGhost>();
                if (rg == null || rg.Track == null || rg.Track.Id != parent.Id) continue;
                var u = go.GetComponent(unitType) ?? go.GetComponentInChildren(unitType);
                if (u != null) return u;
            }
            return null;
        }

        static object? FindFactionOwner(string? coal, Type? unitType)
        {
            if (unitType == null) return null;
            string want = FactionName(coal);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var go = _ghosts[i];
                if (go == null) continue;
                var rg = go.GetComponent<ReplayGhost>();
                if (rg != null && IsOrdnance(rg.Track?.Type, rg.Track?.Name)) continue;
                if (rg?.Track != null && FactionName(rg.Track.Coalition) != want) continue;
                var u = go.GetComponent(unitType) ?? go.GetComponentInChildren(unitType);
                if (u == null) continue;
                var hq = unitType.GetProperty("NetworkHQ", flags)?.GetValue(u);
                if (hq == null) continue;
                string fac = hq.GetType().GetField("faction", flags)?.GetValue(hq)?.ToString() ?? hq.ToString();
                if (fac.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return u;
            }
            return null;
        }

        static object? FindDummyOwner(Type? unitType)
        {
            if (unitType == null) return null;
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var go = _ghosts[i];
                if (go == null) continue;
                var rg = go.GetComponent<ReplayGhost>();
                if (rg != null && IsOrdnance(rg.Track?.Type, rg.Track?.Name)) continue;
                var u = go.GetComponent(unitType) ?? go.GetComponentInChildren(unitType);
                if (u != null) return u;
            }
            foreach (var o in Resources.FindObjectsOfTypeAll(unitType))
            {
                var c = o as Component;
                if (c == null || !c.gameObject.scene.IsValid()) continue;
                var rg = c.GetComponent<ReplayGhost>();
                if (rg != null && IsOrdnance(rg.Track?.Type, rg.Track?.Name)) continue;
                return c;
            }
            return null;
        }

        static Vector3 OrdnanceVelocity(ReplayObject obj)
        {
            if (obj.Samples.Count < 2) return Vector3.zero;
            var a = obj.Samples[0];
            var b = obj.Samples[Math.Min(1, obj.Samples.Count - 1)];
            float dt = b.Time - a.Time;
            if (dt < 0.01f) dt = 0.05f;
            var p0 = GlobalToLocal(a.X, a.Y, a.Z);
            var p1 = GlobalToLocal(b.X, b.Y, b.Z);
            return (p1 - p0) / dt;
        }

        static GameObject? TryGameSpawn(ReplayObject obj, UnityEngine.Object? def, Vector3 pos, Quaternion rot)
        {
            if (_gameSpawnDead || def == null) return null;
            try
            {
                return TryGameSpawnCore(obj, def, pos, rot);
            }
            catch (Exception ex)
            {
                _gameSpawnDead = true;
                Log.LogWarning("GameSpawn Abbruch: " + ex.Message);
                if (ex.InnerException != null)
                    Log.LogWarning("inner: " + ex.InnerException);
                return null;
            }
        }

        static GameObject? TryGameSpawnCore(ReplayObject obj, UnityEngine.Object? def, Vector3 pos, Quaternion rot)
        {
            var st = FindGameType("Spawner");
            if (st == null) return null;
            object? inst = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(st))
                if (o != null) { inst = o; break; }
            if (inst == null) return null;

            GameObject? prefab = GetDefPrefab(def);
            if (prefab == null) return null;
            object? gp = MakeGlobalPos(obj);
            object? hq = GetHq(obj.Coalition);
            object? livery = MakeLivery();
            object? player = null;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            string t = obj.Type ?? "";
            string want;
            if (IsAircraft(t, obj.Name)) want = "SpawnAircraft";
            else if (IsScenery(t, obj.Name)) want = "SpawnScenery";
            else if (IsBuilding(t, obj.Name)) want = "SpawnBuilding";
            else if (t.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0
                  || t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0) want = "SpawnShip";
            else want = "SpawnVehicle";
            float skill = want == "SpawnShip" ? 0.7f : 1f;

            foreach (var m in st.GetMethods(flags))
            {
                if (m.Name != want) continue;
                var ps = m.GetParameters();
                object?[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    string pn = p.Name ?? "";
                    string pt = p.ParameterType.Name;
                    if (p.ParameterType == typeof(GameObject)) args[i] = prefab;
                    else if (pt == "GlobalPosition") args[i] = gp;
                    else if (p.ParameterType == typeof(Quaternion)) args[i] = rot;
                    else if (p.ParameterType == typeof(Vector3)) args[i] = Vector3.zero;
                    else if (pt == "FactionHQ") args[i] = hq;
                    else if (pt == "Loadout") args[i] = null;
                    else if (pt == "LiveryKey") args[i] = livery;
                    else if (pt == "Player" || pt == "INetworkPlayer") args[i] = player;
                    else if (pt == "Hangar") args[i] = null;
                    else if (p.ParameterType == typeof(string)) args[i] = obj.Name ?? obj.Id;
                    else if (p.ParameterType == typeof(float))
                    {
                        if (pn.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = 1f;
                        else if (pn.IndexOf("skill", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = skill;
                        else if (pn.IndexOf("brave", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = skill;
                        else args[i] = skill;
                    }
                    else if (p.ParameterType == typeof(bool))
                        args[i] = false;
                    else if (p.HasDefaultValue) args[i] = p.DefaultValue;
                    else args[i] = null;
                }
                try
                {
                    var ret = m.Invoke(inst, args);
                    if (!_spawnLogged || _ghosts.Count < 4)
                    {
                        _spawnLogged = true;
                        Log.LogInfo("GameSpawn " + want + " " + obj.Name + " player=" + (player ?? "null") + " -> " + (ret ?? "null"));
                    }
                    if (ret is GameObject g) return g;
                    if (ret is Component c) return c.gameObject;
                    if (ret != null)
                    {
                        var rt = ret.GetType();
                        if (rt.IsGenericType && rt.Name.StartsWith("ValueTuple"))
                        {
                            var item1 = rt.GetField("Item1")?.GetValue(ret);
                            if (item1 is GameObject g2) return g2;
                            if (item1 is Component c2) return c2.gameObject;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _gameSpawnDead = true;
                    if (!_spawnLogged)
                    {
                        _spawnLogged = true;
                        Log.LogWarning(want + " " + obj.Name + ": " + ex.Message);
                        if (ex.InnerException != null)
                            Log.LogWarning("inner: " + ex.InnerException);
                        Log.LogWarning("GameSpawn tot — nur noch ein Instantiate pro Unit.");
                    }
                    return null;
                }
            }
            return null;
        }

        static GameObject? SpawnCubeFallback(ReplayObject obj)
        {
            if (obj.Samples.Count == 0) return null;
            ReplaySample s;
            if (!obj.TrySample(Clock.Time, out s))
                s = obj.Samples[0];
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "NOReplay_" + (obj.Name ?? obj.Id);
            bool ship = (obj.Type ?? "").IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0;
            go.transform.localScale = ship ? new Vector3(40f, 8f, 80f) : new Vector3(8f, 4f, 12f);
            go.transform.position = GlobalToLocal(s.X, s.Y, s.Z);
            go.transform.rotation = Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var rg = go.AddComponent<ReplayGhost>();
            rg.Track = obj;
            return go;
        }

        static Component? FindUnitOn(GameObject go)
        {
            string[] names = { "Aircraft", "Ship", "Vehicle", "GroundUnit", "Unit" };
            foreach (var n in names)
            {
                var t = FindGameType(n);
                if (t == null) continue;
                var c = go.GetComponent(t) ?? go.GetComponentInChildren(t, true);
                if (c != null) return c;
            }
            return null;
        }

        static void ForceReplayView()
        {
            GameObject? target = null;
            foreach (var g in _ghosts)
            {
                if (g == null) continue;
                var gh = g.GetComponent<ReplayGhost>();
                if (gh != null && gh.Track != null && IsAircraft(gh.Track.Type, gh.Track.Name))
                { target = g; break; }
            }
            if (target == null)
            {
                foreach (var g in _ghosts)
                    if (g != null) { target = g; break; }
            }
            if (target != null) TryFollowGhost(target);
            Log.LogInfo("Replay-View: orbit auf " + (target != null ? target.name : "kein Ziel"));
        }

        internal static string? PlayerNameFromCallSign(string? callSign)
        {
            if (string.IsNullOrEmpty(callSign)) return null;
            int a = callSign.IndexOf('(');
            int b = callSign.LastIndexOf(')');
            if (a < 0 || b <= a) return null;
            string inner = callSign.Substring(a + 1, b - a - 1).Trim();
            return inner.Length == 0 ? null : inner;
        }

        internal static void ApplyReplayIdentity(GameObject go, ReplayObject? obj)
        {
            if (go == null) return;
            TryApplyRecordedLivery(go, obj);
            TryApplyRecordedLoadout(go, obj);
            if (obj == null) return;
            string? player = obj.Pilot;
            if (string.IsNullOrEmpty(player))
                player = PlayerNameFromCallSign(obj.CallSign);
            string label = string.IsNullOrEmpty(player) ? (obj.Name ?? "") : player;
            if (string.IsNullOrEmpty(label)) return;
            var unit = FindUnitOn(go);
            if (unit == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = unit.GetType();
            try { t.GetProperty("NetworkunitName", flags)?.SetValue(unit, label); } catch { }
            try { t.GetField("unitName", flags)?.SetValue(unit, label); } catch { }
            RefreshSpectatorName(unit, label);
            ApplyPanel(go, obj, label);
            Log.LogInfo("Spielername " + label + " -> " + go.name);
        }

        internal static void ApplyPanel(GameObject go, ReplayObject? obj, string? label)
        {
            if (go == null) return;
            var unit = FindUnitOn(go);
            if (unit == null) return;
            var dbg = FindGameType("UnitDebug");
            if (dbg == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = dbg.GetField("followingUnit", flags);
            if (fFollow == null) return;
            string load = FormatLoadout(obj?.Loadout);
            foreach (var o in Resources.FindObjectsOfTypeAll(dbg))
            {
                if (o == null) continue;
                if (!ReferenceEquals(fFollow.GetValue(o), unit)) continue;
                SetTextField(o, "unitName", label ?? obj?.Name ?? "");
                if (load.Length > 0) SetTextField(o, "loadout", load);
                PinStateText(o);
            }
        }

        static void PinStateText(object host)
        {
            if (StateTextId != 0) return;
            var root = host as Component;
            Component? value = root != null ? FindStateValue(root.transform) : null;
            if (value == null)
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    value = FindStateValue(t);
                    if (value != null) break;
                }
            }
            if (value == null) return;
            StateTextId = value.GetInstanceID();
            value.GetType().GetProperty("text")?.SetValue(value, "Replay");
            Log.LogInfo("State-Text gepinnt: " + value.gameObject.name);
        }

        static Component? FindStateValue(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn != "Text" && tn != "TextMeshProUGUI" && tn != "TMP_Text") continue;
                var p = c.GetType().GetProperty("text");
                if (p?.GetValue(c) is not string s) continue;
                if (!s.Trim().Equals("State", StringComparison.OrdinalIgnoreCase)) continue;
                var parent = c.transform.parent;
                if (parent == null) continue;
                foreach (var sib in parent.GetComponentsInChildren<Component>(true))
                {
                    if (sib == null || sib == c) continue;
                    string sn = sib.GetType().Name;
                    if (sn != "Text" && sn != "TextMeshProUGUI" && sn != "TMP_Text") continue;
                    if (p.GetValue(sib) is string other && other.Trim().Equals("State", StringComparison.OrdinalIgnoreCase))
                        continue;
                    return sib;
                }
            }
            return null;
        }

        static void SetTextField(object host, string field, string value)
        {
            var f = host.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var tmp = f?.GetValue(host);
            tmp?.GetType().GetProperty("text")?.SetValue(tmp, value);
        }

        static string FormatLoadout(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var parts = new List<string>();
            foreach (var part in raw.Split('|'))
            {
                var bits = part.Split(':');
                if (bits.Length < 2 || string.IsNullOrEmpty(bits[1])) continue;
                string name = bits[1].Replace('_', ' ');
                string count = bits.Length > 2 ? bits[2] : "";
                parts.Add(count.Length > 0 ? name + " x" + count : name);
            }
            return string.Join(", ", parts);
        }

        static void RefreshSpectatorName(object unit, string player)
        {
            var dbg = FindGameType("UnitDebug");
            if (dbg == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = dbg.GetField("followingUnit", flags);
            var fText = dbg.GetField("unitName", flags);
            if (fFollow == null || fText == null) return;
            foreach (var o in Resources.FindObjectsOfTypeAll(dbg))
            {
                if (o == null) continue;
                if (!ReferenceEquals(fFollow.GetValue(o), unit)) continue;
                var tmp = fText.GetValue(o);
                tmp?.GetType().GetProperty("text")?.SetValue(tmp, player);
            }
        }

        static UnityEngine.Object[] _iconCache = Array.Empty<UnityEngine.Object>();
        static float _iconCacheAt = -10f;
        static PropertyInfo? _iconUnitProp;

        internal static void SetMapIcon(GameObject go, bool on)
        {
            if (go == null) return;
            var iconType = FindGameType("UnitMapIcon");
            if (iconType == null) return;
            if (Time.unscaledTime - _iconCacheAt > 2f)
            {
                _iconCache = Resources.FindObjectsOfTypeAll(iconType);
                _iconCacheAt = Time.unscaledTime;
                _iconUnitProp = iconType.GetProperty("unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_iconUnitProp == null) return;
            for (int i = 0; i < _iconCache.Length; i++)
            {
                var o = _iconCache[i];
                if (o == null) continue;
                var u = _iconUnitProp.GetValue(o) as Component;
                if (u == null) continue;
                if (u.gameObject != go && !u.transform.IsChildOf(go.transform) && !go.transform.IsChildOf(u.transform))
                    continue;
                if (o is Component c && c.gameObject != null && c.gameObject.activeSelf != on)
                    c.gameObject.SetActive(on);
            }
        }

        static void DetachCamerasFrom(GameObject go)
        {
            if (go == null) return;
            var camType = Type.GetType("UnityEngine.Camera, UnityEngine") ?? FindGameType("Camera");
            if (camType == null) return;
            foreach (var c in go.GetComponentsInChildren(camType, true))
            {
                var t = (c as Component)?.transform;
                if (t == null || t == go.transform) continue;
                t.SetParent(null, true);
            }
            var main = Camera.main;
            if (main != null && (main.transform.IsChildOf(go.transform) || main.transform.parent == go.transform))
                main.transform.SetParent(null, true);
        }

        static void TickOrdnanceCamera()
        {
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = camType.GetField("followingUnit", flags);
            var mSet = camType.GetMethod("SetFollowingUnit", flags);
            var mSw = camType.GetMethod("SwitchState", flags);
            if (fFollow == null) return;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var u = fFollow.GetValue(cam) as Component;
                if (u == null) continue;
                var rg = u.GetComponentInParent<ReplayGhost>();
                if (rg == null || !IsOrdnance(rg.Track?.Type, rg.Track?.Name)) continue;
                DetachCamerasFrom(rg.gameObject);
                object? free = camType.GetField("freeState", flags)?.GetValue(cam);
                mSet?.Invoke(cam, new object[] { null });
                if (free != null) mSw?.Invoke(cam, new[] { free });
            }
        }

        internal static void ReleaseCameraIfOn(GameObject go)
        {
            if (go == null) return;
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = camType.GetField("followingUnit", flags);
            var mSet = camType.GetMethod("SetFollowingUnit", flags);
            if (fFollow == null || mSet == null) return;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var u = fFollow.GetValue(cam) as Component;
                if (u == null) continue;
                var ut = u.transform;
                if (u.gameObject == go || ut.IsChildOf(go.transform) || go.transform.IsChildOf(ut))
                {
                    mSet.Invoke(cam, new object[] { null });
                    break;
                }
            }
        }

        static void TryFollowGhost(GameObject go)
        {
            var unit = FindUnitOn(go);
            if (unit == null)
            {
                Log.LogWarning("Follow ohne Unit: " + go.name);
                return;
            }
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var orbit = camType.GetField("orbitState", flags)?.GetValue(cam);
                var sw = camType.GetMethod("SwitchState", flags);
                var follow = camType.GetMethod("SetFollowingUnit", flags);
                follow?.Invoke(cam, new object[] { unit });
                if (orbit != null) sw?.Invoke(cam, new[] { orbit });
                Log.LogInfo("Orbit auf " + go.name);
                break;
            }
        }

        static void TryRegisterGhostOnMap(GameObject go, bool followCam = true)
        {
            if (go == null) return;
            var unit = FindUnitOn(go);
            if (unit == null)
            {
                if (_ghosts.Count <= 8)
                    Log.LogWarning("Kein Unit-Component an " + go.name);
                return;
            }
            var unitType = unit.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var track = go.GetComponent<ReplayGhost>()?.Track;
            ApplyReplayIdentity(go, track);
            object? hq = GetHq(track?.Coalition);
            if (hq != null)
                try { unitType.GetProperty("NetworkHQ", flags)?.SetValue(unit, hq); } catch { }
            if (followCam)
            {
                try { unitType.GetMethod("InitializeUnit", flags)?.Invoke(unit, null); } catch { }
            }
            try
            {
                var reg = unitType.GetMethod("RegisterUnit", flags);
                if (reg != null)
                {
                    var ps = reg.GetParameters();
                    object?[] args = ps.Length == 0 ? Array.Empty<object>() : new object?[] { null };
                    reg.Invoke(unit, args);
                }
            }
            catch { }
            var mapType = FindGameType("DynamicMap");
            if (mapType != null)
            {
                UnityEngine.Object? map = null;
                foreach (var o in Resources.FindObjectsOfTypeAll(mapType))
                    if (o != null) { map = o; break; }
                if (map != null)
                {
                    try { mapType.GetMethod("SpawnIconForUnit", flags)?.Invoke(map, new object[] { unit }); } catch { }
                    try { mapType.GetMethod("GetOrAddIcon", flags)?.Invoke(map, new object[] { unit }); } catch { }
                }
            }
            if (followCam)
                TryFollowGhost(go);
        }
    }
}