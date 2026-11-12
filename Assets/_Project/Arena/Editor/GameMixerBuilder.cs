using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CarArena.EditorTools
{
    /// <summary>
    /// Creates the game's Audio Mixer (Resources/GameMixer.mixer): Master > Car, Effects, Crowd, each with its
    /// volume exposed as MasterVol / CarVol / EffectsVol / CrowdVol for the settings sliders.
    /// Unity has no public API for building mixers, so this goes through the editor's AudioMixerController by
    /// reflection. An existing mixer is left alone (hand tweaks survive rebuilds).
    /// </summary>
    public static class GameMixerBuilder
    {
        public const string Path = "Assets/_Project/Arena/Resources/GameMixer.mixer";

        [MenuItem("Tools/Car Arena/Build Audio Mixer")]
        public static AudioMixer Build()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
            if (existing != null) return existing;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var asm = typeof(Editor).Assembly;
            var controllerType = asm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            var exposedType = asm.GetType("UnityEditor.Audio.ExposedAudioParameter");

            var mixer = controllerType.GetMethod("CreateMixerControllerAtPath", all).Invoke(null, new object[] { Path });
            var master = controllerType.GetProperty("masterGroup", all).GetValue(mixer);
            var createGroup = controllerType.GetMethod("CreateNewGroup", all);
            var addChild = controllerType.GetMethod("AddChildToParent", all);
            var addExposed = controllerType.GetMethod("AddExposedParameter", all);
            var volumeGuid = groupType.GetMethod("GetGUIDForVolume", all);
            var exposedProp = controllerType.GetProperty("exposedParameters", all);

            void Expose(object group, string name)
            {
                var guid = (GUID)volumeGuid.Invoke(group, null);
                addExposed.Invoke(mixer, new[] { Activator.CreateInstance(pathType, group, guid) });
                // Rename the new parameter (it starts as "MyExposedParam").
                var arr = (Array)exposedProp.GetValue(mixer);
                for (int i = 0; i < arr.Length; i++)
                {
                    var p = arr.GetValue(i);
                    var guidField = exposedType.GetField("guid", all);
                    if ((GUID)guidField.GetValue(p) != guid) continue;
                    exposedType.GetField("name", all).SetValue(p, name);
                    arr.SetValue(p, i);
                }
                exposedProp.SetValue(mixer, arr);
            }

            Expose(master, "MasterVol");
            foreach (var (group, param) in new[] { ("Car", "CarVol"), ("Effects", "EffectsVol"), ("Crowd", "CrowdVol") })
            {
                var g = createGroup.Invoke(mixer, new object[] { group, false });
                addChild.Invoke(mixer, new[] { g, master });
                Expose(g, param);
            }

            EditorUtility.SetDirty((UnityEngine.Object)mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Path);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
        }
    }
}
