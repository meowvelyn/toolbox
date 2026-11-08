using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

/// <summary>
/// Unity Editor tool to capture screenshots of MeshRenderers and SkinnedMeshRenderers
/// from AnimationClips, defining all mesh renderers at referenced paths.
///
/// Analyzes toggles in animations, clones the entire hierarchy, disables all renderers
/// by default, enables appropriate mesh renderers based on mode, and renders the group
/// to PNG with transparency.
///
/// Fits bounds with 5% border in the requested resolution and isolates the clone to
/// a temporary layer.
///
/// Optionally applies Modular Avatar post-processing (via the NDMF build pipeline)
/// to the clone before capturing, so MA tools such as Merge Armature, Material Swap,
/// Shape Changer, Mesh Cutter and Object Sync are reflected in the screenshot. This
/// only applies when the root object is a valid avatar root.
///
/// In folder mode, screenshots are sorted into subdirectories matching the relative
/// folder structure from the input.
///
/// Targets Unity 2021.3+ API. Does NOT modify original assets.
///
/// Capture Modes:
/// - Both: All MeshRenderers and SkinnedMeshRenderers at paths referenced in the clip
///   that have any animated property curve.
/// - OnlyOn: Only mesh renderers under paths that end ENABLED in the animation
///   (have an m_IsActive/m_Enabled curve ending ON).
/// - OnlyOff: Only mesh renderers under paths that end DISABLED in the animation
///   (have an m_IsActive/m_Enabled curve ending OFF).
///
/// For inclusive toggles: when a parent path is referenced/toggled, includes ALL
/// supported mesh renderers in its subtree.
/// </summary>
public class ModelScreenshotter : EditorWindow
{
    // UI Fields for user input and settings
    private InputType inputType = InputType.AnimationClip;
    private AnimationClip selectedClip;
    private GameObject rootObject;
    private string inputFolderPath = "";
    private string outputFolderPath = "";
    private MeshCaptureMode captureMode = MeshCaptureMode.Both;
    private Camera selectedCamera;
    private int resolutionWidth = 1024;
    private int resolutionHeight = 1024;
    private bool applyMaPostProcessing = true;

    // True once NDMF generated temporary assets for the batch's processed source
    // hierarchy, so they can be cleaned up after the batch is done.
    private bool maProcessingApplied = false;

    private static Type NdmfProcessorType =>
        Type.GetType("nadena.dev.ndmf.AvatarProcessor, nadena.dev.ndmf");

    // Enums for UI options
    private enum InputType
    {
        AnimationClip,
        Folder,
        GameObject
    }

    private enum MeshCaptureMode
    {
        OnlyOn,
        OnlyOff,
        Both
    }

    // Temporary layer used to isolate the cloned object from the scene.
    private const int TempLayer = 31;

    [MenuItem("Toolbox/Model Screenshotter", false, 4)]
    private static void ShowWindow()
    {
        GetWindow<ModelScreenshotter>("ModelScreenshotter");
    }

