using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Detecta los dos hechos que dan puntos: encender la hoguera y cocinar en ella.
/// </summary>
/// <remarks>
/// Se engancha en <c>Interact_CastFinished</c> y no en el <c>Light_Rpc</c> que llega a
/// todos, porque el RPC no dice QUIÉN la encendió — y sin eso no hay a quién dar los
/// puntos. El método de interacción sí recibe al personaje que completó el gesto.
/// </remarks>
[HarmonyPatch(typeof(Campfire), nameof(Campfire.Interact_CastFinished))]
internal static class CampfireLitPatch
{
    [HarmonyPostfix]
    static void Postfix(Campfire __instance, Character interactor)
    {
        if (!Plugin.CfgTeams.Value || __instance == null || interactor == null) return;

        // Solo informa el cliente de quien la encendió: el postfix corre en su máquina,
        // y dejar que informara cualquiera duplicaría el aviso por cada jugador.
        if (!interactor.IsLocal) return;

        TeamState.ReportLit((int)__instance.advanceToSegment);
    }
}

/// <summary>
/// Puntúa la primera vez que un equipo cocina en la hoguera de un tramo.
/// </summary>
/// <remarks>
/// El juego avisa de que algo se ha cocinado, pero no de quién lo puso al fuego: la comida
/// se suelta en la hoguera y allí se cocina sola. Así que atribuimos por cercanía —el
/// jugador más próximo a la comida— y solo informa ese cliente. No es perfecto si dos
/// jugadores de equipos distintos están pegados a la misma hoguera, pero cubre el caso
/// real: cada equipo cocina en su turno.
/// </remarks>
[HarmonyPatch(typeof(Item), nameof(Item.SetCookedAmountRPC))]
internal static class CampfireCookPatch
{
    /// A qué distancia de la comida se considera que es tuya.
    const float ClaimRadius = 6f;

    /// Y a qué distancia tiene que estar la hoguera para contar como "cocinar aquí".
    const float FireRadius = 8f;

    [HarmonyPostfix]
    static void Postfix(Item __instance, int amount)
    {
        if (!Plugin.CfgTeams.Value || __instance == null || amount <= 0) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null || local.data.dead) return;
        if (TeamState.MyTeam.Length == 0) return;

        var food = __instance.transform.position;

        if ((local.Center - food).sqrMagnitude > ClaimRadius * ClaimRadius) return;

        // ¿Soy el más cercano? Si no, que informe el otro; así el aviso sale una sola vez.
        foreach (var other in Character.AllCharacters)
        {
            if (other == null || other == local || other.data == null || other.data.dead) continue;
            if ((other.Center - food).sqrMagnitude < (local.Center - food).sqrMagnitude) return;
        }

        var campfire = NearestCampfire(food);
        if (campfire == null) return;

        TeamState.ReportCooked((int)campfire.advanceToSegment);
    }

    static Campfire? NearestCampfire(Vector3 position)
    {
        Campfire? best = null;
        float bestDistance = FireRadius * FireRadius;

        foreach (var campfire in Object.FindObjectsByType<Campfire>(FindObjectsSortMode.None))
        {
            if (campfire == null || !campfire.Lit) continue;

            float distance = (campfire.transform.position - position).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = campfire;
        }

        return best;
    }
}

/// <summary>Al acabar la partida, saca el marcador final.</summary>
[HarmonyPatch(typeof(Character), "EndGame")]
internal static class EndGamePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        if (!Plugin.CfgTeams.Value) return;

        // Primero se reparten los puntos por supervivientes y luego se abre el marcador.
        // No hace falta esperar a que lleguen: el panel lee las propiedades de la sala en
        // cada frame, así que las sumas aparecen solas en cuanto Photon las replica.
        TeamState.Instance?.AwardSurvivors();
        TeamMenu.ShowFinalScores();
    }
}
