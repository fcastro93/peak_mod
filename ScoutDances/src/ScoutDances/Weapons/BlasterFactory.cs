using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using PEAKLib.Core;
using PEAKLib.Items;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>Ajustes del blaster que agranda y encoge.</summary>
internal class BlasterDefinition : IWeaponPlacement
{
    internal string Id => "Blaster";
    string IWeaponPlacement.Id => Id;
    ConfigEntry<string> IWeaponPlacement.DisplayName => DisplayName;
    ConfigEntry<float> IWeaponPlacement.Length => Length;
    ConfigEntry<Vector3> IWeaponPlacement.Offset => Offset;
    ConfigEntry<Vector3> IWeaponPlacement.Rotation => Rotation;

    internal ConfigEntry<string> DisplayName = null!;
    internal ConfigEntry<string> Model = null!;
    internal ConfigEntry<string> BaseItem = null!;
    internal ConfigEntry<int> Ammo = null!;
    internal ConfigEntry<float> Duration = null!;

    internal ConfigEntry<float> GrowScale = null!;
    internal ConfigEntry<float> GrowSpeed = null!;

    internal ConfigEntry<float> ShrinkScale = null!;
    internal ConfigEntry<float> ShrinkSpeed = null!;
    internal ConfigEntry<float> ShrinkStamina = null!;

    internal ConfigEntry<string> MuzzleEffect = null!;
    internal ConfigEntry<string> ImpactEffect = null!;

    internal ConfigEntry<string> GrowSound = null!;
    internal ConfigEntry<string> ShrinkSound = null!;
    internal ConfigEntry<string> HitSound = null!;
    internal ConfigEntry<string> FlightSound = null!;

    internal ConfigEntry<string> GrowOrb = null!;
    internal ConfigEntry<string> ShrinkOrb = null!;

    internal ConfigEntry<float> Length = null!;
    internal ConfigEntry<Vector3> Offset = null!;
    internal ConfigEntry<Vector3> Rotation = null!;

    internal static BlasterDefinition Create(ConfigFile config)
    {
        const string section = "Arma.Blaster";
        return new BlasterDefinition
        {
            DisplayName = config.Bind(section, "Name", "Blaster",
                "Nombre visible. OJO: el itemID sale de un hash de este nombre, así que si " +
                "lo cambias TODOS los jugadores tienen que cambiarlo igual."),

            Model = config.Bind(section, "Model", "Cosmic_Retro_Blaster_11",
                "Prefab del bundle."),

            BaseItem = config.Bind(section, "BaseItem", "Bugle_Scoutmaster",
                "Item del juego que se clona; de él solo se aprovecha la pose de agarre."),

            Ammo = config.Bind(section, "Ammo", 1,
                new ConfigDescription("Cargas. Al gastarse, el arma desaparece. La cuenta " +
                    "es compartida entre los dos clics.",
                    new AcceptableValueRange<int>(1, 100))),

            Duration = config.Bind(section, "Duration", 15f,
                new ConfigDescription("Segundos que dura el efecto sobre el objetivo.",
                    new AcceptableValueRange<float>(1f, 120f))),

            GrowScale = config.Bind(section, "GrowScale", 2f,
                new ConfigDescription("Tamaño al que deja al objetivo el clic DERECHO.",
                    new AcceptableValueRange<float>(1f, 5f))),

            GrowSpeed = config.Bind(section, "GrowSpeed", 2f,
                new ConfigDescription("Velocidad del objetivo agrandado (2 = el doble).",
                    new AcceptableValueRange<float>(0.2f, 6f))),

            ShrinkScale = config.Bind(section, "ShrinkScale", 1f / 3f,
                new ConfigDescription("Tamaño al que deja al objetivo el clic IZQUIERDO.",
                    new AcceptableValueRange<float>(0.15f, 1f))),

            ShrinkSpeed = config.Bind(section, "ShrinkSpeed", 1.3f,
                new ConfigDescription("Velocidad del objetivo encogido (1.3 = un 30% más).",
                    new AcceptableValueRange<float>(0.2f, 6f))),

            ShrinkStamina = config.Bind(section, "ShrinkStamina", 0.5f,
                new ConfigDescription("Gasto de estamina al correr estando encogido " +
                    "(0.5 = la mitad).",
                    new AcceptableValueRange<float>(0.1f, 2f))),

            MuzzleEffect = config.Bind(section, "MuzzleEffect", "",
                "Efecto al disparar, en la boca del cañón. Vacío = ninguno. La bola ya se " +
                "ve salir sola, así que el humo del fogonazo solo la tapaba."),

            ImpactEffect = config.Bind(section, "ImpactEffect", "",
                "Efecto al impactar. Vacío = ninguno. Aquí iba el destello de los power-ups " +
                "de velocidad, que no pinta nada en un disparo que encoge o agranda."),

            ShrinkSound = config.Bind(section, "ShrinkSound", "weapon_fun_small_zapper_03",
                "Sonido del clic IZQUIERDO. Vacío = sin sonido."),

            GrowSound = config.Bind(section, "GrowSound", "weapon_fun_pea_shooter_04",
                "Sonido del clic DERECHO."),

            HitSound = config.Bind(section, "HitSound", "taser_stun_gun_zap_electricity_01",
                "Sonido al impactar la bola, justo antes de desaparecer."),

            FlightSound = config.Bind(section, "FlightSound", "electric_sparks_lightning_loop1",
                "Zumbido en bucle que arrastra la bola mientras vuela. Vacío = sin sonido."),

            GrowOrb = config.Bind(section, "GrowOrb", "LightningOrbSoftBlue",
                "Proyectil del clic DERECHO. Disponibles: blaster_projectile, " +
                "LightningOrbSoftBlue, LightningOrbSoftGreen, LightningOrbSoftPink, " +
                "LightningOrbSoftYellow."),

            ShrinkOrb = config.Bind(section, "ShrinkOrb", "LightningOrbSoftBlue",
                "Proyectil del clic IZQUIERDO."),

            Length = config.Bind(section, "ModelLength", 0.60f,
                new ConfigDescription("Tamaño del arma en metros. El modelo se mide solo.",
                    new AcceptableValueRange<float>(0.05f, 6f))),

            // Calibrado en vivo con F3. Va aquí, y no solo en el .cfg local, porque el
            // config no viaja en el zip: a cada amigo se le genera uno nuevo con esto.
            Offset = config.Bind(section, "ModelOffset", new Vector3(-0.035f, -0.140f, 0.329f),
                "Posición del arma respecto a la vista. Ajústalo en vivo con F3."),

            Rotation = config.Bind(section, "ModelRotation", Vector3.zero,
                "Rotación extra del modelo, en grados."),
        };
    }
}

