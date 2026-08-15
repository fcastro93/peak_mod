using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Cuando sube lo que mata, se acabaron las resurrecciones.
/// </summary>
/// <remarks>
/// <b>Qué es "la neblina".</b> En la playa es niebla, más arriba es lava y en otro tramo es
/// penumbra, pero por dentro es el mismo sistema: <c>LavaRising</c>, con un campo
/// <c>risingFieldType</c> que decide qué aspecto tiene. Por eso no se busca "la niebla" sino
/// cualquier <c>LavaRising</c> que haya arrancado — así la regla vale en todos los tramos sin
/// tener que enumerarlos.
///
/// <b>Lo que hace la regla.</b> Mientras eso sube, quien muere se queda fantasma: ni la
/// reaparición en el checkpoint ni las estatuas de equipo lo levantan. Es lo que convierte
/// la subida en una cuenta atrás de verdad — hasta ahora podías morir en la niebla y volver
/// como si nada, que le quitaba todo el peligro.
///
/// <b>Y el final.</b> No se fuerza el cierre de la partida a mano: cuando ya no queda nadie
/// a quien revivir, el propio juego termina la ronda. Lo que sí se hace es sacar el marcador
/// de equipos en ese momento, para que se vea quién ganó antes de volver al aeropuerto.
/// Forzar el final por nuestra cuenta se pelearía con el cierre del juego y con el reparto
/// de puntos de los supervivientes.
/// </remarks>
internal class FogRules : MonoBehaviour
{
    static bool _wasActive;
    static bool _shownScores;

    /// <summary>¿Está subiendo ahora mismo algo que mata?</summary>
    internal static bool Rising
    {
        get
        {
            if (!Plugin.CfgFogNoRevive.Value) return false;

            try
            {
                var all = LavaRising.ALL_LAVA;
                if (all == null) return false;

                return all.Any(l => l != null && l.started && !l.ended);
            }
            catch { return false; }
        }
    }

    void Update()
    {
        if (!PhotonNetwork.InRoom) return;

        bool rising = Rising;

        if (rising != _wasActive)
        {
            _wasActive = rising;
            Plugin.Log.LogWarning(rising
                ? "Empezó a subir: a partir de ahora quien muera se queda fantasma."
                : "Dejó de subir: se puede volver a revivir.");
        }

        if (!rising) { _shownScores = false; return; }

        // Todos fuera de combate: enseñamos el marcador una vez. El cierre de la partida lo
        // hace el juego, que ya sabe cuándo no queda nadie en pie.
        if (_shownScores) return;

        var characters = PlayerHandler.GetAllPlayerCharacters();
        if (characters == null || characters.Count == 0) return;

        bool anyoneAlive = characters.Any(
            c => c != null && c.data != null && !c.data.dead && !c.data.fullyPassedOut);

        if (anyoneAlive) return;

        _shownScores = true;
        Plugin.Log.LogWarning("La niebla se llevó a todo el mundo: partida terminada.");
        TeamMenu.ShowFinalScores();
    }
}

/// <summary>
/// Las estatuas no funcionan mientras sube la niebla.
/// </summary>
/// <remarks>
/// Se bloquea la interacción y no solo el revivir, para que el cartel lo diga antes de que
/// alguien cruce media montaña a buscar una estatua que no le va a servir.
/// </remarks>
[HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.IsInteractible))]
internal static class FogBlocksStatuePatch
{
    [HarmonyPostfix]
    static void Postfix(ref bool __result)
    {
        if (__result && FogRules.Rising) __result = false;
    }
}

/// <summary>Y lo explica al apuntarla.</summary>
[HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.GetInteractionText))]
internal static class FogStatueTextPatch
{
    [HarmonyPostfix]
    static void Postfix(ref string __result)
    {
        if (FogRules.Rising) __result = "Ya no revive a nadie";
    }
}
