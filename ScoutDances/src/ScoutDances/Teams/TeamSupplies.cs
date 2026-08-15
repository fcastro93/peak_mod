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

        foreach (var (team, members) in TeamState.Roster())
        {
            // El sitio de salida de ESE equipo, el mismo cálculo con el que se coloca a
            // sus jugadores: así las mochilas aparecen donde ellos aterrizan y no en la
            // salida de otro.
            var origin = TeamSpawns.TeamSpawnPoint(spawnOrigin, team);

            for (int i = 0; i < members.Count; i++)
            {
                // En corro alrededor del punto, separadas: amontonadas en el mismo sitio
                // se empujan entre ellas y salen rodando cuesta abajo.
                float angle = i / (float)Mathf.Max(1, members.Count) * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                             * Plugin.CfgTeamBackpackSpread.Value;

                var position = origin + offset + Vector3.up * 1f;

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
