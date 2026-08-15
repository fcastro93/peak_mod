using HarmonyLib;
using UnityEngine;
using Zorro.Core;
using Zorro.Core.Serizalization;

namespace ScoutDances.Props;

/// <summary>
/// Agranda la mochila y la reparte en páginas dentro de la rueda que ya existe.
/// </summary>
/// <remarks>
/// <b>Por qué páginas y no más huecos.</b> La rueda de la mochila NO crea sus porciones:
/// vienen colocadas a mano en el prefab de la interfaz, con sus ángulos y posiciones, y
/// <c>InitWheel</c> solo las enciende y apaga. Meter más slots haría que el juego pidiera
/// <c>slices[5]</c>, que no existe. Paginar reutiliza las cuatro porciones de siempre para
/// mostrar tramos distintos del inventario, así que no hay que recolocar nada.
///
/// <b>El formato de red aguanta el cambio.</b> <c>InventorySyncData</c> escribe primero
/// cuántos slots van y el lector se dimensiona con ese número:
///
/// <code>
/// slotCount = deserializer.ReadByte();
/// slots = new SlotData[slotCount];
/// </code>
///
/// Por eso agrandar la mochila no corrompe nada. Lo único que había que arreglar era el
/// bucle de <c>BackpackData.DeserializeValue</c>, que copiaba con un 4 escrito a mano y se
/// dejaba fuera todo lo que pasara de ahí.
/// </remarks>
internal static class BackpackPages
{
    /// Página que se está viendo ahora mismo.
    internal static int Page;

    /// Cuántos huecos por página. Sale de las porciones que trae el prefab.
    internal static int PerPage = 4;

    internal static int TotalSlots => Mathf.Max(1, Plugin.CfgBackpackPages.Value) * PerPage;

    internal static void NextPage(int direction)
    {
        int pages = Mathf.Max(1, Plugin.CfgBackpackPages.Value);
        Page = ((Page + direction) % pages + pages) % pages;
    }
}

/// <summary>Agranda el array de slots en cuanto la mochila se inicializa.</summary>
[HarmonyPatch(typeof(BackpackData), nameof(BackpackData.Init))]
internal static class BackpackSizePatch
{
    [HarmonyPostfix]
    static void Postfix(BackpackData __instance)
    {
        int wanted = BackpackPages.TotalSlots;
        if (__instance.itemSlots != null && __instance.itemSlots.Length >= wanted) return;

        var grown = new ItemSlot[wanted];

        // Se conservan los que ya hubiera: Init corre también al recuperar una partida
        // guardada, y rehacer el array a secas vaciaría la mochila.
        for (byte b = 0; b < wanted; b++)
        {
            grown[b] = __instance.itemSlots != null && b < __instance.itemSlots.Length &&
                       __instance.itemSlots[b] != null
                ? __instance.itemSlots[b]
                : new ItemSlot(b);
        }

        __instance.itemSlots = grown;

        Plugin.Log.LogInfo($"[mochila] agrandada a {wanted} huecos.");
    }
}

/// <summary>
/// Copia TODOS los slots recibidos, no solo los cuatro primeros.
/// </summary>
/// <remarks>
/// El original lleva el número escrito a mano:
///
/// <code>
/// for (byte b = 0; b &lt; 4; b++) { ... }
/// </code>
///
/// Lee bien los datos —el formato trae su propia cuenta— pero luego solo vuelca cuatro. Con
/// la mochila agrandada, todo lo que hubiera del quinto en adelante se perdía en cada
/// sincronización. Se sustituye el método entero porque el arreglo es justo ese límite.
/// </remarks>
[HarmonyPatch(typeof(BackpackData), nameof(BackpackData.DeserializeValue))]
internal static class BackpackDeserializePatch
{
    [HarmonyPrefix]
    static bool Prefix(BackpackData __instance, BinaryDeserializer deserializer)
    {
        var sync = default(InventorySyncData);

        try
        {
            sync.Deserialize(deserializer);
        }
        catch (System.Exception e)
        {
            // Si los datos vienen mal —normalmente porque alguien de la sala tiene otro
            // número de páginas— se deja la mochila como estaba en vez de dejarla a
            // medias. Con el original, un GUID ilegible reventaba la lectura y dejaba
            // huecos NULOS que luego explotaban cada frame en UpdatePocketBehaviors.
            Plugin.Log.LogWarning($"Mochila: datos ilegibles ({e.GetType().Name}). " +
                                  "¿Tenéis todos el mismo número de páginas?");
            FillGaps(__instance);
            return false;
        }

        int count = Mathf.Min(__instance.itemSlots.Length, sync.slotCount);

        for (byte b = 0; b < count; b++)
        {
            __instance.itemSlots[b] ??= new ItemSlot(b);

            var prefab = ItemDatabase.TryGetItem(sync.slots[b].ItemID, out var item) ? item : null;
            __instance.itemSlots[b].SetItem(prefab, sync.slots[b].Data);
        }

        // Los huecos que el otro no mandó se dejan vacíos pero EXISTIENDO. Un null aquí
        // es el que revienta al recorrer la mochila, y lo hace en Update: una vez por
        // frame y para siempre.
        FillGaps(__instance);

        return false;   // reemplazamos el original
    }

    /// <summary>Se asegura de que no quede ningún hueco a null.</summary>
    static void FillGaps(BackpackData data)
    {
        if (data.itemSlots == null) return;

        for (byte b = 0; b < data.itemSlots.Length; b++)
            data.itemSlots[b] ??= new ItemSlot(b);
    }
}

