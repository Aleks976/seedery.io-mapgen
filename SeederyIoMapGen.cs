﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Plugins;

using Rust;

using UnityEngine;

struct MapMonument
{
    public int X;
    public int Y;
    public string Name;
};

namespace Oxide.Plugins
{
    [Info("SeederyIoMapGen", "Pauli 'Dids' Jokela", "0.8.0")]
    [Description("A public Oxide plugin for seedery.io's Rust map generation.")]

    public class SeederyIoMapGen : CovalencePlugin
    {
        #region Properties
        const string UploadUrl = "http://localhost:5000/upload";
        private int _mapWidth;
        private int _mapHeight;
        private Terrain _terrain;
        private TerrainData _terrainData;
        private Vector3 _terrainPos;
        #endregion

        #region Plugin methods
        private void OnServerInitialized()
        {
            GenerateMap();
        }
        #endregion

        #region Map generation
        private void GenerateMap()
        {
            // Initialize our global values
            Initialize();

            // Generate the texture
            Texture2D terrainTexture = GenerateTerrainTexture(_mapWidth, _mapHeight);

            // Get monuments
            List<MapMonument> monuments = GetMonuments();
            string monumentsJson = JsonConvert.SerializeObject(monuments, Formatting.None);

            // Get server seed
            int serverSeed = ConVar.Server.seed;

            // Get server map size
            int serverMapSize = (int)_terrain.terrainData.size.x;

            // Get server protocol
            string serverProtocol = Rust.Protocol.printable;

            // Save it
            //SaveImage(heightmapTexture, monumentsJson, serverSeed, serverMapSize, serverProtocol);

            // Upload it
            UploadImage(terrainTexture, monumentsJson, serverSeed, serverMapSize, serverProtocol);

            // Destroy the texture
            UnityEngine.Object.Destroy(terrainTexture);
        }

        private void Initialize()
        {
            // Fetch the terrain and the width and height
            _terrain = TerrainMeta.Terrain;
            _terrainData = TerrainMeta.Terrain.terrainData;
            _terrainPos = TerrainMeta.Terrain.GetPosition();
            _mapWidth = _terrainData.heightmapWidth - 1;
            _mapHeight = _terrainData.heightmapHeight - 1;
        }

        private Texture2D GenerateTerrainTexture(int width, int height)
        {
            Log($"Generating a terrain texture of size: {width}x{height}");

            float lowestTerrainheight = GetLowestTerrainHeight();
            float highestTerrainHeight = GetHighestTerrainHeight();

            Vector3 terrainStart = TerrainMeta.Terrain.GetPosition(); // -1500
            Vector3 terrainSize = TerrainMeta.Size; // 3000
            Vector3 terrainScale = new Vector3(TerrainMeta.Size.x / width, TerrainMeta.Size.y / width, TerrainMeta.Size.z / width); // > 1

            // TODO: Can't seem to figure out how to sample the terrain texture itself,
            //       so going with just the colormap for now

            // Draw the colormap
            Texture2D colormapTexture = new Texture2D(width, height);
            for (int y = 0; y < colormapTexture.height; y++)
            {
                for (int x = 0; x < colormapTexture.width; x++)
                {
                    // Mask info
                    /*
                            public const int EVERYTHING = -1;
                            public const int NOTHING = 0;
                            public const int DIRT = 1;
                            public const int SNOW = 2;
                            public const int SAND = 4;
                            public const int ROCK = 8;
                            public const int GRASS = 16;
                            public const int FOREST = 32;
                            public const int STONES = 64;
                            public const int GRAVEL = 128;
                            public const int DIRT_IDX = 0;
                            public const int SNOW_IDX = 1;
                            public const int SAND_IDX = 2;
                            public const int ROCK_IDX = 3;
                            public const int GRASS_IDX = 4;
                            public const int FOREST_IDX = 5;
                            public const int STONES_IDX = 6;
                            public const int GRAVEL_IDX = 7;
                    */

                    int mask = 1 | 2 | 4 | 8 | 16 | 64;
                    mask = -1; // Show all

                    // -1500 (-half of size)
                    float terrainStartX = TerrainMeta.Terrain.GetPosition().x;
                    float terrainStartY = TerrainMeta.Terrain.GetPosition().z;

                    // 3000
                    float terrainSizeX = TerrainMeta.Size.x;
                    float terrainSizeY = TerrainMeta.Size.z;

                    // > 1
                    float terrainScaleUpX = TerrainMeta.Size.x / width;
                    float terrainScaleUpY = TerrainMeta.Size.z / height;

                    // < 0
                    float terrainScaleDownX = width / TerrainMeta.Size.x;
                    float terrainScaleDownY = height / TerrainMeta.Size.z;

                    // Scale our coordinates to match with the terrain
                    float startX = x * terrainScaleUpX;
                    float startY = y * terrainScaleUpY;

                    // Offset our coordinates to match the terrain
                    float calculatedTerrainX = startX + terrainStartX;
                    float calculatedTerrainY = startY + terrainStartY;

                    // Create the position (in world coordinates) that we want to sample a color from
                    Vector3 terrainWorldPosition = new Vector3(calculatedTerrainX, 0, calculatedTerrainY);

                    // Get the water depth at the current location
                    float waterDepth = TerrainMeta.WaterMap.GetDepth(terrainWorldPosition) + 0.5f; // NOTE: I'm assuming there's waves, because this "floats" around 49-50, so +0.5 for accuracy

                    // Apply it to the texture (if above or equal to water level), note that this includes rivers!
                    float terrainHeight = _terrain.terrainData.GetHeight(x, y) - lowestTerrainheight;
                    float currentHeight = terrainHeight / (highestTerrainHeight - lowestTerrainheight);
                    if (currentHeight > waterDepth)
                    {
                        UnityEngine.Color pixelColor = TerrainMeta.Colors.GetColor(terrainWorldPosition, mask);
                        pixelColor.a = TerrainMeta.AlphaMap.GetAlpha(x, y); // This basically removes little spots all over the map
                        colormapTexture.SetPixel(x, y, pixelColor);
                    }

                    // Otherwise just draw a dark water color
                    else
                    {
                        UnityEngine.Color blueColor = new UnityEngine.Color((1f / 255f) * 72f, (1f / 255f) * 61f, (1f / 255f) * 139f, 1);
                        colormapTexture.SetPixel(x, y, blueColor);
                    }
                }
            }
            colormapTexture.Apply();
            return colormapTexture;
        }

