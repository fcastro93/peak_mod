// ScoutRigBuilder.cs — Editor-only.
//
// Reconstruye el esqueleto del Scout de PEAK a partir de scout_skeleton.json
// (extraído de PEAK_Data/resources.assets) y le construye encima un Avatar
// HUMANOID, para poder retargetear animaciones humanoides compradas.
//
// Sin AssetRipper: el JSON trae la pose de reposo real (TRS local de cada hueso).
//
// Uso:  PEAK Emotes → 1. Construir rig del Scout

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PeakEmotes.EditorTools
{
    [Serializable]
    public class ScoutBone
    {
        public string path;
        public string name;
        public string parent;
        public float[] localPosition;
        public float[] localRotation;   // x, y, z, w
        public float[] localScale;
        public uint pathHash;
    }

    [Serializable]
    public class ScoutSkeleton
    {
        public string source;
        public string root;
        public ScoutBone[] bones;
    }

    public static class ScoutRigBuilder
    {
        public const string RigRootName = "ScoutRetargetRig";
        public const string AvatarAssetPath = "Assets/_PeakEmotes/ScoutAvatar.asset";

        /// El Scout va escalado a 0.3308 en el prefab del juego. Lo aplicamos al
        /// root para que el rig tenga tamaño realista en el editor. No afecta al
        /// retargeting (Unity normaliza proporciones) pero ayuda a ver si algo baila mal.
        const float ScoutRootScale = 0.3308f;

        // Mapeo hueso-humanoide de Unity -> hueso del Scout.
        // El Scout NO tiene: cuello, anular, UpperChest. Todos opcionales en Unity.
        static readonly Dictionary<string, string> HumanMap = new Dictionary<string, string>
        {
            { "Hips",          "Hip"        },
            { "Spine",         "Mid"        },
            { "Chest",         "Torso"      },
            { "Head",          "Head"       },

            { "LeftUpperLeg",  "Leg_L"      },
            { "LeftLowerLeg",  "Knee_L"     },
            { "LeftFoot",      "Foot_L"     },
            { "RightUpperLeg", "Leg_R"      },
            { "RightLowerLeg", "Knee_R"     },
            { "RightFoot",     "Foot_R"     },

            // OJO: NO mapear S_Toe_1_L/R como LeftToes/RightToes.
            // S_Toe_1_* y S_Heel_* comparten la MISMA posición local que el tobillo
            // (0, 0.0837, 0) y solo se diferencian por una rotación de ±86°: son un par
            // de pivotes de squash/stretch para el rodado del pie, no una articulación.
            // Unity usa el hueso del dedo para deducir el eje frontal del pie, así que
            // mapearlo hace que el tobillo rote mal y los pies bailen por su cuenta.
            // Los emotes vanilla tampoco los animan.

            { "LeftShoulder",  "S_Shoulder_L" },
            { "LeftUpperArm",  "Arm_L"      },
            { "LeftLowerArm",  "Elbow_L"    },
            { "LeftHand",      "Hand_L"     },
            { "RightShoulder", "S_Shoulder_R" },
            { "RightUpperArm", "Arm_R"      },
            { "RightLowerArm", "Elbow_R"    },
            { "RightHand",     "Hand_R"     },

            { "Left Thumb Proximal",      "Thumb_1_L"  },
            { "Left Thumb Intermediate",  "Thumb_2_L"  },
            { "Left Thumb Distal",        "Thumb_3_L"  },
            { "Left Index Proximal",      "Index_1_L"  },
            { "Left Index Intermediate",  "Index_2_L"  },
            { "Left Index Distal",        "Index_3_L"  },
            { "Left Middle Proximal",     "Middle_1_L" },
            { "Left Middle Intermediate", "Middle_2_L" },
            { "Left Middle Distal",       "Middle_3_L" },
            { "Left Little Proximal",     "Pinky_1_L"  },
            { "Left Little Intermediate", "Pinky_2_L"  },
            { "Left Little Distal",       "Pinky_3_L"  },

            { "Right Thumb Proximal",      "Thumb_R_1"  },
            { "Right Thumb Intermediate",  "Thumb_R_2"  },
            { "Right Thumb Distal",        "Thumb_R_3"  },
            { "Right Index Proximal",      "Index_1_R"  },
            { "Right Index Intermediate",  "Index_2_R"  },
            { "Right Index Distal",        "Index_3_R"  },
            { "Right Middle Proximal",     "Middle_1_R" },
            { "Right Middle Intermediate", "Middle_2_R" },
            { "Right Middle Distal",       "Middle_3_R" },
            { "Right Little Proximal",     "Pinky_1_R"  },
            { "Right Little Intermediate", "Pinky_2_R"  },
            { "Right Little Distal",       "Pinky_3_R"  },
        };

        [MenuItem("PEAK Emotes/1. Construir rig del Scout", priority = 1)]
        public static void Build()
        {
            var json = LocateSkeletonJson();
            if (json == null) return;

            var skel = JsonUtility.FromJson<ScoutSkeleton>(File.ReadAllText(json));
            if (skel?.bones == null || skel.bones.Length == 0)
            {
                Debug.LogError($"[ScoutRigBuilder] {json} no contiene huesos.");
                return;
            }

            var existing = GameObject.Find(RigRootName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            // El root del rig equivale al GameObject "Scout" del juego: el Animator
            // vive ahí y los paths de las curvas salen como "Armature/Hip/...".
            var root = new GameObject(RigRootName);
            root.transform.localScale = Vector3.one * ScoutRootScale;

            // Ojo: el hueso raíz trae parent = null en el JSON, y Dictionary no admite
            // clave null — de ahí el string vacío como centinela.
            const string NoParent = "";
            var made = new Dictionary<string, Transform> { { NoParent, root.transform } };

            foreach (var b in skel.bones.OrderBy(b => b.path.Count(c => c == '/')))
            {
                var parentKey = string.IsNullOrEmpty(b.parent) ? NoParent : b.parent;
                if (!made.TryGetValue(parentKey, out var parent))
                {
                    Debug.LogError($"[ScoutRigBuilder] padre no encontrado para {b.path} (parent='{b.parent}')");
                    continue;
                }

                var go = new GameObject(b.name);
                var t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = new Vector3(b.localPosition[0], b.localPosition[1], b.localPosition[2]);
                t.localRotation = new Quaternion(b.localRotation[0], b.localRotation[1],
                                                 b.localRotation[2], b.localRotation[3]);
                t.localScale = new Vector3(b.localScale[0], b.localScale[1], b.localScale[2]);
                made[b.path] = t;
            }

            Debug.Log($"[ScoutRigBuilder] {made.Count - 1} huesos instanciados bajo '{RigRootName}'.");

            var avatar = BuildAvatar(root, skel);
            if (avatar == null) return;

            var animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Selection.activeGameObject = root;
            Debug.Log("[ScoutRigBuilder] LISTO. Revisa en el inspector que el Avatar sea válido " +
                      "y que la pose de reposo se parezca a una T-pose antes de hornear.");
        }

        static Avatar BuildAvatar(GameObject root, ScoutSkeleton skel)
        {
            var byName = new Dictionary<string, ScoutBone>();
            foreach (var b in skel.bones) byName[b.name] = b;

            var humanBones = new List<HumanBone>();
            var missing = new List<string>();
            foreach (var kv in HumanMap)
            {
                if (!byName.ContainsKey(kv.Value)) { missing.Add($"{kv.Key}->{kv.Value}"); continue; }
                humanBones.Add(new HumanBone
                {
                    humanName = kv.Key,
                    boneName = kv.Value,
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }
            if (missing.Count > 0)
                Debug.LogWarning("[ScoutRigBuilder] huesos del mapeo no encontrados: " + string.Join(", ", missing));

            // El array de skeleton incluye el root + todos los huesos, en pose de reposo.
            var skeleton = new List<SkeletonBone>
            {
                new SkeletonBone
                {
                    name = root.name,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    scale = Vector3.one * ScoutRootScale,
                }
            };
            foreach (var b in skel.bones)
            {
                skeleton.Add(new SkeletonBone
                {
                    name = b.name,
                    position = new Vector3(b.localPosition[0], b.localPosition[1], b.localPosition[2]),
                    rotation = new Quaternion(b.localRotation[0], b.localRotation[1],
                                              b.localRotation[2], b.localRotation[3]),
                    scale = new Vector3(b.localScale[0], b.localScale[1], b.localScale[2]),
                });
            }

            var desc = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            var avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
            if (!avatar.isValid)
            {
                Debug.LogError("[ScoutRigBuilder] El Avatar generado NO es válido. " +
                               "Suele significar que la pose de reposo del Scout no se parece " +
                               "lo bastante a una T-pose. Revisa el mapeo en HumanMap.");
                return null;
            }
            avatar.name = "ScoutAvatar";

            Directory.CreateDirectory(Path.GetDirectoryName(AvatarAssetPath));
            AssetDatabase.CreateAsset(avatar, AvatarAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ScoutRigBuilder] Avatar humanoide VÁLIDO guardado en {AvatarAssetPath} " +
                      $"({humanBones.Count} huesos mapeados).");
            return avatar;
        }

        static string LocateSkeletonJson()
        {
            foreach (var guid in AssetDatabase.FindAssets("scout_skeleton"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            Debug.LogError("[ScoutRigBuilder] No encuentro scout_skeleton.json. " +
                           "Cópialo dentro de Assets/ (por ejemplo Assets/_PeakEmotes/).");
            return null;
        }
    }
}
