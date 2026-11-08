#if UNITY_EDITOR 
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

/*
    Changelog:
    Fixed script errors on avatar build
    Made AddSubmenu add a page if there are 8 items in the menu instead of 7
        - It copies item #8, then puts it in the page it just created.
    AddControl now allows for folders to go deeper
        - for example, "Boolean menus/silly sounds/cow sounds"
          will make a "Boolean menus" menu, put it in vrcMenu,
          create "silly sounds", put it in "Boolean menus" etc.
    AddControl now prevents duplicate menu items.
        - It will now check if there is an item with identical name, parameter, type, and value
*/

namespace AvatarToolbox
{
    public class ParameterHandler
    {
        internal static AnimatorController GetFxController(VRCAvatarDescriptor avatar)
        {
            return avatar?.baseAnimationLayers?.FirstOrDefault(
                layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        }

        internal static bool ValidateParameters(AnimatorController controller, VRCExpressionParameters expressions,
            IEnumerable<(string name, bool isInt)> requested)
        {
            var desired = new Dictionary<string, bool>(StringComparer.Ordinal);
            int additionalCost = 0;
            foreach (var (name, isInt) in requested)
            {
                if (desired.TryGetValue(name, out bool previousType))
                {
                    if (previousType == isInt)
                        continue;
                    Debug.LogError($"The parameter '{name}' is needed as both an Int and a Bool.");
                    return false;
                }
                desired.Add(name, isInt);

                var animatorType = isInt ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool;
                var expressionType = isInt ? VRCExpressionParameters.ValueType.Int : VRCExpressionParameters.ValueType.Bool;
                var expression = expressions?.parameters?.FirstOrDefault(parameter => parameter?.name == name);
                if (controller.parameters.Any(parameter => parameter.name == name && parameter.type != animatorType) ||
                    (expression != null && expression.valueType != expressionType))
                {
                    Debug.LogError($"The parameter '{name}' already exists with an incompatible type.");
                    return false;
                }
                if (expression == null)
                    additionalCost += isInt ? 8 : 1;
            }

            if (expressions != null && (expressions.parameters != null ? expressions.CalcTotalCost() : 0) +
                additionalCost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                Debug.LogError("The new parameters would exceed the VRChat expression parameter budget.");
                return false;
            }
            return true;
        }

        public static void AddExpressionParameter(VRCExpressionParameters vrcParameters, string parameterName, string type)
        {
            if (vrcParameters == null)
            {
                Debug.LogError("Skipping adding parameter because vrcParameters is null");
                return;
            }
            VRCExpressionParameters.ValueType parameterType = VRCExpressionParameters.ValueType.Float;

            switch (type.ToLower())
            {
                case "int":
                    parameterType = VRCExpressionParameters.ValueType.Int;
                    break;
                case "bool":
                    parameterType = VRCExpressionParameters.ValueType.Bool;
                    break;
                case "float":
                    parameterType = VRCExpressionParameters.ValueType.Float;
                    break;
                default:
                    Debug.LogWarning("Invalid parameter type. Defaulting to Float");
                    break;
            }

            var parameters = vrcParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();
            VRCExpressionParameters.Parameter existingParameter = parameters.FirstOrDefault(parameter => parameter?.name == parameterName);

            if (existingParameter != null)
            {
                // Debug.Log("Parameter exists.");
                return;
            }
            else
            {
                // Debug.Log("Parameter does not exist, creating new Parameter.");
                VRCExpressionParameters.Parameter[] updatedParameters = parameters.Append(new VRCExpressionParameters.Parameter()
                { // updatedParameters is created here because Unity is weird and won't properly append
                    name = parameterName,
                    valueType = parameterType,
                    defaultValue = 0.0F,
                    saved = true,
                    networkSynced = true
                }).ToArray();

                vrcParameters.parameters = updatedParameters;

                EditorUtility.SetDirty(vrcParameters);
            }
        }
        public static void AddControllerParameter(AnimatorController controller, string parameterName, string type)
        {
            if (controller == null)
            {
                Debug.LogError("Controller is null.");
                return;
            }

            AnimatorControllerParameterType parameterType = AnimatorControllerParameterType.Float;

            switch (type.ToLower())
            {
                case "int":
                    parameterType = AnimatorControllerParameterType.Int;
                    break;
                case "bool":
                    parameterType = AnimatorControllerParameterType.Bool;
                    break;
                case "float":
                    parameterType = AnimatorControllerParameterType.Float;
                    break;
                default:
                    Debug.LogWarning("Invalid parameter type. Defaulting to Float");
                    break;
            }

            AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(p => p.name == parameterName && p.type == parameterType);
            if (parameter == null)
                controller.AddParameter(parameterName, parameterType);
        }

    }