        private float GetLowestTerrainHeight()
        {
            float lowestHeight = TerrainMeta.Size.y;
            for (var x = 0; x < TerrainMeta.Size.x; x++)
            {
                for (var y = 0; y < TerrainMeta.Size.z; y++)
                {
                    var h = _terrain.terrainData.GetHeight(x, y);
                    if (h < lowestHeight) lowestHeight = h;
                }
            }
            return lowestHeight;
        }

        private float GetHighestTerrainHeight()
        {
            float highestHeight = 0;
            for (var x = 0; x < TerrainMeta.Size.x; x++)
            {
                for (var y = 0; y < TerrainMeta.Size.z; y++)
                {
                    var h = _terrain.terrainData.GetHeight(x, y);
                    if (h > highestHeight) highestHeight = h;
                }
            }
            return highestHeight;
        }

        private List<MapMonument> GetMonuments()
        {
            var results = new List<MapMonument>();
            var monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>();
            Log("Found " + monuments.Length + " monuments.");
            foreach (var m in monuments)
            {
                MapMonument monument;
                monument.X = (int)ConvertWorldToMapCoordinates(m.transform.position.x);
                monument.Y = (int)ConvertWorldToMapCoordinates(-m.transform.position.z); // Note how we're flipping the Z coordinate (Y in our case)
                monument.Name = "Unknown";

                if (m.name.ToLower().Contains("lighthouse")) monument.Name = "Lighthouse";
                else if (m.name.ToLower().Contains("cave_small")) monument.Name = "Cave (Small)";
                else if (m.name.ToLower().Contains("cave_medium")) monument.Name = "Cave (Medium)";
                else if (m.name.ToLower().Contains("cave_large")) monument.Name = "Cave (Large)";
                else if (m.name.ToLower().Contains("power_sub_small")) monument.Name = "Power Substation (Small)";
                else if (m.name.ToLower().Contains("power_sub_big")) monument.Name = "Power Substation (Big)";
                else if (m.name.ToLower().Contains("warehouse")) monument.Name = "Warehouse";
                else if (m.name.ToLower().Contains("sphere_tank")) monument.Name = "Sphere";
                else if (m.name.ToLower().Contains("satellite")) monument.Name = "Satellite";
                else if (m.name.ToLower().Contains("radtown_small")) monument.Name = "Radtown";
                else if (m.name.ToLower().Contains("powerplant")) monument.Name = "Powerplant";
                else if (m.name.ToLower().Contains("trainyard")) monument.Name = "Trainyard";
                else if (m.name.ToLower().Contains("airfield")) monument.Name = "Airfield";
                else if (m.name.ToLower().Contains("military_tunnel")) monument.Name = "Military Tunnel";
                else if (m.name.ToLower().Contains("water_treatment_plant")) monument.Name = "Water Treatment Plant";
                else if (m.name.ToLower().Contains("monuments")) Log("WARNING - Invalid monument detected: " + m.name.ToLower());
                else Log("WARNING - Monument not detected: " + m.name.ToLower());
                if (monument.Name != "Unknown") results.Add(monument);
            }
            return results;
        }
        #endregion

        #region Image handling
        // TODO: Not working due to sandboxing
        private void SaveImage(Texture2D texture, string monumentsJson, int seed, int size, string protocol)
        {
            Log("Saving image..");

            // Encode texture into PNG
            byte[] bytes = texture.EncodeToPNG();

            // TODO: Draw monuments on the map image


            // Write the image
            File.WriteAllBytes(Rust.Application.dataPath + "/../" + seed + "_" + size + "_" + protocol + ".png", bytes);

            Log("Finished saving image");
        }

        private void UploadImage(Texture2D texture, string monumentsJson, int seed, int size, string protocol)
        {
            Log("Uploading to: " + UploadUrl);

            // Encode texture into a PNG
            byte[] bytes = texture.EncodeToPNG();

            WWWForm form = new WWWForm();
            form.AddField("protocol", protocol);
            form.AddField("size", size);
            form.AddField("seed", seed);
            form.AddField("monuments", monumentsJson);
            form.AddField("filename", "map.png");
            form.AddField("data", Convert.ToBase64String(bytes));

            // Upload
            WWW w = new WWW(UploadUrl, form);
        }
        #endregion

        #region Utilities
        private float ConvertWorldToMapCoordinates(float worldPosition)
        {
            // NOTE: I'm assuming the worldPosition starts in the middle of the map, meaning 0,
            //       which is why we're adding half of the map size to it.
            float realPosition = (worldPosition + (TerrainMeta.Size.x / 2));

            // At this point, we need to figure out how to convert the position from 0-3000 -> 0-2048
            float scale = realPosition / TerrainMeta.Size.x;
            float mapPosition = scale * _mapWidth;
            return mapPosition;
        }

        public static void Log(string str)
        {
            Interface.Oxide.LogDebug("SeederyIoMapGen :: " + str);
        }
        #endregion
    }
}
