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

    /// <summary>Dónde devolver a quien acaba de morir.</summary>
    /// <remarks>
    /// <b>Por qué no se busca la hoguera recorriendo la escena.</b> Era lo que hacía antes, y
    /// tiraba a la gente al mar. Al pasar de tramo el juego DESTRUYE lo del anterior —tiene
    /// un campo llamado <c>viewsToDestoryIfNotAlreadyWhenSwitchingSegments</c>— así que en el
    /// segundo mapa la hoguera que acababas de encender ya no existe como objeto, y la del
    /// tramo nuevo todavía no está encendida. La búsqueda no encontraba ninguna y se caía al
    /// último recurso: <c>SpawnPoint.LocalSpawnPoint</c>, que es la salida del aeropuerto y
    /// está a kilómetros. De ahí el agua.
    ///
    /// Ahora se le pregunta al juego, que lleva la cuenta él solo y no depende de que el
    /// objeto siga vivo. <c>CurrentBaseCampSpawnPoint</c> es justo el sitio donde te deja al
    /// entrar en el tramo actual, o sea el checkpoint.
    ///
    /// Cada rama deja su rastro en el log: si esto vuelve a fallar, hay que saber por cuál
    /// se fue sin tener que adivinarlo.
    /// </remarks>
    static Vector3 LastCheckpoint(Character local)
    {
        // 1. El punto de entrada del tramo actual, que es la respuesta buena.
        var basecamp = MapHandler.CurrentBaseCampSpawnPoint;
        if (basecamp != null)
            return Grounded(basecamp.position + Vector3.up * 1f, local, "campamento del tramo");

        // 2. Las hogueras que el juego sigue reconociendo, la de este tramo primero.
        foreach (var (campfire, label) in new[]
                 {
                     (MapHandler.CurrentCampfire, "hoguera del tramo"),
                     (MapHandler.PreviousCampfire, "hoguera anterior"),
                 })
        {
            if (campfire != null)
                return Grounded(campfire.transform.position + Vector3.up * 1.5f, local, label);
        }

        // 3. Cualquier hoguera encendida que quede en pie.
        var lit = Object.FindObjectsByType<Campfire>(FindObjectsSortMode.None)
                        .Where(c => c != null && c.Lit)
                        .OrderByDescending(c => (int)c.advanceToSegment)
                        .FirstOrDefault();

        if (lit != null)
            return Grounded(lit.transform.position + Vector3.up * 1.5f, local, "hoguera encendida");

        // 4. Sin nada de lo anterior, en el sitio: quedarse donde moriste es feo, pero es
        //    dentro del mapa. El punto del aeropuerto ya nos costó un baño.
        Plugin.Log.LogWarning("No encontré ningún checkpoint; te dejo donde estabas.");
        return local.Center + Vector3.up * 1f;
    }

    /// <summary>
    /// Comprueba que haya suelo debajo y apoya el punto encima.
    /// </summary>
    /// <remarks>
    /// Una red por si el sitio que da el juego queda en el aire o sobre agua. Si no hay
    /// suelo en 60 m hacia abajo, ese punto no vale y se devuelve al jugador a donde estaba,
    /// que al menos es terreno de la partida.
    /// </remarks>
    static Vector3 Grounded(Vector3 target, Character local, string source)
    {
        if (Physics.Raycast(target + Vector3.up * 5f, Vector3.down, out var hit, 60f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            var landed = hit.point + Vector3.up * 0.5f;
            Plugin.Log.LogInfo($"Reaparición en '{source}' {landed}.");
            return landed;
        }

        Plugin.Log.LogWarning($"'{source}' {target} no tiene suelo debajo; te dejo donde estabas.");
        return local.Center + Vector3.up * 1f;
    }
}
