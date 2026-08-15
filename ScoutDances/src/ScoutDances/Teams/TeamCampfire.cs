using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Para encender la hoguera basta con TU equipo, no con la partida entera.
/// </summary>
/// <remarks>
/// El juego exige que estén todos, vivos y cerca:
///
/// <code>
/// foreach (Character c in PlayerHandler.GetAllPlayerCharacters())
///     if (!c.data.dead &amp;&amp; !PlayerCharactersInRadius(range).Contains(c)) return false;
/// </code>
///
/// Con la partida en modo competición eso no funciona: el equipo que llega primero se
/// queda esperando al rival, que precisamente va detrás. Se sustituye la comprobación por
/// una que solo mira a los del equipo de quien está delante de la hoguera.
///
/// <b>Se juzga desde el jugador LOCAL.</b> El método no recibe quién quiere encenderla,
/// pero es que no hace falta: cada cliente evalúa si su propio equipo está reunido, que es
/// justo la pregunta que le interesa a quien tiene la hoguera delante.
///
/// Se parchean las DOS versiones. La que devuelve texto es la que pinta "no puedes
/// encenderla, falta fulano a 80 m" en el cartel de interacción; si solo arreglásemos la
/// otra, el cartel seguiría pidiendo gente que ya no hace falta.
/// </remarks>
internal static class TeamCampfireGate
{
    /// <summary>¿Están todos los del equipo del jugador local dentro del radio?</summary>
    internal static bool TeamInRange(Campfire fire, float range, out string printout)
    {
        printout = "";

        var team = TeamState.MyTeam;
        if (team.Length == 0) return true;   // sin equipo, que no estorbe

        var near = fire.PlayerCharactersInRadius(range);
        var position = fire.transform.position;
        bool all = true;

        foreach (var character in PlayerHandler.GetAllPlayerCharacters())
        {
            if (character == null || character.data == null || character.data.dead) continue;
            if (near.Contains(character)) continue;

            // Solo cuentan los del MISMO equipo. Los rivales pueden estar donde quieran.
            if (TeamState.TeamOf(character.photonView?.Owner) != team) continue;

            all = false;
            float distance = Vector3.Distance(position, character.Center);
            printout += $"\n{character.photonView.Owner.NickName} " +
                        $"{Mathf.RoundToInt(distance * CharacterStats.unitsToMeters)}m";
        }

        if (!all) printout = LocalizedText.GetText("CANTLIGHT") + "\n" + printout;

        return all;
    }
}

/// <summary>Versión sin texto: la que decide si la hoguera se enciende.</summary>
[HarmonyPatch(typeof(Campfire), nameof(Campfire.EveryoneInRange), new[] { typeof(float) })]
internal static class CampfireRangePatch
{
    [HarmonyPrefix]
    static bool Prefix(Campfire __instance, float range, ref bool __result)
    {
        if (!Plugin.CfgTeams.Value || !Plugin.CfgTeamCampfire.Value) return true;

        __result = TeamCampfireGate.TeamInRange(__instance, range, out _);
        return false;
    }
}

/// <summary>Versión con texto: la del cartel que dice a quién falta.</summary>
[HarmonyPatch(typeof(Campfire), nameof(Campfire.EveryoneInRange),
              new[] { typeof(string), typeof(float) }, new[] { ArgumentType.Out, ArgumentType.Normal })]
internal static class CampfireRangeTextPatch
{
    [HarmonyPrefix]
    static bool Prefix(Campfire __instance, out string printout, float range, ref bool __result)
    {
        if (!Plugin.CfgTeams.Value || !Plugin.CfgTeamCampfire.Value)
        {
            printout = "";
            return true;
        }

        __result = TeamCampfireGate.TeamInRange(__instance, range, out printout);
        return false;
    }
}

/// <summary>
/// Evita la excepción por frame de <c>CharacterItems.UpdatePocketBehaviors</c>.
/// </summary>
/// <remarks>
/// <c>Character.player</c> no es un campo, es una búsqueda:
///
/// <code>
/// public Player player => PlayerHandler.GetPlayer(view.Owner);
/// </code>
///
/// Devuelve null si el Player de ese personaje aún no está registrado —o ya no lo está, si
/// alguien se desconectó— y entonces esto revienta:
///
/// <code>
/// ItemSlot[] itemSlots = character.player.itemSlots;
/// </code>
///
/// Al estar dentro de <c>Update</c>, no falla una vez: falla en CADA FRAME mientras dure la
/// situación. En el log de una sola partida había 1590. Capturar una traza de excepción es
/// carísimo, y ese pico por frame es lo que hacía que el ragdoll se sacudiera y los pies
/// dieran saltos raros.
///
/// El parche no arregla la causa —que el jugador no esté registrado es cosa del juego y su
/// red— sino que deja de hacerla catastrófica: si no hay a quién mirarle los bolsillos, se
/// salta el frame en silencio.
/// </remarks>
[HarmonyPatch(typeof(CharacterItems), "UpdatePocketBehaviors")]
internal static class PocketBehaviorGuardPatch
{
    static bool _warned;

    [HarmonyPrefix]
    static bool Prefix(CharacterItems __instance)
    {
        var player = __instance.character?.player;
        if (player?.itemSlots == null)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning(
                    "Un personaje sin Player registrado: me salto sus bolsillos. " +
                    "Sin esto el juego lanzaba una excepción por frame y todo se sacudía.");
            }
            return false;
        }

        // Un hueco nulo revienta igual, y los puede dejar una desincronización de
        // inventario. Se rellenan en vez de dejar que explote.
        for (int i = 0; i < player.itemSlots.Length; i++)
            player.itemSlots[i] ??= new ItemSlot((byte)i);

        return true;
    }
}
