using HarmonyLib;
using ScoutDances.Sounds;

namespace ScoutDances.Patches;

/// <summary>
/// Hace que la rueda muestre el NOMBRE del sonido asignado en vez de "Sonido 1".
/// </summary>
/// <remarks>
/// El texto de un emote se fija al registrarlo, con
/// <c>Emote.AddLocalization</c>, y eso ocurre una sola vez al arrancar. Pero el sonido
/// de cada ranura cambia cada vez que lo reasignas en el kiosco, así que no sirve.
///
/// <c>EmoteWheel.Hover</c> hace <c>selectedEmoteName.text = LocalizedText.GetText(...)</c>,
/// de modo que un postfix nos deja sobrescribir ese texto con el nombre real del MP3
/// justo cuando el jugador pasa por encima de la ranura.
/// </remarks>
[HarmonyPatch(typeof(EmoteWheel), "Hover")]
internal static class EmoteWheelHoverPatch
{
    [HarmonyPostfix]
    static void Postfix(EmoteWheel __instance, EmoteWheelData emoteWheelData)
    {
        if (emoteWheelData == null || __instance.selectedEmoteName == null) return;

        int slot = SoundEmotes.SlotOf(emoteWheelData.anim);
        if (slot < 0) return;

        var path = SoundSlots.GetLocalPath(slot);
        __instance.selectedEmoteName.text = path.Length == 0
            ? $"Sonido {slot + 1} — vacío"
            : InstantAudioCache.PrettyName(path);
    }
}
