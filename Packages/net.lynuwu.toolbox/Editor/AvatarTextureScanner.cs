using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

public static class AvatarTextureScanner
{
    public static HashSet<Texture> Scan(GameObject root)
    {
        var textures = new HashSet<Texture>();
        var controllers = new HashSet<RuntimeAnimatorController>();
        var clips = new HashSet<AnimationClip>();
        var materials = new HashSet<Material>();
        if (root == null)
            return textures;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            materials.UnionWith(renderer.sharedMaterials);

        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            controllers.Add(animator.runtimeAnimatorController);

        foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
        {
            if (!descriptor.customizeAnimationLayers)
                continue;

            AddPlayableLayers(descriptor.baseAnimationLayers, controllers);
            AddPlayableLayers(descriptor.specialAnimationLayers, controllers);
        }

        foreach (var controller in controllers)
        {
            if (controller != null)
                clips.UnionWith(controller.animationClips);
        }

        foreach (var clip in clips)
        {
            if (clip == null)
                continue;

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
            {
                if (key.value is Texture texture)
                    textures.Add(texture);
                else if (key.value is Material material)
                    materials.Add(material);
            }
        }

        foreach (var material in materials)
        {
            if (material == null)
                continue;

            foreach (var property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture != null)
                    textures.Add(texture);
            }
        }

        return textures;
    }

    private static void AddPlayableLayers(
        VRCAvatarDescriptor.CustomAnimLayer[] layers, HashSet<RuntimeAnimatorController> controllers)
    {
        if (layers == null)
            return;

        foreach (var layer in layers)
        {
            if (!layer.isDefault)
                controllers.Add(layer.animatorController);
        }
    }
}
