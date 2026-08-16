using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Hace que los items de las páginas 2 y 3 se puedan sacar de verdad.
/// </summary>
/// <remarks>
/// <b>El fallo.</b> <c>BackpackVisuals.RefreshVisuals</c> recorre un 4 escrito a mano y no
/// consulta <c>itemSlots.Length</c> —a diferencia de <c>AddItem</c>, <c>HasFreeSlot</c> y
/// <c>Choose</c>, que sí lo hacen—. Ese método es el que crea el objeto real de cada item
/// guardado y lo apunta en <c>spawnedVisualItems</c>.
///
/// Por eso el síntoma era tan raro: la rueda MOSTRABA los items de las páginas extra,
/// porque el icono sale de los datos del inventario, que sí tienen doce huecos. Pero al
/// elegirlos, <c>Choose</c> pedía <c>TryGetSpawnedItem(slotID)</c>, en el diccionario solo
/// había entradas del 0 al 3, y la cosa se acababa ahí sin un solo error. Guardabas bien,
/// veías bien, y no había nada que sacar.
///
/// <b>Por qué un postfix y no reescribirlo.</b> Son 478 bytes con anclajes, paracaídas y
/// cohete: reimplementarlo mal rompería la mochila para todos, no solo las páginas extra.
/// Dejamos que el original haga lo suyo con los cuatro primeros y nos limitamos a repetir
/// esa misma cuenta para los que faltan.
///
/// <b>Solo el anfitrión.</b> El original arranca comprobando <c>IsMasterClient</c>, porque
/// crear los objetos es cosa suya; si cada cliente los creara habría uno por jugador.
///
/// <b>El modelo solo tiene cuatro anclajes.</b> <c>backpackSlots</c> es un array fijo del
/// prefab, así que los items de más no se ven colgando de la mochila. Da igual: lo que hace
/// falta para poder sacarlos es que EXISTAN y estén apuntados, no que se vean. La mochila
/// nunca mostró doce objetos.
/// </remarks>
[HarmonyPatch(typeof(BackpackVisuals), "RefreshVisuals")]
internal static class BackpackExtraVisuals
{
    /// Lo que cubre el método original antes de que lleguemos nosotros.
    const byte Vanilla = 4;

    [HarmonyPostfix]
    static void Postfix(BackpackVisuals __instance)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        try
        {
            Refresh(__instance);
        }
        catch (System.Exception e)
        {
            // Nunca se deja escapar: esto corre dentro del refresco de la mochila, y una
            // excepción aquí se llevaría por delante también los cuatro huecos normales.
            Plugin.Log.LogWarning($"No pude preparar los huecos extra de la mochila: {e.Message}");
        }
    }

    static void Refresh(BackpackVisuals visuals)
    {
        var data = visuals.GetBackpackData();
        var slots = data?.itemSlots;
        if (slots == null || slots.Length <= Vanilla) return;

        for (byte slot = Vanilla; slot < slots.Length; slot++)
        {
            var itemSlot = slots[slot];
            bool empty = itemSlot == null || itemSlot.IsEmpty();

            bool has = visuals.spawnedVisualItems.TryGetValue(slot, out var spawned) &&
                       spawned != null;

            if (empty)
            {
                // Se vació el hueco: fuera el objeto que había.
                if (has)
                {
                    visuals.spawnedVisualItems.Remove(slot);
                    if (spawned != null) PhotonNetwork.Destroy(spawned.gameObject);
                }
                continue;
            }

            // Ya hay uno y es el que toca.
            if (has && spawned.itemID == itemSlot.prefab.itemID) continue;

            // Cambió de item: se retira el viejo antes de crear el nuevo.
            if (has && spawned != null)
            {
                visuals.spawnedVisualItems.Remove(slot);
                PhotonNetwork.Destroy(spawned.gameObject);
            }

            Spawn(visuals, itemSlot, slot);
        }
    }

    static void Spawn(BackpackVisuals visuals, ItemSlot itemSlot, byte slot)
    {
        var prefab = itemSlot.prefab;
        if (prefab == null) return;

        var created = PhotonNetwork.Instantiate("0_Items/" + prefab.name,
                                                Vector3.zero, Quaternion.identity);
        var item = created != null ? created.GetComponent<Item>() : null;
        if (item == null)
        {
            Plugin.Log.LogWarning($"No pude crear '{prefab.name}' para el hueco {slot}.");
            return;
        }

        // Colgarlo del modelo puede fallar: solo hay cuatro anclajes. No importa —lo que
        // hace falta para poder sacarlo es el registro de abajo, no que se vea.
        try { visuals.PutItemInBackpack(created, slot); }
        catch { created.SetActive(false); }

        visuals.SetSpawnedBackpackItem(slot, item);

        Plugin.Log.LogInfo($"Mochila: '{prefab.name}' listo en el hueco {slot + 1}.");
    }
}
