using System.Collections;
using System.Linq;
using PEAKLib.Core;
using PEAKLib.Items;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Construye el arma clonando un item vanilla en runtime y la registra con PEAKLib.
/// </summary>
/// <remarks>
/// No hace falta ripear el juego ni montar un prefab en Unity. La clave es que
/// <c>NetworkPrefabManager.RegisterNetworkPrefab</c> acepta un <c>GameObject</c>
/// cualquiera, no un asset, y que <c>ItemContent.Register()</c> deriva el
/// <c>itemID</c> de <c>MD5(modId + nombre)</c> — determinista, así que sale igual en
/// todos los clientes sin depender del orden de carga.
///
/// Partimos del <b>Blowgun</b> porque ya trae resuelto todo lo tedioso: el componente
/// <c>Item</c> configurado, su PhotonView, la pose de agarre y el flujo de "cast" del
/// disparo. Solo le cambiamos el modelo, la acción y la munición. El Blowgun original
/// no se toca: trabajamos sobre una copia.
/// </remarks>
internal static class WeaponFactory
{
    /// Item del juego que usamos de esqueleto.
    const string BaseItemName = "Blowgun";

    internal static void BuildAll(Plugin plugin, ModDefinition mod)
    {
        plugin.StartCoroutine(BuildWhenReady(mod));
    }

    static IEnumerator BuildWhenReady(ModDefinition mod)
    {
        // Esperamos a que el ItemDatabase tenga contenido. Contamos por SEGUNDOS y no
        // por frames: durante las pantallas de carga los frames son larguísimos y un
        // contador de frames se agota antes de que el juego termine de cargar.
        float waited = 0f;
        while (CountItems() == 0 && waited < 60f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        foreach (var definition in Plugin.Weapons)
        {
            var source = FindBaseItem(definition.BaseItem.Value);
            if (source == null)
            {
                Plugin.Log.LogError($"Sin item base para '{definition.Id}'; me la salto.");
                continue;
            }

            try
            {
                Create(source, mod, definition);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Fallo creando '{definition.Id}': {e}");
            }
        }
    }

    static int CountItems()
    {
        try { return Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects.Count; }
        catch { return 0; }
    }

    /// <summary>
    /// Localiza el arma de dardos del juego para usarla de base.
    /// </summary>
    /// <remarks>
    /// Buscamos por COMPONENTE (<c>Action_RaycastDart</c>) y no por nombre: los nombres
    /// de prefab del database no coinciden con los visibles — el que se muestra como
    /// "anti-rope cannon" se llama <c>RopeShooterAnti</c> — y buscar "Blowgun" no
    /// encontraba nada. El componente sí identifica sin ambigüedad al único arma de
    /// proyectil de PEAK.
    /// </remarks>
    static Item? FindBaseItem(string wanted)
    {
        try
        {
            var items = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects;

            // Lo primero, lo que pida la config. De la base solo aprovechamos su
            // componente Item, su PhotonView y —sobre todo— su POSE DE AGARRE: la del
            // dardo deja el arma pegada al cuerpo, mientras que la de la corneta la
            // levanta al frente, que es lo que queremos para apuntar.
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                var byConfig = items.FirstOrDefault(
                    i => i != null &&
                         i.name.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) >= 0);

                if (byConfig != null)
                {
                    Plugin.Log.LogInfo($"Base del arma: '{byConfig.name}' (pedida en config).");
                    return byConfig;
                }

                Plugin.Log.LogWarning($"No hay ningún item que contenga '{wanted}'; " +
                                      "tiro del arma de dardos como respaldo.");
            }

            var byComponent = items.FirstOrDefault(
                i => i != null && i.GetComponentInChildren<Action_RaycastDart>(true) != null);

            if (byComponent != null)
            {
                Plugin.Log.LogInfo($"Base del arma: '{byComponent.name}' (tiene Action_RaycastDart).");
                return byComponent;
            }

            // Por si en una actualización cambiaran ese componente.
            var byName = items.FirstOrDefault(
                i => i != null && i.name.IndexOf(BaseItemName, System.StringComparison.OrdinalIgnoreCase) >= 0);

            if (byName != null)
                Plugin.Log.LogInfo($"Base del arma por nombre: '{byName.name}'.");
            else if (items.Count > 0)
                Plugin.Log.LogWarning("Candidatos con pinta de arma: " + string.Join(", ",
                    items.Where(i => i != null && System.Text.RegularExpressions.Regex.IsMatch(
                        i.name, "gun|shoot|dart|zooka|cannon", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        .Select(i => i.name).Take(15)));

            return byName;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"No pude consultar el ItemDatabase: {e.Message}");
            return null;
        }
    }