    public class MenuHandler
    {
        public static VRCExpressionsMenu AddSubmenu(VRCExpressionsMenu vrcMenu, string menuName, string subfolderName)
        {
            var existingControl = vrcMenu.controls.FirstOrDefault(control =>
                control != null && control.name == menuName && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
            if (existingControl?.subMenu != null)
                return existingControl.subMenu;

            string parentPath = AssetDatabase.GetAssetPath(vrcMenu);
            if (!parentPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The expression menu must be an asset under Assets.");

            string folder = AssetFileName(subfolderName ?? "Submenu");
            string path = Path.GetDirectoryName(parentPath).Replace('\\', '/');
            if (Path.GetFileName(path) != folder)
            {
                if (!AssetDatabase.IsValidFolder($"{path}/{folder}") &&
                    string.IsNullOrEmpty(AssetDatabase.CreateFolder(path, folder)))
                    throw new IOException($"Could not create expression menu folder: {path}/{folder}");
                path += "/" + folder;
            }

            var submenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(submenu, AssetDatabase.GenerateUniqueAssetPath($"{path}/{AssetFileName(menuName)}.asset"));
            if (!AssetDatabase.Contains(submenu))
            {
                UnityEngine.Object.DestroyImmediate(submenu);
                throw new IOException($"Could not create expression submenu: {menuName}");
            }
            Undo.RegisterCreatedObjectUndo(submenu, "Create Expression Submenu");

            Undo.RecordObject(vrcMenu, "Add Submenu");
            if (existingControl == null)
            {
                if (vrcMenu.controls.Count >= 8)
                {
                    submenu.controls.AddRange(vrcMenu.controls.Skip(7));
                    vrcMenu.controls.RemoveRange(7, vrcMenu.controls.Count - 7);
                }
                AddSubmenuControl(vrcMenu, submenu, menuName);
            }
            else
            {
                existingControl.subMenu = submenu;
            }
            EditorUtility.SetDirty(submenu);
            EditorUtility.SetDirty(vrcMenu);
            return submenu;
        }

        private static string AssetFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((name ?? "").Select(character =>
                invalid.Contains(character) ? '_' : character).ToArray()).Trim().Trim('.');
            return string.IsNullOrEmpty(cleaned) ? "Menu" : cleaned;
        }

        public static void AddControl(VRCExpressionsMenu vrcMenu, string submenuName, string controlName, string parameterName, float val)
        {
            var menu = ResolveSubmenu(vrcMenu, submenuName);
            foreach (var page in EnumeratePages(menu))
            {
                if (page.controls.Any(control => IsToggleFor(control, parameterName) &&
                    control.name == controlName && control.value == val))
                    return;
            }
            AppendToggle(menu, controlName, parameterName, val);
        }

        internal static void SyncIntControls(VRCExpressionsMenu root, string submenuName,
            string parameterName, Dictionary<string, int> desired)
        {
            var menu = ResolveSubmenu(root, submenuName);
            var retained = new HashSet<string>();
            foreach (var page in EnumeratePages(menu))
            {
                Undo.RecordObject(page, "Update Int Layer Controls");
                for (int i = 0; i < page.controls.Count;)
                {
                    var control = page.controls[i];
                    if (IsToggleFor(control, parameterName))
                    {
                        if (control.name == null || !desired.TryGetValue(control.name, out int value) ||
                            !retained.Add(control.name))
                        {
                            page.controls.RemoveAt(i);
                            EditorUtility.SetDirty(page);
                            continue;
                        }
                        if (control.value != value)
                        {
                            control.value = value;
                            EditorUtility.SetDirty(page);
                        }
                    }
                    i++;
                }
            }
            foreach (var entry in desired)
            {
                if (!retained.Contains(entry.Key))
                    AppendToggle(menu, entry.Key, parameterName, entry.Value);
            }
        }

        private static VRCExpressionsMenu ResolveSubmenu(VRCExpressionsMenu root, string path)
        {
            var menu = root;
            string parentName = null;
            foreach (var name in (path ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pages = EnumeratePages(menu).ToArray();
                var existing = pages.SelectMany(page => page.controls).FirstOrDefault(control =>
                    control != null && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    control.name == name && control.subMenu != null);
                var lastPage = pages.Last();
                if (existing?.subMenu == null && lastPage.controls.Count >= 8)
                    lastPage = AddPage(menu);
                menu = existing?.subMenu ?? AddSubmenu(lastPage, name, parentName);
                parentName = name;
            }
            return menu;
        }

        private static bool IsToggleFor(VRCExpressionsMenu.Control control, string parameterName)
        {
            return control != null && control.type == VRCExpressionsMenu.Control.ControlType.Toggle &&
                control.parameter?.name == parameterName;
        }

        private static void AppendToggle(VRCExpressionsMenu menu, string name, string parameter, float value)
        {
            var page = FindLastPage(menu);
            if (page.controls.Count >= 8)
                page = AddPage(menu);

            Undo.RecordObject(page, "Add Control");
            page.controls.Add(new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value
            });
            EditorUtility.SetDirty(page);
        }
        public static void AddSubmenuControl(VRCExpressionsMenu vrcMenu, VRCExpressionsMenu subMenu, string menuName)
        {
            Undo.RecordObject(vrcMenu, "Add Submenu Control");
            VRCExpressionsMenu.Control newControl = new VRCExpressionsMenu.Control()
            {
                name = menuName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = subMenu
            };
            vrcMenu.controls.Add(newControl);
            EditorUtility.SetDirty(vrcMenu);
            EditorUtility.SetDirty(subMenu);
        }

        internal static void SetSubmenuControl(VRCExpressionsMenu root, VRCExpressionsMenu submenu, string name)
        {
            foreach (var page in EnumeratePages(root))
            {
                var control = page.controls.FirstOrDefault(item => item != null &&
                    item.type == VRCExpressionsMenu.Control.ControlType.SubMenu && item.name == name);
                if (control == null)
                    continue;

                Undo.RecordObject(page, "Update Expression Submenu");
                control.subMenu = submenu;
                EditorUtility.SetDirty(page);
                return;
            }

            var lastPage = FindLastPage(root);
            if (lastPage.controls.Count >= 8)
                lastPage = AddPage(root);
            AddSubmenuControl(lastPage, submenu, name);
        }

        public static VRCExpressionsMenu AddPage(VRCExpressionsMenu vrcMenu)
        {
            var pages = EnumeratePages(vrcMenu).ToArray();
            var lastPage = pages.Last();
            int number = pages.Length + 1;
            while (lastPage.controls.Any(control => control != null && control.name == $"Page {number}"))
                number++;
            return AddSubmenu(lastPage, $"Page {number}", vrcMenu.name);
        }

        public static VRCExpressionsMenu FindLastPage(VRCExpressionsMenu vrcMenu)
        {
            return EnumeratePages(vrcMenu).Last();
        }

        private static IEnumerable<VRCExpressionsMenu> EnumeratePages(VRCExpressionsMenu menu)
        {
            var visited = new HashSet<VRCExpressionsMenu>();
            while (menu != null && visited.Add(menu))
            {
                yield return menu;
                menu = menu.controls.LastOrDefault(control => control != null && control.subMenu != null &&
                    !visited.Contains(control.subMenu) && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    Regex.IsMatch(control.name ?? "", @"^Page \d+$"))?.subMenu;
            }
        }
    }
}
#endif
