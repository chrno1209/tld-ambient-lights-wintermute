﻿using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AmbientLights
{
    internal class SceneConfig
    {
        public List<AmbEmitter> emitters = null;
        public Dictionary<string, AmbPeriod> periods = new Dictionary<string, AmbPeriod>();
        public Dictionary<string, WeatherMod> weathers = new Dictionary<string, WeatherMod>();
        public AmbLocationModifiers options = new AmbLocationModifiers();
    }

    internal class AmbEmitter
    {
        public string description = "";
        public string position = "";
        public string orientation = "";
        public float size = 1f;
        public float cover = 0f;
        public int priority = 1;
    }

    internal class AmbPeriod
    {
        public Dictionary<string, AmbSet> orientations = new Dictionary<string, AmbSet>();
        public float[] intensity = null;
        public float[] range = null;
    }

    internal class WeatherMod
    {
        public float sMod = 1f;
        public float vMod = 1f;
        public float rMod = 1f;
        public float gMod = 1f;
        public float bMod = 1f;

        public float intMod = 1f;
        public float rngMod = 1f;
    }

    internal class AmbSet
    {
        public float[] sun = null;
        public float[] hue = null;
    }

    internal class LightSet
    {
        public Dictionary<string, LightOrientation> orientations = new Dictionary<string, LightOrientation>();
    }

    internal class LightOrientation
    {
        public Color32 color;
        public float intensity;
        public float range;
    }

    internal class AmbLocationModifiers
    {
        public string override_shadows = "";
        public float intensity_multiplier = 1f;
        public float range_multiplier = 1f;
        public float aurora_range_multiplier = 1f;
        public float aurora_intensity_multiplier = 1f;
    }

    internal class LightConfig
    {
        public readonly string[] cardinal = { "north", "south", "east", "west" };

        public bool ready = false;

        private string scene;

        public SceneConfig data = null;
        private static Dictionary<string, AmbPeriod> periodsConfig = null;
        private static Dictionary<string, WeatherMod> weathersConfig = null;
        
        public void Load()
        {
            scene = AmbientLights.currentScene;

            LoadGlobalConfig();
            LoadSceneConfig();

            MergeConfigs();
        }

        public void Reload()
        {

        }

        private void LoadSceneConfig()
        {
            string sceneFile = "scene_" + scene + ".json";

            if (File.Exists(Path.Combine(AmbientLights.modDataFolder, sceneFile)))
            {
                data = Utils.DeserializeObject<SceneConfig>(File.ReadAllText(Path.Combine(AmbientLights.modDataFolder, sceneFile)));
            }
            else
            {
                Debug.Log("[ambient-lights] No lighting data for scene " + scene + " found. Using game default.");
            }
        }

        private void LoadGlobalConfig()
        {
            if (File.Exists(Path.Combine(AmbientLights.modDataFolder, "weather_sets.json")))
            {
                weathersConfig = Utils.DeserializeObject<Dictionary<string, WeatherMod>>(File.ReadAllText(Path.Combine(AmbientLights.modDataFolder, "weather_sets.json")));
            }
            else
            {
                Debug.Log("[ambient-lights] ERROR: No weather sets data found");
            }

            if (File.Exists(Path.Combine(AmbientLights.modDataFolder, "global_sets.json")))
            {
                periodsConfig = Utils.DeserializeObject<Dictionary<string, AmbPeriod>>(File.ReadAllText(Path.Combine(AmbientLights.modDataFolder, "global_sets.json")));
            }
            else
            {
                Debug.Log("[ambient-lights] ERROR: No light sets data found");
            }
        }

        private void MergeConfigs()
        {
            if (data != null)
            {
                if (periodsConfig != null)
                    data.periods = periodsConfig;

                if (weathersConfig != null)
                    data.weathers = weathersConfig;

                ready = true;

                Debug.Log(Utils.SerializeObject(data));
            }
        }

        internal AmbPeriod GetPeriodSet(string periodName = "")
        {
            AmbPeriod period = null;

            if (periodName == "")
                periodName = TimeWeather.currentPeriod;

            if (data.periods.ContainsKey(periodName))
            {
                period = data.periods[periodName];
            }
            else if (data.periods.ContainsKey("default"))
            {
                period = data.periods["default"];
            }

            return period;
        }

        internal LightSet GetCurrentLightSet()
        {
            TimeWeather.GetCurrentPeriodAndWeather();

            LightSet ls = new LightSet();

            UniStormWeatherSystem uniStorm = GameManager.GetUniStorm();
            TODStateConfig state = uniStorm.GetActiveTODState();

            AmbPeriod prd = GetPeriodSet();

            //Base Colors
            Color baseSun = state.m_SunLight;
            Color baseFog = state.m_FogColor;
            baseSun.a = 1;
            baseFog.a = 1;

            float baseInt = 1f;
            float baseRng = 10f;

            foreach (string dir in cardinal)
            {
                Color lColor = baseFog;

                if (prd != null)
                {
                    float sunMix = Mathf.Lerp(prd.orientations[dir].sun[0], prd.orientations[dir].sun[1], TimeWeather.currentPeriodPct);
                    lColor = Color.Lerp(baseFog, baseSun, sunMix);

                    //Apply hue mod
                    if (prd.orientations[dir].hue != null)
                    {
                        float hueMix = Mathf.Lerp(prd.orientations[dir].hue[0], prd.orientations[dir].hue[1], TimeWeather.currentPeriodPct);
                        ColorHSV lColorHSV = new ColorHSV(lColor);

                        lColorHSV.h += hueMix;

                        lColor = lColorHSV;
                    }

                    //Apply weather mods
                    lColor = ApplyWeatherMod(lColor);

                    //Apply Intensity & Range
                    baseInt = Mathf.Lerp(prd.intensity[0], prd.intensity[1], TimeWeather.currentPeriodPct);
                    baseRng = Mathf.Lerp(prd.range[0], prd.range[1], TimeWeather.currentPeriodPct);
                }

                LightOrientation lo = new LightOrientation
                {
                    color = (Color)lColor,
                    intensity = baseInt,
                    range = baseRng
                };

                ls.orientations.Add(dir, lo);
            }

            LightOrientation defaultOrientation = new LightOrientation
            {
                color = baseFog,
                intensity = baseInt,
                range = baseRng
            };

            ls.orientations.Add("default", defaultOrientation);

            return ls;
        }

        internal Color ApplyWeatherMod(Color baseColor)
        {
            Color modColor;

            //Debug.Log(TimeWeather.currentWeather);
            //Debug.Log("Change from: " + (Color32)baseColor);
            
            WeatherMod wthMod = GetWeatherMod(TimeWeather.currentWeather);

            float sMod = wthMod.sMod, vMod = wthMod.vMod, rMod = wthMod.rMod, gMod = wthMod.gMod, bMod = wthMod.bMod;

            
            if (TimeWeather.currentWeatherPct < 1f)
            {
                WeatherMod wthPrev = GetWeatherMod(TimeWeather.previousWeather);

                sMod = Mathf.Lerp(wthPrev.sMod, wthMod.sMod, TimeWeather.currentWeatherPct);
                vMod = Mathf.Lerp(wthPrev.vMod, wthMod.vMod, TimeWeather.currentWeatherPct);
                rMod = Mathf.Lerp(wthPrev.rMod, wthMod.rMod, TimeWeather.currentWeatherPct);
                gMod = Mathf.Lerp(wthPrev.gMod, wthMod.gMod, TimeWeather.currentWeatherPct);
                bMod = Mathf.Lerp(wthPrev.bMod, wthMod.bMod, TimeWeather.currentWeatherPct);
            }

            baseColor.r *= rMod;
            baseColor.g *= gMod;
            baseColor.b *= bMod;

            ColorHSV cHSV = new ColorHSV(baseColor);
            cHSV.s *= sMod;
            cHSV.v *= vMod;

            modColor = cHSV;

            modColor.a = 1;

            //Debug.Log("Change to: " + (Color32)modColor);

            return modColor;
        }

        internal WeatherMod GetWeatherMod(string weather)
        {
            WeatherMod wm = new WeatherMod();

            if (data.weathers.ContainsKey(weather))
            {
                wm = data.weathers[weather];
            }
            else if (data.weathers.ContainsKey("default"))
            {
                wm = data.weathers["default"];
            }

            return wm;
        }
    }
}
