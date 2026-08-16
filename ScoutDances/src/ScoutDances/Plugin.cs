using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PEAKEmoteLib;
using Photon.Pun;
using ScoutDances.Patches;
using ScoutDances.Props;
using ScoutDances.Sounds;
using ScoutDances.Weapons;
using UnityEngine;

namespace ScoutDances;

[BepInAutoPlugin]
[BepInDependency(PEAKEmoteLib.Plugin.Id)]
[BepInDependency(PEAKLib.Core.CorePlugin.Id)]
[BepInDependency(PEAKLib.Items.ItemsPlugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    /// Nombre del AssetBundle que acompaña al .dll (lo genera unity-tools/Editor/BuildEmoteBundle.cs).
    const string BundleFileName = "scoutdances";

    /// Prefijo de propiedad para que los nombres de emote sean globalmente únicos.
    const string EmotePrefix = "fcastro_";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Plugin Instance { get; private set; } = null!;

    internal static ConfigEntry<string> CfgWheelOrder = null!;
    internal static ConfigEntry<bool> CfgThirdPerson = null!;
    internal static ConfigEntry<float> CfgCamDistance = null!;
    internal static ConfigEntry<float> CfgCamHeight = null!;
    internal static ConfigEntry<float> CfgCamSideOffset = null!;
    internal static ConfigEntry<bool> CfgVerbose = null!;
    internal static ConfigEntry<float> CfgSoundVolume = null!;
    internal static ConfigEntry<float> CfgSoundMaxSeconds = null!;
    internal static ConfigEntry<float> CfgSoundDistanceScale = null!;
    internal static ConfigEntry<float> CfgKioskOffset = null!;
    internal static ConfigEntry<float> CfgKioskHeight = null!;
    internal static ConfigEntry<string> CfgKioskModel = null!;
    internal static ConfigEntry<float> CfgKioskScale = null!;
    internal static ConfigEntry<float> CfgKioskTargetHeight = null!;
    internal static ConfigEntry<bool> CfgDisableSmallMeshCulling = null!;
    internal static ConfigEntry<string> CfgItemBoxModel = null!;
    internal static ConfigEntry<float> CfgItemBoxOffset = null!;
    internal static ConfigEntry<float> CfgItemBoxHeight = null!;
    internal static ConfigEntry<string> CfgWeaponShotSound = null!;
    internal static ConfigEntry<bool> CfgWeaponDestroyWhenEmpty = null!;
    internal static ConfigEntry<string> CfgMuzzleFlash = null!;
    internal static ConfigEntry<float> CfgMuzzleFlashScale = null!;
    internal static ConfigEntry<float> CfgMuzzleFlashLifetime = null!;
    internal static ConfigEntry<Vector3> CfgMuzzleOffset = null!;
    internal static ConfigEntry<string> CfgBloodParticle = null!;
    internal static ConfigEntry<float> CfgBloodScale = null!;
    internal static ConfigEntry<float> CfgBloodLifetime = null!;
    internal static ConfigEntry<float> CfgKnockbackRadius = null!;
    internal static ConfigEntry<float> CfgKnockbackUp = null!;
    internal static ConfigEntry<bool> CfgWeaponAimAlign = null!;
    internal static ConfigEntry<bool> CfgWeaponInHand = null!;
    internal static ConfigEntry<float> CfgOrbSpeed = null!;
    internal static ConfigEntry<float> CfgOrbScale = null!;
    internal static ConfigEntry<float> CfgBlasterVolume = null!;
    internal static ConfigEntry<bool> CfgWeaponsInLuggage = null!;
    internal static ConfigEntry<bool> CfgModCrates = null!;
    internal static ConfigEntry<float> CfgCratesPerLuggage = null!;
    internal static ConfigEntry<float> CfgCrateScatter = null!;
    internal static ConfigEntry<float> CfgCrateSize = null!;
    internal static ConfigEntry<string> CfgWeaponRarity = null!;
    internal static ConfigEntry<float> CfgOrbSoundNear = null!;
    internal static ConfigEntry<float> CfgOrbSoundFar = null!;
    internal static ConfigEntry<float> CfgMagnetBeamLength = null!;
    internal static ConfigEntry<bool> CfgMagnetDiagnostics = null!;
    internal static ConfigEntry<bool> CfgTeams = null!;
    internal static ConfigEntry<string> CfgTeamMenuKey = null!;
    internal static ConfigEntry<bool> CfgTeamStatues = null!;
    internal static ConfigEntry<float> CfgStatueSpacing = null!;
    internal static ConfigEntry<bool> CfgTeamSpawnSeparate = null!;
    internal static ConfigEntry<float> CfgTeamSpawnSpread = null!;
    internal static ConfigEntry<bool> CfgCheckpointRespawn = null!;
    internal static ConfigEntry<float> CfgCheckpointRespawnDelay = null!;
    internal static ConfigEntry<bool> CfgBackpackForAll = null!;
    internal static ConfigEntry<bool> CfgTeamBackpacks = null!;
    internal static ConfigEntry<bool> CfgTeamCampfire = null!;
    internal static ConfigEntry<bool> CfgAutoUpdate = null!;
    internal static ConfigEntry<string> CfgUpdateRepo = null!;
    internal static ConfigEntry<float> CfgUnstickLift = null!;
    internal static ConfigEntry<float> CfgTeamBackpackSpread = null!;
    internal static ConfigEntry<int> CfgBackpackPages = null!;
    internal static ConfigEntry<int> CfgAntiGravAmount = null!;
    internal static ConfigEntry<string> CfgMirrorEffect = null!;
    internal static ConfigEntry<string> CfgMirrorSound = null!;
    internal static ConfigEntry<float> CfgMirrorEffectTime = null!;
    internal static ConfigEntry<float> CfgMirrorScale = null!;
    internal static ConfigEntry<bool> CfgMirrorHud = null!;
    internal static ConfigEntry<float> CfgMirrorHudX = null!;
    internal static ConfigEntry<float> CfgMirrorHudY = null!;
    internal static ConfigEntry<float> CfgMirrorHudSize = null!;
    internal static ConfigEntry<string> CfgBackpackPageKey = null!;

    /// <summary>Tecla para cambiar de página en la mochila, ya traducida.</summary>
    internal static UnityEngine.InputSystem.Key BackpackPageKey
    {
        get
        {
            return System.Enum.TryParse<UnityEngine.InputSystem.Key>(
                       CfgBackpackPageKey.Value, ignoreCase: true, out var key)
                ? key
                : UnityEngine.InputSystem.Key.Tab;
        }
    }
    internal static ConfigEntry<bool> CfgMoreLoot = null!;
    internal static ConfigEntry<float> CfgLootPerTeam = null!;
    internal static ConfigEntry<float> CfgLuggageBoost = null!;
    internal static ConfigEntry<float> CfgTeamMemberSpread = null!;
    internal static ConfigEntry<bool> CfgPullToCampfire = null!;
    internal static ConfigEntry<bool> CfgRandomMap = null!;
    internal static ConfigEntry<bool> CfgFogNoRevive = null!;
    internal static ConfigEntry<bool> CfgBackpackDebug = null!;
    internal static ConfigEntry<float> CfgBuffFloatHeight = null!;
    internal static ConfigEntry<float> CfgBuffSpinSpeed = null!;
    internal static ConfigEntry<float> CfgBuffBob = null!;
    internal static ConfigEntry<bool> CfgBuffHud = null!;
    internal static ConfigEntry<float> CfgBuffHudGap = null!;
    internal static ConfigEntry<float> CfgBuffSummarySeconds = null!;
    internal static ConfigEntry<float> CfgBuffInstantMessage = null!;
    internal static ConfigEntry<float> CfgBuffLaunchForce = null!;
    internal static ConfigEntry<string> CfgBuffPickupSound = null!;
    internal static ConfigEntry<float> CfgPullDistance = null!;
    internal static ConfigEntry<bool> CfgMapBuffs = null!;
    internal static ConfigEntry<float> CfgBuffsPerLuggage = null!;
    internal static ConfigEntry<float> CfgBuffScatter = null!;

    /// <summary>Tecla del panel de equipos, ya traducida. F3 la usa el ajustador de armas.</summary>
    internal static UnityEngine.InputSystem.Key TeamMenuKey
    {
        get
        {
            return System.Enum.TryParse<UnityEngine.InputSystem.Key>(
                       CfgTeamMenuKey.Value, ignoreCase: true, out var key)
                ? key
                : UnityEngine.InputSystem.Key.F2;
        }
    }
    internal static ConfigEntry<string> CfgBuffPickupEffect = null!;
    internal static ConfigEntry<float> CfgBuffEffectLifetime = null!;
    internal static ConfigEntry<float> CfgBuffEffectScale = null!;
    internal static ConfigEntry<float> CfgBuffEffectHeight = null!;
    internal static ConfigEntry<float> CfgBuffCastMultiplier = null!;
    internal static ConfigEntry<bool> CfgBuffDiagnostics = null!;
    internal static ConfigEntry<bool> CfgKioskProfiler = null!;
    internal static ConfigEntry<float> CfgWeaponSway = null!;
    internal static ConfigEntry<float> CfgWeaponSwayMax = null!;
    internal static ConfigEntry<float> CfgWeaponSwaySmoothing = null!;
    internal static ConfigEntry<float> CfgWeaponSwayDamping = null!;
    internal static ConfigEntry<bool> CfgLobbyPreventDeath = null!;
    internal static ConfigEntry<bool> CfgLobbyRespawn = null!;
    internal static ConfigEntry<float> CfgLobbyRespawnDelay = null!;

    /// Las armas del mod, cada una con su propia sección en el config.
    internal static readonly List<WeaponDefinition> Weapons = new();

    /// Todos los prefabs del bundle, para buscarlos por nombre.
    static readonly Dictionary<string, GameObject> Prefabs = new(StringComparer.OrdinalIgnoreCase);

    /// Sonidos que vienen en el bundle, buscables por nombre.
    static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nombres, tal cual quedan en el database, de todos los items del mod.</summary>
    internal static IEnumerable<string> ModItemNames()
    {
        foreach (var weapon in Weapons) yield return weapon.DisplayName.Value;
        foreach (var buff in BuffList) yield return buff.DisplayName.Value;
        if (Blaster != null) yield return Blaster.DisplayName.Value;
    }

    internal static AudioClip? FindClip(string? name) =>
        name != null && Clips.TryGetValue(name, out var clip) ? clip : null;

    internal static GameObject? FindPrefab(string name) =>
        Prefabs.TryGetValue(name ?? "", out var prefab) ? prefab : null;

    /// <summary>Busca la colocación de un arma por su id, sea pistola o blaster.</summary>
    internal static ScoutDances.Weapons.IWeaponPlacement? FindWeapon(string? id)
    {
        if (id == null) return null;

        var weapon = Weapons.FirstOrDefault(w => w.Id == id);
        if (weapon != null) return weapon;

        return Blaster != null && Blaster.Id == id ? Blaster : null;
    }

    /// Los power-ups del mod. Se llama BuffList y no Buffs para no chocar con el
    /// namespace ScoutDances.Buffs.
    internal static readonly List<Buffs.BuffDefinition> BuffList = new();

    /// El blaster que agranda y encoge. Va aparte de la lista de armas porque tiene dos
    /// modos con ajustes propios en vez de un único juego de valores.
    internal static ScoutDances.Weapons.BlasterDefinition? Blaster;

    internal static Buffs.BuffDefinition? FindBuff(string? id) =>
        id == null ? null : BuffList.FirstOrDefault(b => b.Id == id);
    internal static ConfigEntry<bool> CfgLobbyCombat = null!;
    internal static ConfigEntry<float> CfgRegenDelay = null!;
    internal static ConfigEntry<float> CfgRegenPerSecond = null!;

    /// Prefab del kiosco cargado del bundle (null si no se encontró).
    internal static GameObject? KioskPrefab;

    /// Prefab de la caja de pruebas de items (null si no está en el bundle).
    internal static GameObject? ItemBoxPrefab;

    /// Modelo del arma cargado del bundle.
    internal static GameObject? WeaponPrefab;

    /// Partícula del fogonazo (null si no está en el bundle).
    internal static GameObject? MuzzleFlashPrefab;

    /// Partícula de impacto en el cuerpo (null si no está en el bundle).
    internal static GameObject? BloodPrefab;

    /// Definición del mod para PEAKLib (registro de contenido).
    internal static PEAKLib.Core.ModDefinition Definition = null!;

    /// Emotes en el orden en que se añaden a la rueda.
    internal static readonly List<Emote> Registered = new();

    readonly Harmony _harmony = new(Id);

    void Awake()
    {
        Log = Logger;
        Instance = this;

        // ANTES de enlazar nada: se lee con qué versión se escribió el fichero, para poder
        // comparar al final.
        ConfigMigration.ReadStamp(Config);

        CfgWheelOrder = Config.Bind(
            "General", "WheelOrder", "",
            "Orden de los bailes en la rueda de emotes, separados por comas, usando el " +
            "nombre del AnimationClip (por ejemplo: Dance01_M,Dance03_M). " +
            "Déjalo vacío para usar el orden alfabético de los clips del bundle.");

        CfgThirdPerson = Config.Bind(
            "Camara", "ThirdPersonOnEmote", true,
            "Pasa a tercera persona y muestra el cuerpo entero mientras haces un emote. " +
            "Vuelve a primera persona al terminar (o en cuanto te mueves).");

        CfgCamDistance = Config.Bind(
            "Camara", "Distance", 3.5f,
            new ConfigDescription("Distancia de la cámara al Scout durante el emote.",
                new AcceptableValueRange<float>(1.5f, 8f)));

        CfgCamHeight = Config.Bind(
            "Camara", "HeightOffset", 0.6f,
            new ConfigDescription("Altura de la cámara respecto al torso del Scout.",
                new AcceptableValueRange<float>(-1f, 3f)));

        CfgVerbose = Config.Bind(
            "Debug", "VerboseLog", false,
            "Escribe en el log el estado de la cámara y cada emote que se dispara. " +
            "Útil solo para diagnosticar; llena el log.");

        CfgCamSideOffset = Config.Bind(
            "Camara", "SideOffset", 0.5f,
            new ConfigDescription("Desplazamiento lateral, para que el Scout no quede " +
                "justo en el centro del encuadre. Negativo = izquierda.",
                new AcceptableValueRange<float>(-3f, 3f)));

        CfgSoundVolume = Config.Bind(
            "Sonidos", "Volume", 1f,
            new ConfigDescription("Multiplicador LOCAL sobre todos los sonidos. El nivel de cada sonido lo pone su dueño en el kiosco; esto es para bajarlo todo de golpe si hace falta.",
                new AcceptableValueRange<float>(0f, 1f)));

        CfgSoundMaxSeconds = Config.Bind(
            "Sonidos", "MaxSeconds", 30f,
            new ConfigDescription("Duración máxima de un sonido, en segundos. El audio ya no " +
                "se corta al moverte, así que este es el único tope. 0 = sin límite.",
                new AcceptableValueRange<float>(0f, 120f)));

        CfgSoundDistanceScale = Config.Bind(
            "Sonidos", "DistanceScale", 0.5f,
            new ConfigDescription(
                "Recorta el alcance de los sonidos respecto al de la voz del personaje. " +
                "0.5 = se oyen a la mitad de distancia que hablando. Escala a la vez la " +
                "distancia mínima y la máxima, así que la curva de caída no se deforma.",
                new AcceptableValueRange<float>(0.05f, 2f)));

        CfgKioskOffset = Config.Bind(
            "Sonidos", "KioskOffset", 2.5f,
            new ConfigDescription("Separación (en metros) entre el kiosco de sonidos y el " +
                "kiosco vanilla de invitar amigos, que usamos de referencia.",
                new AcceptableValueRange<float>(-10f, 10f)));

        CfgKioskHeight = Config.Bind(
            "Sonidos", "KioskHeight", 0f,
            new ConfigDescription("Ajuste vertical del kiosco respecto al suelo detectado. " +
                "Solo si queda hundido o flotando.",
                new AcceptableValueRange<float>(-3f, 3f)));

        CfgKioskModel = Config.Bind(
            "Sonidos", "KioskModel", "Mic_A_grp",
            "Nombre del prefab del bundle que se usa como kiosco. El pack de pies de " +
            "micro trae Mic_A_grp .. Mic_E_grp y sus variantes _Alt.");

        CfgKioskTargetHeight = Config.Bind(
            "Sonidos", "KioskTargetHeight", 1.5f,
            new ConfigDescription("Altura en metros a la que se escala el modelo del kiosco. " +
                "El modelo se mide solo, así que esto vale para cualquier prefab.",
                new AcceptableValueRange<float>(0.3f, 5f)));

        CfgKioskScale = Config.Bind(
            "Sonidos", "KioskScale", 1f,
            new ConfigDescription("Multiplicador extra sobre la altura objetivo.",
                new AcceptableValueRange<float>(0.1f, 5f)));

        CfgDisableSmallMeshCulling = Config.Bind(
            "Sonidos", "DisableSmallMeshCulling", true,
            "URP descarta las mallas que ocupan menos del 0,5 % de la pantalla, y por eso " +
            "el kiosco solo se veía de cerca. Esto pone ese umbral a 0. OJO: es un ajuste " +
            "GLOBAL de render, así que también dejan de descartarse los props pequeños y " +
            "lejanos del juego. Ponlo en false si notas caída de rendimiento.");

        CfgItemBoxModel = Config.Bind(
            "Pruebas", "ItemBoxModel", "BoxReady",
            "Nombre del prefab del bundle que se usa como caja de pruebas de items. " +
            "Si no está en el bundle se usa un cubo.");

        CfgItemBoxOffset = Config.Bind(
            "Pruebas", "ItemBoxOffset", 4.5f,
            new ConfigDescription("Separación entre la caja de items y el kiosco vanilla " +
                "de invitar amigos, que usamos de referencia.",
                new AcceptableValueRange<float>(-15f, 15f)));

        CfgItemBoxHeight = Config.Bind(
            "Pruebas", "ItemBoxHeight", 0.7f,
            new ConfigDescription("Altura en metros a la que se escala la caja.",
                new AcceptableValueRange<float>(0.2f, 3f)));








        CfgWeaponShotSound = Config.Bind(
            "Armas", "ShotSound", "Au_Garand_Fire1",
            "Clip de disparo, de los que ya trae el juego. Otros: Au_Garand_Fire3, " +
            "Au_Gundog_Fire3, Au_Harpoon_Shoot1, Au_Harpoon_Shoot2. Vacío = sin sonido.");


        CfgWeaponDestroyWhenEmpty = Config.Bind(
            "Armas", "DestroyWhenEmpty", true,
            "Destruye el arma al gastar la última bala, igual que un consumible del juego.");





        CfgMuzzleFlash = Config.Bind(
            "Armas", "MuzzleFlash", "SmallExplosionFire",
            "Partícula del fogonazo, del pack Epic Toon FX. Variantes: SmallExplosionFire, " +
            "SmallExplosionBlue, SmallExplosionGreen, SmallExplosionPink. Vacío = sin fogonazo.");

        CfgMuzzleFlashScale = Config.Bind(
            "Armas", "MuzzleFlashScale", 0.35f,
            new ConfigDescription("Tamaño del fogonazo. El pack está pensado para explosiones, " +
                "así que a escala 1 tapa la pantalla.",
                new AcceptableValueRange<float>(0.02f, 3f)));

        CfgMuzzleFlashLifetime = Config.Bind(
            "Armas", "MuzzleFlashLifetime", 2f,
            new ConfigDescription("Segundos antes de destruir la partícula.",
                new AcceptableValueRange<float>(0.2f, 10f)));

        CfgMuzzleOffset = Config.Bind(
            "Armas", "MuzzleOffset", Vector3.zero,
            "Ajuste fino de la boca del cañón, si el fogonazo no sale justo de la punta.");

        CfgBloodParticle = Config.Bind(
            "Armas", "BloodParticle", "BloodExplosion",
            "Partícula en el punto de impacto (Epic Toon FX, carpeta Blood/Red). Otras: " +
            "BloodExplosionRound, BloodExplosionSpiky, BloodSplatCritical, BloodSplatWide. " +
            "Vacío = sin sangre.");

        CfgBloodScale = Config.Bind(
            "Armas", "BloodScale", 0.4f,
            new ConfigDescription("Tamaño de la salpicadura.",
                new AcceptableValueRange<float>(0.02f, 3f)));

        CfgBloodLifetime = Config.Bind(
            "Armas", "BloodLifetime", 3f,
            new ConfigDescription("Segundos antes de destruir la salpicadura. Es lo que evita " +
                "que se acumulen GameObjects muertos durante la partida.",
                new AcceptableValueRange<float>(0.2f, 15f)));


        CfgKnockbackRadius = Config.Bind(
            "Armas", "KnockbackRadius", 3f,
            new ConfigDescription("Radio de reparto del impulso entre las partes del ragdoll. " +
                "Más pequeño = el empujón se concentra donde impactó la bala.",
                new AcceptableValueRange<float>(0.2f, 10f)));

        CfgKnockbackUp = Config.Bind(
            "Armas", "KnockbackUp", 0.4f,
            new ConfigDescription("Cuánto se mezcla hacia arriba el empujón. 0 = empuja recto " +
                "hacia atrás; más alto = lo levanta del suelo.",
                new AcceptableValueRange<float>(0f, 2f)));

        CfgWeaponAimAlign = Config.Bind(
            "Armas", "AimAlign", true,
            "Orienta el arma hacia donde miras, ignorando el vaivén de la animación de la " +
            "mano. Sin esto, mientras cargas el disparo la pistola apunta a un lado y la " +
            "mira sigue en el centro, y despista aunque el tiro salga bien.");

        CfgOrbSpeed = Config.Bind(
            "Armas", "OrbSpeed", 13f,
            new ConfigDescription("Velocidad del proyectil del blaster, en metros por " +
                "segundo. Bajo = se ve volar mejor; alto = responde más.",
                new AcceptableValueRange<float>(5f, 300f)));

        CfgOrbScale = Config.Bind(
            "Armas", "OrbScale", 1f,
            new ConfigDescription("Tamaño del proyectil del blaster.",
                new AcceptableValueRange<float>(0.1f, 6f)));

        CfgBlasterVolume = Config.Bind(
            "Armas", "BlasterVolume", 0.7f,
            new ConfigDescription("Volumen de los disparos y el impacto del blaster.",
                new AcceptableValueRange<float>(0f, 1f)));

        CfgOrbSoundNear = Config.Bind(
            "Armas", "OrbSoundNear", 3f,
            new ConfigDescription("Hasta esta distancia el zumbido del proyectil suena a " +
                "volumen completo, en metros.",
                new AcceptableValueRange<float>(0.5f, 30f)));

        CfgOrbSoundFar = Config.Bind(
            "Armas", "OrbSoundFar", 35f,
            new ConfigDescription("A partir de esta distancia ya no se oye, en metros.",
                new AcceptableValueRange<float>(5f, 200f)));

        CfgModCrates = Config.Bind(
            "Armas", "ModCrates", true,
            "Reparte cajas del mod por el mapa, del mismo modelo que la del aeropuerto. " +
            "Al abrirlas dan un arma. Existen para que las armas no ocupen huecos dentro " +
            "de las maletas normales, que son los que traen curas, comida y cuerdas.");

        CfgCratesPerLuggage = Config.Bind(
            "Armas", "CratesPerLuggage", 1f,
            new ConfigDescription("Cuántas cajas del mod por cada maleta del mapa. 1 = " +
                "tantas cajas como maletas.",
                new AcceptableValueRange<float>(0.05f, 3f)));

        CfgCrateSize = Config.Bind(
            "Armas", "CrateSize", 0.9f,
            new ConfigDescription("Lado mayor de la caja del mod, en metros. El modelo se " +
                "mide solo y se escala a esto.",
                new AcceptableValueRange<float>(0.2f, 4f)));

        CfgCrateScatter = Config.Bind(
            "Armas", "CrateScatter", 3f,
            new ConfigDescription("A cuántos metros de su maleta se pone cada caja.",
                new AcceptableValueRange<float>(0.5f, 20f)));

        CfgWeaponsInLuggage = Config.Bind(
            "Armas", "InLuggage", false,
            "Las armas del mod aparecen en las maletas normales de todos los biomas. Sin " +
            "esto solo salen de la caja de pruebas del aeropuerto.");

        CfgWeaponRarity = Config.Bind(
            "Armas", "LuggageRarity", "Epic",
            new ConfigDescription(
                "Cómo de raro es encontrar un arma en una maleta. Va emparejado con " +
                "LuggageBoost: al subir las maletas hay que bajar la rareza, o el mapa se " +
                "llena de armas del mod y no salen curas ni comida. Los pesos del juego " +
                "son Common 100, Uncommon 50, Rare 35, Epic 20, Legendary 15, Mythic 6, " +
                "RidiculouslyRare 3.",
                new AcceptableValueList<string>("Common", "Uncommon", "Rare", "Epic",
                                                "Legendary", "Mythic", "RidiculouslyRare")));

        CfgMagnetBeamLength = Config.Bind(
            "Armas", "MagnetBeamLength", 0f,
            new ConfigDescription("Hasta dónde llega el chorro del imán, en metros. CERO " +
                "significa 'lo mismo que el tirón' (el Range del arma), que es lo suyo: " +
                "así lo que ves dibujado es exactamente lo que arrastra.",
                new AcceptableValueRange<float>(0f, 200f)));

        CfgMagnetDiagnostics = Config.Bind(
            "Armas", "MagnetDiagnostics", true,
            "Escribe en el log dónde está el cono del imán, hacia dónde apunta y cuánto " +
            "llega, comparado con el jugador. Para afinar la orientación.");

        CfgTeams = Config.Bind(
            "Equipos", "Enabled", true,
            "Competición por equipos: se forman en el aeropuerto y puntúan en la montaña.");

        CfgTeamSpawnSeparate = Config.Bind(
            "Equipos", "SeparateSpawns", true,
            "Cada equipo aparece en un sitio distinto, repartidos en círculo alrededor del " +
            "punto de salida normal.");

        CfgTeamSpawnSpread = Config.Bind(
            "Equipos", "SpawnSpread", 90f,
            new ConfigDescription("Cuánto se separan los equipos entre sí, en metros. Es el " +
                "RADIO del corro, así que entre dos equipos opuestos hay el doble.",
                new AcceptableValueRange<float>(2f, 300f)));

        CfgCheckpointRespawn = Config.Bind(
            "Equipos", "CheckpointRespawn", true,
            "Al morir en la montaña reapareces con la vida llena y SIN items en la última " +
            "hoguera encendida, o en la salida de tu equipo si aún no hay ninguna.");

        CfgCheckpointRespawnDelay = Config.Bind(
            "Equipos", "CheckpointRespawnDelay", 4f,
            new ConfigDescription("Segundos en el suelo antes de reaparecer.",
                new AcceptableValueRange<float>(0f, 60f)));

        CfgAutoUpdate = Config.Bind(
            "General", "AutoUpdate", true,
            "Al arrancar, comprueba si hay una versión más nueva en GitHub y la deja " +
            "instalada para el siguiente arranque. Así todos jugáis con lo mismo sin " +
            "pasaros el zip.");

        CfgUpdateRepo = Config.Bind(
            "General", "UpdateRepo", "fcastro93/peak_mod",
            "Repositorio de GitHub del que bajar las actualizaciones. Vacío = no comprobar.");

        CfgTeamCampfire = Config.Bind(
            "Equipos", "TeamCampfire", true,
            "Para encender la hoguera basta con que esté TU equipo cerca, no la partida " +
            "entera. Sin esto, el equipo que llega primero se queda esperando al rival.");

        CfgUnstickLift = Config.Bind(
            "Equipos", "UnstickLift", 2.5f,
            new ConfigDescription("Cuánto te levanta el botón de 'estoy atascado', en metros.",
                new AcceptableValueRange<float>(0.5f, 20f)));

        CfgTeamBackpacks = Config.Bind(
            "Equipos", "TeamBackpacks", true,
            "Al empezar cada tramo, deja en el suelo una mochila por integrante junto a la " +
            "salida de cada equipo.");

        CfgTeamBackpackSpread = Config.Bind(
            "Equipos", "TeamBackpackSpread", 1.8f,
            new ConfigDescription("A cuántos metros de su dueño se deja cada mochila. " +
                "Antes era el radio de un corro propio de mochilas, que caía casi encima " +
                "del de los jugadores y aparecías con un bulto entre los pies.",
                new AcceptableValueRange<float>(0.5f, 15f)));

        // Por defecto FALSE ahora que las mochilas se reparten en la salida del equipo: si
        // además se equipara una a cada uno, un equipo de tres tendría seis.
        CfgBackpackForAll = Config.Bind(
            "Pruebas", "BackpackForAll", false,
            "Le da una mochila a cada jugador en el aeropuerto, para que todos lleven más " +
            "cosas encima sin tocar el sistema de inventario.");

        CfgAntiGravAmount = Config.Bind(
            "Armas", "AntiGravAmount", 3,
            new ConfigDescription("Intensidad de la ingravidez. 3 es lo que usa el orbe del " +
                "propio juego; con menos apenas se nota.",
                new AcceptableValueRange<int>(1, 10)));

        CfgMirrorEffect = Config.Bind(
            "Armas", "MirrorEffect", "ShieldSoftGreen",
            "Destello que sale en quien refleja un efecto con el espejo.");

        CfgMirrorSound = Config.Bind(
            "Armas", "MirrorSound", "chimes_magic_bell_ding_1",
            "Sonido del reflejo, una sola vez y desde quien lo devolvió.");

        CfgMirrorEffectTime = Config.Bind(
            "Armas", "MirrorEffectTime", 1f,
            new ConfigDescription("Segundos que dura el destello del reflejo.",
                new AcceptableValueRange<float>(0.2f, 10f)));

        CfgMirrorScale = Config.Bind(
            "Armas", "MirrorScale", 1f,
            new ConfigDescription("Tamaño del destello del reflejo.",
                new AcceptableValueRange<float>(0.1f, 5f)));

        CfgMirrorHud = Config.Bind(
            "Armas", "MirrorHud", true,
            "Dibuja un espejito sobre la barra de vida mientras llevas el escudo puesto. " +
            "Solo lo ves tú: el espejo no se anuncia a los demás hasta que refleja.");

        CfgMirrorHudX = Config.Bind(
            "Armas", "MirrorHudX", 30f,
            new ConfigDescription("Distancia del icono al borde izquierdo, en píxeles.",
                new AcceptableValueRange<float>(0f, 400f)));

        CfgMirrorHudY = Config.Bind(
            "Armas", "MirrorHudY", 110f,
            new ConfigDescription("Altura del icono sobre el borde inferior, en píxeles.",
                new AcceptableValueRange<float>(0f, 600f)));

        CfgMirrorHudSize = Config.Bind(
            "Armas", "MirrorHudSize", 96f,
            new ConfigDescription("Tamaño del icono, en píxeles.",
                new AcceptableValueRange<float>(16f, 160f)));

        CfgBackpackPages = Config.Bind(
            "Pruebas", "BackpackPages", 1,
            new ConfigDescription(
                "Páginas de la mochila, de 4 huecos cada una. EN 1 A PROPÓSITO: por encima " +
                "de eso el juego revienta. Su rueda indexa un array de 5 porciones con el " +
                "número de hueco, así que con 12 lanza una excepción POR FRAME, y su " +
                "RefreshVisuals solo prepara los 4 primeros, de modo que los demás se ven " +
                "pero no se pueden sacar. Súbelo solo si sabes lo que haces.",
                new AcceptableValueRange<int>(1, 8)));

        CfgBackpackPageKey = Config.Bind(
            "Pruebas", "BackpackPageKey", "Tab",
            "Tecla para pasar de página con la mochila abierta. También sirve la rueda del " +
            "ratón, si el juego no se la queda para el cinturón.");

        CfgMoreLoot = Config.Bind(
            "Equipos", "MoreLoot", true,
            "Multiplica las maletas del mapa según cuántos equipos jueguen. Con varios " +
            "equipos compitiendo por el mismo botín, la cantidad de fábrica se queda corta.");

        CfgLuggageBoost = Config.Bind(
            "Equipos", "LuggageBoost", 2.0f,
            new ConfigDescription("Cuántas maletas más hay en el mapa, pase lo que pase. " +
                "2.0 = el doble. Se multiplica encima del reparto por equipos. Si lo " +
                "subes, baja también LuggageRarity: si no, suben las armas del mod en la " +
                "misma proporción que todo lo demás.",
                new AcceptableValueRange<float>(1f, 5f)));

        CfgBackpackDebug = Config.Bind(
            "Mochila", "Debug", true,
            "Escribe en el log lo que pasa al guardar y sacar de la mochila. Sirve para " +
            "encontrar por qué un hueco no devuelve su item; se puede apagar cuando esté " +
            "resuelto.");

        CfgBuffFloatHeight = Config.Bind(
            "Buffs", "FloatHeight", 1.25f,
            new ConfigDescription("A qué altura del suelo flota la caja, en metros. 1.25 " +
                "es más o menos el pecho de un Scout.",
                new AcceptableValueRange<float>(0f, 4f)));

        CfgBuffSpinSpeed = Config.Bind(
            "Buffs", "SpinSpeed", 55f,
            new ConfigDescription("Grados por segundo que gira la caja sobre sí misma.",
                new AcceptableValueRange<float>(0f, 360f)));

        CfgBuffBob = Config.Bind(
            "Buffs", "Bob", 0.25f,
            new ConfigDescription("Cuánto sube y baja flotando. Cero la deja quieta.",
                new AcceptableValueRange<float>(0f, 2f)));

        CfgBuffHud = Config.Bind(
            "Buffs", "ShowHud", true,
            "Lista de power-ups activos encima de la barra de vida.");

        CfgBuffHudGap = Config.Bind(
            "Buffs", "HudGap", 12f,
            new ConfigDescription(
                "Separación entre la lista de power-ups y la barra de aguante, en píxeles. " +
                "La lista se coloca midiendo dónde ha quedado la barra de verdad, así que " +
                "se adapta sola a cada resolución; esto solo separa un poco más o menos.",
                new AcceptableValueRange<float>(0f, 200f)));

        CfgBuffSummarySeconds = Config.Bind(
            "Buffs", "SummarySeconds", 5f,
            new ConfigDescription("Segundos que se ve el resumen del efecto antes de que la " +
                "entrada se encoja a una línea.",
                new AcceptableValueRange<float>(0f, 20f)));

        CfgBuffInstantMessage = Config.Bind(
            "Buffs", "InstantMessage", 5f,
            new ConfigDescription("Segundos que se queda en pantalla un power-up sin " +
                "duración, como la curación total.",
                new AcceptableValueRange<float>(1f, 15f)));

        CfgBuffLaunchForce = Config.Bind(
            "Buffs", "LaunchForce", 1f,
            new ConfigDescription("Multiplica la fuerza de los impulsos. Cada nivel ya trae " +
                "la suya; esto sube o baja los tres a la vez sin borrar la diferencia.",
                new AcceptableValueRange<float>(0.2f, 4f)));

        CfgBuffPickupSound = Config.Bind(
            "Buffs", "PickupSound", "",
            "Clip del bundle que suena con el aviso al recoger. Vacío = sin sonido.");

        CfgFogNoRevive = Config.Bind(
            "Equipos", "FogNoRevive", true,
            "Cuando empieza a subir la niebla (o la lava, o la penumbra: por dentro son el " +
            "mismo sistema), quien muere se queda fantasma. Ni checkpoint ni estatuas. Es " +
            "lo que convierte la subida en una cuenta atrás de verdad.");

        CfgRandomMap = Config.Bind(
            "Equipos", "RandomMap", true,
            "Cada partida cae en un mapa distinto en vez de en el del día. Se elige " +
            "siempre de los que el juego ya trae, así que no hay terreno sin probar. El " +
            "anfitrión sortea el número y se lo reparte a todos; se ve en el F2.");

        CfgPullToCampfire = Config.Bind(
            "Equipos", "PullToCampfire", true,
            "Cuando un equipo enciende la hoguera y el mapa avanza, sube a la hoguera a " +
            "quien siguiera escalando por el tramo anterior. Sin esto se quedan en el " +
            "vacío, porque el juego descarga la zona que dejan atrás.");

        CfgPullDistance = Config.Bind(
            "Equipos", "PullDistance", 25f,
            new ConfigDescription(
                "A partir de cuántos metros de la hoguera se considera que te has quedado " +
                "atrás. Más cerca de eso no se te mueve: dar un tirón a quien ya está " +
                "donde debe es peor que no hacer nada.",
                new AcceptableValueRange<float>(5f, 200f)));

        CfgTeamMemberSpread = Config.Bind(
            "Equipos", "TeamMemberSpread", 3f,
            new ConfigDescription(
                "Metros entre dos compañeros del mismo equipo al aparecer o reaparecer. " +
                "Antes caían todos en el MISMO punto y los ragdolls se incrustaban unos " +
                "en otros: eso era lo que se sentía como que el juego se buguea al cargar.",
                new AcceptableValueRange<float>(1f, 12f)));

        CfgLootPerTeam = Config.Bind(
            "Equipos", "LootPerTeam", 3f,
            new ConfigDescription("Por cuánto se multiplican las maletas por cada equipo.",
                new AcceptableValueRange<float>(1f, 8f)));

        CfgMapBuffs = Config.Bind(
            "Equipos", "MapBuffs", true,
            "Reparte power-ups de velocidad por el mapa. La cantidad sale de las maletas " +
            "ya colocadas, así que escalan con los equipos y siempre son menos que ellas.");

        CfgBuffsPerLuggage = Config.Bind(
            "Equipos", "BuffsPerLuggage", 1.17f,
            new ConfigDescription("Power-ups por cada maleta del mapa. Por encima de 1 sale " +
                "más de uno por maleta.",
                new AcceptableValueRange<float>(0.05f, 4f)));

        CfgBuffScatter = Config.Bind(
            "Equipos", "BuffScatter", 4f,
            new ConfigDescription("A qué distancia de una maleta puede aparecer un power-up.",
                new AcceptableValueRange<float>(0.5f, 20f)));

        CfgTeamStatues = Config.Bind(
            "Equipos", "Statues", true,
            "Pone una estatua de reaparición por equipo en cada etapa, y cada una solo " +
            "revive a los suyos. Con esto apagado se queda la del juego, que revive a todos.");

        CfgStatueSpacing = Config.Bind(
            "Equipos", "StatueSpacing", 3f,
            new ConfigDescription("Separación entre las estatuas de cada equipo, en metros.",
                new AcceptableValueRange<float>(1f, 12f)));

        CfgTeamMenuKey = Config.Bind(
            "Equipos", "MenuKey", "F2",
            "Tecla que abre el panel de equipos y el marcador. F3 está ocupada por el " +
            "ajustador de armas.");

        CfgWeaponInHand = Config.Bind(
            "Armas", "InHand", true,
            "El arma va rígida dentro de la mano y se mueve solo con el brazo. Es lo más " +
            "natural, pero mientras cargas el disparo el cañón apunta a donde apunte la " +
            "mano (el tiro sigue saliendo de la mira). Ponlo en false para anclarla a la " +
            "vista, quieta como en un FPS, a costa de que se vea despegada del cuerpo.");

        CfgWeaponSwayDamping = Config.Bind(
            "Armas", "SwayDamping", 0.09f,
            new ConfigDescription("Segundos de suavizado del vaivén ya calculado. Es lo que " +
                "quita el temblor: la mano es un ragdoll y su posición viene con ruido de " +
                "física. Más alto = más suave y más perezoso.",
                new AcceptableValueRange<float>(0.005f, 0.5f)));

        CfgWeaponSway = Config.Bind(
            "Armas", "Sway", 0.55f,
            new ConfigDescription("Cuánto acompaña el arma al vaivén de la mano al correr y " +
                "saltar. 0 = clavada a la vista (se ve desconectada del cuerpo); 1 = sigue " +
                "el balanceo entero.",
                new AcceptableValueRange<float>(0f, 1.5f)));

        CfgWeaponSwayMax = Config.Bind(
            "Armas", "SwayMax", 0.09f,
            new ConfigDescription("Tope del vaivén, en metros. Es lo que evita que la " +
                "animación de carga del disparo levante el arma hasta taparte la pantalla.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        CfgWeaponSwaySmoothing = Config.Bind(
            "Armas", "SwaySmoothing", 0.30f,
            new ConfigDescription("Segundos que tarda el arma en dar por buena una posición " +
                "nueva de la mano. Bajo = sigue movimientos lentos; alto = solo el balanceo.",
                new AcceptableValueRange<float>(0.05f, 2f)));

        CfgBuffPickupEffect = Config.Bind(
            "Buffs", "PickupEffect", "PowerboxPickupColSpeed",
            "Prefab del destello que sale sobre la cabeza al recoger un power-up.");

        CfgBuffEffectLifetime = Config.Bind(
            "Buffs", "EffectLifetime", 2.5f,
            new ConfigDescription("Segundos antes de destruir ese destello. Los prefabs de " +
                "Epic Toon FX no se autodestruyen, así que sin esto cada recogida deja " +
                "basura colgada del esqueleto para el resto de la partida.",
                new AcceptableValueRange<float>(0.5f, 20f)));

        CfgBuffEffectScale = Config.Bind(
            "Buffs", "EffectScale", 1f,
            new ConfigDescription("Tamaño del destello de recogida.",
                new AcceptableValueRange<float>(0.1f, 5f)));

        CfgBuffEffectHeight = Config.Bind(
            "Buffs", "EffectHeight", 0.55f,
            new ConfigDescription("Altura del destello sobre la cabeza, en metros.",
                new AcceptableValueRange<float>(0f, 3f)));

        CfgBuffCastMultiplier = Config.Bind(
            "Buffs", "CastTimeMultiplier", 0.2f,
            new ConfigDescription("Multiplica el tiempo de uso del item base. Un power-up " +
                "debe usarse casi al instante.",
                new AcceptableValueRange<float>(0.02f, 3f)));

        CfgBuffDiagnostics = Config.Bind(
            "Buffs", "Diagnostics", true,
            "Escribe en el log el modificador y la velocidad real cada segundo mientras " +
            "dura un power-up. Sirve para comprobar que el efecto llega de verdad.");

        CfgKioskProfiler = Config.Bind(
            "Pruebas", "KioskProfiler", true,
            "Escribe en el log cuánto cuesta dibujar cada ventana del mod y cuántas veces " +
            "por frame se dibuja. Para diagnosticar tirones al abrir los kioscos.");

        CfgLobbyRespawn = Config.Bind(
            "Pruebas", "LobbyRespawn", true,
            "Si alguien cae en el aeropuerto, vuelve a levantarse en su punto de entrada. " +
            "Allí no hay hogueras ni estatuas, así que sin esto te quedas de fantasma.");

        CfgLobbyRespawnDelay = Config.Bind(
            "Pruebas", "LobbyRespawnDelay", 3f,
            new ConfigDescription("Segundos en el suelo antes de levantarse.",
                new AcceptableValueRange<float>(0f, 30f)));

        CfgLobbyPreventDeath = Config.Bind(
            "Pruebas", "LobbyPreventDeath", true,
            "Impide desmayarse en el aeropuerto: la vida se queda justo por encima de cero. " +
            "Allí no hay sistema de reaparición, así que al caer te vuelves fantasma y te " +
            "quedas así, sin hoguera ni nadie que te reviva.");

        CfgLobbyCombat = Config.Bind(
            "Pruebas", "LobbyCombat", true,
            "Permite recibir daño en el aeropuerto para poder probar las armas sin entrar " +
            "a una partida. El juego lo bloquea de serie (AddStatus corta si estás en el " +
            "aeropuerto), por eso allí no ves barra de vida.");

        CfgRegenDelay = Config.Bind(
            "Pruebas", "RegenDelay", 5f,
            new ConfigDescription("Segundos sin recibir daño antes de empezar a regenerar.",
                new AcceptableValueRange<float>(0f, 60f)));

        CfgRegenPerSecond = Config.Bind(
            "Pruebas", "RegenPerSecond", 0.15f,
            new ConfigDescription("Vida que se recupera por segundo. El KO llega a 1.0, " +
                "así que 0.15 tarda unos 7 s en curar del todo.",
                new AcceptableValueRange<float>(0.01f, 1f)));

        // Pistola: 3 balas que juntas hacen el daño que antes hacía una sola, carga
        // rapidísima, empujón fuerte y disparo flojito.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Pistola", "Pistola", "pistol_001",
            // Posición calibrada en vivo con F3. Va aquí, y no solo en el .cfg local,
            // porque el config no viaja en el zip: a cada amigo se le genera uno nuevo
            // a partir de estos valores.
            ammo: 3, damage: 0.5f / 3f, castMultiplier: 0.25f, shotVolume: 0.21f,
            knockback: 450f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f)));

        // Pistolón: la misma arma a lo bestia. Un solo tiro, media vida de golpe,
        // carga lenta y estruendo a todo volumen.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Pistolon", "Pistolon", "pistol_001",
            // El retroceso es 3x el empujón de la pistola: el pistolón te tumba a ti también.
            // Su Z es el doble que el de la Pistola: al ser 4 veces más grande hay que
            // alejarlo de la cara o se come media pantalla. También calibrado con F3.
            ammo: 1, damage: 0.5f, castMultiplier: 0.5f, shotVolume: 0.7f,
            knockback: 450f, recoil: 450f * 3f, length: 2.30f,
            offset: new Vector3(-0.018f, -0.439f, 0.811f)));

        // Arma de empuje: no hace daño, solo manda por los aires. El radio grande es el
        // que usa el juego en su trampa de flechas (400): reparte el impulso por todo el
        // cuerpo y lo lanza entero, en vez de doblarlo por donde le da.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Empujadora", "Empujadora", "Cosmic_Retro_Blaster_1",
            ammo: 2, damage: 0f, castMultiplier: 0.25f, shotVolume: 0.8f,
            knockback: 1600f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "weapon_fun_pea_shooter_03",
            hitEffect: "StarExplosionBlue",
            knockbackRadius: 400f,
            muteBase: true));

        // Antigravedad: no hace daño ni empuja, solo le quita el peso al objetivo.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Antigravedad", "Antigravedad", "Cosmic_Retro_Blaster_10",
            ammo: 2, damage: 0f, castMultiplier: 0.25f, shotVolume: 0.8f,
            knockback: 0f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "sci-fi_weapon_blaster_laser_boom_01",
            hitEffect: "", knockbackRadius: 0f, muteBase: true,
            floatSeconds: 5f, auraEffect: "AuraSoftBlue"));

        // Intercambio: te cambia el sitio con quien recibe el disparo. Una sola carga.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Intercambio", "Intercambio", "Cosmic_Retro_Blaster_3_4",
            ammo: 1, damage: 0f, castMultiplier: 0.25f, shotVolume: 0.8f,
            knockback: 0f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "sci-fi_weapon_blaster_laser_boom_01",
            hitEffect: "MagicBuffGreen", knockbackRadius: 0f, muteBase: true,
            floatSeconds: 0f, auraEffect: "", swapPositions: true));

        // Imán: cono que arrastra hacia ti mientras mantienes el botón. La munición es
        // durabilidad: cada punto da un segundo de chorro (FloatSeconds). El tiempo de
        // carga va al mínimo porque este no se apunta, se enciende.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Iman", "Iman", "Cosmic_Retro_Blaster_3_6",
            ammo: 8, damage: 0f, castMultiplier: 0f, shotVolume: 0.8f,
            knockback: 130f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "sci-fi_forcefield_hum_loop_01",
            // El alcance sube con el chorro: lo dibujado y lo que arrastra tienen que
            // coincidir, o estarías tirando de gente que el cono no toca (y al revés).
            hitEffect: "FlamethrowerToonyBlue", knockbackRadius: 0f, muteBase: true,
            floatSeconds: 1f, auraEffect: "", swapPositions: false, kind: "Iman"));

        // Portales: FloatSeconds son los segundos que quedan abiertos.
        // Dos cargas: una por portal. El primer disparo coloca el azul, el segundo el
        // dorado y arranca la cuenta atrás.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Portales", "Portales", "Cosmic_Retro_Blaster_3_4",
            ammo: 2, damage: 0f, castMultiplier: 0.25f, shotVolume: 0.8f,
            knockback: 0f, recoil: 0f, length: 0.60f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "fireball_blast_projectile_spell_06",
            hitEffect: "", knockbackRadius: 0f, muteBase: true,
            floatSeconds: 5f, auraEffect: "", swapPositions: false, kind: "Portales"));

        // Granada de estados. Reaprovecha dos campos con otro sentido, porque no dispara:
        // Range es el radio de la explosión y KnockbackRadius el tamaño de la partícula.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Granada", "Granada", "Cosmic_Retro_Grenades_Pack_2",
            ammo: 1, damage: 0f, castMultiplier: 1f, shotVolume: 0.9f,
            knockback: 0f, recoil: 0f, length: 0.35f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "explosion_large_04",
            hitEffect: "PoisonSkullExplosion", knockbackRadius: 4f, muteBase: true,
            floatSeconds: 0f, auraEffect: "", swapPositions: false, kind: "Granada"));

        // Varita de fuego, de un solo uso. Reaprovecha campos con otro sentido, como la
        // granada: FloatSeconds es la vida del orbe, Knockback su velocidad y
        // KnockbackRadius el radio en el que quema.
        Weapons.Add(WeaponDefinition.Create(
            Config, "VaritaFuego", "Varita de fuego", "wand02_red",
            ammo: 1, damage: 0f, castMultiplier: 0.25f, shotVolume: 0.8f,
            knockback: 5f, recoil: 0f, length: 0.70f,
            offset: new Vector3(-0.035f, -0.211f, 0.439f),
            shotSound: "fire_large_flames_magic_loop_01",
            hitEffect: "ToonRadialFireRed", knockbackRadius: 4f, muteBase: true,
            floatSeconds: 5f, auraEffect: "", swapPositions: false, kind: "Varita"));

        // Espejo: se consume al usarlo y deja un escudo que devuelve el siguiente efecto a
        // quien te lo lance. FloatSeconds es cuánto aguanta si no lo aprovecha nadie.
        Weapons.Add(WeaponDefinition.Create(
            Config, "Espejo", "Espejo", "HandMirror Variant",
            // Posición calibrada en vivo con F3. Va aquí, y no solo en el .cfg local,
            // porque el config no viaja en el zip: a cada amigo se le genera uno nuevo.
            ammo: 1, damage: 0f, castMultiplier: 0.25f, shotVolume: 0f,
            knockback: 0f, recoil: 0f, length: 0.61f,
            offset: new Vector3(-0.053f, -0.018f, 0.176f),
            shotSound: "", hitEffect: "", knockbackRadius: 0f, muteBase: true,
            floatSeconds: 60f, auraEffect: "", swapPositions: false, kind: "Espejo"));

        Blaster = ScoutDances.Weapons.BlasterDefinition.Create(Config);

        // --- Cajas de power-ups ------------------------------------------------------
        // Una caja POR CATEGORÍA, no por power-up. El bufo concreto se sortea al abrirla,
        // así que el jugador ve de qué familia es —y decide si desvía la ruta— pero no cuál
        // le va a tocar. Con dieciocho power-ups, una caja por cada uno habría llenado el
        // mapa de modelos distintos y el database de items.
        //
        // El azul es 'PowerboxColSpeed 1': los tres de velocidad no dicen el color en el
        // nombre, y se comprobó en el prefab que el base es verde (g:1), el " 1" azul
        // (b:1) y el " 2" morado.
        BuffList.Add(Buffs.BuffDefinition.Create(
            Config, "Movilidad", "Caja de movilidad", "PowerboxColSpeed 1",
            Buffs.BuffCategory.Movilidad, length: 0.45f));

        BuffList.Add(Buffs.BuffDefinition.Create(
            Config, "Escalada", "Caja de escalada", "PowerboxColLightning",
            Buffs.BuffCategory.Escalada, length: 0.45f));

        BuffList.Add(Buffs.BuffDefinition.Create(
            Config, "Supervivencia", "Caja de supervivencia", "PowerboxColHealth",
            Buffs.BuffCategory.Supervivencia, length: 0.45f));

        BuffList.Add(Buffs.BuffDefinition.Create(
            Config, "Especial", "Caja especial", "PowerboxColStar",
            Buffs.BuffCategory.Especial, length: 0.45f));

        SoundSlots.Init(Config);
        InstantAudioCache.Init(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        if (!LoadAndRegister())
            return;

        // Se parchea el ENSAMBLADO ENTERO, no clase por clase.
        //
        // Antes iban listadas a mano, y cada parche nuevo había que acordarse de añadirlo
        // aquí. Se acumularon OCHO sin registrar —la puntuación por hogueras, las estatuas
        // por equipo, el multiplicador de maletas y las páginas de la mochila— y el fallo
        // no daba ninguna señal: sin error, sin aviso, simplemente no ocurría nada. Con
        // PatchAll() basta con marcar la clase con [HarmonyPatch] para que entre.
        _harmony.PatchAll();

        int patched = _harmony.GetPatchedMethods().Count();
        Log.LogInfo($"Harmony: {patched} métodos parcheados.");

        Definition = PEAKLib.Core.ModDefinition.GetOrCreate(Info.Metadata);
        WeaponFactory.BuildAll(this, Definition);
        Buffs.BuffFactory.BuildAll(this, Definition);
        ScoutDances.Weapons.BlasterFactory.Build(this, Definition);

        var tunerObject = new GameObject("ScoutDancesWeaponTuner");
        DontDestroyOnLoad(tunerObject);
        tunerObject.hideFlags = HideFlags.HideAndDontSave;
        tunerObject.AddComponent<WeaponTuner>();
        tunerObject.AddComponent<LobbyHealth>();
        tunerObject.AddComponent<MirrorHud>();
        tunerObject.AddComponent<Updater>();
        tunerObject.AddComponent<Teams.TeamState>();
        tunerObject.AddComponent<Teams.TeamMenu>();

        Teams.TeamStatues.Mod = Definition;
        tunerObject.AddComponent<Teams.TeamStatues>();

        Props.ModCrateSpawner.Mod = Definition;
        tunerObject.AddComponent<Props.ModCrateSpawner>();

        tunerObject.AddComponent<Teams.MapSeed>();
        tunerObject.AddComponent<Buffs.BuffHud>();
        tunerObject.AddComponent<Buffs.Storm>();
        tunerObject.AddComponent<Teams.FogRules>();
        tunerObject.AddComponent<Teams.MapSpawns>();
        tunerObject.AddComponent<Teams.TeamSpawns>();
        tunerObject.AddComponent<Teams.TeamSupplies>();
        tunerObject.AddComponent<Props.BackpackForAll>();

        // Y DESPUÉS de enlazarlo todo: los ajustes técnicos que hayan cambiado de valor
        // por defecto se ponen al día solos. Los sonidos del kiosco y las posiciones que
        // hayas calibrado con F3 no se tocan, así que ya no hace falta borrar el fichero
        // a mano al actualizar el mod.
        ConfigMigration.Apply(Config);

        SoundKiosk.Hook();
        ItemTestBox.Hook();
        StartCoroutine(NetworkSyncLoop());

        Log.LogInfo($"Cámara en tercera persona durante emotes: {CfgThirdPerson.Value}");
        Log.LogInfo($"{Name} v{Version} cargado con {Registered.Count} bailes " +
                    $"y {SoundEmotes.Emotes.Count} sonidos.");
    }

    /// <summary>
    /// Mantiene publicados nuestros IDs y descarga por adelantado los de los demás.
    /// </summary>
    /// <remarks>
    /// Sondeamos en vez de implementar IInRoomCallbacks porque hay que cubrir varios
    /// eventos (entrar a la sala, que alguien entre después, que alguien cambie sus
    /// sonidos) y una comprobación por segundo sale mucho más barata que mantener
    /// tres callbacks sincronizados.
    /// </remarks>
    IEnumerator NetworkSyncLoop()
    {
        bool wasInRoom = false;

        while (true)
        {
            yield return new WaitForSeconds(1f);

            bool inRoom = PhotonNetwork.InRoom;

            if (inRoom && !wasInRoom)
            {
                SoundSlots.PushToNetwork();
            }
            wasInRoom = inRoom;

            // Prefetch: si esperamos a que alguien use el emote, la primera vez no suena.
            var paths = new List<string>();
            if (inRoom)
            {
                foreach (var player in PhotonNetwork.PlayerList)
                    for (int slot = 0; slot < SoundSlots.Count; slot++)
                        paths.Add(SoundSlots.GetPathFor(player, slot));
            }
            else
            {
                paths.AddRange(SoundSlots.GetLocalPaths());
            }

            foreach (var path in paths)
                if (path.Length > 0) InstantAudioCache.Request(path);
        }
    }

    void OnDestroy()
    {
        EmoteCamera.Reset();
        SoundKiosk.Unhook();
        ItemTestBox.Unhook();
        RenderingTweaks.Restore();
        _harmony.UnpatchSelf();
        Log.LogInfo($"{Name} descargado.");
    }

    bool LoadAndRegister()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var bundlePath = Path.Combine(dir, BundleFileName);

        if (!File.Exists(bundlePath))
        {
            Log.LogError($"No encuentro el AssetBundle en '{bundlePath}'. " +
                         "Construye el bundle desde Unity (PEAK Emotes → 3) y cópialo junto a este .dll.");
            return false;
        }

        AssetBundle bundle;
        try
        {
            bundle = AssetBundle.LoadFromFile(bundlePath);
        }
        catch (Exception e)
        {
            Log.LogError($"Fallo al abrir el AssetBundle: {e}");
            return false;
        }

        if (bundle == null)
        {
            Log.LogError("AssetBundle.LoadFromFile devolvió null. ¿Lo construiste para StandaloneWindows64?");
            return false;
        }

        ItemBoxPrefab = bundle.LoadAllAssets<GameObject>()
            .FirstOrDefault(g => g.name.Equals(CfgItemBoxModel.Value, StringComparison.OrdinalIgnoreCase));
        if (ItemBoxPrefab != null) Log.LogInfo($"Modelo de la caja de items: '{ItemBoxPrefab.name}'.");

        foreach (var prefab in bundle.LoadAllAssets<GameObject>())
            Prefabs[prefab.name] = prefab;
        Log.LogInfo($"{Prefabs.Count} prefabs disponibles en el bundle.");

        foreach (var clip in bundle.LoadAllAssets<AudioClip>())
            Clips[clip.name] = clip;
        if (Clips.Count > 0)
            Log.LogInfo($"{Clips.Count} sonidos en el bundle: {string.Join(", ", Clips.Keys)}");

        BloodPrefab = bundle.LoadAllAssets<GameObject>()
            .FirstOrDefault(g => g.name.Equals(CfgBloodParticle.Value, StringComparison.OrdinalIgnoreCase));
        if (BloodPrefab != null) Log.LogInfo($"Partícula de impacto: '{BloodPrefab.name}'.");
        else if (!string.IsNullOrWhiteSpace(CfgBloodParticle.Value))
            Log.LogWarning($"El bundle no trae la partícula '{CfgBloodParticle.Value}'.");

        MuzzleFlashPrefab = bundle.LoadAllAssets<GameObject>()
            .FirstOrDefault(g => g.name.Equals(CfgMuzzleFlash.Value, StringComparison.OrdinalIgnoreCase));
        if (MuzzleFlashPrefab != null) Log.LogInfo($"Fogonazo: '{MuzzleFlashPrefab.name}'.");
        else if (!string.IsNullOrWhiteSpace(CfgMuzzleFlash.Value))
            Log.LogWarning($"El bundle no trae la partícula '{CfgMuzzleFlash.Value}'.");


        KioskPrefab = bundle.LoadAllAssets<GameObject>()
            .FirstOrDefault(g => g.name.Equals(CfgKioskModel.Value, StringComparison.OrdinalIgnoreCase));
        if (KioskPrefab != null)
            Log.LogInfo($"Modelo del kiosco: '{KioskPrefab.name}'.");
        else
            Log.LogWarning($"El bundle no trae ningún prefab llamado '{CfgKioskModel.Value}'. " +
                           "Disponibles: " +
                           string.Join(", ", bundle.LoadAllAssets<GameObject>().Select(g => g.name)));

        var clips = bundle.LoadAllAssets<AnimationClip>();
        if (clips.Length == 0)
        {
            Log.LogError("El bundle no contiene ningún AnimationClip.");
            return false;
        }

        // Los iconos son opcionales: si el bundle trae una Texture2D con el mismo
        // nombre que el clip, la usamos; si no, generamos una de color.
        var icons = bundle.LoadAllAssets<Texture2D>()
            .ToDictionary(t => t.name, t => t, StringComparer.OrdinalIgnoreCase);

        // El idle no es un baile: es la animación sobre la que suenan los emotes de
        // sonido, para que el Scout se quede de pie de forma natural mientras suena.
        var idleClip = clips.FirstOrDefault(c => c.name.StartsWith("Idle", StringComparison.OrdinalIgnoreCase));
        var danceClips = clips.Where(c => c != idleClip).ToArray();

        foreach (var clip in Order(danceClips))
        {
            var emote = new Emote(
                EmotePrefix + clip.name,
                clip,
                icons.TryGetValue(clip.name, out var tex) ? tex : IconFor(Registered.Count),
                // OneShot => PEAKEmoteLib deja correr el clip entero en vez de
                // cortarlo a los 2 s como hace el juego con los emotes vanilla.
                type: Emote.EmoteType.OneShot,
                // Los bailes mueven los pies; el IK de suelo del Scout pelea con ellos.
                disableIK: true);

            var pretty = Prettify(clip.name);
            emote.AddLocalization(pretty, LocalizedText.Language.English);
            emote.AddLocalization(pretty, LocalizedText.Language.SpanishSpain);
            emote.AddLocalization(pretty, LocalizedText.Language.SpanishLatam);

            this.RegisterEmote(emote);
            Registered.Add(emote);
            Log.LogInfo($"  {Registered.Count,2}. {clip.name} ({clip.length:0.0}s)");
        }

        if (idleClip != null)
            SoundEmotes.Register(this, idleClip, EmotePrefix);
        else
            Log.LogWarning("No hay clip 'Idle*' en el bundle: los emotes de sonido no se registran. " +
                           "Vuelve a hornear incluyendo la carpeta de Idles.");

        return Registered.Count > 0;
    }

    /// Aplica el orden de CfgWheelOrder; lo no mencionado va detrás, alfabético.
    static IEnumerable<AnimationClip> Order(AnimationClip[] clips)
    {
        var byName = clips.ToDictionary(c => c.name, c => c, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in CfgWheelOrder.Value.Split(','))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            if (byName.TryGetValue(name, out var c) && seen.Add(name))
                yield return c;
            else if (!byName.ContainsKey(name))
                Log.LogWarning($"WheelOrder menciona '{name}' pero no está en el bundle.");
        }

        foreach (var c in clips.Where(c => !seen.Contains(c.name)).OrderBy(c => c.name, StringComparer.Ordinal))
            yield return c;
    }

    /// "Dance01_M" -> "Dance 01 M". El baker ya normaliza los nombres del pack
    /// ("HumanM@Dance01 - Loop"), así que aquí solo hace falta darles formato.
    static string Prettify(string clipName)
    {
        var s = clipName.Replace('_', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-zA-Z])(?=[0-9])", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// Icono de reserva: un disco de color distinto por slot, para que la rueda
    /// no muestre huecos en blanco cuando el bundle no trae PNGs.
    static Texture2D IconFor(int index)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var color = Color.HSVToRGB((index * 0.137f) % 1f, 0.55f, 0.95f);
        float r = size * 0.38f, c = size / 2f;

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01((r - d) / 2f);   // borde suavizado
            pixels[y * size + x] = new Color(color.r, color.g, color.b, a);
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.name = $"ScoutDancesIcon{index}";
        return tex;
    }
}
