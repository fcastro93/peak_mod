using UnityEngine;
using UnityEngine.InputSystem;

namespace ScoutDances.Teams;

/// <summary>
/// Panel de equipos: se montan en el aeropuerto y se consultan los puntos en partida.
/// </summary>
/// <remarks>
/// Una sola ventana para las dos cosas, como pediste. Lo que cambia es qué se puede tocar:
/// en el aeropuerto salen los botones de crear y unirse; en la montaña el equipo ya está
/// cerrado y solo queda el marcador. Cerrarlo a mitad de partida evita que alguien se
/// cambie de bando cuando ve que va perdiendo.
/// </remarks>
internal class TeamMenu : MonoBehaviour
{
    static TeamMenu? _instance;

    bool _open;
    bool _final;                 // marcador de fin de partida
    Rect _window;
    string _newTeam = "";
    Vector2 _scroll;

    static Texture2D? _bg;
    static GUIStyle? _label, _title, _big;

    void Awake() => _instance = this;
    void OnDestroy() { if (_instance == this) _instance = null; }

    /// <summary>Abre el panel con el resultado final al terminar la partida.</summary>
    internal static void ShowFinalScores()
    {
        if (_instance == null) return;
        _instance._final = true;
        _instance.Open();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !Plugin.CfgTeams.Value) return;

