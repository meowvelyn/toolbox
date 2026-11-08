using UnityEditor;
using UnityEngine;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


public class TextureImportSettingsExporter : EditorWindow
{
    private GameObject target;


    [MenuItem("Toolbox/Texture Import Settings Exporter", false, 8)]
    static void Open()
    {
        GetWindow<TextureImportSettingsExporter>(
            "Texture Exporter"
        );
    }


    void OnGUI()
    {
        target = (GameObject)EditorGUILayout.ObjectField(
            "Avatar Root",
            target,
            typeof(GameObject),
            true
        );


        if (GUILayout.Button("Export") && target != null)
        {
            Export(target);
        }
    }


    static void Export(GameObject root)
    {
        var textures = new Dictionary<string, TextureData>();


        foreach (Texture texture in AvatarTextureScanner.Scan(root))
        {
            AddTexture(texture, textures);
        }


        string json = JsonUtility.ToJson(
            new TextureCollection
            {
                textures = textures.Values.OrderBy(texture => texture.path, System.StringComparer.Ordinal).ToList()
            },
            true
        );


        string path = EditorUtility.SaveFilePanel(
            "Save Texture Import Settings",
            "",
            "TextureImportSettings.json",
            "json"
        );


        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                File.WriteAllText(path, json);
                Debug.Log($"Exported {textures.Count} textures");
            }
            catch (System.Exception exception) when (exception is IOException ||
                exception is System.UnauthorizedAccessException || exception is System.ArgumentException)
            {
                Debug.LogError($"Could not export texture import settings: {exception.Message}");
            }
        }
    }


    static void AddTexture(
        Texture texture,
        Dictionary<string, TextureData> textures)
    {
        string path =
            AssetDatabase.GetAssetPath(texture);


        if (string.IsNullOrEmpty(path))
            return;


        string guid =
            AssetDatabase.AssetPathToGUID(path);


        if (textures.ContainsKey(guid))
            return;


        TextureImporter importer =
            AssetImporter.GetAtPath(path)
            as TextureImporter;


        if (importer == null)
            return;


        TextureData data = new TextureData
        {
            guid = guid,
            path = path,

            textureType =
                importer.textureType.ToString(),

            sRGBTexture =
                importer.sRGBTexture,

            mipmapEnabled =
                importer.mipmapEnabled,

            alphaSource =
                importer.alphaSource.ToString(),

            maxTextureSize =
                importer.maxTextureSize,

            wrapMode =
                importer.wrapMode.ToString(),

            filterMode =
                importer.filterMode.ToString(),

            textureCompression =
                importer.textureCompression.ToString(),

            isReadable =
                importer.isReadable,

            streamingMipmaps =
                importer.streamingMipmaps,

            platformOverrides =
                new List<TexturePlatformData>()
        };
        
        string[] platforms =
            {
                "Standalone",
                "Android",
                "iPhone",
            };

        foreach (string platform in platforms)
        {
            TextureImporterPlatformSettings settings =
                importer.GetPlatformTextureSettings(platform);


            data.platformOverrides.Add(
                new TexturePlatformData
                {
                    platform = platform,

                    overridden =
                        settings.overridden,

                    maxTextureSize =
                        settings.maxTextureSize,

                    format =
                        settings.format.ToString(),

                    compressionQuality =
                        settings.compressionQuality,

                    crunchedCompression =
                        settings.crunchedCompression,

                    allowsAlphaSplitting =
                        settings.allowsAlphaSplitting
                }
            );
        }


        textures.Add(guid, data);
    }



    [Serializable]
    class TextureCollection
    {
        public List<TextureData> textures;
    }


    [Serializable]
    class TextureData
    {
        public string guid;
        public string path;

        public string textureType;

        public bool sRGBTexture;
        public bool mipmapEnabled;

        public string alphaSource;

        public int maxTextureSize;

        public string wrapMode;
        public string filterMode;

        public string textureCompression;

        public bool isReadable;
        public bool streamingMipmaps;

        public List<TexturePlatformData> platformOverrides;
    }


    [Serializable]
    class TexturePlatformData
    {
        public string platform;

        public bool overridden;

        public int maxTextureSize;

        public string format;

        public int compressionQuality;

        public bool crunchedCompression;

        public bool allowsAlphaSplitting;
    }
}