/// <summary>
/// Construye el blaster: mismo clonado que las demás armas, pero con dos acciones.
/// </summary>
internal static class BlasterFactory
{
    internal static void Build(Plugin plugin, ModDefinition mod)
    {
        plugin.StartCoroutine(BuildWhenReady(mod));
    }

    static IEnumerator BuildWhenReady(ModDefinition mod)
    {
        float waited = 0f;
        while (CountItems() == 0 && waited < 60f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        try { Create(mod); }
        catch (System.Exception e) { Plugin.Log.LogError($"Fallo creando el blaster: {e}"); }
    }

    static int CountItems()
    {
        try { return Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects.Count; }
        catch { return 0; }
    }

    static void Create(ModDefinition mod)
    {
        var definition = Plugin.Blaster;
        if (definition == null) return;

        var items = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects;
        var source = items.FirstOrDefault(
            i => i != null && i.name.IndexOf(definition.BaseItem.Value,
                                             System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (source == null)
        {
            Plugin.Log.LogError("Sin item base para el blaster.");
            return;
        }

        var clone = Object.Instantiate(source.gameObject);
        clone.SetActive(false);
        Object.DontDestroyOnLoad(clone);
        clone.name = definition.DisplayName.Value;

        var item = clone.GetComponent<Item>();
        if (item == null) { Object.Destroy(clone); return; }

        if (item.UIData != null)
        {
            RegisterName(definition.DisplayName.Value);
            item.UIData.itemName = definition.DisplayName.Value;
            item.UIData.mainInteractPrompt = "Encoger";

            // Sin esto el clic derecho NO llega nunca. Item.CanUseSecondary() exige
            // UIData.hasSecondInteract, y la corneta que clonamos no lo trae; el juego ni
            // siquiera llegaba a llamar a StartUseSecondary(), así que nuestro segundo
            // BlasterAction estaba suscrito a un evento que nadie disparaba.
            item.UIData.hasSecondInteract = true;
            item.UIData.hideSecondInteract = false;
            item.UIData.secondaryInteractPrompt = "Agrandar";

            // Y que no exija apuntar a un compañero: con canUseOnFriend el secundario se
            // condiciona a tener un objetivo válido delante.
            item.canUseOnFriend = false;
            item.mustUseOnFriend = false;
        }

        SwapModel(clone, Plugin.FindPrefab(definition.Model.Value), definition);
        SwapActions(clone, definition);

        clone.AddComponent<WeaponTag>().DefinitionId = "Blaster";
        WeaponLoot.Apply(clone);

        new ItemContent(item).Register(mod);

        Plugin.Log.LogInfo(
            $"Blaster '{clone.name}' registrado (itemID {item.itemID}): " +
            $"izq encoge a x{definition.ShrinkScale.Value:0.00} " +
            $"(velocidad x{definition.ShrinkSpeed.Value:0.##}, " +
            $"estamina x{definition.ShrinkStamina.Value:0.##}), " +
            $"der agranda a x{definition.GrowScale.Value:0.00} " +
            $"(velocidad x{definition.GrowSpeed.Value:0.##}), " +
            $"{definition.Duration.Value:0.#}s, {definition.Ammo.Value} carga(s).");
    }

    static void RegisterName(string displayName)
    {
        var key = "NAME_" + displayName.ToUpperInvariant();
        try
        {
            var translation = PEAKLib.UI.MenuAPI.CreateLocalization(key);
            foreach (var language in new[]
                     {
                         LocalizedText.Language.English,
                         LocalizedText.Language.SpanishSpain,
                         LocalizedText.Language.SpanishLatam,
                     })
            {
                translation.AddLocalization(displayName, language);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude registrar la localización '{key}': {e.Message}");
        }
    }

    static void SwapModel(GameObject clone, GameObject? model, BlasterDefinition definition)
    {
        if (model == null)
        {
            Plugin.Log.LogWarning($"Sin modelo '{definition.Model.Value}' en el bundle: " +
                                  "el blaster se queda con el aspecto del item base.");
            return;
        }

        // Apagar, no destruir: el Item guarda referencias a sus renderers y destruirlos
        // deja nulos que revientan al recoger o soltar el objeto.
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<ParticleSystem>() != null) continue;
            renderer.enabled = false;
        }

        var instance = Object.Instantiate(model, clone.transform);
        instance.name = "WeaponModel";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        WeaponFactory.PlaceModel(instance, definition.Offset.Value,
                                 definition.Rotation.Value, definition.Length.Value);

        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            Object.Destroy(collider);

        Props.PropBuilder.RebindShaders(instance);
    }

    static void SwapActions(GameObject clone, BlasterDefinition definition)
    {
        foreach (var inherited in clone.GetComponentsInChildren<ItemActionBase>(true))
            Object.DestroyImmediate(inherited);

        // Clic IZQUIERDO: encoge. Un tercio de alto, algo más rápido y gastando la mitad
        // de estamina al esprintar.
        var shrink = clone.AddComponent<BlasterAction>();
        shrink.OnPressed = true;
        shrink.targetScale = definition.ShrinkScale.Value;
        shrink.targetSpeed = definition.ShrinkSpeed.Value;
        shrink.targetStamina = definition.ShrinkStamina.Value;
        shrink.duration = definition.Duration.Value;
        shrink.orbPrefab = definition.ShrinkOrb.Value;
        shrink.shotSound = definition.ShrinkSound.Value;
        shrink.impactSound = definition.HitSound.Value;
        shrink.flightSound = definition.FlightSound.Value;

        // Clic DERECHO: agranda. El doble de alto y el doble de rápido.
        //
        // Va en un componente aparte porque ItemAction encamina TODOS sus eventos al mismo
        // RunAction(): dentro de un solo componente no hay forma de saber qué botón lo
        // disparó. Con dos, cada uno se suscribe a lo suyo.
        var grow = clone.AddComponent<BlasterAction>();
        grow.OnSecondaryPressed = true;
        grow.targetScale = definition.GrowScale.Value;
        grow.targetSpeed = definition.GrowSpeed.Value;
        grow.targetStamina = 1f;
        grow.duration = definition.Duration.Value;
        grow.orbPrefab = definition.GrowOrb.Value;
        grow.shotSound = definition.GrowSound.Value;
        grow.impactSound = definition.HitSound.Value;
        grow.flightSound = definition.FlightSound.Value;

        // Una sola cuenta de munición para los dos: el arma tiene UNA carga, la gastes
        // como la gastes.
        var ammo = clone.AddComponent<PistolAmmo>();
        ammo.MaxAmmo = definition.Ammo.Value;

        clone.AddComponent<WeaponAim>();
    }
}
