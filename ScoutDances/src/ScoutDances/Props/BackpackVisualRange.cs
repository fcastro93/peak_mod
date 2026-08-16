using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Hace que el juego prepare TODOS los huecos de la mochila, no solo los cuatro primeros.
/// </summary>
/// <remarks>
/// <b>Qué se cambia.</b> <c>BackpackVisuals.RefreshVisuals</c> —el método que crea el objeto
/// real de cada item guardado y lo apunta en <c>spawnedVisualItems</c>— lleva su tope escrito
/// a mano:
///
/// <code>
/// for (byte b = 0; b &lt; 4; b++)     // ldc.i4.4 como límite del bucle
/// </code>
///
/// Sin objeto creado, <c>BackpackWheel.Choose</c> pide <c>TryGetSpawnedItem</c>, no encuentra
/// nada y se acaba ahí sin error: la rueda muestra el item —el icono sale de los datos, que
/// sí tienen doce huecos— pero al elegirlo no pasa nada.
///
/// <b>Por qué un transpiler y no hacerlo yo por fuera.</b> Ya lo intenté: un postfix que
/// creaba los objetos que faltaban. Salió mal de dos maneras a la vez, y el registro lo dejó
/// negro sobre blanco. Primero, <c>PhotonNetwork.Instantiate</c> dispara otro
/// <c>RefreshVisuals</c>, así que mi bucle se reentraba y solo completaba un hueco por
/// pasada: en el log aparecían siete "preparo" seguidos sin un solo "listo". Y segundo,
/// <c>PutItemInBackpack</c> vuelve a METER el item en la mochila, así que un hueco recién
/// vaciado se rellenaba solo — de ahí los iconos fantasma y que los items nuevos
/// desaparecieran.
///
/// Cambiando la constante, todo eso lo hace el juego con su propia contabilidad, en su propio
/// orden y una sola vez. No hay nada mío corriendo dentro de ese método.
///
/// <b>Se toma el tamaño real de esa mochila</b>, no un número fijo: si una no llegó a
/// agrandarse —porque la creó alguien con otra configuración— recorrerla entera de todos
/// modos indexaría fuera de rango.
/// </remarks>
[HarmonyPatch(typeof(BackpackVisuals), "RefreshVisuals")]
internal static class BackpackVisualRange
{
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
                                                   ILGenerator _)
    {
        var replaced = false;

        foreach (var instruction in instructions)
        {
            // El 4 aparece UNA sola vez en todo el método, comprobado en el ensamblado, así
            // que no hay riesgo de confundirlo con otra cosa. Aun así se sustituye solo el
            // primero: si una actualización del juego añadiera otro, mejor quedarnos cortos
            // que tocar algo que no toca.
            if (!replaced && instruction.opcode == OpCodes.Ldc_I4_4)
            {
                replaced = true;

                // La instancia, para poder preguntarle su propio número de huecos.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return CodeInstruction.Call(typeof(BackpackVisualRange), nameof(SlotCount));
                continue;
            }

            yield return instruction;
        }

        if (!replaced)
        {
            Plugin.Log.LogWarning(
                "No encontré el tope del bucle en RefreshVisuals: la mochila se queda con " +
                "cuatro huecos utilizables. ¿Actualizó el juego?");
        }
    }

    /// <summary>Cuántos huecos tiene ESTA mochila.</summary>
    internal static int SlotCount(BackpackVisuals visuals)
    {
        try
        {
            var slots = visuals.GetBackpackData()?.itemSlots;
            return slots != null ? Mathf.Max(1, slots.Length) : 4;
        }
        catch
        {
            // Ante la duda, el comportamiento de siempre.
            return 4;
        }
    }
}
