using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScoutDances.Sounds;
using UnityEngine;

namespace ScoutDances.Patches;

/// <summary>
/// Captura cuántas páginas trae la rueda de serie, antes de que ningún mod la toque.
/// </summary>
/// <remarks>
/// PEAKEmoteLib añade sus páginas en un <c>Postfix</c> de <c>EmoteWheel.Start</c>, y los
/// prefixes corren antes que cualquier postfix — así que aquí el valor todavía es el original.
/// </remarks>
[HarmonyPatch(typeof(EmoteWheel), "Start")]
internal static class EmoteWheelVanillaPageCountPatch
{
    internal static int VanillaPages = 2;

    [HarmonyPrefix]
    static void Prefix(EmoteWheel __instance) => VanillaPages = __instance.pages;
}

/// <summary>
/// Reorganiza la zona de páginas custom para que los 3 emotes de sonido tengan su
/// propia página, en vez de quedar mezclados al final de los bailes.
/// </summary>
/// <remarks>
/// PEAKEmoteLib rellena las ranuras en el orden en que se registraron los emotes. Con 12
/// bailes + 3 sonidos, los sonidos caerían en mitad de la segunda página de bailes.
/// Corremos después de él (<c>HarmonyAfter</c>) y volvemos a repartir: primero todo lo
/// demás, relleno hasta cerrar página, y luego los sonidos solos.
///
/// Respetamos los emotes de otros mods: van con "todo lo demás", no se descartan.
/// </remarks>
[HarmonyPatch(typeof(EmoteWheel), "Start")]
[HarmonyAfter("PEAKEmoteLib")]
internal static class EmoteWheelSoundPagePatch
{
    const int SlicesPerPage = 8;

    [HarmonyPostfix]
    static void Postfix(EmoteWheel __instance)
    {
        if (SoundEmotes.FullNames.Count == 0) return;
        if (__instance.data == null) return;

        int vanillaSlots = EmoteWheelVanillaPageCountPatch.VanillaPages * SlicesPerPage;
        if (__instance.data.Length <= vanillaSlots) return;

        var custom = new List<EmoteWheelData>();
        for (int i = vanillaSlots; i < __instance.data.Length; i++)
        {
            if (__instance.data[i] != null) custom.Add(__instance.data[i]);
        }

        var sounds = custom.Where(d => SoundEmotes.FullNames.Contains(d.anim)).ToList();
        var others = custom.Where(d => !SoundEmotes.FullNames.Contains(d.anim)).ToList();
        if (sounds.Count == 0) return;

        int otherPages = (others.Count + SlicesPerPage - 1) / SlicesPerPage;
        int soundPages = (sounds.Count + SlicesPerPage - 1) / SlicesPerPage;
        int totalPages = EmoteWheelVanillaPageCountPatch.VanillaPages + otherPages + soundPages;

        var rebuilt = new EmoteWheelData[totalPages * SlicesPerPage];
        System.Array.Copy(__instance.data, rebuilt, vanillaSlots);

        for (int i = 0; i < others.Count; i++)
            rebuilt[vanillaSlots + i] = others[i];

        int soundStart = vanillaSlots + otherPages * SlicesPerPage;
        for (int i = 0; i < sounds.Count; i++)
            rebuilt[soundStart + i] = sounds[i];

        __instance.data = rebuilt;
        __instance.pages = totalPages;

        Plugin.Log.LogInfo(
            $"Rueda de emotes: {EmoteWheelVanillaPageCountPatch.VanillaPages} páginas vanilla + " +
            $"{otherPages} de bailes + {soundPages} de sonidos = {totalPages}. " +
            $"Los sonidos están en la página {totalPages}.");
    }
}
