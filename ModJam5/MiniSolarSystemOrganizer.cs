using NewHorizons.External;
using Newtonsoft.Json.Linq;
using OWML.Common;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModJam5;

internal static class MiniSolarSystemOrganizer
{
    public const float MINI_SYSTEM_RADIUS = 2500f;
    public const float MINI_SYSTEM_DISTANCE = 12000f;
    public const float MINI_SYSTEM_DISTANCE_CLOSE = 8000f;

    public static bool HubActive { get; private set; }

    public static void Apply(IEnumerable<NewHorizonsBody> bodies, IModBehaviour[] jamEntries)
    {
        int halfEntries = 8;
        int baseMod = 1;
        int distanceThreshold = halfEntries + baseMod;
        float miniSystemDist = jamEntries.Count() <= distanceThreshold ? MINI_SYSTEM_DISTANCE_CLOSE : MINI_SYSTEM_DISTANCE;

        // Only allow one spawn point for now ig and let them override the sun one immediately
        // Only include spawns that set both because idk why don't you want a ship bro?
        var bodiesWithSpawns = bodies.Where(x => x.Config.Spawn?.playerSpawn != null && x.Config.Spawn?.shipSpawn != null);
        if (bodiesWithSpawns.Count() > 1)
        {
            var foundSpawnFlag = false;
            foreach (var body in bodiesWithSpawns)
            {
                var isDefaultSpawn = body.Config.name == "Hub";
                var isValidSpawn = (isDefaultSpawn && !ModJam5.Instance.AllowSpawnOverride) || (!isDefaultSpawn && ModJam5.Instance.AllowSpawnOverride);
                var keepSpawn = isValidSpawn && !foundSpawnFlag;
                if (keepSpawn)
                {
                    foundSpawnFlag = true;
                }
                else
                {
                    body.Config.Spawn = null;
                }
            }
        }

        foreach (var body in bodies)
        {
            var hadShipLog = body.Config.ShipLog?.mapMode != null;

            // Force all bodies to have shiplogs so we can remove them if need be
            body.Config.ShipLog ??= new();
            body.Config.ShipLog.mapMode ??= new();

            // Force all planets to be automatic placement
            var mapMode = body.Config.ShipLog.mapMode;

            mapMode.manualPosition = null;
            mapMode.manualNavigationPosition = null;

            // If it's a moon with no ship logs, just get rid of it
            var isMoon = !string.IsNullOrEmpty(body.Config.Orbit.primaryBody) && !body.Config.Base.centerOfSolarSystem;
            var shouldRemove = isMoon && !hadShipLog;
            if (shouldRemove)
            {
                mapMode.remove = true;
            }
        }

        var centers = bodies.Where(x => x.Config.Base.centerOfSolarSystem && x.Config.name != "Central Station");
        var staticBodies = bodies.Where(x => x.Config.Orbit.isStatic || x.Config.Orbit.staticPosition != null)
                .Where(x => !x.Config.Base.centerOfSolarSystem).Where(x => !centers.Any(y => y.Config.name == x.Config.name));
        var platforms = bodies.Where(ModJam5.IsPlatform);

        // Verify mods are all valid
        var angularPosition = new Dictionary<string, float>();

        ModJam5.Log($"Order systems.");
        string[] order = [
            ModJam5.Instance.ModHelper.Manifest.UniqueName, // Doesn't get placed in the circle with the rest of them, must go first
            "2walker2.OWJam5ModProject",                 //Heliostudy         MAX
            "TheLoweDown256.TLDJam5",                    //Apostrolytum       Medium (?)
            "APOLLO.939sPlanetdotSTRUCTURE",             //MagMag             MAX
            "Trifid.TrifidJam5",                         //Scale              Tiny
            "LeeSpork.ModJam5",                          //Cat                MAX (Explodes!)
            "CantAffordaName.OnARail",                   //Train              Medium
            "Boreas.Jam5CakeCapere",                     //Patrick Star       Medium
            "Hawkbar.Terrarium",                         //Lonely Lump        Medium (?) (Expands)
            "TheSignalJammers.FifthModJam",              //Silver Lining      Medium (?) (Expands)
            "orclecle.Jam5PingBox",                      //Diorama Interface  Medium (?) (Expands)
            "Onbvb.catastrophe",                         //Dark Green Star    Large
            "Vambok.HearthianParable",                   //Parable            Medium
            "Ender.Beryl.BrightSpark",                   //RGB                Large
            "MegaPiggy.AnomalyResearchAndContainment",   //Verdant Beacon     Medium-Large
            "TeamGeswaldo.Jam5",                         //Bruised Brother    Large-Max
            "Etherpod.T0187",                            //Radio Moon         Small (?)
        ];

        List<IModBehaviour> orderedMods = [];
        foreach (var orderName in order)
        {
            foreach (var mod in jamEntries)
            {
                if (mod.ModHelper.Manifest.UniqueName == orderName)
                {
                    orderedMods.Add(mod);
                }
            }
        }

        // Just put it anyway and they'll overlap if its like gleeberdome or smthng
        foreach (var mod in jamEntries)
        {
            if (!order.Contains(mod.ModHelper.Manifest.UniqueName))
            {
                ModJam5.LogError($"Mod unaccounted for! {mod.ModHelper.Manifest.UniqueName} will cause overlaps!");
                orderedMods.Add(mod);
            }
        }

        for (int i = 0; i < orderedMods.Count - 1; i++)
        {
            // Skip the base mod which is first
            var mod = orderedMods[i+1];
            var angle = 360f * (float)i / (float)(orderedMods.Count - 1);
            angularPosition[mod.ModHelper.Manifest.UniqueName] = angle;
            if (!centers.Any(x => x.Mod.ModHelper.Manifest.UniqueName == mod.ModHelper.Manifest.UniqueName))
            {
                ModJam5.LogError($"INVALID JAM ENTRY {mod.ModHelper.Manifest.UniqueName} HAS NO CENTER");
            }
            if (!platforms.Any(x => x.Mod.ModHelper.Manifest.UniqueName == mod.ModHelper.Manifest.UniqueName))
            {
                ModJam5.LogError($"INVALID JAM ENTRY {mod.ModHelper.Manifest.UniqueName} HAS NO PLATFORM");
            }
        }
        angularPosition[ModJam5.Instance.ModHelper.Manifest.UniqueName] = -10f;

        var ignoreStaticBodies = new List<string>();

        foreach (var center in centers)
        {
            ModJam5.LogDebug($"Fixing mod center {center.Config.name}");

            center.Config.Orbit ??= new();
            center.Config.Base.centerOfSolarSystem = false;
            center.Config.Orbit.isStatic = true;
            var angle = angularPosition[center.Mod.ModHelper.Manifest.UniqueName];
            if (center.Config.name == "Starship Community")
            {
                center.Config.Orbit.staticPosition = (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * 300) + Vector3.up * 100;
            }
            else
            {
                center.Config.Orbit.staticPosition = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * miniSystemDist;
            }
            center.Config.Orbit.primaryBody = "Central Station";
            var dict = new Dictionary<string, object>();
            if (center.Config.extras is JObject jObject)
            {
                dict = jObject.ToObject<Dictionary<string, object>>();
            }
            if (center.Config.name != "Starship Community")
            {
                dict["isCenterOfMiniSystem"] = true;
            }
            center.Config.extras = JObject.FromObject(dict);

            ignoreStaticBodies.Add(center.Config.name.Trim().ToLowerInvariant());
        }

        foreach (var platform in platforms)
        {
            var dict = new Dictionary<string, object>();
            if (platform.Config.extras is JObject jObject)
            {
                dict = jObject.ToObject<Dictionary<string, object>>();
            }
            dict["angle"] = angularPosition[platform.Mod.ModHelper.Manifest.UniqueName];
            platform.Config.extras = JObject.FromObject(dict);

            platform.Config.ReferenceFrame ??= new();
            platform.Config.ReferenceFrame.enabled = false;
            platform.Config.Base.showMinimap = false;

            ignoreStaticBodies.Add(platform.Config.name.Trim().ToLowerInvariant());
        }

        HubActive = false;
        foreach (var staticBody in staticBodies)
        {
            //Handle jam hub bodies
            if(staticBody.Config.name.Equals("AnonDomain") || staticBody.Config.name.Equals("Mod Jam Hub"))
            {
                HubActive = true;
                continue;
            }

            ModJam5.LogDebug($"Fixing static body position {staticBody.Config.name}");

            if (ignoreStaticBodies.Contains(staticBody.Config.name.Trim().ToLowerInvariant()))
            {
                // No idea why this happens
                continue;
            }

            if (ignoreStaticBodies.Contains(staticBody.Config.name.Trim().ToLowerInvariant()))
            {
                // No idea why this happens
                continue;
            }

            staticBody.Config.Orbit ??= new();
            staticBody.Config.Orbit.staticPosition ??= Vector3.zero;
            if (((Vector3)staticBody.Config.Orbit.staticPosition).magnitude > MINI_SYSTEM_RADIUS)
            {
                ModJam5.LogError($"INVALID JAM ENTRY {staticBody.Config.name} IS OUTSIDE MAXIMUM RADIUS {MINI_SYSTEM_RADIUS}");
            }
            var angle = angularPosition[staticBody.Mod.ModHelper.Manifest.UniqueName];
            staticBody.Config.Orbit.staticPosition += Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * miniSystemDist;
        }
    }
}