/// <summary>Reemplaza el montaje de la rueda para mostrar solo la página actual.</summary>
/// <remarks>
/// Tiene que ser un PREFIX que sustituya al original, no un postfix. El bucle de fábrica
/// recorre <c>itemSlots.Length</c> para indexar <c>slices[b + 1]</c>:
///
/// <code>
/// for (byte b = 0; b &lt; itemSlots.Length; b++)   // 12 con la mochila agrandada
///     slices[b + 1]...                             // el prefab solo trae 5
/// </code>
///
/// Con la mochila agrandada eso lanza IndexOutOfRange ANTES de que un postfix llegue a
/// ejecutarse. Y como revienta dentro de <c>Backpack.Interact</c>, el juego ya había puesto
/// <c>usingBackpackWheel = true</c>: la cámara se quedaba bloqueada y no se abría nada.
///
/// El resto del método se replica tal cual —jetpack, indicador de combustible, item en la
/// mano— porque al sustituirlo somos responsables de todo lo que hacía.
/// </remarks>
[HarmonyPatch(typeof(BackpackWheel), nameof(BackpackWheel.InitWheel))]
internal static class BackpackWheelPagePatch
{
    [HarmonyPrefix]
    static bool Prefix(BackpackWheel __instance, BackpackReference bp,
                       BackpackSlot.BackpackType backpackType)
    {
        var slices = __instance.slices;
        if (slices == null || slices.Length < 2) return true;   // que siga el original

        // La porción 0 es la de coger la mochila; las demás son huecos.
        BackpackPages.PerPage = slices.Length - 1;

        __instance.backpack = bp;
        __instance.backpackType = backpackType;
        __instance.chosenSlice = Optionable<BackpackWheelSlice.SliceData>.None;
        __instance.chosenItemText.text = "";

        var data = bp.GetData();
        var slots = data?.itemSlots;

        // Si la mochila se creó antes del parche de tamaño, se agranda aquí: es el momento
        // en que sabemos seguro que existe.
        if (slots != null && slots.Length < BackpackPages.TotalSlots)
        {
            var grown = new ItemSlot[BackpackPages.TotalSlots];
            for (byte b = 0; b < grown.Length; b++)
                grown[b] = b < slots.Length && slots[b] != null ? slots[b] : new ItemSlot(b);

            data!.itemSlots = grown;
            slots = grown;
        }

        int perPage = BackpackPages.PerPage;
        int offset = BackpackPages.Page * perPage;

        for (int i = 0; i < perPage; i++)
        {
            var slice = slices[i + 1];
            if (slice == null) continue;

            int slot = offset + i;
            bool exists = slots != null && slot < slots.Length;

            slice.gameObject.SetActive(exists);
            if (exists) slice.InitItemSlot((bp, (byte)slot), __instance);
        }

        bool jetpack = backpackType == BackpackSlot.BackpackType.Jetpack;
        __instance.jetpackSlice.gameObject.SetActive(jetpack);
        __instance.fuelGauge.SetActive(jetpack);

        if (bp.GetItemInstanceData().TryGetDataEntry<FloatItemData>(
                DataEntryKey.UseRemainingPercentage, out var fuel))
        {
            __instance.fuelGaugeArrow.localRotation =
                Quaternion.Euler(0f, 0f, 50f - fuel.Value * 100f);
        }

        if (jetpack) __instance.jetpackSlice.InitJetpackSlot(bp, __instance);

        __instance.gameObject.SetActive(true);
        slices[0].InitPickupBackpack(bp, __instance);

        var held = Character.localCharacter?.data?.currentItem;
        if (held != null)
        {
            __instance.currentlyHeldItem.texture = held.UIData.GetIcon();
            __instance.UpdateCookedAmount(held);
            __instance.currentlyHeldItem.enabled = true;
        }

        // El componente que pasa de página. Se añade aquí porque la rueda no existe hasta
        // que el juego construye su interfaz.
        if (__instance.GetComponent<BackpackPageFlipper>() == null)
            __instance.gameObject.AddComponent<BackpackPageFlipper>().Wheel = __instance;

        Plugin.Log.LogInfo($"[mochila] rueda: {slices.Length} porciones, " +
                           $"{slots?.Length ?? -1} huecos, página " +
                           $"{BackpackPages.Page + 1}/{Plugin.CfgBackpackPages.Value}.");

        return false;   // sustituimos el original
    }
}

/// <summary>Cambia de página con la rueda del ratón mientras la mochila está abierta.</summary>
internal class BackpackPageFlipper : MonoBehaviour
{
    internal BackpackWheel? Wheel;

    void Update()
    {
        if (Wheel == null || !Wheel.gameObject.activeInHierarchy) return;
        if (Plugin.CfgBackpackPages.Value <= 1) return;

        // Dos formas de pasar página. La rueda del ratón es lo natural, pero el juego la
        // usa para el cinturón y puede quedársela antes de que llegue aquí; la tecla es el
        // respaldo que siempre funciona.
        float scroll = UnityEngine.InputSystem.Mouse.current?.scroll.ReadValue().y ?? 0f;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        bool pressed = keyboard != null && keyboard[Plugin.BackpackPageKey].wasPressedThisFrame;

        if (Mathf.Abs(scroll) < 0.01f && !pressed) return;

        Plugin.Log.LogInfo($"[mochila] cambio de página (scroll {scroll:0.00}, tecla {pressed}).");

        BackpackPages.NextPage(scroll < -0.01f ? -1 : 1);

        // Se reconstruye la rueda para que repinte el tramo nuevo. Es la misma llamada que
        // hace el juego al abrirla, así que no hay ningún estado a medias.
        Wheel.InitWheel(Wheel.backpack, BackpackPages.TotalSlots, Wheel.backpackType);

        Plugin.Log.LogInfo($"Mochila: página {BackpackPages.Page + 1} de {Plugin.CfgBackpackPages.Value}.");
    }
}
