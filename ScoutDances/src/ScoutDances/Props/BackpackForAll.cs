using System.Collections;
using System.Linq;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Le pone una mochila a cada jugador al empezar.
/// </summary>
/// <remarks>
/// Es la forma más barata de que todos lleven más cosas encima: no toca el sistema de
/// inventario ni su formato de red, solo reparte un objeto que el juego ya sabe manejar.
/// Cuatro huecos más por cabeza sin arriesgar nada.
///
/// Cada cliente se la pone a SÍ MISMO. El inventario se sincroniza solo cuando cambia, así
/// que no hay que mandar nada a mano; y hacerlo desde una sola máquina para todos sería
/// escribir en un estado que no le pertenece.
/// </remarks>
internal class BackpackForAll : MonoBehaviour
{
    void Start() => StartCoroutine(HandOut());

    IEnumerator HandOut()
    {
        var wait = new WaitForSeconds(3f);
        bool done = false;

        while (true)
        {
            yield return wait;

            if (!Plugin.CfgBackpackForAll.Value) continue;

            var local = Character.localCharacter;
            if (local == null || local.player == null) continue;

            // Solo en el aeropuerto: es el punto de partida, y así no se la regalamos a
            // alguien que la haya perdido a propósito en mitad de la montaña.
            if (!local.inAirport) { done = false; continue; }

            var slot = local.player.backpackSlot;
            if (slot == null) continue;

            // Si ya lleva una, no se toca: reemplazarla tiraría lo que tuviera dentro.
            // Ojo: NO se marca como hecho por haberla entregado antes, se mira el hueco
            // cada vez. Si la pierdes, la sueltas o te la quita otro mod, vuelve a
            // dártela — antes bastaba una entrega para no volver a comprobarlo nunca.
            if (!slot.IsEmpty()) { done = true; continue; }

            if (done) Plugin.Log.LogInfo("Te quedaste sin mochila; te doy otra.");
            done = false;

            var backpack = FindBackpack();
            if (backpack == null)
            {
                Plugin.Log.LogWarning("No encontré ninguna mochila en el ItemDatabase.");
                done = true;
                continue;
            }

            var data = new ItemInstanceData(System.Guid.NewGuid());
            ItemInstanceDataHandler.AddInstanceData(data);

            // Se usa AddItem del propio juego en vez de escribir el slot a mano: él ya
            // reconoce que una mochila va al hueco de la espalda, le pone su backpackType
            // y avisa a los demás clientes con SyncInventoryRPC. Metiéndolo a pelo en el
            // slot, el resto de la sala no se enteraría.
            done = local.player.AddItem(backpack.itemID, data, out _);

            Plugin.Log.LogInfo(done
                ? $"Mochila entregada: '{backpack.name}'."
                : $"No pude entregar la mochila '{backpack.name}'.");
        }
    }

    /// <summary>
    /// Busca la mochila normal del juego.
    /// </summary>
    /// <remarks>
    /// Por COMPONENTE y no por nombre: los prefabs del database no se llaman como lo que se
    /// ve en pantalla —ya nos pasó con el arma de dardos, que se llama RopeShooterAnti— y el
    /// componente <c>Backpack</c> identifica sin ambigüedad. Se descarta el jetpack, que
    /// también es una mochila pero cambia cómo te mueves.
    /// </remarks>
    static Item? FindBackpack()
    {
        try
        {
            return Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects
                .FirstOrDefault(i => i != null &&
                                     i.GetComponentInChildren<Backpack>(true) != null &&
                                     i.name.IndexOf("jet", System.StringComparison.OrdinalIgnoreCase) < 0);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"No pude consultar el ItemDatabase: {e.Message}");
            return null;
        }
    }
}
