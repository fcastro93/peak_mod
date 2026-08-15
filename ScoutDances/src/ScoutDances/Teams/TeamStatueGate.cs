using HarmonyLib;

namespace ScoutDances.Teams;

/// <summary>
/// Dice de quién es cada estatua antes de que la pulses.
/// </summary>
/// <remarks>
/// La reja ya existe —<c>StatueInteractPatch</c> impide usar la ajena y
/// <c>StatueRevivePatch</c> levanta solo a los del dueño— pero desde fuera todas se veían
/// iguales. Con tres en fila y sin etiqueta, la única forma de saber cuál era la tuya era
/// probarlas una a una y ver en cuál no pasaba nada.
///
/// Si la estatua no lleva dueño es una del juego, y ahí no se toca el texto: en una partida
/// sin equipos no habría a quién atribuírsela.
/// </remarks>
[HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.GetInteractionText))]
internal static class StatueTextPatch
{
    [HarmonyPostfix]
    static void Postfix(RespawnChest __instance, ref string __result)
    {
        if (!Plugin.CfgTeams.Value || !Plugin.CfgTeamStatues.Value) return;

        var statue = __instance != null ? __instance.GetComponent<TeamStatue>() : null;
        if (statue == null || statue.Owner.Length == 0) return;

        __result = statue.Owner == TeamState.MyTeam
            ? $"{__result}  (tu equipo)"
            : $"Estatua de '{statue.Owner}'";
    }
}
