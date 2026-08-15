using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Equipos y puntuación de la competición, sincronizados por la red de Photon.
/// </summary>
/// <remarks>
/// <b>Dónde vive cada cosa, y por qué.</b>
///
/// La PERTENENCIA a un equipo va en las propiedades del jugador
/// (<c>Player.CustomProperties</c>). Photon las replica solas, las mantiene al cambiar de
/// escena y las reenvía a quien entre después: el viaje del aeropuerto a la montaña no se
/// las lleva por delante, que es justo lo que necesitábamos.
///
/// Los PUNTOS van en las propiedades de la sala (<c>Room.CustomProperties</c>), y los
/// escribe ÚNICAMENTE el anfitrión. Es la parte importante: "el primero en llegar" es una
/// carrera, y si cada cliente sumara por su cuenta, dos equipos que enciendan la hoguera
/// con medio segundo de diferencia se darían los 5 puntos los dos. Al pasar todo por una
/// sola máquina hay un único orden de llegada y el segundo ve que el sitio ya está pillado.
///
/// Los avisos se mandan con <c>PhotonNetwork.RaiseEvent</c> y no con un RPC. Un RPC
/// necesita un <c>PhotonView</c> donde vivir, y aquí no hay ningún objeto de la escena que
/// sea nuestro; RaiseEvent va suelto por la sala.
/// </remarks>
internal class TeamState : MonoBehaviour, IOnEventCallback
{
    internal static TeamState? Instance;

    /// Clave de la propiedad de jugador con el nombre de su equipo.
    const string TeamKey = "sd_team";

    /// Clave con la versión del mod que lleva cada uno.
    const string VersionKey = "sd_ver";

    /// Prefijos de las propiedades de sala.
    const string ScorePrefix = "sd_pts_";      // sd_pts_<equipo>      -> int
    const string FirstPrefix = "sd_first_";    // sd_first_<segmento>  -> string (equipo)
    const string CookPrefix = "sd_cook_";      // sd_cook_<seg>_<eq>   -> true

    /// Códigos de evento. Photon reserva del 200 para arriba; por debajo es nuestro.
    const byte EventLit = 101;
    const byte EventCooked = 102;

    internal const int PointsFirst = 5;
    internal const int PointsCook = 3;
    internal const int PointsSurvivor = 5;

    /// Marca de que el reparto final ya se hizo.
    const string FinishKey = "sd_finish";

    void Awake()
    {
        Instance = this;
        PhotonNetwork.AddCallbackTarget(this);
        StartCoroutine(AnnounceVersion());
    }

    /// <summary>
    /// Mantiene publicada la versión mientras estemos en una sala.
    /// </summary>
    /// <remarks>
    /// En un bucle lento y no una sola vez al entrar: las propiedades de jugador se pierden
    /// al salir de la sala, y este objeto sobrevive a los cambios de escena. Cada 5 segundos
    /// no le cuesta nada a nadie, y <c>PublishVersion</c> no manda nada si ya está puesta.
    /// </remarks>
    System.Collections.IEnumerator AnnounceVersion()
    {
        var wait = new WaitForSeconds(5f);

        while (true)
        {
            yield return wait;
            PublishVersion();
        }
    }

    // ------------------------------------------------------------------ versiones

    /// <summary>Versión del mod de un jugador, o "?" si no la ha publicado.</summary>
    /// <remarks>
    /// Devuelve "?" tanto para quien no lleva el mod como para quien lleva una versión
    /// anterior a esta: la propiedad no existía, así que no hay forma de distinguirlos.
    /// </remarks>
    internal static string VersionOf(Photon.Realtime.Player? player)
    {
        if (player == null) return "?";
        return player.CustomProperties != null &&
               player.CustomProperties.TryGetValue(VersionKey, out var value) && value is string v
            ? v
            : "?";
    }

    internal static string MyVersion => Plugin.Instance.Info.Metadata.Version.ToString();

