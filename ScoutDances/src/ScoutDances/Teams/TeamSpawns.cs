using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Separa a los equipos al aparecer y los devuelve al último checkpoint al morir.
/// </summary>
/// <remarks>
/// <b>El respawn reaprovecha el del propio juego.</b> <c>RPCA_ReviveAtPosition</c> ya hace
/// exactamente las tres cosas que hacían falta, y en el orden correcto:
///
/// <code>
/// refs.items.DropAllItems(includeBackpack: true);   // sin items
/// ReviveCharacter(applyStatus);                     // en pie
/// WarpPlayer(position, poof: true);                 // en el sitio que le digas
/// </code>
///
/// Se le pasa <c>applyStatus: false</c> a propósito: ese parámetro mete maldición y hambre
/// como penalización por revivir, y aquí queremos que vuelva entero.
///
/// <b>El checkpoint se busca por la hoguera encendida más avanzada.</b> No hay un "último
/// checkpoint" guardado en ninguna parte, pero encender una hoguera ES el checkpoint: cada
/// una lleva su <c>advanceToSegment</c>, así que la encendida con el número más alto es la
/// más lejos que ha llegado el grupo. Si no hay ninguna, se vuelve al punto de salida del
/// equipo.
/// </remarks>
internal class TeamSpawns : MonoBehaviour
{
    /// Cuánto se separan los equipos entre sí al aparecer, en metros.
    internal static float Spread => Plugin.CfgTeamSpawnSpread.Value;

    float _reviveAt;
    bool _placed;
    string _placedFor = "";

    void Update()
    {
        if (!Plugin.CfgTeams.Value) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null) return;

        PlaceAtTeamSpawn(local);
        CheckRespawn(local);
    }

    /// <summary>
    /// Coloca al jugador en el sitio de salida de SU equipo, una vez por escena.
    /// </summary>
    /// <remarks>
    /// Los equipos se reparten en círculo alrededor del punto de salida normal. Se usa el
    /// orden alfabético del marcador para decidir a quién le toca cada sitio: es el mismo
    /// en todas las máquinas sin necesidad de acordar nada por la red, porque todas ven la
    /// misma lista de equipos.
    /// </remarks>
    void PlaceAtTeamSpawn(Character local)
    {
        if (!Plugin.CfgTeamSpawnSeparate.Value) return;

        // En el aeropuerto NO. Allí la gente entra y sale de equipos, y como la clave de
        // colocación incluye el nombre del equipo, cada cambio te teletransportaba lejos
        // mientras estabais organizándoos. Las salidas separadas son para la montaña.
        if (local.inAirport) return;

        var team = TeamState.MyTeam;
        if (team.Length == 0) return;

        // Una vez por equipo y por escena: si no, te reposicionaría cada frame.
        var key = team + "@" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (_placed && _placedFor == key) return;

        var spawn = SpawnPoint.LocalSpawnPoint;
        if (spawn == null) return;

        _placed = true;
        _placedFor = key;

        var target = TeamSpawnPoint(spawn.transform.position, team);

        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, target, false);

        // El número de equipos se registra porque de él depende el reparto: con uno solo
        // no hay a quién separarse y todos caen en el punto de siempre.
        int teams = TeamState.Scoreboard().Count;
        Plugin.Log.LogInfo($"Salida del equipo '{team}' en {target} " +
                           $"({teams} equipo(s), separación {Spread:0.#} m).");
    }

    /// <summary>Reparte los equipos en círculo alrededor de un punto.</summary>
    internal static Vector3 TeamSpawnPoint(Vector3 origin, string team)
    {
        var teams = TeamState.Scoreboard().Select(e => e.Team).OrderBy(t => t).ToList();

        int index = teams.IndexOf(team);
        if (index < 0 || teams.Count <= 1) return origin;

        float angle = index / (float)teams.Count * Mathf.PI * 2f;
        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Spread;

        var target = origin + offset;

        // Apoyado en el suelo: el círculo puede caer en una rampa o sobre un saliente.
        if (Physics.Raycast(target + Vector3.up * 8f, Vector3.down, out var ground, 30f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            target.y = ground.point.y + 0.5f;
        }

        return target;
    }

    void CheckRespawn(Character local)
    {
        if (!Plugin.CfgCheckpointRespawn.Value) return;

        // En el aeropuerto manda LobbyHealth, que ya te levanta en tu punto de entrada.
        if (local.inAirport) return;

        bool down = local.data.dead || local.data.fullyPassedOut;

        if (!down) { _reviveAt = 0f; return; }

        if (_reviveAt == 0f)
        {
            _reviveAt = Time.time + Plugin.CfgCheckpointRespawnDelay.Value;
            return;
        }

        if (Time.time < _reviveAt) return;
        _reviveAt = 0f;

        var point = LastCheckpoint(local);

        // applyStatus false: vuelve entero, sin la maldición ni el hambre con que el juego
        // castiga una reanimación normal. DropAllItems ya va dentro del RPC.
        local.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, point, false, -1);

        Plugin.Log.LogInfo($"Reaparecido en el checkpoint {point}, sin items.");
    }

    /// <summary>La hoguera encendida más avanzada, o la salida del equipo.</summary>
    static Vector3 LastCheckpoint(Character local)
    {
        Campfire? best = null;

        foreach (var campfire in Object.FindObjectsByType<Campfire>(FindObjectsSortMode.None))
        {
            if (campfire == null || !campfire.Lit) continue;
            if (best == null || (int)campfire.advanceToSegment > (int)best.advanceToSegment)
                best = campfire;
        }

        if (best != null) return best.transform.position + Vector3.up * 1.5f;

        var spawn = SpawnPoint.LocalSpawnPoint;
        var origin = spawn != null ? spawn.transform.position : local.Center;

        return TeamSpawnPoint(origin, TeamState.MyTeam) + Vector3.up * 0.5f;
    }
}