    private void OnGUI()
    {
        inputType = (InputType)EditorGUILayout.EnumPopup(
            "Input Type",
            inputType
        );

        rootObject = (GameObject)EditorGUILayout.ObjectField(
            "Root GameObject (Prefab)",
            rootObject,
            typeof(GameObject),
            true
        );

        if (inputType == InputType.AnimationClip)
        {
            selectedClip = (AnimationClip)EditorGUILayout.ObjectField(
                "Animation Clip",
                selectedClip,
                typeof(AnimationClip),
                false
            );
        }
        else if (inputType == InputType.Folder)
        {
            EditorGUILayout.BeginHorizontal();

            inputFolderPath = FolderField(
                "Input Folder",
                inputFolderPath
            );

            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                inputFolderPath = EditorUtility.OpenFolderPanel(
                    "Select Input Folder",
                    inputFolderPath,
                    ""
                );
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();

        outputFolderPath = FolderField(
            "Output Folder",
            outputFolderPath
        );

        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            outputFolderPath = EditorUtility.OpenFolderPanel(
                "Select Output Folder",
                outputFolderPath,
                ""
            );
        }

        EditorGUILayout.EndHorizontal();

        if (inputType != InputType.GameObject)
        {
            captureMode = (MeshCaptureMode)EditorGUILayout.EnumPopup(
                "Capture Meshes",
                captureMode
            );
        }

        selectedCamera = (Camera)EditorGUILayout.ObjectField(
            "Scene Camera",
            selectedCamera,
            typeof(Camera),
            true
        );

        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(
            "Resolution",
            GUILayout.Width(70)
        );

        resolutionWidth = EditorGUILayout.IntField(
            "",
            resolutionWidth,
            GUILayout.Width(50)
        );

        EditorGUILayout.LabelField(
            "x",
            GUILayout.Width(10)
        );

        resolutionHeight = EditorGUILayout.IntField(
            "",
            resolutionHeight,
            GUILayout.Width(50)
        );

        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledGroupScope(NdmfProcessorType == null))
        {
            applyMaPostProcessing = EditorGUILayout.Toggle(
                new GUIContent(
                    "Apply MA Post-Processing",
                    "Runs the Modular Avatar / NDMF build on a clone of the root object " +
                    "before capturing, so MA post-processing tools (Merge Armature, Material " +
                    "Swap, Shape Changer, Mesh Cutter, Object Sync, etc.) are applied to the " +
                    "screenshot. Only takes effect when the root object is a valid avatar " +
                    "(e.g. has a VRC avatar descriptor); plain outfits/models are captured " +
                    "as-is."
                ),
                applyMaPostProcessing
            );
        }
        if (NdmfProcessorType == null)
            EditorGUILayout.HelpBox("Install NDMF to enable avatar post-processing.", MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Process") && ValidateInputs())
        {
            Process();
        }

        if (GUILayout.Button("Test on Selected Prefab"))
        {
            TestOnSelectedPrefab();
        }
    }

    /// <summary>
    /// Draws a labeled folder path field that also accepts drag-and-dropped folders
    /// (or files, using their parent folder) from the Project window.
    /// </summary>
    private string FolderField(
        string label,
        string value
    )
    {
        Rect fieldRect =
            EditorGUILayout.GetControlRect();

        Rect labelRect =
            new Rect(
                fieldRect.x,
                fieldRect.y,
                EditorGUIUtility.labelWidth,
                fieldRect.height
            );

        Rect textRect =
            new Rect(
                fieldRect.x + EditorGUIUtility.labelWidth,
                fieldRect.y,
                fieldRect.width - EditorGUIUtility.labelWidth,
                fieldRect.height
            );

        GUI.Label(
            labelRect,
            label
        );

        string newValue =
            EditorGUI.TextField(
                textRect,
                value
            );

        return HandleFolderDragDrop(
            textRect,
            newValue
        );
    }

    /// <summary>
    /// Handles DragUpdated / DragPerform events over a folder field's rect.
    /// </summary>
    private string HandleFolderDragDrop(
        Rect dropRect,
        string currentValue
    )
    {
        Event evt =
            Event.current;

        if (
            evt.type != EventType.DragUpdated &&
            evt.type != EventType.DragPerform
        )
        {
            return currentValue;
        }

        if (!dropRect.Contains(evt.mousePosition))
            return currentValue;

        bool hasValidPath =
            DragAndDrop.paths != null &&
            DragAndDrop.paths.Length > 0 &&
            (
                Directory.Exists(DragAndDrop.paths[0]) ||
                File.Exists(DragAndDrop.paths[0])
            );

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode =
                hasValidPath
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;

            evt.Use();

            return currentValue;
        }

        // EventType.DragPerform
        if (hasValidPath)
        {
            DragAndDrop.AcceptDrag();

            string resolved =
                ResolveFolderPath(
                    DragAndDrop.paths[0]
                );

            evt.Use();

            return resolved;
        }

