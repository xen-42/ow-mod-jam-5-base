using HarmonyLib;
using NewHorizons;
using NewHorizons.Builder.Atmosphere;
using NewHorizons.Builder.Props;
using NewHorizons.External;
using NewHorizons.External.Modules.Props;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ModJam5
{
    public class ModJam5 : ModBehaviour
    {
        public static string SystemName = "Jam5";

        public static ModJam5 Instance { get; private set; }
        public INewHorizons NewHorizons;

        public bool AllowSpawnOverride { get; private set; }
        public bool ShowAllowedVolume { get; private set; }

        private List<GameObject> allowedVolumeObjects = new();

        public void Awake()
        {
            Instance = this;
            // You won't be able to access OWML's mod helper in Awake.
            // So you probably don't want to do anything here.
            // Use Start() instead.
        }

        public override void Configure(IModConfig config)
        {
            base.Configure(config);

            AllowSpawnOverride = config.GetSettingsValue<bool>("allowSpawnOverride");
            ShowAllowedVolume = config.GetSettingsValue<bool>("showAllowedVolume");

            foreach (var sphere in allowedVolumeObjects)
            {
                if (sphere != null)
                {
                    sphere.SetActive(ShowAllowedVolume);
                }
            }
        }

        public void FixCompatIssues()
        {
            var jamEntries = NewHorizons.GetInstalledAddons()
                .Select(ModHelper.Interaction.TryGetMod)
                .Where(addon => (addon.GetDependencies().Select(x => x.ModHelper.Manifest.UniqueName).Contains(ModHelper.Manifest.UniqueName)))
                .Append(this)
                .ToArray();

            ModHelper.Console.WriteLine($"Found {jamEntries.Length} jam entries");

            // Moves the planets
            MiniSolarSystemOrganizer.Apply(Main.BodyDict[SystemName], jamEntries);

            // Make sure all ship log entries don't overlap
            ShipLogPacking.Apply(jamEntries);

            // Make sure that the root mod for the system remains us
            Main.SystemDict[SystemName].Mod = this;

            ModHelper.Console.WriteLine($"Finished packing jam entry ship logs");
        }

        public void Start()
        {
            // Starting here, you'll have access to OWML's mod helper.
            ModHelper.Console.WriteLine($"My mod {nameof(ModJam5)} is loaded!", MessageType.Success);

            // Get the New Horizons API and load configs
            NewHorizons = ModHelper.Interaction.TryGetModApi<INewHorizons>("xen.NewHorizons");
            NewHorizons.LoadConfigs(this);

            new Harmony("xen-42.ModJam5").PatchAll(Assembly.GetExecutingAssembly());

            // Example of accessing game code.
            OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen); // We start on title screen
            LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;

            NewHorizons.GetStarSystemLoadedEvent().AddListener(OnStarSystemLoaded);
            NewHorizons.RegisterCustomBuilder(CustomBuilder);

            ModHelper.Events.Unity.FireOnNextUpdate(FixCompatIssues);

            this.gameObject.AddComponent<StarshipCommunityHelper>();

            PingConditionHandler.Setup();
        }

        public void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene)
        {
            allowedVolumeObjects.Clear();

            if (newScene != OWScene.SolarSystem) return;
            ModHelper.Console.WriteLine("Loaded into solar system!", MessageType.Success);
        }

        public void OnStarSystemLoaded(string system)
        {
            if (system != SystemName)
            {
                return;
            }

            ModHelper.Console.WriteLine("Loaded into Jam 5 system!", MessageType.Success);

            ModHelper.Events.Unity.FireInNUpdates(() =>
            {
                // So stupid but tidal locking is dumb and honestly nh should just let u set rotation directly but i cant be bothered
                // Some sort of euler angle-y degeneracy breaking here
                NewHorizons.GetPlanet("Starship Community").transform.rotation = Quaternion.Euler(307.7599f, 131.2078f, 19.5048f);

                /*
                // This is silly and jank and NH should support this better
                // Stops the potentially like 10 different stars all casting weird shadows all over the station
                SunOverrideBuilder.Make(NewHorizons.GetPlanet("Central Station"), null, new NewHorizons.External.Modules.AtmosphereModule()
                {
                    clouds = new()
                    {
                        innerCloudRadius = 100,
                    }
                }, null, 0);
                */
            }, 10);

            ModHelper.Events.Unity.FireInNUpdates(() =>
            {
                GameObject.Find("PlayerHUD/HelmetOnUI/UICanvas/SecondaryGroup/HUD_Minimap/Minimap_Root").GetComponent<Minimap>().SetComponentsEnabled(false);
            }, 40);
        }

        public void CustomBuilder(GameObject planet, string extrasConfig)
        {
            if (string.IsNullOrEmpty(extrasConfig))
            {
                return;
            }
            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(extrasConfig);
                if (dict.TryGetValue("isCenterOfMiniSystem", out var isCenter) && isCenter is bool isCenterBool && isCenterBool)
                {
                    // big sphere
                    GameObject sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphereGO.transform.parent = planet.transform;
                    sphereGO.transform.localPosition = Vector3.zero;
                    sphereGO.transform.localScale = Vector3.one * 2f * MiniSolarSystemOrganizer.MINI_SYSTEM_RADIUS;
                    sphereGO.GetComponent<Collider>().enabled = false;
                    sphereGO.GetComponent<MeshRenderer>().material.color = UnityEngine.Color.white;
                    sphereGO.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    sphereGO.GetComponent<MeshRenderer>().receiveShadows = false;
                    var mesh = sphereGO.GetComponent<MeshFilter>().mesh;                    
                    var normals = mesh.normals;
                    for (var i = 0; i < normals.Length; i++) normals[i] = -normals[i];
                    mesh.normals = normals;
                    sphereGO.GetComponent<MeshFilter>().mesh = mesh;
                    var triangles = mesh.triangles;
                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        (triangles[i + 1], triangles[i]) = (triangles[i], triangles[i + 1]);
                    }
                    mesh.triangles = triangles;
                    sphereGO.SetActive(ShowAllowedVolume);
                    allowedVolumeObjects.Add(sphereGO);
                }
                if (dict.TryGetValue("isPlatform", out var isPlatform) && isPlatform is bool isPlatformBool && isPlatformBool)
                {
                    var platformAngle = 0f;
                    if (dict.TryGetValue("angle", out var angle))
                    {
                        platformAngle = float.Parse(angle.ToString());
                    }

                    var platform = DetailBuilder.Make(planet, null, this, new DetailInfo()
                    {
                        assetBundle = "planets/assets/jam5bundle",
                        path = "Assets/Jam 5/root-jam5-start-platform.prefab"
                    });

                    // Not sure why it doesn't work this frame
                    ModHelper.Events.Unity.FireInNUpdates(() =>
                    {
                        planet.transform.position = Quaternion.AngleAxis(platformAngle, Vector3.up) * (Vector3.forward * 5) + NewHorizons.GetPlanet("Central Station").transform.position;
                        planet.transform.rotation = Quaternion.Euler(0, platformAngle, 0);
                    }, 10);
                }
            }
            catch (Exception e)
            {
                LogError($"Failed when running custom builder for [{planet?.name}]: " + e.ToString());
            }
        }

        public static void Log(string message)
        {
            Instance.ModHelper.Console.WriteLine(message, MessageType.Info);
        }

        public static void LogError(string message)
        {
            Instance.ModHelper.Console.WriteLine(message, MessageType.Error);
        }

        [Conditional("DEBUG")]
        public static void LogDebug(string message)
        {
            Instance.ModHelper.Console.WriteLine("DEBUG: " + message, MessageType.Info);
        }

        private static bool GetBool(string name, JObject extras)
        {
            return extras.GetValue(name)?.ToObject<bool>() is bool result && result;
        }

        public static bool IsPlatform(NewHorizonsBody body)
        {
            if (body.Config.extras is JObject extras)
            {
                if (GetBool("isPlatform", extras))
                {
                    return true;
                }
            }
            return false;
        }
    }

}
