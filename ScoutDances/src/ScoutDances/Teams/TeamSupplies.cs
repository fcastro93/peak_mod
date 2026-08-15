using System.Collections;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Deja una mochila por integrante junto a la salida de cada equipo.
/// </summary>
/// <remarks>
/// <b>Se sueltan en el suelo, no se equipan.</b> Equipar una a cada uno obliga a tocar el
/// inventario de un personaje que no es el tuyo, y eso en PEAK no se puede: cada cliente
/// manda sobre el suyo. Dejándolas en el suelo, cada uno recoge la suya y el juego hace el
/// resto con sus propias reglas.
///
/// <b>Solo las pone el anfitrión.</b> Van con <c>PhotonNetwork.Instantiate</c>, que las
/// crea una vez para toda la sala; si las creara cada cliente saldrían tantas copias como
/// jugadores hubiera.
///
/// <b>Y solo en la montaña.</b> En el aeropuerto la gente entra y sale de equipos
/// constantemente, así que no hay un reparto estable al que repartir nada.
/// </remarks>
internal class TeamSupplies : MonoBehaviour
{
    /// Tramo en el que ya repartimos, para no hacerlo dos veces.
    int _doneSegment = -999;

    void Start() => StartCoroutine(Watch());

    IEnumerator Watch()
    {
        var wait = new WaitForSeconds(3f);

        while (true)
        {
            yield return wait;

            if (!Plugin.CfgTeams.Value || !Plugin.CfgTeamBackpacks.Value) continue;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) continue;

            var local = Character.localCharacter;
            if (local == null || local.inAirport) { _doneSegment = -999; continue; }

            int segment = CurrentSegment();
            if (segment == _doneSegment) continue;

            var spawn = SpawnPoint.LocalSpawnPoint;
            if (spawn == null) continue;

            _doneSegment = segment;
            Deliver(spawn.transform.position);
        }
    }

    static int CurrentSegment()
    {
        try
        {
            return Zorro.Core.Singleton<MapHandler>.Instance != null
                ? (int)Zorro.Core.Singleton<MapHandler>.Instance.GetCurrentSegment()
                : 0;
        }
        catch { return 0; }
    }

    void Deliver(Vector3 spawnOrigin)
    {
        var backpack = FindBackpack();
        if (backpack == null)
        {
            Plugin.Log.LogWarning("No encontré ninguna mochila en el ItemDatabase.");
            return;
        }

        int total = 0;

        // Agrupados por equipo y ordenados por ActorNumber: EXACTAMENTE el mismo criterio
        // con el que TeamState.SlotInTeam reparte los puestos. Si aquí se ordenara de otra
        // forma, la mochila del puesto 2 acabaría en el hueco del puesto 3.
        var byTeam = Photon.Pun.PhotonNetwork.PlayerList
            .Where(p => p != null && TeamState.TeamOf(p).Length > 0)
            .GroupBy(p => TeamState.TeamOf(p));

        foreach (var group in byTeam)
        {
            var team = group.Key;
            var members = group.OrderBy(p => p.ActorNumber).ToList();

            // El sitio de salida de ESE equipo, el mismo cálculo con el que se coloca a
            // sus jugadores: así las mochilas aparecen donde ellos aterrizan y no en la
            // salida de otro.
            var origin = TeamSpawns.TeamSpawnPoint(spawnOrigin, team);

            for (int i = 0; i < members.Count; i++)
            {
                // Junto al hueco de SU dueño, no en un corro aparte. Antes las mochilas
                // iban en un círculo de 2.5 m y los jugadores caían en otro de 3: casi el
                // mismo sitio, así que aparecías con un bulto entre los pies empujándote.
                var owner = TeamSpawns.PersonalSpot(origin, i, members.Count);

                // Un paso hacia fuera del corro, para que quede AL LADO y no debajo.
                var outward = owner - origin;
                outward = outward.sqrMagnitude > 0.01f
                    ? outward.normalized
                    : Vector3.forward;

                var position = owner + outward * Plugin.CfgTeamBackpackSpread.Value
                               + Vector3.up * 1f;

                try
                {
                    PhotonNetwork.Instantiate("0_Items/" + backpack.name, position,
                                              Quaternion.identity, 0);
                    total++;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"No pude soltar una mochila: {e.Message}");
                }
            }

            Plugin.Log.LogInfo($"{members.Count} mochila(s) para '{team}' en {origin}.");
        }

        if (total > 0)
            Plugin.Log.LogInfo($"Repartidas {total} mochilas entre los equipos.");
    }

    /// <summary>
    /// Busca la mochila normal del juego.
    /// </summary>
    /// <remarks>
    /// Por COMPONENTE y no por nombre: los prefabs del database no se llaman como lo que se
    /// ve en pantalla. Se descarta el jetpack, que también lleva <c>Backpack</c> pero
    /// cambia cómo te mueves.
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