        return currentValue;
    }

    /// <summary>
    /// Converts a dragged asset path into an absolute OS folder path, matching the
    /// paths returned by OpenFolderPanel. Project-relative paths (e.g. Assets/Foo)
    /// are expanded relative to the project root; files resolve to their parent
    /// folder.
    /// </summary>
    private string ResolveFolderPath(
        string path
    )
    {
        string directory =
            Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory))
            directory = path;

        if (
            !Path.IsPathRooted(directory) &&
            (
                directory.StartsWith("Assets") ||
                directory.StartsWith("Packages")
            )
        )
        {
            string projectRoot =
                Directory.GetParent(
                    Application.dataPath
                ).FullName;

            directory =
                Path.Combine(
                    projectRoot,
                    directory
                );
        }

        return directory;
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrEmpty(outputFolderPath))
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select an output folder.",
                "OK"
            );

            return false;
        }

        if (!rootObject)
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select a Root GameObject (Prefab).",
                "OK"
            );

            return false;
        }

        if (inputType == InputType.AnimationClip && !selectedClip)
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select an AnimationClip.",
                "OK"
            );

            return false;
        }

        if (
            inputType == InputType.Folder &&
            string.IsNullOrEmpty(inputFolderPath)
        )
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select an input folder.",
                "OK"
            );

            return false;
        }

        if (!selectedCamera)
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select a scene camera.",
                "OK"
            );

            return false;
        }

        if (resolutionWidth <= 0 || resolutionHeight <= 0)
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Resolution must be greater than zero.",
                "OK"
            );

            return false;
        }

        Directory.CreateDirectory(outputFolderPath);

        return true;
    }

    private void Process()
    {
        try
        {
            List<ClipTarget> targets =
                CollectClipTargets();

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Info",
                    "No supported mesh renderers found.",
                    "OK"
                );

                return;
            }

            // The Modular Avatar / NDMF build is applied once to a single source
            // hierarchy, then reused for every clip, rather than re-running the build
            // once per animation.
            GameObject sourceRoot =
                BuildProcessedSource();

            try
            {
                foreach (var target in targets)
                {
                    CaptureClipScreenshot(
                        target,
                        sourceRoot
                    );
                }

                AssetDatabase.Refresh();
            }
            finally
            {
                CleanupProcessedSource(
                    sourceRoot
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogError(
                "Error during processing: " + e
            );
        }
    }

    /// <summary>
    /// Builds a cloned source hierarchy, applying Modular Avatar post-processing to it
    /// once. Every screenshot is captured from an Instantiate() of this hierarchy, so
    /// the MA / NDMF build only runs a single time instead of once per animation.
    /// </summary>
    private GameObject BuildProcessedSource()
    {
        GameObject source =
            GameObject.Instantiate(
                rootObject,
                Vector3.zero,
                Quaternion.identity
            );

        source.name =
            rootObject.name;

        ApplyMaPostProcessing(
            source
        );

        return source;
    }

    /// <summary>
    /// Destroys the processed source hierarchy and cleans up any NDMF temporary assets
    /// generated while building it.
    /// </summary>
    private void CleanupProcessedSource(
        GameObject sourceRoot
    )
    {
        if (sourceRoot != null)
            DestroyImmediate(sourceRoot);

        if (maProcessingApplied)
        {
            try
            {
                NdmfProcessorType?.GetMethod("CleanTemporaryAssets", Type.EmptyTypes)
                    .Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "ModelScreenshotter: failed to clean up NDMF temporary " +
                    "assets: " +
                    e.Message
                );
            }

            maProcessingApplied = false;
        }
    }

    /// <summary>
    /// Collects all supported mesh renderer paths for the selected input.
    /// </summary>
    private List<ClipTarget> CollectClipTargets()
    {
        Dictionary<AnimationClip, HashSet<string>> clipPaths =
            new Dictionary<AnimationClip, HashSet<string>>();

        Dictionary<AnimationClip, string> clipSubDirs =
            new Dictionary<AnimationClip, string>();

        if (inputType == InputType.AnimationClip)
        {
            clipPaths[selectedClip] = new HashSet<string>();
            clipSubDirs[selectedClip] = "";

            var referencedPaths =
                AnimationUtility
                    .GetCurveBindings(selectedClip)
                    .Select(b => b.path)
                    .Distinct();

            var analyzed =
                AnalyzeAnimationClip(selectedClip);

            foreach (var path in referencedPaths)
            {
                if (analyzed.endStates.ContainsKey(path))
                {
                    bool endOn =
                        analyzed.endStates[path];

                    bool shouldIncludePath = false;

                    if (captureMode == MeshCaptureMode.Both)
                    {
                        shouldIncludePath = true;
                    }
                    else if (
                        captureMode == MeshCaptureMode.OnlyOn &&
                        endOn
                    )
                    {
                        shouldIncludePath = true;
                    }
                    else if (
                        captureMode == MeshCaptureMode.OnlyOff &&
                        !endOn
                    )
                    {
                        shouldIncludePath = true;
                    }

                    if (shouldIncludePath)
                    {
                        var rendererPaths =
                            FindMeshRendererPathsInClipSubtree(
                                selectedClip,
                                path,
                                rootObject
                            );

                        foreach (var rendererPath in rendererPaths)
                        {
                            clipPaths[selectedClip].Add(
                                rendererPath
                            );
                        }
                    }
                }
                else if (captureMode == MeshCaptureMode.Both)
                {
                    var rendererPaths =
                        FindMeshRendererPathsInClipSubtree(
                            selectedClip,
                            path,
                            rootObject
                        );

                    foreach (var rendererPath in rendererPaths)
                    {
                        clipPaths[selectedClip].Add(
                            rendererPath
                        );
                    }
                }
            }
        }
        else if (inputType == InputType.Folder)
        {
            string relativeInputPath =
                inputFolderPath
                    .Replace(Application.dataPath, "")
                    .TrimStart('/', '\\');

            if (!relativeInputPath.StartsWith("Assets/"))
            {
                relativeInputPath =
                    "Assets/" + relativeInputPath;
            }

            if (!AssetDatabase.IsValidFolder(relativeInputPath))
            {
                return new List<ClipTarget>();
            }

            var assets =
                AssetDatabase.FindAssets(
                    "t:AnimationClip",
                    new[] { relativeInputPath }
                );

            foreach (var guid in assets)
            {
                var path =
                    AssetDatabase.GUIDToAssetPath(guid);

                var clip =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        path
                    );

                if (clip == null)
                    continue;

                clipPaths[clip] =
                    new HashSet<string>();

                string relativeClipPath =
                    path.Substring(relativeInputPath.TrimEnd('/').Length).TrimStart('/');

                string subDir =
                    Path.GetDirectoryName(relativeClipPath)?
                        .Replace("\\", "/")
                    ?? "";

                clipSubDirs[clip] = subDir;

                var referencedPaths =
                    AnimationUtility
                        .GetCurveBindings(clip)
                        .Select(b => b.path)
                        .Distinct();

                var analyzed =
                    AnalyzeAnimationClip(clip);

                foreach (var p in referencedPaths)
                {
                    if (analyzed.endStates.ContainsKey(p))
                    {
                        bool endOn =
                            analyzed.endStates[p];

                        bool shouldIncludePath = false;

                        if (captureMode == MeshCaptureMode.Both)
                        {
                            shouldIncludePath = true;
                        }
                        else if (
                            captureMode == MeshCaptureMode.OnlyOn &&
                            endOn
                        )
                        {
                            shouldIncludePath = true;
                        }
                        else if (
                            captureMode == MeshCaptureMode.OnlyOff &&
                            !endOn
                        )
                        {
                            shouldIncludePath = true;
                        }

                        if (shouldIncludePath)
                        {
                            var rendererPaths =
                                FindMeshRendererPathsInClipSubtree(
                                    clip,
                                    p,
                                    rootObject
                                );

                            foreach (var rendererPath in rendererPaths)
                            {
                                clipPaths[clip].Add(
                                    rendererPath
                                );
                            }
                        }
                    }
                    else if (
                        captureMode == MeshCaptureMode.Both
                    )
                    {
                        var rendererPaths =
                            FindMeshRendererPathsInClipSubtree(
                                clip,
                                p,
                                rootObject
                            );

                        foreach (var rendererPath in rendererPaths)
                        {
                            clipPaths[clip].Add(
                                rendererPath
                            );
                        }
                    }
                }
            }
        }
        else if (inputType == InputType.GameObject)
        {
            var rendererPaths =
                rootObject
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(IsSupportedMeshRenderer)
                    .Where(r =>
                        IsEffectivelyActive(r.transform) &&
                        r.enabled
                    )
                    .Select(r =>
                        GetGameObjectPathRelative(
                            r.transform,
                            rootObject.transform
                        )
                    )
                    .ToList();

            if (rendererPaths.Count > 0)
            {
                return new List<ClipTarget>
                {
                    new ClipTarget(
                        null,
                        rendererPaths,
                        "",
                        rootObject.name
                    )
                };
            }

            return new List<ClipTarget>();
        }

        return clipPaths
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp =>
                new ClipTarget(
                    kvp.Key,
                    kvp.Value.ToList(),
                    clipSubDirs.TryGetValue(
                        kvp.Key,
                        out string subDir
                    )
                        ? subDir
                        : ""
                )
            )
            .ToList();
    }

    /// <summary>
    /// Determines whether a Renderer is a supported mesh renderer.
    /// </summary>
    private static bool IsSupportedMeshRenderer(Renderer renderer)
    {
        return renderer is MeshRenderer ||
               renderer is SkinnedMeshRenderer;
    }

    /// <summary>
    /// Analyzes an AnimationClip for paths with active/enabled curves,
    /// determining their end state at clip.length.
    /// </summary>
    private (
        Dictionary<string, bool> endStates,
        Dictionary<string, bool> toggles
    ) AnalyzeAnimationClip(AnimationClip clip)
    {
        Dictionary<string, bool> endStates =
            new Dictionary<string, bool>();

        Dictionary<string, bool> toggles =
            new Dictionary<string, bool>();

        var bindings =
            AnimationUtility.GetCurveBindings(clip);

        var groupedBindings =
            bindings.GroupBy(b => b.path);

        foreach (var group in groupedBindings)
        {
            string path = group.Key;

            var activeBinding =
                group.FirstOrDefault(
                    b =>
                        b.propertyName.Contains("m_IsActive") ||
                        b.propertyName.Contains("m_Enabled")
                );

            if (activeBinding.propertyName != null)
            {
                AnimationCurve curve =
                    AnimationUtility.GetEditorCurve(
                        clip,
                        activeBinding
                    );

                if (curve.keys.Length > 0)
                {
                    float startTime =
                        curve.keys.First().time;

                    bool startState =
                        curve.Evaluate(startTime) > 0.5f;

                    bool endState =
                        curve.Evaluate(clip.length) > 0.5f;

                    endStates[path] = endState;

                    bool isTurnedOn =
                        !startState && endState;

                    bool isTurnedOff =
                        startState && !endState;

                    if (isTurnedOn)
                        toggles[path] = true;

                    if (isTurnedOff)
                        toggles[path] = false;
                }
            }
        }

        return (endStates, toggles);
    }

    /// <summary>
    /// Finds all MeshRenderers and SkinnedMeshRenderers in the subtree
    /// represented by an animation path.
    /// </summary>
    private List<string> FindMeshRendererPathsInClipSubtree(
        AnimationClip clip,
        string path,
        GameObject root
    )
    {
        List<string> rendererPaths =
            new List<string>();

        Transform t =
            TraverseToPath(
                root.transform,
                path
            );

        if (t == null)
            return rendererPaths;

        var renderers =
            t.GetComponentsInChildren<Renderer>(true);

        foreach (var renderer in renderers)
        {
            if (!IsSupportedMeshRenderer(renderer))
                continue;

            rendererPaths.Add(
                GetGameObjectPathRelative(
                    renderer.transform,
                    root.transform
                )
            );
        }

        return rendererPaths;
    }

    /// <summary>
    /// Traverses a Transform hierarchy using a slash-separated path.
    /// </summary>
    private Transform TraverseToPath(
        Transform root,
        string path
    )
    {
        var parts =
            path.Split('/');

        Transform current =
            root;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            Transform child =
                current.Find(part);

            if (child == null)
                return null;

            current = child;
        }

        return current;
    }

    /// <summary>
    /// Checks whether a Transform and all its parents are active.
    /// </summary>
    private bool IsEffectivelyActive(Transform t)
    {
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                return false;

            t = t.parent;
        }

        return true;
    }

    /// <summary>
    /// Captures a screenshot for a ClipTarget.
    /// </summary>
    private void CaptureClipScreenshot(
        ClipTarget target,
        GameObject baseRoot
    )
    {
        GameObject tempRoot =
            GameObject.Instantiate(
                baseRoot,
                Vector3.zero,
                Quaternion.identity
            );

        tempRoot.name =
            baseRoot.name;

        try
        {
            EnableGameObjectsRecursively(
                tempRoot
            );

            SetLayerRecursively(
                tempRoot,
                TempLayer
            );

            // Disable every renderer first.
            var allRenderers =
                tempRoot
                    .GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in allRenderers)
            {
                renderer.enabled = false;
            }

            // Reset Animator to its default values.
            Animator animator =
                tempRoot.GetComponentInChildren<Animator>(true);

            if (animator)
            {
                animator.WriteDefaultValues();
            }

            // Enable only the selected mesh renderers.
            var supportedRenderers =
                tempRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(IsSupportedMeshRenderer)
                    .ToArray();

            int enabledCount = 0;

            foreach (var renderer in supportedRenderers)
            {
                if (renderer is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                }

                string path =
                    GetGameObjectPathRelative(
                        renderer.transform,
                        tempRoot.transform
                    );

                bool shouldEnable =
                    target.rendererPaths.Contains(path);

                renderer.enabled =
                    shouldEnable;

                if (shouldEnable)
                    enabledCount++;
            }

            if (enabledCount == 0)
                return;

            Bounds meshBounds =
                CalculateWorldBounds(
                    tempRoot
                );

            Camera tempCam =
                CreateTemporaryCamera(
                    selectedCamera,
                    meshBounds,
                    resolutionWidth,
                    resolutionHeight
                );

            RenderTexture rt =
                new RenderTexture(
                    resolutionWidth,
                    resolutionHeight,
                    24,
                    RenderTextureFormat.ARGB32
                );

            tempCam.targetTexture =
                rt;

            var previousRenderTexture = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;

                tempCam.Render();

                tex =
                    new Texture2D(
                        rt.width,
                        rt.height,
                        TextureFormat.ARGB32,
                        false
                    );

                tex.ReadPixels(
                    new Rect(
                        0,
                        0,
                        rt.width,
                        rt.height
                    ),
                    0,
                    0
                );

                tex.Apply();

                string outputDir =
                    string.IsNullOrEmpty(target.subDir)
                        ? outputFolderPath
                        : Path.Combine(
                            outputFolderPath,
                            target.subDir
                        );

                Directory.CreateDirectory(
                    outputDir
                );

                string baseName =
                    target.clip != null
                        ? target.clip.name
                        : rootObject.name;

                var invalidFileNameChars = Path.GetInvalidFileNameChars();
                string safeName = new string(baseName.Select(character =>
                    invalidFileNameChars.Contains(character) ? '_' : character).ToArray()).Trim().Trim('.');
                string fileName = (string.IsNullOrEmpty(safeName) ? "Screenshot" : safeName) + ".png";

                string fullPath =
                    Path.Combine(
                        outputDir,
                        fileName
                    );

                File.WriteAllBytes(
                    fullPath,
                    tex.EncodeToPNG()
                );

            }
            finally
            {
                RenderTexture.active = previousRenderTexture;
                if (tex != null)
                    DestroyImmediate(tex);

                if (rt != null)
                {
                    if (tempCam != null)
                        tempCam.targetTexture = null;

                    rt.Release();
                    DestroyImmediate(rt);
                }

                if (tempCam != null)
                    DestroyImmediate(
                        tempCam.gameObject
                    );
            }
        }
        finally
        {
            if (tempRoot != null)
                DestroyImmediate(tempRoot);
        }
    }

    private void EnableGameObjectsRecursively(GameObject go)
    {
        go.SetActive(true);

        foreach (Transform child in go.transform)
        {
            EnableGameObjectsRecursively(
                child.gameObject
            );
        }
    }

    private void SetLayerRecursively(
        GameObject go,
        int layer
    )
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(
                child.gameObject,
                layer
            );
        }
    }

    /// <summary>
    /// Applies Modular Avatar / NDMF post-processing to the given avatar clone before
    /// it is rendered, so MA post-processing tools are reflected in the screenshot.
    ///
    /// No-ops if post-processing is disabled or the object is not a valid avatar root.
    /// Failures are logged but do not abort the capture; the unprocessed clone is still
    /// rendered as a fallback.
    /// </summary>
    private void ApplyMaPostProcessing(
        GameObject avatarRoot
    )
    {
        if (!applyMaPostProcessing)
            return;

        if (avatarRoot == null)
            return;

        var processorType = NdmfProcessorType;
        if (processorType == null)
            return;

        try
        {
            if (!(bool)processorType.GetMethod("CanProcessObject", new[] { typeof(GameObject) })
                    .Invoke(null, new object[] { avatarRoot }))
            {
                // Not a valid avatar root (e.g. a plain outfit / model), so there is
                // no Modular Avatar build to apply. Render the clone as-is.
                return;
            }

            object platform = null;
            var assembly = processorType.Assembly;
            var platformType = assembly.GetType("nadena.dev.ndmf.platform.INDMFPlatformProvider", true);

            try
            {
                platform =
                    assembly.GetType("nadena.dev.ndmf.platform.PlatformRegistry", true)
                        .GetMethod("GetPrimaryPlatformForAvatar", new[] { typeof(GameObject) })
                        .Invoke(null, new object[] { avatarRoot });
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "ModelScreenshotter: failed to resolve MA build platform; " +
                    "falling back to ambient platform: " +
                    e.Message
                );
            }

            if (platform == null)
                platform = assembly.GetType("nadena.dev.ndmf.platform.AmbientPlatform", true)
                    .GetProperty("CurrentPlatform").GetValue(null);

            maProcessingApplied = true;
            processorType.GetMethod("ProcessAvatar", new[] { typeof(GameObject), platformType })
                .Invoke(null, new[] { avatarRoot, platform });
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "ModelScreenshotter: Modular Avatar post-processing failed; " +
                "rendering unprocessed clone. " +
                e
            );
        }
    }

    /// <summary>
    /// Calculates combined world bounds of all enabled supported mesh renderers.
    /// </summary>
    private Bounds CalculateWorldBounds(
        GameObject obj
    )
    {
        Renderer[] renderers =
            obj
                .GetComponentsInChildren<Renderer>(true)
                .Where(r =>
                    r.enabled &&
                    IsSupportedMeshRenderer(r)
                )
                .ToArray();

        if (renderers.Length == 0)
        {
            return new Bounds(
                Vector3.zero,
                Vector3.one
            );
        }

        Bounds combinedBounds =
            renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(
                renderers[i].bounds
            );
        }

        return combinedBounds;
    }

    /// <summary>
    /// Creates a temporary orthographic camera fitted to the mesh bounds.
    /// </summary>
    private Camera CreateTemporaryCamera(
        Camera source,
        Bounds meshBounds,
        int width,
        int height
    )
    {
        GameObject camObj =
            new GameObject("TempCam");

        Camera tempCam =
            camObj.AddComponent<Camera>();

        tempCam.CopyFrom(source);

        tempCam.orthographic = true;
        tempCam.clearFlags =
            CameraClearFlags.SolidColor;

        tempCam.backgroundColor =
            new Color(0, 0, 0, 0);

        tempCam.nearClipPlane =
            -1000f;

        tempCam.farClipPlane =
            1000f;

        tempCam.cullingMask =
            1 << TempLayer;

        tempCam.transform.rotation =
            Quaternion.LookRotation(
                source.transform.forward,
                source.transform.up
            );

        Vector3 expandedExtents =
            meshBounds.extents * 1.1f;

        Vector3 center =
            meshBounds.center;

        float aspectRatio =
            (float)width / height;

        float orthoHeight =
            Mathf.Max(
                expandedExtents.y,
                expandedExtents.x / aspectRatio
            );

        tempCam.orthographicSize =
            orthoHeight;

        float backDistance =
            expandedExtents.magnitude * 2f;

        tempCam.transform.position =
            center -
            tempCam.transform.forward *
            backDistance;

        return tempCam;
    }

    /// <summary>
    /// Builds a slash-separated path relative to a root Transform.
    /// </summary>
    private string GetGameObjectPathRelative(
        Transform t,
        Transform root
    )
    {
        if (t == root)
            return "";

        string path =
            t.name;

        while (
            t.parent != null &&
            t.parent != root
        )
        {
            t = t.parent;

            path =
                t.name +
                "/" +
                path;
        }

        return path;
    }

    [System.Serializable]
    private class ClipTarget
    {
        public AnimationClip clip;

        public List<string> rendererPaths;

        public string subDir;

        public string displayName;

        public string name =>
            displayName ??
            clip?.name ??
            "UnknownClip";

        public ClipTarget(
            AnimationClip clip,
            List<string> rendererPaths,
            string subDir = "",
            string displayName = null
        )
        {
            this.clip = clip;
            this.rendererPaths =
                rendererPaths;
            this.subDir =
                subDir;
            this.displayName =
                displayName;
        }

        public override bool Equals(
            object obj
        )
        {
            return obj is ClipTarget other &&
                   clip == other.clip;
        }

        public override int GetHashCode()
        {
            return clip?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// Captures all supported mesh renderers from the selected prefab.
    /// </summary>
    private void TestOnSelectedPrefab()
    {
        var selected =
            Selection.activeGameObject;

        if (
            selected == null ||
            !PrefabUtility.IsPartOfRegularPrefab(
                selected
            )
        )
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Select a prefab in the scene.",
                "OK"
            );

            return;
        }

        var renderers =
            selected
                .GetComponentsInChildren<Renderer>(true)
                .Where(IsSupportedMeshRenderer)
                .ToArray();

        if (renderers.Length == 0)
            return;

        var target =
            new ClipTarget(
                null,
                renderers
                    .Select(r =>
                        GetGameObjectPathRelative(
                            r.transform,
                            selected.transform
                        )
                    )
                    .ToList(),
                "",
                "Test"
            );

        // Apply MA post-processing once (reusing the normal source flow) and capture.
        GameObject sourceRoot =
            BuildProcessedSource();

        try
        {
            CaptureClipScreenshot(
                target,
                sourceRoot
            );
        }
        finally
        {
            CleanupProcessedSource(
                sourceRoot
            );
        }
    }
}