        if (keyboard[Plugin.TeamMenuKey].wasPressedThisFrame)
        {
            if (_open) Close(); else Open();
        }
    }

    void LateUpdate()
    {
        if (_open) Props.KioskUi.Free();
    }

    void Open()
    {
        _open = true;
        Props.KioskUi.UseSystemCursor();

        float w = Mathf.Min(560f, Screen.width - 40f);
        float h = Mathf.Min(560f, Screen.height - 40f);
        _window = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
    }

    void Close()
    {
        _open = false;
        _final = false;
        Props.KioskUi.Restore();
    }

    /// <summary>¿Estamos en el aeropuerto, donde todavía se pueden formar equipos?</summary>
    static bool InLobby
    {
        get
        {
            var local = Character.localCharacter;
            return local != null && local.inAirport;
        }
    }

    void OnGUI()
    {
        if (!_open) return;

        Props.KioskUi.Begin();
        _window = GUI.Window(GetInstanceID(), _window, Draw,
                             _final ? "Resultado final" : "Equipos");
        Props.KioskUi.End("equipos");
    }

    static void EnsureStyles()
    {
        if (_bg != null) return;

        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.13f, 0.97f));
        _bg.Apply();
        _bg.hideFlags = HideFlags.HideAndDontSave;

        _label = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = new Color(0.92f, 0.93f, 0.96f) },
            fontSize = 13,
        };
        _title = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        _big = new GUIStyle(_label) { fontStyle = FontStyle.Bold, fontSize = 18 };
    }

    void Draw(int id)
    {
        EnsureStyles();
        GUI.DrawTexture(new Rect(0f, 18f, _window.width, _window.height - 18f), _bg!);

        if (GUI.Button(new Rect(_window.width - 26f, 3f, 22f, 18f), "X")) { Close(); return; }
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            return;
        }

        GUILayout.Space(8);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_window.height - 70f));

        DrawScoreboard();

        if (InLobby && !_final)
        {
            GUILayout.Space(14);
            DrawTeamBuilder();
        }
        else if (!_final)
        {
            GUILayout.Space(10);
            GUILayout.Label("Los equipos se forman en el aeropuerto. Aquí solo se consulta.",
                            _label);
        }

        // Solo en la montaña: en el aeropuerto nadie ha subido nada todavía.
        if (!InLobby)
        {
            GUILayout.Space(14);
            DrawAltitudes();
        }

        GUILayout.Space(14);
        DrawSeed();

        GUILayout.Space(14);
        DrawVersions();

        GUILayout.EndScrollView();

        // Botón de desatasco, solo en partida: en el aeropuerto ya te levanta LobbyHealth.
        if (!InLobby && !_final)
        {
            GUILayout.Space(6);
            if (GUILayout.Button("Estoy atascado  —  recolócame", GUILayout.Height(30)))
                Unstick();
        }

        GUILayout.Space(4);
        if (GUILayout.Button("Cerrar  (o Esc)", GUILayout.Height(26))) Close();

        GUI.DragWindow(new Rect(0, 0, 10000, 18));
    }

    string _seedInput = "";

    /// <summary>
    /// La semilla del mapa: qué partida os va a tocar.
    /// </summary>
    /// <remarks>
    /// Se enseña siempre y se puede escribir solo en el aeropuerto, porque una vez cargado
    /// el mapa cambiarla no haría nada.
    ///
    /// Merece la pena que sea visible aunque no se toque: todo el mapa sale de ese único
    /// número, así que apuntarlo es lo que os deja repetir una partida que os gustó.
    /// </remarks>
    void DrawSeed()
    {
        if (!Plugin.CfgRandomMap.Value) return;

        GUILayout.Label("Mapa", _title);

        int seed = MapSeed.Current;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Semilla", _label, GUILayout.Width(150));
        GUILayout.Label(seed == 0 ? "— (mapa del día)" : seed.ToString(), _label);
        GUILayout.EndHorizontal();

        if (!InLobby || !Photon.Pun.PhotonNetwork.IsMasterClient)
        {
            GUILayout.Label(InLobby
                ? "Solo el anfitrión puede cambiarla."
                : "Apunta el número si quieres repetir este mapa.", _label);
            return;
        }

        GUILayout.BeginHorizontal();
        _seedInput = GUILayout.TextField(_seedInput, 9, GUILayout.Width(150));

        if (GUILayout.Button("Poner", GUILayout.Width(70)) &&
            int.TryParse(_seedInput, out var wanted))
        {
            MapSeed.Set(wanted);
            _seedInput = "";
        }

        if (GUILayout.Button("Sortear otra")) MapSeed.Roll();
        GUILayout.EndHorizontal();

        GUILayout.Label("Escribe un número para repetir un mapa concreto.", _label);
    }

    /// <summary>
    /// Hasta dónde ha subido cada equipo, en vivo.
    /// </summary>
    /// <remarks>
    /// Es seguimiento de la carrera SIN chivar posiciones: se ve la altura máxima que ha
    /// alcanzado cada equipo, no dónde está nadie. Sabes que te sacan 200 m; no por qué
    /// ladera suben ni si están juntos.
    ///
    /// Se marca la diferencia respecto al primero en vez de dejar solo las alturas: entre
    /// "1420 m" y "1380 m" hay que restar, y lo que se quiere saber de un vistazo es cuánto
    /// te falta para alcanzarlos.
    /// </remarks>
    void DrawAltitudes()
    {
        var altitudes = TeamState.Altitudes();
        if (altitudes.Count == 0) return;

        GUILayout.Label("Altura por equipo", _title);

        float leader = altitudes[0].Meters;
        int position = 1;

        foreach (var (team, meters, known) in altitudes)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label($"{position}.", _label, GUILayout.Width(24));
            GUILayout.Label(team + (team == TeamState.MyTeam ? "  (tu equipo)" : ""),
                            _label, GUILayout.Width(160));

            if (!known)
            {
                // Nadie de ese equipo ha publicado altura: o no han salido del aeropuerto,
                // o van con una versión del mod anterior a esto.
                GUILayout.Label("—", _label);
            }
            else
            {
                GUILayout.Label($"{meters:0} m", _label, GUILayout.Width(70));

                float behind = leader - meters;
                if (behind > 1f) GUILayout.Label($"-{behind:0} m", _label);
                else if (altitudes.Count > 1) GUILayout.Label("en cabeza", _label);
            }

            GUILayout.EndHorizontal();
            position++;
        }
    }

    /// <summary>
    /// Qué versión del mod lleva cada uno.
    /// </summary>
    /// <remarks>
    /// Existe para poder comprobar de un vistazo, sin pedirle a nadie que abra ficheros ni
    /// mande logs, que el actualizador está funcionando en los demás ordenadores. El
    /// contador de descargas de GitHub no sirve para esto: tarda horas en moverse.
    ///
    /// Un "?" significa que esa persona lleva una versión anterior a la que empezó a
    /// publicar este dato, o que no lleva el mod. No se pueden distinguir, y tampoco hace
    /// falta: en ambos casos lo que toca es que reinicie el juego.
    /// </remarks>
    void DrawVersions()
    {
        GUILayout.Label("Versiones del mod", _title);

        var others = TeamState.Mismatched();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Tú", _label, GUILayout.Width(150));
        GUILayout.Label(TeamState.MyVersion, _label);
        GUILayout.EndHorizontal();

        foreach (var (name, version) in others)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, _label, GUILayout.Width(150));
            GUILayout.Label(version == "?" ? "?  (versión vieja)" : version, _label);
            GUILayout.EndHorizontal();
        }

        GUILayout.Label(others.Count == 0
            ? "Todos vais igual."
            : $"{others.Count} van con otra versión. Que cierren y vuelvan a abrir el " +
              "juego: el mod ya se la habrá descargado.",
            _label);
    }

    /// <summary>
    /// Recoloca al personaje sin tocarle la vida, para salir de un atasco.
    /// </summary>
    /// <remarks>
    /// Usa <c>WarpPlayerRPC</c>, el teletransporte del propio juego: recompone el ragdoll al
    /// llegar, que es justo lo que hace falta cuando te has quedado enganchado en el
    /// decorado o el cuerpo se ha vuelto loco.
    ///
    /// A propósito NO se usa <c>RPCA_ReviveAtPosition</c>, que es lo que usamos al morir:
    /// ese suelta todos los items y te reinicia los estados. Aquí solo queremos despegar el
    /// cuerpo, con la vida y el inventario tal como estaban.
    ///
    /// Se sube un poco sobre el sitio donde estás, no a un checkpoint: la idea es
    /// desatascarte donde te has quedado, no perder el avance.
    /// </remarks>
    void Unstick()
    {
        var local = Character.localCharacter;
        if (local == null) return;

        var target = local.Center + Vector3.up * Plugin.CfgUnstickLift.Value;

        // Si hay suelo justo debajo, se aterriza encima en vez de quedar flotando.
        if (Physics.Raycast(target, Vector3.down, out var ground, 40f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            target = ground.point + Vector3.up * 1f;
        }

        local.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, target, true);

        Plugin.Log.LogInfo($"Desatascado en {target}.");
        Close();
    }

    void DrawScoreboard()
    {
        var scores = TeamState.Scoreboard();

        if (scores.Count == 0)
        {
            GUILayout.Label("Todavía no hay equipos.", _title);
            return;
        }

        if (_final)
        {
            var best = scores[0];
            bool tie = scores.Count > 1 && scores[1].Points == best.Points;

            GUILayout.Label(tie ? "¡Empate en cabeza!" : $"Gana {best.Team}", _big);
            GUILayout.Space(8);
        }

        GUILayout.Label("Marcador", _title);
        GUILayout.Space(4);

        int position = 1;
        foreach (var (team, points) in scores)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{position}.", _label, GUILayout.Width(24));
            GUILayout.Label(team + (team == TeamState.MyTeam ? "  (tu equipo)" : ""),
                            _label, GUILayout.Width(240));
            GUILayout.Label($"{points} pts", _title, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            position++;
        }

        GUILayout.Space(8);
        GUILayout.Label($"Encender la hoguera primero: {TeamState.PointsFirst} pts   ·   " +
                        $"cocinar en ella por primera vez: {TeamState.PointsCook} pts   ·   " +
                        $"llegar vivo al final: {TeamState.PointsSurvivor} pts por integrante",
                        _label);
    }

    void DrawTeamBuilder()
    {
        GUILayout.Label("Equipos", _title);
        GUILayout.Space(4);

        var mine = TeamState.MyTeam;

        foreach (var (team, members) in TeamState.Roster())
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{team}  —  {string.Join(", ", members)}", _label,
                            GUILayout.Width(360));

            if (team != mine && GUILayout.Button("Unirme", GUILayout.Width(90)))
                TeamState.JoinTeam(team);

            GUILayout.EndHorizontal();
        }

        var loose = TeamState.Unassigned();
        if (loose.Count > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("Sin equipo: " + string.Join(", ", loose), _label);
        }

        GUILayout.Space(12);
        GUILayout.Label("Crear equipo nuevo", _title);
        GUILayout.BeginHorizontal();
        _newTeam = GUILayout.TextField(_newTeam ?? "", 24, GUILayout.Width(240));

        if (GUILayout.Button("Crear", GUILayout.Width(90)) && _newTeam.Trim().Length > 0)
        {
            TeamState.JoinTeam(_newTeam);
            _newTeam = "";
        }

        if (mine.Length > 0 && GUILayout.Button("Salirme", GUILayout.Width(90)))
            TeamState.JoinTeam("");

        GUILayout.EndHorizontal();
    }
}
