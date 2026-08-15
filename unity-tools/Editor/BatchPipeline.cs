// BatchPipeline.cs — Editor-only.
//
// Ejecuta el pipeline entero (rig -> horneado -> bundle) de una sentada, para poder
// lanzarlo sin abrir el editor:
//
//   Unity.exe -batchmode -quit -projectPath C:\Users\fcast\Peak_MOD ^
//             -executeMethod PeakEmotes.EditorTools.BatchPipeline.Run -logFile -
//
// Requiere que el proyecto NO esté abierto en el editor (Unity bloquea el proyecto).

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PeakEmotes.EditorTools
{
    public static class BatchPipeline
    {
        /// Carpetas de donde sacamos los clips a hornear.
        static readonly string[] SourceFolders =
        {
            // Solo los masculinos: sobre el rig del Scout las versiones femeninas
            // quedaban prácticamente idénticas y duplicaban la rueda para nada.
            "Assets/Kevin Iglesias/Human Animations/Animations/Male/Social/Dance/Steps",
            // El idle lo usan los emotes de SONIDO: el Scout se queda de pie de forma
            // natural mientras suena el audio, en vez de congelarse en la pose de reposo.
            "Assets/Kevin Iglesias/Human Animations/Animations/Male/Idles",
        };

        [MenuItem("PEAK Emotes/Ejecutar pipeline completo", priority = 0)]
        public static void Run()
        {
            var ok = false;
            try
            {
                ok = Execute();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BatchPipeline] excepción: {e}");
            }

            Debug.Log(ok ? "[BatchPipeline] === PIPELINE OK ===" : "[BatchPipeline] === PIPELINE FALLÓ ===");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Execute()
        {
            Debug.Log("[BatchPipeline] --- paso 1: construir rig del Scout ---");
            ScoutRigBuilder.Build();

            var rig = GameObject.Find(ScoutRigBuilder.RigRootName);
            if (rig == null)
            {
                Debug.LogError("[BatchPipeline] el rig no se creó.");
                return false;
            }

            var animator = rig.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[BatchPipeline] el Avatar humanoide no es válido; no se puede retargetear.");
                return false;
            }
            Debug.Log($"[BatchPipeline] Avatar OK: isHuman={animator.avatar.isHuman}, isValid={animator.avatar.isValid}");

            Debug.Log("[BatchPipeline] --- paso 2: hornear clips ---");
            var clips = CollectClips();
            if (clips.Count == 0)
            {
                Debug.LogError("[BatchPipeline] no encontré clips en:\n  " + string.Join("\n  ", SourceFolders));
                return false;
            }
            Debug.Log($"[BatchPipeline] {clips.Count} clips fuente: " +
                      string.Join(", ", clips.Select(c => c.name)));

            var baked = EmoteBaker.BakeClips(rig, clips);
            if (baked.Count == 0)
            {
                Debug.LogError("[BatchPipeline] no se horneó ningún clip.");
                return false;
            }

            Debug.Log("[BatchPipeline] --- paso 3: construir AssetBundle ---");
            BuildEmoteBundle.Build();

            return true;
        }

        static List<AnimationClip> CollectClips()
        {
            var folders = SourceFolders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (folders.Length == 0) return new List<AnimationClip>();

            var clips = new List<AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")));
            }
            return clips.Distinct().OrderBy(c => c.name, System.StringComparer.Ordinal).ToList();
        }
    }
}
