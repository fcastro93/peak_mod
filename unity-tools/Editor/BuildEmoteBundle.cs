// BuildEmoteBundle.cs — Editor-only.
//
// Empaqueta los clips horneados + los iconos en un AssetBundle que el plugin
// carga en runtime.
//
// Uso:  PEAK Emotes → 3. Construir AssetBundle

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PeakEmotes.EditorTools
{
    public static class BuildEmoteBundle
    {
        public const string BundleName = "scoutdances";

        const string BakedDir = "Assets/_PeakEmotes/Baked";
        const string IconDir = "Assets/_PeakEmotes/Icons";
        const string OutputDir = "Assets/AssetBundles";

        /// <summary>
        /// Carpetas con prefabs que van al bundle (el modelo del kiosco del aeropuerto).
        /// Unity arrastra solas las dependencias: materiales, shader y texturas.
        /// </summary>
        static readonly string[] PrefabFolders =
        {
            "Assets/_PeakEmotes/Kiosk",                                   // lo que dejes tú
            "Assets/LoafbrrAssets/MicStands/prefab/MicStand",             // kiosco de sonidos
            "Assets/Hocker/Cartoon Wooden Box/Box/Prefabs",               // caja de pruebas de items
            "Assets/ithappy/Weapons_FREE/Prefabs",                        // modelos de armas
            "Assets/Epic Toon FX/Prefabs/Combat/Explosions/SmallExplosion", // fogonazo del disparo
            // Solo la subcarpeta Red: las de Green/Yellow/Water repiten nombres y
            // acabaríamos con colisiones al buscar el prefab por nombre en el bundle.
            "Assets/Epic Toon FX/Prefabs/Combat/Blood/Red",                 // impacto en el cuerpo
        };

        /// <summary>
        /// Prefabs sueltos, por ruta exacta.
        /// </summary>
        /// <remarks>
        /// De las carpetas de power-ups solo queremos 4 de los 36 prefabs que contienen.
        /// Meter la carpeta entera engordaría el bundle con 32 modelos que nadie usa, y
        /// además arrastraría sus materiales y texturas.
        /// </remarks>
        static readonly string[] ExtraPrefabs =
        {
            // Cajas de velocidad. Van enteras: dentro llevan las partículas (GlowCircle,
            // Tinysparkles) y el Animator que las hace girar, que es lo que las vende.
            "Assets/Epic Toon FX/Prefabs/Interactive/Powerups/PowerBox/Box Colored/PowerboxColSpeed.prefab",
            "Assets/Epic Toon FX/Prefabs/Interactive/Powerups/PowerBox/Box Colored/PowerboxColSpeed 1.prefab",
            "Assets/Epic Toon FX/Prefabs/Interactive/Powerups/PowerBox/Box Colored/PowerboxColSpeed 2.prefab",

            // Efecto que sale sobre la cabeza al recogerlo.
            "Assets/Epic Toon FX/Prefabs/Interactive/Powerups/PowerBox/Pickup Colored/PowerboxPickupColSpeed.prefab",

            // Blaster que agranda y encoge.
            "Assets/gun/Cosmic_Retro_Blasters Pack_1_FREE/Prefabs/Cosmic_Retro_Blaster_11.prefab",
            "Assets/gun/Cosmic_Retro_Blasters Pack_1_FREE/Prefabs/Cosmic_Retro_Blaster_1.prefab",
            "Assets/gun/Cosmic_Retro_Blasters Pack_1_FREE/Prefabs/Cosmic_Retro_Blaster_10.prefab",
            "Assets/gun/Cosmic_Retro_Blasters Pack_3_Demo/Prefabs/Cosmic_Retro_Blaster_3_4.prefab",
            "Assets/gun/Cosmic_Retro_Blasters Pack_3_Demo/Prefabs/Cosmic_Retro_Blaster_3_6.prefab",

            // Granada de efectos y su explosion.
            "Assets/Cosmic_Retro_Grenades_Pack_1_Demo/Prefabs/Cosmic_Retro_Grenades_Pack_2.prefab",

            // Varita de fuego y su orbe.
            "Assets/3D Items - Wand Pack/Prefabs/wand02_red.prefab",

            // Espejo que devuelve efectos, y el destello al reflejar.
            "Assets/HandMirror/HandMirror Variant.prefab",
            "Assets/Epic Toon FX/Prefabs/Combat/Shield/ShieldSoftGreen.prefab",
            "Assets/Epic Toon FX/Prefabs/Environment/Fire/Cartoon/Radial/ToonRadialFireRed.prefab",
            "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/PoisonSkullExplosion.prefab",

            // Cono del iman y portales.
            "Assets/Epic Toon FX/Prefabs/Combat/Flamethrower/Cartoon/FlamethrowerToonyBlue.prefab",
            "Assets/Epic Toon FX/Prefabs/Interactive/Portals/SimplePortal/SimplePortalBlue.prefab",
            "Assets/Epic Toon FX/Prefabs/Interactive/Portals/SimplePortal/SimplePortalGold.prefab",

            // Destello del intercambio de posiciones.
            "Assets/Epic Toon FX/Prefabs/Combat/Magic/Buff/MagicBuffGreen.prefab",

            // Aura que acompana al jugador sin gravedad.
            "Assets/Epic Toon FX/Prefabs/Combat/Magic/Aura Soft/AuraSoftBlue.prefab",

            // Estrellazo del arma de empuje.
            "Assets/Epic Toon FX/Prefabs/Combat/Explosions/StarExplosion/StarExplosionBlue.prefab",

            // Proyectil del blaster. Van los cuatro colores para poder distinguir de un
            // vistazo el disparo que encoge del que agranda; se eligen desde el config.
            "Assets/Epic Toon FX/Prefabs/Environment/Lightning/Soft Orb/blaster_projectile.prefab",
            "Assets/Epic Toon FX/Prefabs/Environment/Lightning/Soft Orb/LightningOrbSoftBlue.prefab",
            "Assets/Epic Toon FX/Prefabs/Environment/Lightning/Soft Orb/LightningOrbSoftGreen.prefab",
            "Assets/Epic Toon FX/Prefabs/Environment/Lightning/Soft Orb/LightningOrbSoftPink.prefab",
            "Assets/Epic Toon FX/Prefabs/Environment/Lightning/Soft Orb/LightningOrbSoftYellow.prefab",
        };

        /// <summary>
        /// Assets sueltos que NO son prefabs (sonidos, por ejemplo).
        /// </summary>
        /// <remarks>
        /// Van en su propia lista porque la de prefabs comprueba que cada ruta cargue como
        /// GameObject, y un .wav no lo hace. Al bundle le da igual el tipo: lo que marca la
        /// pertenencia es el assetBundleName del importador.
        /// </remarks>
        static readonly string[] ExtraAssets =
        {
            // Disparos del blaster y su impacto.
            "Assets/Gamemaster Audio - Pro Sound Collection/Guns_Weapons/Θ Fun Weapons Θ/weapon_fun_small_zapper_03.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Guns_Weapons/Θ Fun Weapons Θ/weapon_fun_pea_shooter_04.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Guns_Weapons/Taser/taser_stun_gun_zap_electricity_01.wav",

            // Zumbido del proyectil mientras vuela.
            "Assets/Gamemaster Audio - Pro Sound Collection/Magic_Spells/electric_sparks_lightning_loop1.wav",

            // Disparo del arma de empuje.
            "Assets/Gamemaster Audio - Pro Sound Collection/Guns_Weapons/Θ Fun Weapons Θ/weapon_fun_pea_shooter_03.wav",

            // Impacto de la pistola antigravedad.
            "Assets/Gamemaster Audio - Pro Sound Collection/Sci-Fi Weapons/sci-fi_weapon_blaster_laser_boom_01.wav",

            // Zumbido del iman y apertura de portales.
            "Assets/Gamemaster Audio - Pro Sound Collection/Sci-Fi/sci-fi_forcefield_hum_loop_01.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Explosion_Fire_Gas/explosion_large_04.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Magic_Spells/fire_large_flames_magic_loop_01.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Magic_Spells/chimes_magic_bell_ding_1.wav",
            "Assets/Gamemaster Audio - Pro Sound Collection/Magic_Spells/fireball_blast_projectile_spell_06.wav",
        };

        [MenuItem("PEAK Emotes/3. Construir AssetBundle", priority = 3)]
        public static void Build()
        {
            var assets = AssetDatabase.FindAssets("t:AnimationClip", new[] { BakedDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Concat(Directory.Exists(IconDir)
                    ? AssetDatabase.FindAssets("t:Texture2D", new[] { IconDir })
                        .Select(AssetDatabase.GUIDToAssetPath)
                    : Enumerable.Empty<string>())
                .ToList();

            var prefabFolders = PrefabFolders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (prefabFolders.Length > 0)
            {
                var prefabs = AssetDatabase.FindAssets("t:Prefab", prefabFolders)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .ToList();
                assets.AddRange(prefabs);
                Debug.Log($"[BuildEmoteBundle] {prefabs.Count} prefabs añadidos desde:\n  " +
                          string.Join("\n  ", prefabFolders));
            }

            foreach (var extra in ExtraPrefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(extra) == null)
                {
                    Debug.LogWarning($"[BuildEmoteBundle] No existe el prefab: {extra}");
                    continue;
                }
                assets.Add(extra);
                Debug.Log($"[BuildEmoteBundle] prefab suelto: {extra}");
            }

            foreach (var extra in ExtraAssets)
            {
                if (AssetImporter.GetAtPath(extra) == null)
                {
                    Debug.LogWarning($"[BuildEmoteBundle] No existe el asset: {extra}");
                    continue;
                }
                assets.Add(extra);
                Debug.Log($"[BuildEmoteBundle] asset suelto: {extra}");
            }

            if (assets.Count == 0)
            {
                Debug.LogError($"[BuildEmoteBundle] No hay nada en {BakedDir}. " +
                               "Hornea los clips primero (paso 2).");
                return;
            }

            CapTextureSizes(assets);

            foreach (var path in assets)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;
                importer.assetBundleName = BundleName;
                importer.assetBundleVariant = "";
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
            Directory.CreateDirectory(OutputDir);

            var manifest = BuildPipeline.BuildAssetBundles(
                OutputDir,
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                Debug.LogError("[BuildEmoteBundle] La construcción del bundle falló. Mira la consola.");
                return;
            }

            AssetDatabase.Refresh();

            var bundlePath = Path.Combine(OutputDir, BundleName);
            Debug.Log($"[BuildEmoteBundle] OK -> {bundlePath}\n" +
                      $"Assets incluidos ({assets.Count}):\n  " + string.Join("\n  ", assets) +
                      "\n\nCopia ese fichero junto al .dll del plugin en BepInEx/plugins/.");

            if (!Application.isBatchMode) EditorUtility.RevealInFinder(bundlePath);
        }

        /// <summary>Resolución máxima de las texturas que entran al bundle.</summary>
        const int MaxTextureSize = 1024;

        /// <summary>
        /// Baja la resolución de las texturas que arrastran los assets del bundle.
        /// </summary>
        /// <remarks>
        /// Algunos assets de la Asset Store vienen con texturas enormes pensadas para
        /// primeros planos de cine. El espejo, por ejemplo, trae cinco mapas PBR de 8K:
        /// 85 MB solo la normal. Eso engordó el bundle de 15 a 82 MB, y entran aunque en
        /// el juego se use un nivel de detalle bajo, porque el prefab las referencia todas.
        ///
        /// A 1024 no se nota la diferencia en objetos que se sostienen en la mano o se ven
        /// a unos metros, que es todo lo que metemos.
        ///
        /// OJO: esto cambia los ajustes de importación del proyecto de Unity, no solo el
        /// bundle. Es reversible desde el inspector de cada textura.
        /// </remarks>
        static void CapTextureSizes(System.Collections.Generic.List<string> assets)
        {
            int capped = 0;

            foreach (var dependency in AssetDatabase.GetDependencies(assets.ToArray(), true))
            {
                if (AssetImporter.GetAtPath(dependency) is not TextureImporter texture) continue;
                if (texture.maxTextureSize <= MaxTextureSize) continue;

                Debug.Log($"[BuildEmoteBundle] {dependency}: {texture.maxTextureSize} -> {MaxTextureSize}");

                texture.maxTextureSize = MaxTextureSize;
                texture.SaveAndReimport();
                capped++;
            }

            if (capped > 0)
                Debug.Log($"[BuildEmoteBundle] {capped} texturas limitadas a {MaxTextureSize} px.");
        }

        [MenuItem("PEAK Emotes/4. Copiar bundle a BepInEx/plugins", priority = 4)]
        public static void CopyToGame()
        {
            var src = Path.Combine(OutputDir, BundleName);
            if (!File.Exists(src))
            {
                Debug.LogError($"[BuildEmoteBundle] No existe {src}. Construye el bundle primero (paso 3).");
                return;
            }

            var dstDir = EditorPrefs.GetString("PeakEmotes.PluginDir", "");
            if (string.IsNullOrEmpty(dstDir) || !Directory.Exists(dstDir))
            {
                dstDir = EditorUtility.OpenFolderPanel(
                    "Selecciona BepInEx/plugins/ScoutDances de PEAK", "", "");
                if (string.IsNullOrEmpty(dstDir)) return;
                EditorPrefs.SetString("PeakEmotes.PluginDir", dstDir);
            }

            var dst = Path.Combine(dstDir, BundleName);
            File.Copy(src, dst, true);
            Debug.Log($"[BuildEmoteBundle] Copiado a {dst}");
        }
    }
}