    static void Create(Item source, ModDefinition mod, WeaponDefinition definition)
    {
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
            // El juego NO muestra itemName tal cual: lo usa como clave de localización
            // ("NAME_PISTOLA"), y si no existe pinta "LOC: NAME_PISTOLA" en pantalla.
            // Así que registramos la traducción para esa clave exacta.
            RegisterName(definition.DisplayName.Value);

            item.UIData.itemName = definition.DisplayName.Value;
            item.UIData.mainInteractPrompt = "Disparar";
        }

        // La barrita de carga avanza a 1/usingTimePrimary por segundo, así que este es
        // el tiempo desde que pulsas hasta que sale el disparo. La corneta tarda lo suyo
        // en sonar; un arma tiene que ser más rápida.
        float before = item.usingTimePrimary;
        item.usingTimePrimary *= definition.CastMultiplier.Value;
        Plugin.Log.LogInfo($"Tiempo de carga: {before:0.00}s -> {item.usingTimePrimary:0.00}s.");

        var model = Plugin.FindPrefab(definition.Model.Value);
        SwapModel(clone, model, definition);
        SwapAction(clone, item, definition);

        clone.AddComponent<WeaponTag>().DefinitionId = definition.Id;
        if (definition.MuteBase.Value) MuteInheritedAudio(clone);
        WeaponLoot.Apply(clone);

        new ItemContent(item).Register(mod);

