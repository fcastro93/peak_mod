using System.Collections.Generic;
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

    /// Cuándo vimos por primera vez a este personaje fuera del aeropuerto.
    float _awakeSince;
    string _awakeFor = "";

    /// Último tramo visto, para detectar cuándo el mapa avanza.
    int _lastSegment = int.MinValue;

    void Update()
    {
        if (!Plugin.CfgTeams.Value) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null) return;

        PullToCampfire(local);
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

        // NO mientras se está levantando. Al cargar el mapa el personaje despierta tumbado
        // y el juego reproduce la animación de ponerse en pie; teletransportarlo justo ahí
        // deja el ragdoll peleándose con esa animación y se queda dando saltitos en bucle
        // sin llegar a levantarse nunca. Es el "bugueo al cargar".
        //
        // Se espera a que esté de pie de verdad y se reintenta al frame siguiente.
        if (!StandingUp(local, key)) return;

        _placed = true;
        _placedFor = key;

        var target = PersonalSpot(TeamSpawnPoint(spawn.transform.position, team), local);

        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, target, false);

        // El número de equipos se registra porque de él depende el reparto: con uno solo
        // no hay a quién separarse y todos caen en el punto de siempre.
        int teams = TeamState.Scoreboard().Count;
        int slot = TeamState.SlotInTeam(PhotonNetwork.LocalPlayer);
        Plugin.Log.LogInfo($"Salida del equipo '{team}' en {target} " +
                           $"({teams} equipo(s), separación {Spread:0.#} m, " +
                           $"puesto {slot + 1} de {TeamState.TeamSize(PhotonNetwork.LocalPlayer)}).");
    }

    /// <summary>
    /// Sube a la hoguera a quien se quede atrás cuando otro equipo enciende y el mapa avanza.
    /// </summary>
    /// <remarks>
    /// Al avanzar de tramo el juego DESCARGA el anterior. Quien siguiera escalando por ahí
    /// se queda sin suelo bajo los pies, en el vacío, sin nada que hacer salvo esperar.
    ///
    /// <b>Cada uno se rescata a sí mismo.</b> En PEAK cada cliente manda sobre su propio
    /// personaje, así que no se puede tirar de los demás desde aquí: lo que se hace es que
    /// todos vigilen el cambio de tramo y quien esté lejos se mueva solo. El resultado es el
    /// mismo y no hay que pelearse con la autoridad de red.
    ///
    /// <b>Solo a los que están lejos.</b> Quien ya estaba en la hoguera no se toca: dar un
    /// tirón a alguien que está donde debe es peor que no hacer nada.
    ///
    /// El sitio sale del puesto entre TODOS los jugadores, no entre los del equipo: aquí
    /// llegan de equipos distintos a la vez, y lo que hay que evitar es que dos aparezcan
    /// encima, sean compañeros o no.
    /// </remarks>
    void PullToCampfire(Character local)
    {
        if (!Plugin.CfgPullToCampfire.Value) return;
        if (local.inAirport || local.data.dead) return;

        int segment = CurrentSegment();

        // Primera lectura: solo tomar nota, sin mover a nadie.
        if (_lastSegment == int.MinValue) { _lastSegment = segment; return; }
        if (segment == _lastSegment) return;

        _lastSegment = segment;

        var checkpoint = LastCheckpoint(local);
        float distance = Vector3.Distance(local.Center, checkpoint);

        if (distance <= Plugin.CfgPullDistance.Value)
        {
            Plugin.Log.LogInfo($"Tramo {segment}: ya estabas en la hoguera ({distance:0} m), " +
                               "no te muevo.");
            return;
        }

        var spot = PersonalSpot(checkpoint,
                                TeamState.SlotAmongAll(PhotonNetwork.LocalPlayer),
                                TeamState.PlayerCount);

        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot, true);

        Plugin.Log.LogInfo($"Tramo {segment}: estabas a {distance:0} m, te subo a la " +
                           $"hoguera en {spot}.");
    }

    static int CurrentSegment()
    {
        try
        {
            return Zorro.Core.Singleton<MapHandler>.Instance != null
                ? (int)Zorro.Core.Singleton<MapHandler>.Instance.GetCurrentSegment()
                : int.MinValue;
        }
        catch { return int.MinValue; }
    }

    /// <summary>
    /// ¿Ya terminó de levantarse y se le puede mover sin romperlo?
    /// </summary>
    /// <remarks>
    /// <c>currentRagdollControll</c> es cuánto manda la animación sobre el muñeco: 0 es un
    /// trapo y 1 es de pie y bajo control. Mientras se levanta va subiendo, y ese es
    /// justamente el rato en el que no hay que tocarlo.
    ///
    /// Se pide además <c>groundedFor</c>, porque el valor de control también llega a 1 en
    /// el aire, y <c>IsInitialized</c>, que es la propia señal del juego de que el personaje
    /// ya está montado del todo.
    ///
    /// <b>Con plazo máximo.</b> Si en 20 segundos no se cumple —un personaje colgado de una
    /// cuerda, uno al que llevan en brazos— se coloca igualmente. Quedarse sin separar a los
    /// equipos es un fastidio; quedarse esperando para siempre y no separarlos nunca, peor.
    /// </remarks>
    bool StandingUp(Character local, string key)
    {
        if (_awakeFor != key)
        {
            _awakeFor = key;
            _awakeSince = Time.time;
        }

        bool late = Time.time - _awakeSince > 20f;

        bool ready = local.IsInitialized &&
                     !local.data.dead && !local.data.passedOut &&
                     local.data.currentRagdollControll > 0.9f &&
                     local.data.groundedFor > 0.4f;

        if (ready || late)
        {
            if (late && !ready)
                Plugin.Log.LogWarning("Se acabó el plazo esperando a que te levantaras; " +
                                      "te coloco igual.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Aparta a cada compañero de equipo a su propio hueco, para que no caigan encima.
    /// </summary>
    /// <remarks>
    /// Dos personajes teletransportados a las MISMAS coordenadas se quedan incrustados: los
    /// dos ragdolls se solapan, el motor intenta separarlos empujando con todo, y el
    /// resultado son los saltos y sacudidas que se sentían al cargar. No es un fallo de red
    /// ni de sincronización, es física.
    ///
    /// Reparto en círculo por puesto dentro del equipo, no al azar: si fuera aleatorio, dos
    /// podrían sacar sitios casi iguales y volveríamos a lo mismo. Con el puesto, la
    /// separación está garantizada.
    ///
    /// El radio crece con el tamaño del equipo para que el hueco entre dos vecinos no
    /// dependa de cuántos sean: con un radio fijo, seis personas volverían a rozarse.
    /// </remarks>
    static Vector3 PersonalSpot(Vector3 teamSpot, Character local) =>
        PersonalSpot(teamSpot, TeamState.SlotInTeam(PhotonNetwork.LocalPlayer),
                     TeamState.TeamSize(PhotonNetwork.LocalPlayer));

    /// <summary>
    /// El hueco del puesto <paramref name="slot"/> dentro de un equipo de
    /// <paramref name="size"/>.
    /// </summary>
    /// <remarks>
    /// Separado para que las mochilas usen EXACTAMENTE la misma cuenta que los jugadores.
    /// Cuando cada uno la calculaba por su lado, las mochilas caían en un corro de 2.5 m y
    /// los jugadores en otro de 3 m: casi encima, así que aparecías con un bulto entre los
    /// pies empujándote. Compartiendo la fórmula, cada uno aterriza en su hueco y su mochila
    /// está al lado, no debajo.
    /// </remarks>
    internal static Vector3 PersonalSpot(Vector3 teamSpot, int slot, int size)
    {
        if (size <= 1) return teamSpot;

        float gap = Plugin.CfgTeamMemberSpread.Value;

        // Radio que mantiene 'gap' metros de arco entre vecinos.
        float radius = Mathf.Max(gap, gap * size / (2f * Mathf.PI));
        float angle = slot / (float)size * Mathf.PI * 2f;

        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        // Mismas comprobaciones que para la salida del equipo. Aquí la distancia es corta,
        // así que casi siempre vale el primer intento; pero un compañero puede tocarle
        // justo el borde de un lago o una roca, y entonces se le busca otro hueco.
        return SafeNear(teamSpot, offset, $"el puesto {slot + 1}");
    }

    // ------------------------------------------------------- sitio seguro donde aterrizar

    /// <summary>
    /// Busca suelo firme cerca del sitio ideal, evitando agua, vacío y objetos.
    /// </summary>
    /// <remarks>
    /// Hace falta porque al separar los equipos 90 m del punto de salida, ese punto ya no
    /// es "el mismo sitio un poco más allá": puede caer en el mar, en un barranco o encima
    /// de una roca. El punto de salida del juego está elegido a mano y es bueno; a 90 m no
    /// hay ninguna garantía.
    ///
    /// Se prueban varias posiciones y se acepta la primera que pase las tres pruebas. Si
    /// ninguna pasa, se vuelve al punto de salida original: apiñarse es un fastidio menor
    /// que aparecer ahogándose.
    ///
    /// El orden de las alternativas importa: primero se gira alrededor del origen
    /// conservando la distancia —para no romper la separación entre equipos, que es lo que
    /// se buscaba— y solo después se acorta el radio.
    /// </remarks>
    static Vector3 SafeNear(Vector3 origin, Vector3 ideal, string what)
    {
        foreach (var candidate in Candidates(origin, ideal))
        {
            if (TryGround(candidate, out var spot)) return spot;
        }

        Plugin.Log.LogWarning($"No encontré suelo seguro para {what} cerca de {origin + ideal}; " +
                              "te dejo en el punto de salida del juego.");

        return TryGround(origin, out var fallback) ? fallback : origin;
    }

    /// <summary>Posiciones a probar, de la ideal a la más conservadora.</summary>
    static IEnumerable<Vector3> Candidates(Vector3 origin, Vector3 ideal)
    {
        yield return origin + ideal;

        // Giros conservando la distancia: mantienen la separación entre equipos.
        foreach (float degrees in new[] { 15f, -15f, 30f, -30f, 50f, -50f, 75f, -75f, 110f, -110f, 180f })
            yield return origin + Quaternion.Euler(0f, degrees, 0f) * ideal;

        // Y si nada vale, acercándose al origen, que es terreno conocido.
        foreach (float factor in new[] { 0.7f, 0.45f, 0.25f })
            foreach (float degrees in new[] { 0f, 60f, -60f, 140f, -140f })
                yield return origin + Quaternion.Euler(0f, degrees, 0f) * ideal * factor;
    }

    /// <summary>¿Hay suelo firme, seco y despejado bajo este punto?</summary>
    /// <remarks>
    /// El rayo sale de MUY arriba y es largo: a 90 m de distancia el terreno puede estar
    /// decenas de metros por encima o por debajo, y el rayo corto de antes —8 m arriba, 30
    /// de alcance— simplemente no llegaba, así que se daba por bueno un punto flotando en
    /// el aire con la altura del origen.
    /// </remarks>
    internal static bool TryGround(Vector3 candidate, out Vector3 spot)
    {
        spot = candidate;

        if (!Physics.Raycast(candidate + Vector3.up * 150f, Vector3.down, out var hit, 400f,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        spot = hit.point + Vector3.up * 0.5f;

        if (InWater(spot)) return false;
        if (Blocked(spot)) return false;

        return true;
    }

    static WaterZone[] _waterZones = System.Array.Empty<WaterZone>();
    static float _waterZonesAt = -999f;

    /// <summary>¿Cae dentro de alguna zona de agua?</summary>
    /// <remarks>
    /// La lista se cachea unos segundos a propósito: se prueban hasta 27 posiciones por
    /// colocación, y recorrer la escena entera en cada una sería un tirón perfectamente
    /// evitable. Unos segundos de antigüedad no importan, porque las zonas de agua no se
    /// mueven; solo cambian al cargar un tramo nuevo, y para entonces la caché ya caducó.
    /// </remarks>
    static bool InWater(Vector3 point)
    {
        try
        {
            if (Time.time - _waterZonesAt > 5f)
            {
                _waterZones = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
                _waterZonesAt = Time.time;
            }

            foreach (var zone in _waterZones)
            {
                if (zone != null && zone.zoneBounds.Contains(point)) return true;
            }
        }
        catch { /* si no se puede consultar, no bloqueamos por ello */ }

        return false;
    }

    /// <summary>¿Hay algo ocupando ya ese hueco?</summary>
    /// <remarks>
    /// La esfera se centra un metro por encima del suelo y es más pequeña que esa altura, o
    /// sea que no toca el propio terreno: lo que detecta son rocas, árboles, maletas y demás
    /// cosas dentro de las que no queremos que nadie aparezca.
    /// </remarks>
    static bool Blocked(Vector3 spot) =>
        Physics.CheckSphere(spot + Vector3.up * 1f, 0.6f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

    /// <summary>Reparte los equipos en círculo alrededor de un punto.</summary>
    internal static Vector3 TeamSpawnPoint(Vector3 origin, string team)
    {
        var teams = TeamState.Scoreboard().Select(e => e.Team).OrderBy(t => t).ToList();

        int index = teams.IndexOf(team);
        if (index < 0 || teams.Count <= 1) return origin;

        float angle = index / (float)teams.Count * Mathf.PI * 2f;
        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Spread;

        return SafeNear(origin, offset, $"la salida de '{team}'");
    }

    void CheckRespawn(Character local)
    {
        if (!Plugin.CfgCheckpointRespawn.Value) return;

        // Con la niebla subiendo, morir es definitivo: te quedas fantasma. Es lo que le da
        // peso a la cuenta atrás; si no, morir ahí dentro no costaría nada.
        if (FogRules.Rising) return;

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

        // Con su hueco propio: dos compañeros que mueren a la vez volverían al mismo punto
        // de la hoguera y se incrustarían igual que en la salida.
        var point = PersonalSpot(LastCheckpoint(local), local);

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
