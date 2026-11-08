using UnityEditor;
using UnityEngine;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


public class TextureImportSettingsApplier : EditorWindow
{
    private TextAsset jsonFile;


    [MenuItem("Toolbox/Apply Texture Import Settings", false, 7)]
    private static void ApplyFromProject()
    {
        var guids = AssetDatabase.FindAssets("TextureImportSettings t:TextAsset");

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Texture Import Settings",
                "TextureImportSettings.json could not be found in the project.",
                "OK");
            return;
        }

        string path;

        if (guids.Length == 1)
        {
            path = AssetDatabase.GUIDToAssetPath(guids[0]);
        }
        else
        {
            path = EditorUtility.OpenFilePanel(
                "Select TextureImportSettings.json",
                Application.dataPath,
                "json");

            if (string.IsNullOrEmpty(path))
                return;

            if (!path.StartsWith(Application.dataPath + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid File",
                    "The selected file must be inside the Unity project.",
                    "OK");
                return;
            }

            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        if (!EditorUtility.DisplayDialog(
            "Apply Texture Import Settings",
            $"Apply texture import settings from:\n\n{path}\n\nThis will reimport affected textures.",
            "Apply",
            "Cancel"))
        {
            return;
        }

        try
        {
            Apply(File.ReadAllText(path));
        }
        catch (System.Exception exception) when (exception is IOException ||
            exception is System.UnauthorizedAccessException || exception is System.ArgumentException)
        {
            Debug.LogError($"Could not apply texture import settings: {exception.Message}");
        }
    }


    static void Apply(string json)
    {
        TextureCollection collection =
            JsonUtility.FromJson<TextureCollection>(json);

        if (collection?.textures == null)
        {
            Debug.LogError("The texture settings JSON must contain a textures array.");
            return;
        }

        int applied = 0;
        int failed = 0;


        foreach (TextureData data in collection.textures)
        {
            if (data == null)
            {
                failed++;
                continue;
            }

            string path = string.IsNullOrEmpty(data.guid) ? null : AssetDatabase.GUIDToAssetPath(data.guid);


            // Fallback if GUID does not exist
            if (string.IsNullOrEmpty(path))
            {
                path = data.path;
            }


            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"Texture not found: {data.guid} ({data.path})"
                );

                failed++;
                continue;
            }


            TextureImporter importer =
                AssetImporter.GetAtPath(path)
                as TextureImporter;


            if (importer == null)
            {
                Debug.LogWarning(
                    $"Not a texture importer: {path}"
                );

                failed++;
                continue;
            }


            ApplyTextureSettings(
                importer,
                data
            );


            importer.SaveAndReimport();

            applied++;
        }

        EditorUtility.DisplayDialog(
            "Texture Import Settings",
            $"Applied settings to {applied} textures.\nFailed: {failed}",
            "OK");
    }



    static void ApplyTextureSettings(
        TextureImporter importer,
        TextureData data)
    {
        importer.textureType =
            ParseEnum<TextureImporterType>(
                data.textureType
            );


        importer.sRGBTexture =
            data.sRGBTexture;


        importer.mipmapEnabled =
            data.mipmapEnabled;


        importer.alphaSource =
            ParseEnum<TextureImporterAlphaSource>(
                data.alphaSource
            );


        importer.maxTextureSize =
            data.maxTextureSize;


        importer.wrapMode =
            ParseEnum<TextureWrapMode>(
                data.wrapMode
            );


        importer.filterMode =
            ParseEnum<FilterMode>(
                data.filterMode
            );


        importer.textureCompression =
            ParseEnum<TextureImporterCompression>(
                data.textureCompression
            );


        importer.isReadable =
            data.isReadable;


        importer.streamingMipmaps =
            data.streamingMipmaps;



        if (data.platformOverrides == null)
            return;

        foreach (TexturePlatformData platform in data.platformOverrides)
        {
            if (platform == null || string.IsNullOrEmpty(platform.platform))
                continue;

            TextureImporterPlatformSettings settings =
                new TextureImporterPlatformSettings
                {
                    name = platform.platform,

                    overridden =
                        platform.overridden,

                    maxTextureSize =
                        platform.maxTextureSize,

                    compressionQuality =
                        platform.compressionQuality,

                    crunchedCompression =
                        platform.crunchedCompression,

                    allowsAlphaSplitting =
                        platform.allowsAlphaSplitting,

                    format =
                        ParseEnum<TextureImporterFormat>(
                            platform.format
                        )
                };


            importer.SetPlatformTextureSettings(settings);
        }
    }



    static T ParseEnum<T>(string value)
    {
        try
        {
            return (T)Enum.Parse(
                typeof(T),
                value
            );
        }
        catch
        {
            return default;
        }
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