        Plugin.Log.LogInfo(
            $"Arma '{clone.name}' registrada (itemID {item.itemID}) a partir de '{source.name}'. " +
            $"Munición: {definition.Ammo.Value}, daño: {definition.Damage.Value}, " +
            $"empujón: {definition.Knockback.Value}, retroceso: {definition.Recoil.Value}, " +
            $"volumen: {definition.ShotVolume.Value}.");
    }

    /// <summary>
    /// Apaga los AudioSource que vienen del item clonado.
    /// </summary>
    /// <remarks>
    /// El item base es la corneta del Scoutmaster, y sopla mientras se carga el disparo.
    /// Ese sonido no lo lanza ninguna acción —las quitamos todas— sino AudioSource propios
    /// del prefab, así que hay que apagarlos uno a uno.
    ///
    /// Se apagan en vez de destruirlos: el Item guarda referencias a sus componentes y
    /// destruirlos deja nulos que revientan al recoger o soltar el objeto, igual que nos
    /// pasaba con los renderers. Nuestros disparos no se ven afectados porque suenan con
    /// <c>PlayClipAtPoint</c>, que crea su propia fuente aparte.
    /// </remarks>
    static void MuteInheritedAudio(GameObject clone)
    {
        int muted = 0;
        foreach (var source in clone.GetComponentsInChildren<AudioSource>(true))
        {
            if (source == null) continue;
            source.playOnAwake = false;
            source.Stop();
            source.mute = true;
            muted++;
        }

        if (muted > 0) Plugin.Log.LogInfo($"Silenciados {muted} sonidos del item base.");
    }

    /// <summary>
    /// Coloca el modelo del arma: tamaño, rotación y posición respecto a la mano.
    /// </summary>
    /// <remarks>
    /// Se usa tanto al construir el arma como desde el ajustador en vivo (F3), para que
    /// lo que ves mientras lo mueves sea exactamente lo que quedará guardado.
    ///
    /// El modelo se centra sobre su caja envolvente porque el pivote del prefab está
    /// donde quiso el artista. Eso deja el CENTRO del arma en la mano, que para una
    /// pistola no es lo ideal (lo suyo sería la empuñadura), pero da un punto de partida
    /// razonable; el resto se afina con el offset.
    /// </remarks>
    internal static void PlaceModel(GameObject model, Vector3 offset, Vector3 euler, float length)
    {
        var size = Props.PropBuilder.LocalBounds(model).size;
        float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        float scale = longest > 0.001f ? length / longest : 1f;

        var rotation = Quaternion.Euler(euler);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localRotation = rotation;

        var center = rotation * (Props.PropBuilder.LocalBounds(model).center * scale);
        model.transform.localPosition = offset - center;

        // WeaponAim reescribe la posición cada frame para el que la lleva, y necesita
        // aplicar ESTA MISMA compensación; si no, el offset significa una cosa aquí y
        // otra allí, y los valores calibrados con F3 no se corresponden con lo que se ve.
        var pivot = model.GetComponent<WeaponPivot>() ?? model.AddComponent<WeaponPivot>();
        pivot.Compensation = center;
    }

    /// <summary>
    /// Da de alta el nombre del arma en el sistema de localización del juego.
    /// </summary>
    /// <remarks>
    /// PEAK compone la clave como <c>"NAME_" + nombre.ToUpper()</c>. Sin registrarla,
    /// en la mano se lee literalmente <c>LOC: NAME_PISTOLA</c>.
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
            Plugin.Log.LogInfo($"Nombre del arma registrado bajo la clave '{key}'.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude registrar la localización '{key}': {e.Message}");
        }
    }

    /// <summary>Oculta la malla del item base y cuelga la nuestra en su sitio.</summary>
    static void SwapModel(GameObject clone, GameObject? weaponModel, WeaponDefinition definition)
    {
        if (weaponModel == null)
        {
            Plugin.Log.LogWarning("Sin modelo de arma en el bundle: se queda con el aspecto del Blowgun.");
            return;
        }

        // Apagamos los renderers originales en vez de destruirlos: el Item guarda
        // referencias a ellos (mainRenderer, addtlRenderers) y destruirlos deja nulos
        // que revientan al recoger o soltar el objeto.
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<ParticleSystem>() != null) continue;   // VFX no
            renderer.enabled = false;
        }

        var model = Object.Instantiate(weaponModel, clone.transform);
        model.name = "WeaponModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        PlaceModel(model, definition.Offset.Value, definition.Rotation.Value,
                   definition.Length.Value);

        // Los colliders del modelo estorbarían al sistema de agarre del item.
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            Object.Destroy(collider);

        // Imprescindible: el shader que viene en el bundle es una copia sin variantes
        // y el arma sale invisible aunque todo lo demás esté bien.
        Props.PropBuilder.RebindShaders(model);

        ForceBestLod(model);

        CreateMuzzle(clone, model);

        Plugin.Log.LogInfo(
            $"Modelo '{weaponModel.name}' colocado: escala {model.transform.localScale.x:0.000}, " +
            $"posLocal {model.transform.localPosition}. Ajústalo en vivo con F3.");
    }

    /// <summary>
    /// Desactiva los grupos de LOD del modelo y deja solo el más detallado.
    /// </summary>
    /// <remarks>
    /// Un LODGroup elige el nivel de detalle por cuánta PANTALLA ocupa el objeto. Un arma
    /// en la mano mide unos centímetros, así que cae en el peor nivel o directamente en el
    /// "culled" y desaparece — que es lo que pasaba con el espejo, que trae LOD0 a LOD3.
    ///
    /// Se apaga el grupo y se encienden los renderers del nivel 0. Para un objeto que
    /// siempre está a un palmo de la cámara, tener niveles de detalle no aporta nada.
    /// </remarks>
    static void ForceBestLod(GameObject model)
    {
        foreach (var group in model.GetComponentsInChildren<LODGroup>(true))
        {
            if (group == null) continue;

            var lods = group.GetLODs();
            group.enabled = false;

            for (int level = 0; level < lods.Length; level++)
            foreach (var renderer in lods[level].renderers)
            {
                if (renderer == null) continue;
                renderer.enabled = level == 0;
            }

            Plugin.Log.LogInfo($"LODGroup de '{model.name}' desactivado " +
                               $"({lods.Length} niveles); se queda el más detallado.");
        }
    }

    /// <summary>
    /// Crea el punto por donde "sale la bala", en la punta del cañón.
    /// </summary>
    /// <remarks>
    /// Se deduce del propio modelo: el eje más largo de un arma es su cañón, así que el
    /// extremo de ese eje es la boca. Sirve para cualquiera de las 12 del pack sin tener
    /// que marcar el punto a mano en cada una.
    ///
    /// Va colgado del ROOT del item, no del modelo, para que al reajustar el modelo con
    /// F3 no se quede desincronizado... y aun así se recalcula si hace falta.
    /// </remarks>
    static void CreateMuzzle(GameObject clone, GameObject model)
    {
        var bounds = Props.PropBuilder.LocalBounds(model);
        var size = bounds.size;

        int axis = (size.x >= size.y && size.x >= size.z) ? 0 : (size.y >= size.z ? 1 : 2);
        var tip = bounds.center;
        tip[axis] = bounds.max[axis];

        var world = model.transform.TransformPoint(tip);

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(clone.transform, false);
        muzzle.transform.localPosition =
            clone.transform.InverseTransformPoint(world) + Plugin.CfgMuzzleOffset.Value;
        muzzle.transform.localRotation = model.transform.localRotation;

        Plugin.Log.LogInfo($"Boca del cañón en {muzzle.transform.localPosition} " +
                           $"(eje largo {"XYZ"[axis]}).");
    }

    /// <summary>Sustituye la acción de la cerbatana por la nuestra.</summary>
    static void SwapAction(GameObject clone, Item item, WeaponDefinition definition)
    {
        // Fuera TODAS las acciones heredadas, no solo las que reconocemos por nombre.
        // El item base es un dardo consumible y hay media docena de sitios en el juego
        // que llaman a Item.ConsumeDelayed(); ir quitándolas una a una es jugar a
        // adivinar cuál sobra. El arma no necesita ninguna acción del dardo: solo la suya.
        var inherited = clone.GetComponentsInChildren<ItemActionBase>(true);
        foreach (var inheritedAction in inherited)
            Object.DestroyImmediate(inheritedAction);

        if (inherited.Length > 0)
            Plugin.Log.LogInfo($"Quitadas {inherited.Length} acciones heredadas del item base.");

        // NO tocar item.totalUses. Tentador, porque es el contador nativo y el RopeShooter
        // lo usa para su munición — pero Item.Start() hace, si totalUses != -1:
        //
        //     GetData<OptionableIntItemData>(DataEntryKey.ItemUses)
        //
        // y el dardo no declara esa entrada en su esquema de datos, así que revienta su
        // inicialización. Como eso ocurre dentro del RPC de spawn, el fallo llega
        // envuelto en un TargetInvocationException y el arma simplemente no aparece.
        // Llevamos la munición por nuestra cuenta en PistolAmmo.

        // Las armas con mecánica propia montan su componente y se van; el resto usan la
        // acción de disparo de siempre.
        switch (definition.Kind.Value)
        {
            case "Iman":
            {
                var magnet = clone.AddComponent<MagnetAction>();

                // OnPressed además de OnHeld: sin él, el chorro no arranca hasta el
                // segundo frame de mantener pulsado y se nota el retraso.
                magnet.OnPressed = true;
                magnet.OnHeld = true;
                magnet.range = definition.Range.Value;
                magnet.pullForce = definition.Knockback.Value;
                magnet.secondsPerCharge = definition.FloatSeconds.Value;
                magnet.beamEffect = definition.HitEffect.Value;
                magnet.humSound = definition.ShotSound.Value;
                magnet.beamLength = Plugin.CfgMagnetBeamLength.Value;

                var magnetAmmo = clone.AddComponent<PistolAmmo>();
                magnetAmmo.MaxAmmo = definition.Ammo.Value;
                clone.AddComponent<WeaponAim>();
                return;
            }

            case "Espejo":
            {
                var mirror = clone.AddComponent<MirrorAction>();
                mirror.OnCastFinished = true;
                mirror.duration = definition.FloatSeconds.Value;

                clone.AddComponent<WeaponAim>();
                return;
            }

            case "Varita":
            {
                var wand = clone.AddComponent<WandAction>();
                wand.OnCastFinished = true;
                wand.cooldown = 0f;                    // un solo uso: la recarga sobra
                wand.orbSpeed = definition.Knockback.Value;

                // La vida del orbe va en FloatSeconds y NO en Damage: ese campo está
                // limitado a 0..2 porque nació para las afflictions de las pistolas, así
                // que un 5 se recortaba a 2 sin avisar y el fuego duraba menos de la mitad.
                wand.orbLifetime = definition.FloatSeconds.Value;
                wand.orbRadius = definition.KnockbackRadius.Value;
                wand.orbPrefab = definition.HitEffect.Value;
                wand.loopSound = definition.ShotSound.Value;

                var wandAmmo = clone.AddComponent<PistolAmmo>();
                wandAmmo.MaxAmmo = definition.Ammo.Value;

                clone.AddComponent<WeaponAim>();
                return;
            }

            case "Granada":
            {
                // Sin acción de uso: no se dispara, se lanza con Q como cualquier objeto.
                // Lo único que añadimos es qué pasa al chocar.
                var grenade = clone.AddComponent<Grenade>();
                grenade.radius = definition.Range.Value;
                grenade.effectScale = definition.KnockbackRadius.Value;
                grenade.explosionEffect = definition.HitEffect.Value;
                grenade.explosionSound = definition.ShotSound.Value;
                return;
            }

            case "Portales":
            {
                var portals = clone.AddComponent<PortalAction>();
                portals.OnCastFinished = true;
                portals.maxDistance = definition.Range.Value;
                portals.seconds = definition.FloatSeconds.Value;
                portals.openSound = definition.ShotSound.Value;

                var portalAmmo = clone.AddComponent<PistolAmmo>();
                portalAmmo.MaxAmmo = definition.Ammo.Value;
                clone.AddComponent<WeaponAim>();
                return;
            }
        }

        var action = clone.AddComponent<PistolAction>();
        action.OnCastFinished = true;         // se dispara al completar el uso primario
        action.maxDistance = definition.Range.Value;
        action.injuryPerHit = definition.Damage.Value;
        action.shotVolume = definition.ShotVolume.Value;
        action.knockback = definition.Knockback.Value;
        action.recoil = definition.Recoil.Value;
        action.shotSound = definition.ShotSound.Value;
        action.hitEffect = definition.HitEffect.Value;
        action.knockbackRadius = definition.KnockbackRadius.Value;
        action.floatSeconds = definition.FloatSeconds.Value;
        action.auraEffect = definition.AuraEffect.Value;
        action.swapPositions = definition.SwapPositions.Value;

        // Unity remapea las referencias internas del prefab al instanciarlo, así que
        // esto apunta al Muzzle de cada copia y no al del clon original.
        action.muzzle = clone.transform.Find("Muzzle");

        var ammo = clone.AddComponent<PistolAmmo>();
        ammo.MaxAmmo = definition.Ammo.Value;

        // Mantiene el arma apuntando a donde miras aunque la mano se mueva con la
        // animación de carga.
        clone.AddComponent<WeaponAim>();
    }
}
