// EmoteBaker.cs — Editor-only.
//
// Hornea animaciones HUMANOIDES (las del pack de la Asset Store) a clips
// GENÉRICOS atados a las rutas de huesos del Scout, que es el único formato
// que PEAK sabe reproducir.
//
// Cómo funciona:
//   1. AnimationMode.SampleAnimationClip() aplica el clip humanoide sobre el rig
//      del Scout -> Unity hace el retargeting.
//   2. GameObjectRecorder toma un snapshot de todos los Transform en cada frame.
//   3. SaveToClip() escupe un AnimationClip con curvas de Transform por path,
//      exactamente como "Armature/Hip/Mid/AimJoint/Torso/Head".
//
// Uso:  PEAK Emotes → 2. Hornear clips seleccionados

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PeakEmotes.EditorTools
{
    public static class EmoteBaker
    {
        const string OutputDir = "Assets/_PeakEmotes/Baked";

        /// PEAK muestrea sus clips a 60 fps. Igualamos.
        const float SampleRate = 60f;

        /// Huesos que NO grabamos, para que el clip toque exactamente lo mismo que un
        /// emote vanilla (las 45 rutas de A_Scout_Emote_Dance2).
        ///
        /// - AimJoint: lo controla el sistema de mirada del juego.
        /// - S_Toe_* / S_Heel_*: pivotes de squash/stretch del pie, ambos en la misma
        ///   posición local que el tobillo. Animarlos descoloca los pies.
        static readonly HashSet<string> ExcludedBones = new HashSet<string>
        {
            "AimJoint",
            "S_Toe_1_L", "S_Toe_1_R",
            "S_Heel_L", "S_Heel_R",
        };

        /// Pose de reposo de la cadera, capturada antes de muestrear ninguna animación.
        static Vector3 hipRestLocalPosition;

        /// Quita el desplazamiento horizontal de la cadera para que el baile no
        /// haga viajar al Scout. La altura (Y) se conserva: hace falta para que
        /// los bailes con flexión de rodillas se vean bien.
        const bool LockHorizontalRootMotion = true;

        /// Los clips del pack son loops de ~2 s. Registrados como OneShot, PEAKEmoteLib
        /// para el emote al acabar el clip, así que un baile duraría 2 s. Como son loops
        /// sin costura, repetimos la animación al hornear hasta llegar a esta duración.
        /// Tope práctico: PEAKEmoteLib corta a los 10 s pase lo que pase.
        const float MinSeconds = 6f;

        /// Recoloca la cadera a la altura de reposo del Scout. Sin esto el retargeting
        /// la deja ~0,47 m por debajo y el baile sale en cuclillas.
        const bool AnchorHipHeight = true;

        [MenuItem("PEAK Emotes/2. Hornear clips seleccionados", priority = 2)]
        public static void BakeSelection()
        {
            var rig = GameObject.Find(ScoutRigBuilder.RigRootName);
            if (rig == null)
            {
                Debug.LogError("[EmoteBaker] No encuentro el rig. Ejecuta primero " +
                               "'PEAK Emotes → 1. Construir rig del Scout'.");
                return;
            }

            var animator = rig.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[EmoteBaker] El rig no tiene un Avatar humanoide válido.");
                return;
            }

            var clips = Selection.objects.OfType<AnimationClip>().ToList();

            // Si seleccionaste los FBX en vez de los clips, sacamos los clips de dentro.
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")));
            }

            clips = clips.Distinct().ToList();
            if (clips.Count == 0)
            {
                Debug.LogError("[EmoteBaker] Selecciona uno o más AnimationClip (o los FBX que los contienen) " +
                               "en el Project window.");
                return;
            }

            BakeClips(rig, clips);
        }

        /// Hornea una lista de clips. Reutilizable desde el pipeline en batchmode.
        /// Vacía OutputDir primero: si no, un renombrado deja clips huérfanos que
        /// acabarían colándose en el AssetBundle.
        internal static List<string> BakeClips(GameObject rig, IList<AnimationClip> clips)
        {
            if (AssetDatabase.IsValidFolder(OutputDir))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { OutputDir }))
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }
            Directory.CreateDirectory(OutputDir);
            var baked = new List<string>();

            // Capturamos la pose de reposo de la cadera ANTES de muestrear nada:
            // en cuanto empieza el AnimationMode el rig queda posado por la animación.
            var hipTransform = rig.transform.Find("Armature/Hip");
            if (hipTransform == null)
            {
                Debug.LogError("[EmoteBaker] no encuentro Armature/Hip en el rig.");
                return baked;
            }
            hipRestLocalPosition = hipTransform.localPosition;
            Debug.Log($"[EmoteBaker] cadera en reposo (local) = {hipRestLocalPosition:F4}");

            try
            {
                AnimationMode.StartAnimationMode();
                foreach (var src in clips)
                {
                    var outPath = Bake(rig, src);
                    if (outPath != null) baked.Add(outPath);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EmoteBaker] {baked.Count}/{clips.Count} clips horneados:\n  " +
                      string.Join("\n  ", baked));
            return baked;
        }

        static string Bake(GameObject rig, AnimationClip src)
        {
            if (!src.isHumanMotion)
            {
                Debug.LogWarning($"[EmoteBaker] '{src.name}' no es humanoide — saltado. " +
                                 "Importa el FBX con Rig → Animation Type: Humanoid.");
                return null;
            }

            // GameObjectRecorder solo sabe añadir bindings (no hay Unbind), así que
            // vinculamos hueso a hueso en vez de usar BindComponentsOfType.
            var recorder = new GameObjectRecorder(rig);
            int bound = 0;
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t == rig.transform) continue;              // el root no se anima
                if (ExcludedBones.Contains(t.name)) continue;  // AimJoint lo lleva el juego
                recorder.BindComponent(t);
                bound++;
            }
            if (bound == 0)
            {
                Debug.LogError("[EmoteBaker] no se vinculó ningún hueso; ¿el rig está vacío?");
                return null;
            }

            float dt = 1f / SampleRate;
            int loops = src.length > 0.01f
                ? Mathf.Max(1, Mathf.CeilToInt(MinSeconds / src.length))
                : 1;
            float total = src.length * loops;
            int frames = Mathf.Max(2, Mathf.CeilToInt(total * SampleRate));

            for (int i = 0; i < frames; i++)
            {
                // El módulo es lo que encadena las repeticiones del loop.
                float t = src.length > 0.01f ? (i * dt) % src.length : 0f;
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(rig, src, t);
                AnimationMode.EndSampling();
                recorder.TakeSnapshot(dt);
            }

            // El nombre del objeto es el que lee el plugin desde el bundle, así que
            // guardamos ya el nombre normalizado (no el "HumanF@Dance01 - Loop" original).
            var cleanName = Sanitize(src.name);
            var dst = new AnimationClip { name = cleanName, frameRate = SampleRate };
            recorder.SaveToClip(dst, SampleRate);
            recorder.ResetRecording();

            MatchVanillaCurveShape(dst);
            AnchorHipToRestPose(dst, rig, hipRestLocalPosition);

            // Los emotes de PEAK loopean; el propio juego corta a los 5 s (o al
            // acabar el clip si lo registramos como OneShot).
            var settings = AnimationUtility.GetAnimationClipSettings(dst);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(dst, settings);

            var outPath = $"{OutputDir}/{cleanName}.anim";
            AssetDatabase.CreateAsset(dst, AssetDatabase.GenerateUniqueAssetPath(outPath));

            int curves = AnimationUtility.GetCurveBindings(dst).Length;
            Debug.Log($"[EmoteBaker] '{src.name}' -> {outPath}  " +
                      $"({frames} frames, {bound} huesos, {curves} curvas, " +
                      $"{src.length:0.00}s x{loops} = {dst.length:0.00}s)");
            return outPath;
        }

        /// <summary>
        /// Deja el clip con la MISMA forma de curvas que un emote vanilla: rotaciones en
        /// todos los huesos + posición únicamente en <c>Armature/Hip</c>. Sin escalas.
        /// </summary>
        /// <remarks>
        /// GameObjectRecorder graba posición, rotación y escala de cada hueso. Eso rompe
        /// el juego de dos formas:
        ///
        /// 1. El retargeting humanoide estira los huesos para alcanzar las poses
        ///    (armStretch/legStretch), así que las curvas de posición llevan piernas
        ///    comprimidas -> el Scout baila agachado.
        /// 2. Peor: el Animator solo escribe las propiedades que anima alguna clip del
        ///    estado activo. Las animaciones vanilla de idle/andar solo tocan rotaciones,
        ///    así que al acabar el emote NADIE devuelve las posiciones a su sitio y el
        ///    personaje se queda agachado para siempre.
        ///
        /// Comprobado contra A_Scout_Emote_Dance2: 45 bindings de Transform, de los
        /// cuales uno solo es de posición (Armature/Hip) y el resto rotaciones.
        /// </remarks>
        static void MatchVanillaCurveShape(AnimationClip clip)
        {
            const string hipPath = "Armature/Hip";
            int removed = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                bool isScale = binding.propertyName.StartsWith("m_LocalScale", System.StringComparison.Ordinal);
                bool isPosition = binding.propertyName.StartsWith("m_LocalPosition", System.StringComparison.Ordinal);

                if (isScale || (isPosition && binding.path != hipPath))
                {
                    AnimationUtility.SetEditorCurve(clip, binding, null);   // null = borrar curva
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[EmoteBaker]   '{clip.name}': {removed} curvas de posición/escala eliminadas " +
                          "para igualar la forma de los emotes vanilla.");
        }

        /// <summary>
        /// Devuelve la cadera a la altura de reposo del Scout y congela su desplazamiento
        /// horizontal, conservando el rebote vertical del baile.
        /// </summary>
        /// <remarks>
        /// Cuidado con los ejes: el hueso <c>Armature</c> está rotado -90° en X, así que
        /// sus ejes locales NO coinciden con los del mundo:
        ///
        ///     local X -> mundo X   (horizontal)
        ///     local Y -> mundo -Z  (horizontal, profundidad)
        ///     local Z -> mundo +Y  (VERTICAL)
        ///
        /// Se ve en la pose de reposo: Hip local (0, 0.1696, 0.0127) acaba en mundo
        /// (0, 2.013, -0.170). Dar por hecho que "y es arriba" aplana el rebote del baile
        /// y deja la deriva hacia delante, que es exactamente lo contrario de lo que
        /// queremos. Por eso deducimos el eje vertical del propio transform en vez de
        /// escribirlo a mano.
        ///
        /// Y hay un segundo problema: el retargeting humanoide NO coloca la cadera a la
        /// altura de reposo del Scout. Medido, la deja en local z ~ -1.4 cuando en reposo
        /// vale +0.013 — 1,4 unidades de armature (~0,47 m) por debajo. El Scout baila
        /// permanentemente en cuclillas. Por eso desplazamos toda la curva vertical para
        /// centrarla en la altura de reposo, conservando el rebote relativo del baile.
        /// </remarks>
        static void AnchorHipToRestPose(AnimationClip clip, GameObject rig, Vector3 hipRest)
        {
            const string hipPath = "Armature/Hip";

            var armature = rig.transform.Find("Armature");
            var localUp = armature != null
                ? armature.InverseTransformDirection(Vector3.up)
                : Vector3.up;

            int upAxis = 0;
            float best = Mathf.Abs(localUp.x);
            if (Mathf.Abs(localUp.y) > best) { upAxis = 1; best = Mathf.Abs(localUp.y); }
            if (Mathf.Abs(localUp.z) > best) { upAxis = 2; }

            var axisNames = new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            var restValues = new[] { hipRest.x, hipRest.y, hipRest.z };

            for (int axis = 0; axis < 3; axis++)
            {
                var binding = EditorCurveBinding.FloatCurve(hipPath, typeof(Transform), axisNames[axis]);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;

                var keys = curve.keys;

                if (axis == upAxis)
                {
                    if (!AnchorHipHeight) continue;

                    // Centramos la curva en la altura de reposo: conserva el rebote y
                    // quita el desfase constante del retargeting.
                    float mean = keys.Average(k => k.value);
                    float shift = restValues[axis] - mean;
                    float before = keys.Min(k => k.value);
                    for (int i = 0; i < keys.Length; i++) keys[i].value += shift;

                    Debug.Log($"[EmoteBaker]   '{clip.name}': cadera subida {shift:+0.000;-0.000} " +
                              $"(min {before:0.000} -> {keys.Min(k => k.value):0.000}, reposo {restValues[axis]:0.000})");
                }
                else if (LockHorizontalRootMotion)
                {
                    // Clavamos el eje horizontal en el valor de reposo: el baile no
                    // desplaza al Scout ni lo deja descentrado respecto a su collider.
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value = restValues[axis];
                        keys[i].inTangent = 0f;
                        keys[i].outTangent = 0f;
                    }
                }
                else continue;

                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
        }

        /// Los clips del pack se llaman "HumanM@Dance01 - Loop". Eso, tal cual, queda
        /// ilegible en la rueda de emotes, así que lo normalizamos a "Dance01".
        internal static string Sanitize(string name)
        {
            foreach (var suffix in new[] { " - Loop", " - Begin", " - Stop" })
            {
                if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }

            // "HumanM@Dance01" -> "Dance01". Sin sufijo de género: solo horneamos un
            // set, y el sufijo únicamente ensuciaba el nombre en la rueda de emotes.
            int at = name.IndexOf('@');
            if (at > 0) name = name.Substring(at + 1);

            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('@', '_').Replace(' ', '_');
        }

        [MenuItem("PEAK Emotes/Diagnóstico: listar curvas del clip seleccionado", priority = 20)]
        public static void DumpCurves()
        {
            var clip = Selection.activeObject as AnimationClip;
            if (clip == null) { Debug.LogError("Selecciona un AnimationClip."); return; }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var paths = bindings.Select(b => b.path).Distinct().OrderBy(p => p).ToList();
            Debug.Log($"'{clip.name}': {bindings.Length} curvas sobre {paths.Count} transforms, " +
                      $"{clip.length:0.00}s @ {clip.frameRate}fps, humanMotion={clip.isHumanMotion}\n" +
                      string.Join("\n", paths));
        }
    }
}
