using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static bool IsHeartlandMap(string? mapId)
        {
            string m = (mapId ?? "").ToLowerInvariant();
            if (m.Length == 0) return true;
            if (m.IndexOf("ignus", StringComparison.Ordinal) >= 0) return false;
            if (m.IndexOf("igus", StringComparison.Ordinal) >= 0) return false;
            if (m.IndexOf("archipel", StringComparison.Ordinal) >= 0) return false;
            if (m.IndexOf("naval", StringComparison.Ordinal) >= 0) return false;
            if (m.IndexOf("terrain2", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static string MapKeyFromAcmi(string? mapId)
        {
            string raw = (mapId ?? "").Trim();
            Plugin.Log.LogInfo("ACMI MapId='" + raw + "'");
            if (IsHeartlandMap(raw))
            {
                Plugin.Log.LogInfo("MapKey None (Default = Heartland)");
                return "{\"Type\":\"None\",\"Path\":\"\"}";
            }
            string path = raw;
            int dot = path.LastIndexOf('.');
            if (dot >= 0 && dot < path.Length - 1) path = path.Substring(dot + 1);
            if (path.IndexOf("ignus", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("igus", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("archipel", StringComparison.OrdinalIgnoreCase) >= 0)
                path = "Terrain_naval";
            Plugin.Log.LogInfo("MapKey GameWorldPrefab Path=" + path);
            return "{\"Type\":\"GameWorldPrefab\",\"Path\":\"" + path.Replace("\"", "") + "\"}";
        }

        internal static string LastExportName = "";
        static bool _bindGhosts;
        static float _bindAt;
        static float _dedupAt;

        static void TickBindGhosts()
        {
            if (_dedupAt > 0f && Time.unscaledTime >= _dedupAt)
            {
                _dedupAt = 0f;
                try { DedupNearbyStatics(); }
                catch (Exception ex) { Log.LogError("Dedup: " + ex); }
            }
            if (!_bindGhosts) return;
            if (Time.unscaledTime < _bindAt) return;
            _bindGhosts = false;
            try { BindGhostsToMissionUnits(); }
            catch (Exception ex) { Log.LogError("Bind Ghosts: " + ex); }
            try { DedupNearbyStatics(); }
            catch (Exception ex) { Log.LogError("Dedup: " + ex); }
            _dedupAt = Time.unscaledTime + 2f;
        }

        internal static void ScheduleBindGhosts()
        {
            if (CurrentDoc == null) return;
            _bindGhosts = true;
            _bindAt = Time.unscaledTime + 0.5f;
            Log.LogInfo("Binde Ghosts in 0,5s an Missions-Units.");
        }

        static string MissionsDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "Shockfront", "NuclearOption", "Missions");
        }

        internal static string? ExportMissionJson(ReplayDocument doc)
        {
            if (doc == null) return null;
            string name = "NOReplay";
            string folder = Path.Combine(MissionsDir(), name);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".json");
            File.WriteAllText(Path.Combine(folder, "meta.json"), "{\"FileName\":\"NOReplay\"}");

            var air = new StringBuilder();
            var veh = new StringBuilder();
            var sea = new StringBuilder();
            var bld = new StringBuilder();
            var scn = new StringBuilder();
            int na = 0, nv = 0, ns = 0, nb = 0, nsc = 0;
            var usedAir = new List<float>();

            var dupIds = StaticDupIds(doc);
            int late = 0;
            foreach (var obj in doc.Objects)
            {
                if (dupIds.Contains(obj.Id ?? "")) continue;
                if (!ShouldQueue(obj) || obj.Samples.Count == 0) continue;
                if (IsScenery(obj.Type, obj.Name) || IsOrdnance(obj.Type, obj.Name)) { late++; continue; }
                if (!IsStartUnit(obj)) { late++; continue; }
                if (IsBuilding(obj.Type, obj.Name) && nb >= 180) { late++; continue; }
                ReplaySample s;
                if (!obj.TrySample(0f, out s)) s = obj.Samples[0];

                string type = MissionTypeKey(obj);
                string faction = FactionName(obj.Coalition);
                string uid = UniqueId(obj, na + nv + ns);
                string t = obj.Type ?? "";
                if (!IsBuilding(t, obj.Name) && (IsAircraft(t, obj.Name)
                    || t.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("Ground+", StringComparison.OrdinalIgnoreCase) >= 0))
                    NudgeApart(obj, ref s, usedAir, 36f);
                var q = Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
                if (na + nv + ns + nb + nsc < 12)
                    Log.LogInfo("JSON type " + type + " <- " + (obj.Name ?? obj.Id));
                if (IsAircraft(t, obj.Name))
                {
                    late++;
                    continue;
                }
                else if (IsScenery(t, obj.Name))
                {
                    if (nsc > 0) scn.Append(',');
                    scn.Append(SurfaceJson(type, faction, uid, s, q));
                    nsc++;
                }
                else if (IsBuilding(t, obj.Name))
                {
                    if (nb > 0) bld.Append(',');
                    bld.Append(SurfaceJson(type, faction, uid, s, q));
                    nb++;
                }
                else if (t.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0
                      || t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (ns > 0) sea.Append(',');
                    sea.Append(SurfaceJson(type, faction, uid, s, q));
                    ns++;
                }
                else
                {
                    if (nv > 0) veh.Append(',');
                    veh.Append(SurfaceJson(type, faction, uid, s, q));
                    nv++;
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"JsonVersion\":6,");
            sb.Append("\"MapKey\":").Append(MapKeyFromAcmi(doc.MapId)).Append(",");
            sb.Append("\"missionSettings\":{");
            sb.Append("\"description\":\"NO Replay\",");
            sb.Append("\"allowEventContent\":false,\"Tags\":[],");
            sb.Append("\"playerMode\":\"SingleAndMultiplayer\",");
            sb.Append("\"allowRespawn\":false,\"playerStartingRank\":0,");
            sb.Append("\"rankMultiplier\":1.0,\"successfulSortieBonus\":0.0,");
            sb.Append("\"nuclearEscalationThreshold\":0.0,\"strategicEscalationThreshold\":0.0,");
            sb.Append("\"minRankTacticalWarhead\":0,\"minRankStrategicWarhead\":0,");
            sb.Append("\"cameraStartPosition\":{\"IsOverride\":true,\"Value\":{\"Position\":{\"x\":4302.135,\"y\":1388.94714,\"z\":-3401.50269},\"Rotation\":{\"x\":0.0208932459,\"y\":0.5931841,\"z\":-0.0154024288,\"w\":0.8046483}}},");
            sb.Append("\"missionRoads\":{\"roads\":[]},\"missionSeaLanes\":{\"roads\":[]},");
            sb.Append("\"wrecksMaxNumber\":0,\"wrecksDecayTime\":0.0},");
            sb.Append("\"environment\":{");
            sb.Append("\"timeOfDay\":12.0,\"timeFactor\":0.0,\"weatherIntensity\":0.0,");
            sb.Append("\"cloudAltitude\":1800.0,\"windSpeed\":0.0,\"windTurbulence\":0.0,");
            sb.Append("\"windHeading\":0.0,\"windRandomHeading\":0.0,\"moonPhase\":14.0},");
            sb.Append("\"aircraft\":[").Append(air).Append("],");
            sb.Append("\"vehicles\":[").Append(veh).Append("],");
            sb.Append("\"ships\":[").Append(sea).Append("],");
            sb.Append("\"buildings\":[").Append(bld).Append("],");
            sb.Append("\"scenery\":[").Append(scn).Append("],");
            sb.Append("\"containers\":[],\"missiles\":[],\"pilots\":[],");
            sb.Append("\"factions\":[").Append(FactionJson("Boscali")).Append(',')
              .Append(FactionJson("Primeva")).Append(',').Append(FactionJson("Neutral")).Append("],");
            sb.Append("\"airbases\":").Append(IsHeartlandMap(doc.MapId) ? HeartlandAirbases() : "[]").Append(",");
            sb.Append("\"objectives\":[{\"Type\":\"None\",\"UniqueName\":\"Mission Start\",\"Faction\":\"\",\"DisplayName\":\"\",\"Hidden\":true,\"Outcomes\":[]}],");
            sb.Append("\"outcomes\":[]}");
            File.WriteAllText(path, sb.ToString());
            LastExportName = name;
            _pendingAir.Clear();
            _flareEvents.Clear();
            _flareDone.Clear();
            _flareClock = -1f;
            int bJson = 0;
            foreach (var obj in doc.Objects)
            {
                if (dupIds.Contains(obj.Id ?? "")) continue;
                if (!ShouldQueue(obj) || obj.Samples.Count == 0) continue;
                if (IsFlareType(obj.Type, obj.Name))
                {
                    _flareEvents.Add(obj);
                    continue;
                }
                bool start = IsStartUnit(obj);
                bool scnObj = IsScenery(obj.Type, obj.Name);
                bool bldObj = IsBuilding(obj.Type, obj.Name);
                if (scnObj || IsOrdnance(obj.Type, obj.Name) || !start)
                    _pendingAir.Add(obj);
                else if (bldObj && ++bJson > 180)
                    _pendingAir.Add(obj);
            }
            Log.LogInfo("Mission JSON: " + path + " air=" + na + " veh=" + nv + " ship=" + ns
                        + " bld=" + nb + " scn=" + nsc + " late=" + _pendingAir.Count
                        + " dupSkip=" + dupIds.Count);
            return path;
        }

        static bool LooksLikeStaticName(string? n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string[] hit =
            {
                "Bunker", "Hangar", "Helipad", "Factory", "Shelter", "Depot",
                "Tower", "Tank", "Radar", "Revetment", "Pillbox", "Plant",
                "Refinery", "ammunition", "hangar", "shelter", "factory",
                "storageTank", "controlTower", "VehicleDepot", "guardTower"
            };
            for (int i = 0; i < hit.Length; i++)
                if (n.IndexOf(hit[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static string StaticKeyName(GameObject go)
        {
            var rg = go.GetComponent<ReplayGhost>();
            if (rg?.Track != null)
            {
                string mapped = LookupDevName(rg.Track.Name);
                if (mapped.Length > 0) return mapped;
                string typeKey = MissionTypeKey(rg.Track);
                if (!string.IsNullOrEmpty(typeKey)) return typeKey;
            }
            string n = go.name ?? "";
            if (n.StartsWith("NOReplay_", StringComparison.Ordinal)) n = n.Substring(9);
            int p = n.IndexOf('(');
            if (p > 0) n = n.Substring(0, p).Trim();
            string via = LookupDevName(n);
            return via.Length > 0 ? via : n;
        }

        static void DedupNearbyStatics()
        {
            var list = new List<(GameObject go, string key, Vector3 p, bool ghost)>();
            var seen = new HashSet<int>();
            void add(GameObject go, bool ghost)
            {
                if (go == null) return;
                if (!go.scene.IsValid()) return;
                int id = go.GetInstanceID();
                if (!seen.Add(id)) return;
                string key = StaticKeyName(go);
                if (string.IsNullOrEmpty(key)) return;
                list.Add((go, key, go.transform.position, ghost));
            }
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var g = _ghosts[i];
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg?.Track == null) continue;
                if (!IsBuilding(rg.Track.Type, rg.Track.Name)
                    && !IsScenery(rg.Track.Type, rg.Track.Name)
                    && !IsEmplacement(rg.Track.Type, rg.Track.Name))
                    continue;
                add(g, true);
            }
            string[] types = { "Building", "Scenery", "Unit" };
            for (int t = 0; t < types.Length; t++)
            {
                var ty = FindGameType(types[t]);
                if (ty == null) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(ty))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    if (types[t] == "Unit" && !LooksLikeStaticName(c.gameObject.name)) continue;
                    add(c.gameObject, c.GetComponent<ReplayGhost>() != null);
                }
            }
            int killed = 0;
            const float maxDistSq = 60f * 60f;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.go == null) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.go == null) continue;
                    if (ReferenceEquals(a.go, b.go)) continue;
                    if (!string.Equals(a.key, b.key, StringComparison.OrdinalIgnoreCase)) continue;
                    float dx = a.p.x - b.p.x;
                    float dz = a.p.z - b.p.z;
                    if (dx * dx + dz * dz > maxDistSq) continue;
                    GameObject keep = a.go, drop = b.go;
                    if (a.ghost && !b.ghost) { keep = a.go; drop = b.go; }
                    else if (!a.ghost && b.ghost) { keep = b.go; drop = a.go; }
                    if (drop == null || !drop.activeSelf) continue;
                    var rends = drop.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rends.Length; r++)
                        if (rends[r] != null) rends[r].enabled = false;
                    drop.SetActive(false);
                    killed++;
                    if (killed <= 12)
                        Log.LogInfo("Dedup " + a.key + " drop=" + drop.name + " keep=" + keep.name
                                    + " d=" + Mathf.Sqrt(dx * dx + dz * dz).ToString("0.0"));
                }
            }
            Log.LogInfo("Statik-Dedup Kandidaten=" + list.Count + " ausgeblendet=" + killed);
        }

        static bool IsVanillaTitle(string? title)
        {
            string t = title ?? "";
            return t.IndexOf("Escalation", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Confrontation", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Domination", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Altercation", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Terminal Control", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Breakout", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Carrier Duel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool SkipVanillaStatic(ReplayDocument doc, ReplayObject obj)
        {
            if (!IsVanillaTitle(doc.Title)) return false;
            if (IsScenery(obj.Type, obj.Name)) return false;
            return IsBuilding(obj.Type, obj.Name) || IsEmplacement(obj.Type, obj.Name);
        }

        static HashSet<string> StaticDupIds(ReplayDocument doc)
        {
            var drop = new HashSet<string>();
            var kept = new List<(string name, float x, float y, float z)>();
            foreach (var obj in doc.Objects)
            {
                if (obj.Samples.Count == 0) continue;
                if (!IsBuilding(obj.Type, obj.Name) && !IsScenery(obj.Type, obj.Name)
                    && !IsEmplacement(obj.Type, obj.Name))
                    continue;
                ReplaySample s;
                if (!obj.TrySample(obj.SpawnAt >= 0f ? obj.SpawnAt : 0f, out s))
                    s = obj.Samples[0];
                string n = obj.Name ?? obj.Id ?? "";
                bool dup = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    var k = kept[i];
                    if (!string.Equals(k.name, n, StringComparison.OrdinalIgnoreCase)) continue;
                    float dx = s.X - k.x, dy = s.Y - k.y, dz = s.Z - k.z;
                    if (dx * dx + dy * dy + dz * dz < 4f)
                    {
                        dup = true;
                        break;
                    }
                }
                if (dup)
                {
                    if (!string.IsNullOrEmpty(obj.Id)) drop.Add(obj.Id);
                }
                else
                    kept.Add((n, s.X, s.Y, s.Z));
            }
            if (drop.Count > 0)
                Log.LogInfo("Statik-Duplikate entfernt: " + drop.Count);
            return drop;
        }

        static readonly string[][] DevNames =
        {
            new[] { "SAH-46 Chicane", "AttackHelo1" },
            new[] { "A-19 Brawler", "CAS1" },
            new[] { "CI-22 Cricket", "COIN" },
            new[] { "SFB-81 Darkreach", "Darkreach" },
            new[] { "EW-25 Medusa", "EW1" },
            new[] { "FS-12 Revoker", "Fighter1" },
            new[] { "KR-67 Ifrit", "Multirole1" },
            new[] { "VL-49 Tarantula", "QuadVTOL1" },
            new[] { "FS-20 Vortex", "SmallFighter1" },
            new[] { "T/A-30 Compass", "trainer" },
            new[] { "VT-7 Vagrant", "VTOLTrainer1" },
            new[] { "UH-90 Ibis", "UtilityHelo1" },
            new[] { "Munitions Bunker", "ammunitionBunker" },
            new[] { "Control Tower", "controlTower1" },
            new[] { "23mm AAA Emplacement", "Emplacement1_23mm" },
            new[] { "IRM-S1 Emplacement", "Emplacement1_MANPADS" },
            new[] { "Enrichment Plant", "enrichmentPlant1" },
            new[] { "Large Factory", "factory_large" },
            new[] { "Vertical Factory", "factory_tall" },
            new[] { "Guard Tower", "guardTower1" },
            new[] { "Medium Hangar", "hangar_med" },
            new[] { "Medium Aircraft Hangar", "hangar_med" },
            new[] { "Helipad", "Helipad" },
            new[] { "Pillbox", "pillbox" },
            new[] { "Radar Station", "radarStation1" },
            new[] { "Refinery Structure", "refinery_main" },
            new[] { "Aircraft Revetment", "revetment1" },
            new[] { "Hardened Shelter", "shelter1" },
            new[] { "Hardened Aircraft Shelter", "shelter1" },
            new[] { "Storage Tank", "storageTank" },
            new[] { "Vehicle Depot", "VehicleDepot1" },
            new[] { "Ammo Dump", "ammoDump" },
            new[] { "AT-145 Emplacement", "Emplacement1_ATGM" },
            new[] { "Fuel Depot", "fuelTank1" },
            new[] { "12.7mm MG Emplacement", "Emplacement1_MG" },
            new[] { "Infantry Bunker", "gabionBunker1" },
            new[] { "Annex Class Carrier", "AssaultCarrier1" },
            new[] { "Shard Class Corvette", "Corvette1" },
            new[] { "Dynamo Class Destroyer", "Destroyer1" },
            new[] { "Hyperion Class Carrier", "FleetCarrier1" },
            new[] { "Argus Class Frigate", "Frigate1" },
            new[] { "OTB-31 Landing Craft", "LandingCraft1" },
            new[] { "Cursor Class LFD", "SmallCarrier1" },
            new[] { "Surf Class Patrol Boat", "PatrolBoat1" },
            new[] { "AFV6 AA", "6x6_1_AA" },
            new[] { "AFV6 APC", "6x6_1_APC" },
            new[] { "AFV6 AT", "6x6_1_AT" },
            new[] { "AFV6 IFV", "6x6_1_IFV" },
            new[] { "AFV8 APC", "AFV8_APC" },
            new[] { "AFV8 IFV", "AFV8_IFV" },
            new[] { "AFV8 Mobile Air Defense", "AFV8_SAM" },
            new[] { "HLT-CRAM", "CRAMTrailer1" },
            new[] { "HLT Fire Control", "HLT-FC" },
            new[] { "HLT Fuel Tanker", "HLT-FT" },
            new[] { "HLT Flatbed", "HLT-L" },
            new[] { "HLT Munitions Truck", "HLT-M" },
            new[] { "HLT Radar Truck", "HLT-R" },
            new[] { "HLT Tractor", "HLT-T" },
            new[] { "HLT-HEL", "LaserTrailer1" },
            new[] { "LCV45", "LCV45" },
            new[] { "LCV25 AA", "LightTruck1_AA" },
            new[] { "LCV25 AT", "LightTruck1_AT" },
            new[] { "Linebreaker APC", "Linebreaker_APC" },
            new[] { "Linebreaker IFV", "Linebreaker_IFV" },
            new[] { "Linebreaker SAM", "Linebreaker_SAM" },
            new[] { "Type-12 MBT", "MBT" },
            new[] { "Spearhead MBT", "MBT1" },
            new[] { "T9K41 Boltstrike", "RadarSAM1" },
            new[] { "StratoLance R9 Launcher", "SAMTrailer1" },
            new[] { "AeroSentry SPAAG", "SPAAG1" },
            new[] { "FGA-57 Anvil", "SPAAG2" },
            new[] { "MSV CRAM", "Truck2-CRAM" },
            new[] { "MSV Fire Control", "Truck2-FC" },
            new[] { "MSV Fuel Tanker", "Truck2-FT" },
            new[] { "MSV Flatbed", "Truck2-L" },
            new[] { "MSV LADS", "Truck2-LADS" },
            new[] { "MSV Munitions", "Truck2-M" },
            new[] { "MSV MRAP", "Truck2-MRAP" },
            new[] { "MSV Radar", "Truck2-R" },
            new[] { "MSV StratoLance R9 Launcher", "Truck2-RSAM" },
            new[] { "MSV Tractor", "Truck2-T" },
            new[] { "MSV Nuclear Ballistic Missile Launcher", "Truck2-TBM" },
            new[] { "Hexhound GMG", "UGV1_grenade" },
            new[] { "Hexhound SAM", "UGV1_SAM" },
            new[] { "M12 Jackknife", "UGVDozer1" },
            new[] { "HLT Mobile Artillery", "HLT-MART" },
            new[] { "MSV MLRS", "Truck2-MLRS" },
        };

        static string LookupDevName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            for (int i = 0; i < DevNames.Length; i++)
                if (string.Equals(name, DevNames[i][0], StringComparison.OrdinalIgnoreCase))
                    return DevNames[i][1];
            for (int i = 0; i < DevNames.Length; i++)
                if (name.IndexOf(DevNames[i][0], StringComparison.OrdinalIgnoreCase) >= 0)
                    return DevNames[i][1];
            return "";
        }

        static string MissionTypeKey(ReplayObject obj)
        {
            string mapped = LookupDevName(obj.Name);
            if (mapped.Length > 0) return mapped;
            string prefab = "";
            var def = FindAnyDef(obj);
            if (def != null)
            {
                var go = GetDefPrefab(def);
                if (go != null) prefab = go.name;
                if (string.IsNullOrEmpty(prefab)) prefab = def.name;
            }
            if (!string.IsNullOrEmpty(prefab))
            {
                string viaPrefab = LookupDevName(prefab);
                if (viaPrefab.Length > 0) return viaPrefab;
                return prefab;
            }
            return obj.Name ?? "unknown";
        }

        static string FactionName(string? coal)
        {
            if (string.IsNullOrEmpty(coal)) return "Neutral";
            if (coal.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0) return "Primeva";
            if (coal.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0) return "Boscali";
            if (coal.IndexOf("Neutral", StringComparison.OrdinalIgnoreCase) >= 0) return "Neutral";
            return "Neutral";
        }

        static string UniqueId(ReplayObject obj, int i)
        {
            string raw = obj.Id ?? obj.Name ?? ("u" + i);
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : ("u" + i);
        }

        static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);

        static string Vec(float x, float y, float z) =>
            "{\"x\":" + F(x) + ",\"y\":" + F(y) + ",\"z\":" + F(z) + "}";

        static string Quat(Quaternion q) =>
            "{\"x\":" + F(q.x) + ",\"y\":" + F(q.y) + ",\"z\":" + F(q.z) + ",\"w\":" + F(q.w) + "}";

        static string Cap() => "{\"IsOverride\":false,\"Value\":0.0}";

        static string AircraftJson(string type, string faction, string uid, ReplaySample s, Quaternion q, ReplayObject obj)
        {
            return "{\"type\":\"" + Esc(type) + "\",\"faction\":\"" + faction + "\",\"UniqueName\":\"" + Esc(uid)
                + "\",\"globalPosition\":" + Vec(s.X, s.Y, s.Z)
                + ",\"rotation\":" + Quat(q)
                + ",\"CaptureStrength\":" + Cap() + ",\"CaptureDefense\":" + Cap()
                + ",\"playerControlled\":false,\"playerControlledPriority\":0"
                + ",\"savedLoadout\":" + LoadoutJson(obj.Loadout)
                + "," + LiveryJson(obj.Livery)
                + ",\"fuel\":1.0,\"skill\":0.0,\"bravery\":0.0,\"startingSpeed\":0.0}";
        }

        static string LoadoutJson(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "{\"Selected\":[]}";
            var slots = new Dictionary<int, string>();
            int max = -1;
            foreach (var part in raw.Split('|'))
            {
                var bits = part.Split(':');
                if (bits.Length < 2) continue;
                if (!int.TryParse(bits[0], out int idx)) continue;
                string key = bits[1];
                if (string.IsNullOrEmpty(key)) continue;
                slots[idx] = key;
                if (idx > max) max = idx;
            }
            if (max < 0) return "{\"Selected\":[]}";
            var sb = new StringBuilder("{\"Selected\":[");
            for (int i = 0; i <= max; i++)
            {
                if (i > 0) sb.Append(',');
                slots.TryGetValue(i, out var key);
                sb.Append("{\"Key\":\"").Append(Esc(key ?? "")).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string LiveryJson(string? raw)
        {
            string type = "Builtin";
            int index = 0;
            string name = "";
            if (!string.IsNullOrEmpty(raw))
            {
                int c = raw.IndexOf(':');
                string kind = c < 0 ? raw : raw.Substring(0, c);
                string rest = c < 0 ? "" : raw.Substring(c + 1);
                if (kind.Equals("Workshop", StringComparison.OrdinalIgnoreCase))
                {
                    type = "Workshop";
                    name = rest;
                }
                else if (kind.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                {
                    type = "AppData";
                    name = rest;
                }
                else if (int.TryParse(rest, out var n))
                    index = n;
            }
            return "\"livery\":" + index + ",\"liveryType\":\"" + type + "\",\"liveryName\":\"" + Esc(name) + "\"";
        }

        static string SurfaceJson(string type, string faction, string uid, ReplaySample s, Quaternion q)
        {
            return "{\"type\":\"" + Esc(type) + "\",\"faction\":\"" + faction + "\",\"UniqueName\":\"" + Esc(uid)
                + "\",\"globalPosition\":" + Vec(s.X, s.Y, s.Z)
                + ",\"rotation\":" + Quat(q)
                + ",\"CaptureStrength\":" + Cap() + ",\"CaptureDefense\":" + Cap()
                + ",\"holdPosition\":true,\"skill\":0.0,\"waypoints\":[]}";
        }

        static string FactionJson(string name)
        {
            return "{\"factionName\":\"" + name + "\",\"preventJoin\":false,\"preventDonation\":false,"
                + "\"supplies\":[],\"startingBalance\":0,\"playerJoinAllowance\":20,\"playerTaxRate\":0.2,"
                + "\"regularIncome\":0,\"excessFundsDistributePercent\":0,\"killReward\":0,"
                + "\"airSkillMultiplier\":0,\"surfaceSkillMultiplier\":0,"
                + "\"startingWarheads\":0,\"reserveWarheads\":0,\"reserveAirframes\":0,\"extraReservesPerPlayer\":0,"
                + "\"AIAircraftLimit\":0,\"reduceAIPerFriendlyPlayer\":0,\"addAIPerEnemyPlayer\":0,"
                + "\"restrictions\":{\"aircraft\":[],\"weapons\":[]}}";
        }

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void ExportCurrentAndLog()
        {
            if (CurrentDoc == null)
            {
                Log.LogWarning("Zuerst F8 — ACMI laden.");
                return;
            }
            ExportMissionJson(CurrentDoc);
            Log.LogInfo("Mission 'NOReplay' unter Missions/NOReplay/. Im Menü als Custom Mission starten.");
        }

        static void BindGhostsToMissionUnits()
        {
            if (CurrentDoc == null) return;
            var names = new[] { "Aircraft", "GroundVehicle", "Ship", "Building", "Unit" };
            int n = 0;
            var seen = new HashSet<int>();
            foreach (var tn in names)
            {
                var t = FindGameType(tn);
                if (t == null || !typeof(UnityEngine.Object).IsAssignableFrom(t)) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(t))
                {
                    if (o == null) continue;
                    var go = (o as Component)?.gameObject;
                    if (go == null) continue;
                    int id = go.GetInstanceID();
                    if (!seen.Add(id)) continue;
                    if (go.GetComponent<ReplayGhost>() != null) continue;
                    var obj = MatchReplay(ReadUniqueName(o) ?? go.name);
                    if (obj == null) continue;
                    var rg = go.AddComponent<ReplayGhost>();
                    rg.Track = obj;
                    ApplyReplayIdentity(go, obj);
                    SilenceAiOnly(go);
                    _ghosts.Add(go);
                    rg.SetGhostVisible(false);
                    n++;
                }
            }
            Log.LogInfo("Ghosts an Missions-Units: +" + n + " gesamt=" + _ghosts.Count);
        }

        static void TryAttachFromSpawn(object[]? args, object? result, bool silence)
        {
            if (CurrentDoc == null) return;
            string? uid = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] is string s && s.Length >= 2 && s.Length <= 12)
                        uid = s;
            }
            GameObject? go = null;
            if (result is GameObject g) go = g;
            else if (result is Component c) go = c.gameObject;
            else if (result != null)
            {
                var t = result.GetType();
                if (t.IsGenericType && t.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
                {
                    var item1 = t.GetField("Item1")?.GetValue(result);
                    go = item1 as GameObject ?? (item1 as Component)?.gameObject;
                }
            }
            if (go == null && args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is GameObject ag && ag.scene.IsValid()) { go = ag; break; }
                    if (args[i] is Component ac && ac.gameObject.scene.IsValid()) { go = ac.gameObject; break; }
                }
            }
            if (go == null) return;
            var obj = MatchReplay(uid ?? go.name);
            if (obj == null) return;
            if (go.GetComponent<ReplayGhost>() != null) return;
            var rg = go.AddComponent<ReplayGhost>();
            rg.Track = obj;
            ApplyReplayIdentity(go, obj);
            SilenceAiOnly(go);
            _ghosts.Add(go);
            rg.SetGhostVisible(false);
            if (_ghosts.Count <= 8 || (_ghosts.Count % 50) == 0)
                Log.LogInfo("Ghost " + (uid ?? "?") + " -> " + go.name + " n=" + _ghosts.Count);
        }

        static void NudgeApart(ReplayObject obj, ref ReplaySample s, List<float> used, float min)
        {
            float ox = s.X;
            float oz = s.Z;
            float x = ox;
            float z = oz;
            float min2 = min * min;
            int k = 0;
            while (k < 64)
            {
                bool hit = false;
                for (int i = 0; i + 1 < used.Count; i += 2)
                {
                    float dx = x - used[i];
                    float dz = z - used[i + 1];
                    if (dx * dx + dz * dz < min2) { hit = true; break; }
                }
                if (!hit) break;
                k++;
                float ang = k * 2.4f;
                float rad = min * (1f + k * 0.15f);
                x = ox + Mathf.Cos(ang) * rad;
                z = oz + Mathf.Sin(ang) * rad;
            }
            obj.NudgeX = x - ox;
            obj.NudgeZ = z - oz;
            s.X = x;
            s.Z = z;
            used.Add(x);
            used.Add(z);
        }

        static string? ReadUniqueName(object o)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = o.GetType();
            foreach (var n in new[] { "UniqueName", "uniqueName" })
            {
                var p = t.GetProperty(n, flags);
                if (p?.GetValue(o) is string s && s.Length > 0) return s;
                var f = t.GetField(n, flags);
                if (f?.GetValue(o) is string s2 && s2.Length > 0) return s2;
            }
            return null;
        }

        static ReplayObject? MatchReplay(string goName)
        {
            if (CurrentDoc == null || string.IsNullOrEmpty(goName)) return null;
            int cut = goName.IndexOf('(');
            string baseName = cut > 0 ? goName.Substring(0, cut).Trim() : goName;
            foreach (var obj in CurrentDoc.Objects)
            {
                string uid = UniqueId(obj, 0);
                if (uid.Length < 2) continue;
                if (baseName.Equals(uid, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
            return null;
        }

        static string HeartlandAirbases() => "[{\"IsOverride\":true,\"faction\":\"Boscali\",\"UniqueName\":\"North Boscali Airbase\",\"DisplayName\":\"North Boscali Airbase\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1561.0,\"Center\":{\"x\":-18632.0,\"y\":138.76001,\"z\":32049.0},\"SelectionPosition\":{\"x\":-18550.0,\"y\":137.057,\"z\":32192.7},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Boscali\",\"UniqueName\":\"Maris Airport\",\"DisplayName\":\"Maris Airport\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1473.0,\"Center\":{\"x\":13090.0,\"y\":142.7454,\"z\":19716.0},\"SelectionPosition\":{\"x\":12802.6,\"y\":135.155411,\"z\":19235.3},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Boscali\",\"UniqueName\":\"K92 Highway Strip\",\"DisplayName\":\"K92 Highway Strip\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":746.0,\"Center\":{\"x\":18756.0,\"y\":136.16,\"z\":12406.0},\"SelectionPosition\":{\"x\":18943.8867,\"y\":132.815,\"z\":12574.958},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Primeva\",\"UniqueName\":\"Dustbowl Highway Strip\",\"DisplayName\":\"Dustbowl Highway Strip\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":746.0,\"Center\":{\"x\":13326.0,\"y\":146.12,\"z\":-7450.0},\"SelectionPosition\":{\"x\":13351.44,\"y\":145.872009,\"z\":-7709.05},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Primeva\",\"UniqueName\":\"Sandrift Airbase\",\"DisplayName\":\"Sandrift Airbase\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":975.0,\"Center\":{\"x\":27807.69,\"y\":135.47345,\"z\":-18910.1543},\"SelectionPosition\":{\"x\":27574.4785,\"y\":135.432465,\"z\":-19351.6035},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Primeva\",\"UniqueName\":\"Agrapol Airbase\",\"DisplayName\":\"Agrapol Airbase\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1463.0,\"Center\":{\"x\":-1131.0,\"y\":137.6,\"z\":-26845.0},\"SelectionPosition\":{\"x\":-905.9,\"y\":136.73999,\"z\":-27339.9},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Primeva\",\"UniqueName\":\"Vigil Cay Naval Airbase\",\"DisplayName\":\"Vigil Cay Naval Airbase\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1332.0,\"Center\":{\"x\":-33994.5,\"y\":18.3999939,\"z\":-12111.0},\"SelectionPosition\":{\"x\":-34213.5664,\"y\":19.985,\"z\":-12459.1035},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":true,\"faction\":\"Boscali\",\"UniqueName\":\"South Boscali General Aviation\",\"DisplayName\":\"South Boscali General Aviation\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":900.0,\"Center\":{\"x\":-9875.6,\"y\":136.6,\"z\":-6479.2},\"SelectionPosition\":{\"x\":-10265.02,\"y\":136.43,\"z\":-6532.09},\"Tower\":\"\",\"VerticalLandingPoints\":[],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":false,\"faction\":\"Boscali\",\"UniqueName\":\"Maris Heliport\",\"DisplayName\":\"Maris Heliport\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1000.00024,\"Center\":{\"x\":6635.401,\"y\":17.12793,\"z\":9529.732},\"SelectionPosition\":{\"x\":6625.79,\"y\":43.05858,\"z\":9571.562},\"Tower\":\"\",\"VerticalLandingPoints\":[{\"x\":6590.609,\"y\":14.3747559,\"z\":9680.181}],\"ServicePoints\":[{\"x\":6637.26953,\"y\":12.291626,\"z\":9715.959}],\"roads\":{\"roads\":[]},\"runways\":[{\"Name\":\"Seaview Parade\",\"Reversable\":false,\"Takeoff\":false,\"Landing\":false,\"Arrestor\":false,\"SkiJump\":false,\"Width\":30.0,\"Start\":{\"x\":7029.08838,\"y\":23.8081188,\"z\":9723.687},\"End\":{\"x\":7843.773,\"y\":23.4057617,\"z\":9795.549},\"exitPoints\":[]}]},{\"IsOverride\":false,\"faction\":\"Primeva\",\"UniqueName\":\"The Farm\",\"DisplayName\":\"The Farm\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1000.00372,\"Center\":{\"x\":2370.73682,\"y\":134.7276,\"z\":-19493.666},\"SelectionPosition\":{\"x\":2295.533,\"y\":134.718414,\"z\":-19576.2813},\"Tower\":\"\",\"VerticalLandingPoints\":[{\"x\":2262.664,\"y\":134.906769,\"z\":-19702.3711},{\"x\":2110.84839,\"y\":134.6929,\"z\":-19621.9219}],\"ServicePoints\":[{\"x\":2469.15,\"y\":133.40332,\"z\":-19406.5918}],\"roads\":{\"roads\":[]},\"runways\":[{\"Name\":\"The Access Road\",\"Reversable\":false,\"Takeoff\":true,\"Landing\":false,\"Arrestor\":false,\"SkiJump\":false,\"Width\":30.0,\"Start\":{\"x\":2517.619,\"y\":133.443787,\"z\":-19412.9316},\"End\":{\"x\":3092.14453,\"y\":130.363846,\"z\":-19120.08},\"exitPoints\":[]}]},{\"IsOverride\":false,\"faction\":\"Boscali\",\"UniqueName\":\"EnrichmentPlantNorth\",\"DisplayName\":\"North Coast Enrichment Plant\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1000.00024,\"Center\":{\"x\":2852.64282,\"y\":2.961914,\"z\":35160.56},\"SelectionPosition\":{\"x\":2965.05762,\"y\":8.263275,\"z\":34942.1},\"Tower\":\"\",\"VerticalLandingPoints\":[{\"x\":2956.385,\"y\":21.4312744,\"z\":34857.3828},{\"x\":3029.25977,\"y\":20.0135345,\"z\":34893.8359},{\"x\":2786.321,\"y\":3.079464,\"z\":35189.65},{\"x\":2876.06982,\"y\":3.04394531,\"z\":35224.38}],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]},{\"IsOverride\":false,\"faction\":\"Primeva\",\"UniqueName\":\"EnrichmentPlantSouth\",\"DisplayName\":\"South Coast Enrichment Plant\",\"Disabled\":false,\"Capturable\":true,\"CaptureDefense\":10.0,\"CaptureRange\":1000.0,\"Center\":{\"x\":7004.273,\"y\":2.39550781,\"z\":-35508.1328},\"SelectionPosition\":{\"x\":7034.67773,\"y\":2.39544678,\"z\":-35556.9063},\"Tower\":\"\",\"VerticalLandingPoints\":[{\"x\":7014.589,\"y\":2.478918,\"z\":-35624.957},{\"x\":7045.49365,\"y\":2.638134,\"z\":-35484.2578},{\"x\":7093.85059,\"y\":2.5039978,\"z\":-35578.08}],\"ServicePoints\":[],\"roads\":{\"roads\":[]},\"runways\":[]}]";
    }
}