    /// <summary>
    /// Anuncia a la sala qué versión llevo.
    /// </summary>
    /// <remarks>
    /// Es la única forma de saber, sin pedirle a nadie que abra ficheros, si el
    /// actualizador está haciendo su trabajo en los demás ordenadores. El contador de
    /// descargas de GitHub no vale: tarda horas en moverse.
    ///
    /// Se vuelve a mandar en cada entrada a sala porque las propiedades de jugador se
    /// borran al salir.
    /// </remarks>
    internal static void PublishVersion()
    {
        if (!PhotonNetwork.InRoom) return;

        if (VersionOf(PhotonNetwork.LocalPlayer) == MyVersion) return;   // ya está puesta

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [VersionKey] = MyVersion });
    }

    /// <summary>Quién va con una versión distinta a la mía.</summary>
    internal static List<(string Name, string Version)> Mismatched()
    {
        var list = new List<(string, string)>();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player == null || player.IsLocal) continue;

            var version = VersionOf(player);
            if (version != MyVersion) list.Add((player.NickName ?? "?", version));
        }

        return list;
    }

    void OnDestroy()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
        if (Instance == this) Instance = null;
    }

    // ---------------------------------------------------------------- pertenencia

    /// <summary>Equipo de un jugador, o cadena vacía si no tiene.</summary>
    internal static string TeamOf(Photon.Realtime.Player? player)
    {
        if (player == null) return "";
        return player.CustomProperties != null &&
               player.CustomProperties.TryGetValue(TeamKey, out var value) && value is string name
            ? name
            : "";
    }

    internal static string MyTeam => TeamOf(PhotonNetwork.LocalPlayer);

    /// <summary>Qué puesto ocupa un jugador dentro de su equipo, empezando en 0.</summary>
    /// <remarks>
    /// Sirve para que dos compañeros no aterricen en las MISMAS coordenadas. Antes el sitio
    /// de salida se calculaba solo con el nombre del equipo, así que a los tres miembros se
    /// les mandaba al mismo punto: dos ragdolls incrustados uno dentro de otro, y el motor
    /// de física haciendo lo que puede. Es lo que se sentía como "bugueo al cargar".
    ///
    /// Se ordena por <c>ActorNumber</c>, que Photon asigna y no cambia mientras dure la
    /// sala. Da igual que cada cliente calcule esto por su cuenta: todos ven la misma lista
    /// de actores, así que a nadie le toca el puesto de otro. Por nombre no valdría, porque
    /// dos jugadores pueden llamarse igual.
    /// </remarks>
    internal static int SlotInTeam(Photon.Realtime.Player? player)
    {
        var team = TeamOf(player);
        if (player == null || team.Length == 0) return 0;

        var mates = PhotonNetwork.PlayerList
            .Where(p => p != null && TeamOf(p) == team)
            .OrderBy(p => p.ActorNumber)
            .ToList();

        int index = mates.FindIndex(p => p.ActorNumber == player.ActorNumber);
        return index < 0 ? 0 : index;
    }

    /// <summary>Puesto de un jugador entre TODOS los de la sala, empezando en 0.</summary>
    /// <remarks>
    /// Para repartos que no van por equipos, como el rescate a la hoguera: allí llegan
    /// jugadores de equipos distintos a la vez y lo que hay que evitar es que dos caigan
    /// encima, sin importar de quién sean compañeros.
    ///
    /// Mismo criterio que <see cref="SlotInTeam"/>: <c>ActorNumber</c>, que no cambia
    /// mientras dure la sala y todos los clientes ven igual.
    /// </remarks>
    internal static int SlotAmongAll(Photon.Realtime.Player? player)
    {
        if (player == null) return 0;

        var all = PhotonNetwork.PlayerList
            .Where(p => p != null)
            .OrderBy(p => p.ActorNumber)
            .ToList();

        int index = all.FindIndex(p => p.ActorNumber == player.ActorNumber);
        return index < 0 ? 0 : index;
    }

    internal static int PlayerCount => PhotonNetwork.PlayerList?.Length ?? 1;

    /// <summary>Cuánta gente hay en el equipo de ese jugador.</summary>
    internal static int TeamSize(Photon.Realtime.Player? player)
    {
        var team = TeamOf(player);
        if (team.Length == 0) return 1;

        return PhotonNetwork.PlayerList.Count(p => p != null && TeamOf(p) == team);
    }

    /// <summary>Mete al jugador local en un equipo. Cadena vacía = salirse.</summary>
    internal static void JoinTeam(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > 24) name = name.Substring(0, 24);

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [TeamKey] = name });
        Plugin.Log.LogInfo(name.Length > 0 ? $"Te uniste al equipo '{name}'." : "Saliste del equipo.");
    }

    /// <summary>Equipos existentes con sus miembros, ordenados por nombre.</summary>
    internal static List<(string Team, List<string> Members)> Roster()
    {
        var byTeam = new Dictionary<string, List<string>>();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            var team = TeamOf(player);
            if (team.Length == 0) continue;

            if (!byTeam.TryGetValue(team, out var members))
                byTeam[team] = members = new List<string>();

            members.Add(player.NickName);
        }

        return byTeam.OrderBy(e => e.Key)
                     .Select(e => (e.Key, e.Value))
                     .ToList();
    }

    /// <summary>Jugadores todavía sin equipo.</summary>
    internal static List<string> Unassigned() =>
        PhotonNetwork.PlayerList.Where(p => TeamOf(p).Length == 0)
                                .Select(p => p.NickName).ToList();

    // ---------------------------------------------------------------- puntos

    internal static int ScoreOf(string team)
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room?.CustomProperties == null || team.Length == 0) return 0;

        return room.CustomProperties.TryGetValue(ScorePrefix + team, out var value) && value is int points
            ? points
            : 0;
    }

    /// <summary>Marcador ordenado de mayor a menor.</summary>
    internal static List<(string Team, int Points)> Scoreboard()
    {
        var teams = new HashSet<string>();

        // Los equipos salen de los jugadores presentes Y de los puntos ya anotados: así
        // un equipo cuyo último miembro se desconectó sigue apareciendo en el marcador.
        foreach (var player in PhotonNetwork.PlayerList)
        {
            var team = TeamOf(player);
            if (team.Length > 0) teams.Add(team);
        }

        var room = PhotonNetwork.CurrentRoom;
        if (room?.CustomProperties != null)
        {
            foreach (var key in room.CustomProperties.Keys)
            {
                if (key is string name && name.StartsWith(ScorePrefix, System.StringComparison.Ordinal))
                    teams.Add(name.Substring(ScorePrefix.Length));
            }
        }

        return teams.Select(t => (t, ScoreOf(t)))
                    .OrderByDescending(e => e.Item2)
                    .ThenBy(e => e.Item1)
                    .ToList();
    }

    // ---------------------------------------------------------------- avisos

    /// <summary>Avisa de que el jugador local ha encendido la hoguera de un tramo.</summary>
    internal static void ReportLit(int segment) => Report(EventLit, segment);

    /// <summary>Avisa de que el jugador local ha cocinado en la hoguera de un tramo.</summary>
    internal static void ReportCooked(int segment) => Report(EventCooked, segment);

    static void Report(byte code, int segment)
    {
        var team = MyTeam;
        if (team.Length == 0 || !PhotonNetwork.InRoom) return;

        // El anfitrión se lo aplica directamente; los demás se lo mandan. Mismo camino
        // lógico, pero sin dar la vuelta por la red cuando no hace falta.
        if (PhotonNetwork.IsMasterClient)
        {
            Instance?.Award(code, segment, team);
            return;
        }

        PhotonNetwork.RaiseEvent(code, new object[] { segment, team },
                                 new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                                 SendOptions.SendReliable);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != EventLit && photonEvent.Code != EventCooked) return;
        if (!PhotonNetwork.IsMasterClient) return;              // solo puntúa el anfitrión
        if (photonEvent.CustomData is not object[] data || data.Length < 2) return;

        Award(photonEvent.Code, (int)data[0], (string)data[1]);
    }

    /// <summary>
    /// Reparto final: 5 puntos por cada integrante que llegue vivo.
    /// </summary>
    /// <remarks>
    /// Lo hace SOLO el anfitrión, y una única vez: <c>EndGame</c> puede dispararse en más
    /// de una máquina o más de una vez, y sin la marca en la sala el marcador se doblaría
    /// justo en el momento en que ya nadie puede corregirlo.
    ///
    /// Se cuenta por PERSONAJE y no por jugador de Photon porque lo que importa es quién
    /// sigue en pie: un jugador conectado cuyo Scout murió no puntúa.
    /// </remarks>
    internal void AwardSurvivors()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null || !PhotonNetwork.IsMasterClient) return;
        if (room.CustomProperties.ContainsKey(FinishKey)) return;

        var alive = new Dictionary<string, int>();

        foreach (var character in Character.AllCharacters)
        {
            if (character == null || character.data == null) continue;
            if (character.data.dead || character.data.fullyPassedOut) continue;

            var team = TeamOf(character.photonView?.Owner);
            if (team.Length == 0) continue;

            alive.TryGetValue(team, out int count);
            alive[team] = count + 1;
        }

        var properties = new Hashtable { [FinishKey] = true };

        foreach (var (team, count) in alive.Select(e => (e.Key, e.Value)))
        {
            int points = count * PointsSurvivor;
            properties[ScorePrefix + team] = ScoreOf(team) + points;
            Plugin.Log.LogInfo($"'{team}' llegó al final con {count} vivo(s): +{points}.");
        }

        room.SetCustomProperties(properties);
    }

    /// <summary>
    /// Aplica los puntos. Solo corre en el anfitrión.
    /// </summary>
    /// <remarks>
    /// Las dos reglas son "una vez y ya", y por eso se anota en la sala QUIÉN se llevó cada
    /// cosa antes de sumar: el primero en encender queda apuntado en
    /// <c>sd_first_&lt;tramo&gt;</c>, y cada equipo que cocina en
    /// <c>sd_cook_&lt;tramo&gt;_&lt;equipo&gt;</c>. Sin esas marcas, volver a la misma
    /// hoguera daría puntos otra vez.
    /// </remarks>
    internal void Award(byte code, int segment, string team)
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null || team.Length == 0) return;

        var properties = new Hashtable();
        int points;

        if (code == EventLit)
        {
            var key = FirstPrefix + segment;
            if (room.CustomProperties.ContainsKey(key)) return;   // ya hubo un primero

            properties[key] = team;
            points = PointsFirst;
            Plugin.Log.LogInfo($"'{team}' llegó primero al tramo {segment}: +{points}.");
        }
        else
        {
            // Los 3 puntos son para quien NO fue el primero: el que enciende ya cobró 5.
            var firstKey = FirstPrefix + segment;
            if (room.CustomProperties.TryGetValue(firstKey, out var first) &&
                first is string firstTeam && firstTeam == team) return;

            var key = CookPrefix + segment + "_" + team;
            if (room.CustomProperties.ContainsKey(key)) return;   // este equipo ya cocinó aquí

            properties[key] = true;
            points = PointsCook;
            Plugin.Log.LogInfo($"'{team}' cocinó en el tramo {segment}: +{points}.");
        }

        properties[ScorePrefix + team] = ScoreOf(team) + points;
        room.SetCustomProperties(properties);
    }
}
