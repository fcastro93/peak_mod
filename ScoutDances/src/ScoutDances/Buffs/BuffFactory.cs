using System.Collections;
using System.Linq;
using PEAKLib.Core;
using PEAKLib.Items;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Construye los power-ups clonando un item vanilla en runtime y los registra con PEAKLib.
/// </summary>
/// <remarks>
/// Mismo truco que <see cref="Weapons.WeaponFactory"/>: no hace falta montar un prefab en
/// Unity porque <c>ItemContent.Register()</c> acepta cualquier GameObject y deriva el
/// <c>itemID</c> de <c>MD5(modId + nombre)</c>, determinista en todos los clientes.
///
/// La diferencia con las armas es que aquí el modelo se respeta ENTERO: las cajas de
/// Epic Toon FX traen dentro sus partículas (GlowCircle, Tinysparkles) y un Animator que
/// las hace girar, y eso es justo lo que las hace parecer un power-up. Solo las escalamos
/// y las colocamos.
/// </remarks>
internal static class BuffFactory
{
    internal static void BuildAll(Plugin plugin, ModDefinition mod)
    {
        plugin.StartCoroutine(BuildWhenReady(mod));
    }

    static IEnumerator BuildWhenReady(ModDefinition mod)
    {
        // Por SEGUNDOS y no por frames: durante las pantallas de carga los frames duran
        // muchísimo y un contador de frames se agota antes de que el juego cargue.
        float waited = 0f;
        while (CountItems() == 0 && waited < 60f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        foreach (var definition in Plugin.BuffList)
        {
            try
            {
                Create(mod, definition);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Fallo creando el buff '{definition.Id}': {e}");
            }
        }
    }

    static int CountItems()
    {
        try { return Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects.Count; }
        catch { return 0; }
    }

    static Item? FindBaseItem(string wanted)
    {
        try
        {
            var items = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects;
            return items.FirstOrDefault(
                i => i != null &&
                     i.name.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"No pude consultar el ItemDatabase: {e.Message}");
            return null;
        }
    }

    static void Create(ModDefinition mod, BuffDefinition definition)
    {
        var source = FindBaseItem(definition.BaseItem.Value);
        if (source == null)
        {
            Plugin.Log.LogError($"Sin item base para el buff '{definition.Id}'; me lo salto.");
            return;
        }

        // Clon inactivo y persistente: hace de prefab sin ser un asset de Unity.
        var clone = Object.Instantiate(source.gameObject);
        clone.SetActive(false);
        Object.DontDestroyOnLoad(clone);
        clone.name = definition.DisplayName.Value;

        var item = clone.GetComponent<Item>();
        if (item == null)
        {
            Plugin.Log.LogError("El clon no tiene componente Item.");
            Object.Destroy(clone);
            return;
        }

        if (item.UIData != null)
        {
            RegisterName(definition.DisplayName.Value);
            item.UIData.itemName = definition.DisplayName.Value;
            item.UIData.mainInteractPrompt = "Usar";
        }

        // Usarlo tiene que ser casi instantáneo: es un power-up, no un arma que se carga.
        item.usingTimePrimary *= Plugin.CfgBuffCastMultiplier.Value;

        SwapModel(clone, Plugin.FindPrefab(definition.Model.Value), definition);
        SwapAction(clone, definition);

        clone.AddComponent<BuffTag>().DefinitionId = definition.Id;
        clone.AddComponent<BuffFloat>();
        KeepOutOfLuggage(clone);

        new ItemContent(item).Register(mod);

        int inside = BuffCatalog.All.Count(b => b.Category == definition.Category);

        Plugin.Log.LogInfo(
            $"Caja '{clone.name}' registrada (itemID {item.itemID}): sortea entre " +
            $"{inside} power-up(s) de {definition.Category}, radio de recogida " +
            $"{definition.PickupRadius.Value:0.0} m.");
    }

    /// <summary>
    /// Impide que el power-up salga dentro de las maletas del juego.
    /// </summary>
    /// <remarks>
    /// La tabla de botín se construye recorriendo el ItemDatabase y leyendo el
    /// <c>LootData</c> de cada item: su rareza y un campo de banderas con las maletas donde
    /// puede aparecer. Como clonamos un item vanilla, el nuestro venía arrastrando el suyo
    /// —rareza mítica, maletas malditas y ataúdes— sin que nadie lo pidiera.
    ///
    /// Se deja el componente pero con las banderas a <c>None</c>, en vez de destruirlo: es
    /// suficiente para que la tabla lo descarte (ninguna bandera casa) y no nos arriesgamos
    /// a dejar una referencia nula si algún sistema del juego lee la rareza por su cuenta.
    ///
    /// Los power-ups aparecen por el mapa a la manera de las maletas, repartidos por
    /// <see cref="Teams.MapSpawns"/>, no dentro de ellas.
    /// </remarks>
    static void KeepOutOfLuggage(GameObject clone)
    {
        var loot = clone.GetComponent<LootData>();
        if (loot == null) return;

        loot.spawnLocations = SpawnPool.None;
        loot.rarityOverrides?.Clear();
    }

    /// <summary>Da de alta el nombre en el sistema de localización del juego.</summary>
    /// <remarks>
    /// PEAK no muestra <c>itemName</c> tal cual: lo usa como clave y compone
    /// <c>"NAME_" + nombre.ToUpper()</c>. Sin registrarla se lee literalmente
    /// <c>LOC: NAME_TURBO</c> en pantalla.
    /// </remarks>
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

    static void SwapModel(GameObject clone, GameObject? model, BuffDefinition definition)
    {
        if (model == null)
        {
            Plugin.Log.LogWarning($"Sin modelo '{definition.Model.Value}' en el bundle: " +
                                  "el buff se queda con el aspecto del item base.");
            return;
        }

        // Apagamos los renderers originales en vez de destruirlos: el Item guarda
        // referencias a ellos (mainRenderer, addtlRenderers) y destruirlos deja nulos que
        // revientan al recoger o soltar el objeto.
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<ParticleSystem>() != null) continue;
            renderer.enabled = false;
        }

        var instance = Object.Instantiate(model, clone.transform);
        instance.name = "BuffModel";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // Centrado en el item, no con el offset de empuñadura. Ese offset existe para que
        // un arma quede bien agarrada, pero un power-up se pasa la vida en el suelo: hay
        // que dejarlo donde está el objeto para que verlo y recogerlo coincidan.
        Weapons.WeaponFactory.PlaceModel(instance, Vector3.zero,
                                         definition.Rotation.Value, definition.Length.Value);

        // Los colliders del modelo estorbarían al sistema de agarre del item.
        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            Object.Destroy(collider);

        // Imprescindible: el shader que viene en el bundle es una copia sin variantes y
        // sin esto la caja sale invisible aunque todo lo demás esté bien.
        Props.PropBuilder.RebindShaders(instance);
    }

    static void SwapAction(GameObject clone, BuffDefinition definition)
    {
        // Fuera TODAS las acciones heredadas del item base, no solo las reconocibles por
        // nombre: media docena de sitios del juego llaman a Item.ConsumeDelayed() por su
        // cuenta y quitarlas una a una es adivinar cuál sobra.
        foreach (var inherited in clone.GetComponentsInChildren<ItemActionBase>(true))
            Object.DestroyImmediate(inherited);

        var action = clone.AddComponent<BuffAction>();
        action.OnCastFinished = true;
        action.category = (int)definition.Category;
        action.pickupRadius = definition.PickupRadius.Value;
    }
}
