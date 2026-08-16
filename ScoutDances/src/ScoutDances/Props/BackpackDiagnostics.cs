using System.Linq;
using HarmonyLib;
using Photon.Pun;

namespace ScoutDances.Props;

/// <summary>
/// Registro de lo que pasa al guardar y sacar de la mochila.
/// </summary>
/// <remarks>
/// Existe porque llevo dos arreglos a ciegas en esta pieza y el segundo salió peor que el
/// primero. El camino para sacar un item pasa por cuatro sitios y cualquiera de ellos
/// explica el mismo síntoma —"no sale"— así que en vez de seguir adivinando se registra cada
/// uno y se mira dónde se corta:
///
/// <list type="number">
/// <item>¿Se crea el objeto del hueco? (<c>RefreshVisuals</c>, solo el anfitrión)</item>
/// <item>¿Queda apuntado en <c>spawnedVisualItems</c>?</item>
/// <item>Al elegir en la rueda, ¿qué número de hueco viaja?</item>
/// <item>¿Ese número encuentra su objeto?</item>
/// </list>
///
/// Solo se registra al abrir la rueda y al pulsar, nunca por frame: un log que escribe cada
/// frame es lo que ya nos costó una vez el rendimiento del juego.
/// </remarks>
internal static class BackpackDiagnostics
{
    internal static bool On => Plugin.CfgBackpackDebug.Value;

    /// <summary>Foto del estado de la mochila, para el log.</summary>
    internal static string Snapshot(BackpackVisuals visuals)
    {
        try
        {
            var slots = visuals.GetBackpackData()?.itemSlots;
            if (slots == null) return "sin datos";

            var filled = string.Join(", ", slots
                .Select((s, i) => (s, i))
                .Where(e => e.s != null && !e.s.IsEmpty())
                .Select(e => $"{e.i + 1}:{e.s.prefab?.name ?? "?"}"));

            var spawned = string.Join(", ",
                visuals.spawnedVisualItems.Keys.OrderBy(k => k).Select(k => (k + 1).ToString()));

            return $"{slots.Length} huecos | con item: [{filled}] | objeto creado en: [{spawned}]";
        }
        catch (System.Exception e)
        {
            return $"no pude leerla ({e.Message})";
        }
    }
}

/// <summary>Qué hueco viaja al pulsar una porción de la rueda.</summary>
[HarmonyPatch(typeof(BackpackWheel), nameof(BackpackWheel.Choose))]
internal static class BackpackChooseLog
{
    [HarmonyPrefix]
    static void Prefix(BackpackWheel __instance)
    {
        if (!BackpackDiagnostics.On) return;

        try
        {
            if (!__instance.chosenSlice.IsSome)
            {
                Plugin.Log.LogInfo("[mochila] pulsaste sin porción elegida.");
                return;
            }

            var slice = __instance.chosenSlice.Value;
            Plugin.Log.LogInfo($"[mochila] eliges el hueco {slice.slotID + 1} " +
                               $"(ponerse={slice.isBackpackWear}, guardar={slice.isStashSlice}, " +
                               $"combustible={slice.isJetpackFuelSlice}).");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"[mochila] no pude leer la porción elegida: {e.Message}");
        }
    }
}

/// <summary>Si ese hueco encuentra su objeto. Aquí es donde se corta si falla.</summary>
[HarmonyPatch(typeof(BackpackVisuals), nameof(BackpackVisuals.TryGetSpawnedItem))]
internal static class BackpackLookupLog
{
    [HarmonyPostfix]
    static void Postfix(BackpackVisuals __instance, byte slotID, ref bool __result)
    {
        if (!BackpackDiagnostics.On) return;

        Plugin.Log.LogInfo(
            $"[mochila] busco el objeto del hueco {slotID + 1}: " +
            $"{(__result ? "ENCONTRADO" : "NO ESTÁ")}. {BackpackDiagnostics.Snapshot(__instance)}");
    }
}

/// <summary>Y qué ve el anfitrión cada vez que refresca.</summary>
[HarmonyPatch(typeof(BackpackVisuals), "RefreshVisuals")]
internal static class BackpackRefreshLog
{
    [HarmonyPostfix]
    static void Postfix(BackpackVisuals __instance)
    {
        if (!BackpackDiagnostics.On) return;

        Plugin.Log.LogInfo(
            $"[mochila] refresco ({(PhotonNetwork.IsMasterClient ? "soy anfitrión" : "NO soy anfitrión")}): " +
            BackpackDiagnostics.Snapshot(__instance));
    }
}
