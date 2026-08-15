using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScoutDances.Props;

/// <summary>
/// Caja del aeropuerto que da acceso a TODOS los items del juego, para probarlos en el
/// lobby sin tener que entrar a una partida.
/// </summary>
/// <remarks>
/// Usa el mismo camino que el juego para darte un item:
/// <c>CharacterItems.SpawnItemInHand(nombre)</c>, que hace un RPC al MasterClient y allí
/// llama a <c>PhotonNetwork.Instantiate("0_Items/" + nombre)</c> seguido de
/// <c>Interact(character)</c>. Al ir por la vía oficial, el item queda correctamente
/// registrado en red y los demás lo ven.
///
/// El catálogo sale de <c>ItemDatabase.GetAllObjectNames()</c>, heredado de
/// <c>ObjectDatabaseAsset</c>, que expone todos los prefabs registrados.
/// </remarks>
internal class ItemTestBox : MonoBehaviour, IInteractible
{
    /// <summary>Una entrada del catálogo: lo que hace falta para listar y spawnear.</summary>
    readonly struct Entry
    {
        internal readonly string PrefabName;   // el que necesita SpawnItemInHand
        internal readonly string Display;      // UIData.itemName, más legible
        internal readonly Texture2D? Icon;     // el mismo icono que sale en la barra
        internal readonly int Category;        // índice de pestaña

        internal Entry(string prefabName, string display, Texture2D? icon, int category)
        {
            PrefabName = prefabName;
            Display = display;
            Icon = icon;
            Category = category;
        }
    }

    static ItemTestBox? _instance;
    static Entry[] _catalogue = Array.Empty<Entry>();

    bool _open;
    Rect _window;
    string _filter = "";
    Vector2 _scroll;
    string _lastSpawned = "";
    int _tab;

    // Estilos propios: NO tocamos GUI.skin, que es compartido con el juego y otros mods.
    static Texture2D? _panelBg;
    static GUIStyle? _tileStyle, _labelStyle, _titleStyle;

    // ---------------------------------------------------------------- spawn

    internal static void Hook() => SceneManager.sceneLoaded += OnSceneLoaded;

    internal static void Unhook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance != null) Destroy(_instance.gameObject);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != Sounds.SoundKiosk.AirportScene) return;
        Plugin.Instance.StartCoroutine(SpawnWhenReady());
    }

    static IEnumerator SpawnWhenReady()
    {
        AirportInviteFriendsKiosk? anchor = null;
        for (int i = 0; i < 120 && anchor == null; i++)
        {
            anchor = UnityEngine.Object.FindFirstObjectByType<AirportInviteFriendsKiosk>();
            if (anchor == null) yield return null;
        }
        if (anchor == null) yield break;

        if (_instance != null) Destroy(_instance.gameObject);

        var root = PropBuilder.Spawn(
            "ScoutDancesItemBox", anchor.transform,
            Plugin.CfgItemBoxOffset.Value,
            Plugin.ItemBoxPrefab,
            Plugin.CfgItemBoxHeight.Value);

        _instance = root.AddComponent<ItemTestBox>();

        yield return null;
        PropBuilder.SnapToGround(root);

        Plugin.Log.LogInfo($"Caja de items colocada en {root.transform.position}.");
    }

    // ---------------------------------------------------------- IInteractible

    public bool IsInteractible(Character interactor) => !_open;

    public void Interact(Character interactor)
    {
        RefreshCatalogue();
        _open = true;

        float w = Mathf.Min(900f, Screen.width - 40f);
        float h = Mathf.Min(680f, Screen.height - 40f);
        _window = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        KioskUi.UseSystemCursor();
    }

    public void HoverEnter() { }
    public void HoverExit() { }
    public Vector3 Center() => transform.position + Vector3.up * 0.4f;
    public Transform GetTransform() => transform;
    public string GetInteractionText() => "Probar items";
    public string GetName() => "Caja de items";

    /// Cuántos items tenía el database la última vez que armamos el catálogo.
    static int _catalogueSize = -1;

    /// <summary>
    /// Rehace la lista de items si el juego ha registrado alguno más.
    /// </summary>
    /// <remarks>
    /// Antes se construía una vez y no se volvía a mirar. El problema es que las armas del
    /// mod se registran desde una corrutina que espera al ItemDatabase, así que si la caja
    /// se abría antes de que terminara, las últimas en registrarse no salían en la lista —
    /// y no había forma de que aparecieran sin reiniciar el juego.
    ///
    /// Comparar el número de items es suficiente: solo se añaden al arrancar, nunca se
    /// quitan, así que si el total cambió es que hay algo nuevo que enseñar.
    /// </remarks>
    static void RefreshCatalogue()
    {
        int size = 0;
        try { size = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects.Count; }
        catch { }

        if (_catalogue.Length > 0 && size == _catalogueSize) return;
        _catalogueSize = size;

        try
        {
            // Los Item del database son los prefabs, así que de ahí sacamos también el
            // icono y el nombre bonito que el juego ya usa en la barra de inventario.
            var items = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects;

            _catalogue = items
                .Where(i => i != null)
                .Select(i => new Entry(
                    i.name,
                    string.IsNullOrWhiteSpace(i.UIData?.itemName) ? i.name : i.UIData.itemName,
                    i.UIData?.icon,
                    ItemCategories.Of(i)))
                .OrderBy(e => e.Display, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int withIcon = _catalogue.Count(e => e.Icon != null);
            Plugin.Log.LogInfo($"Catálogo de items: {_catalogue.Length} entradas, {withIcon} con icono.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"No pude leer el ItemDatabase: {e.Message}");

            // Plan B: al menos los nombres, sin iconos.
            try
            {
                _catalogue = ItemDatabase.GetAllObjectNames()
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new Entry(n, n, null, ItemCategories.Other))
                    .ToArray();
            }
            catch (Exception inner)
            {
                Plugin.Log.LogError($"Tampoco pude listar los nombres: {inner.Message}");
            }
        }
    }

    // ------------------------------------------------------------------- UI

    void LateUpdate()
    {
        if (_open) KioskUi.Free();
    }

    void OnGUI()
    {
        if (!_open) return;

        KioskUi.Begin();
        _window = GUI.Window(GetInstanceID(), _window, DrawWindow, "Caja de pruebas — items del juego");
        KioskUi.End("caja de items");
    }

    // Resultado del filtro, cacheado. Antes se recalculaba DENTRO de DrawWindow, es decir
    // varias veces por frame sobre las ~200 entradas del catálogo, con su array nuevo cada
    // vez. Solo cambia al cambiar de pestaña o al escribir en el filtro.
    Entry[] _matches = System.Array.Empty<Entry>();
    int _matchTab = -1;
    string _matchFilter = "\n";

    void RefreshMatches()
    {
        var needle = (_filter ?? "").Trim();
        if (_tab == _matchTab && needle == _matchFilter) return;

        _matchTab = _tab;
        _matchFilter = needle;

        _matches = _catalogue.Where(e =>
            ItemCategories.Matches(_tab, e.Category) &&
            (needle.Length == 0 ||
             e.Display.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
             e.PrefabName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
    }

    /// <summary>
    /// Crea los estilos una sola vez. Con el skin por defecto de IMGUI el texto salía
    /// oscuro sobre un panel semitransparente con el aeropuerto detrás: ilegible.
    /// </summary>
    static void EnsureStyles()
    {
        if (_panelBg != null) return;

        _panelBg = new Texture2D(1, 1);
        _panelBg.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.13f, 0.97f));   // casi opaco
        _panelBg.Apply();
        _panelBg.hideFlags = HideFlags.HideAndDontSave;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = new Color(0.92f, 0.93f, 0.96f) },
            fontSize = 13,
        };

        _titleStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold };

        // Botón cuadrado con el icono arriba y el nombre debajo.
        _tileStyle = new GUIStyle(GUI.skin.button)
        {
            imagePosition = ImagePosition.ImageAbove,
            alignment = TextAnchor.LowerCenter,
            wordWrap = true,
            fontSize = 10,
            padding = new RectOffset(3, 3, 4, 3),
            normal = { textColor = new Color(0.92f, 0.93f, 0.96f) },
            hover = { textColor = Color.white },
        };
    }

    const float TileSize = 92f;
    const float RowHeight = TileSize + 4f;

    void DrawWindow(int id)
    {
        EnsureStyles();

        // Fondo opaco propio: el del skin deja ver el juego a través y no hay forma
        // de leer nada encima.
        GUI.DrawTexture(new Rect(0f, 18f, _window.width, _window.height - 18f), _panelBg!);

        if (GUI.Button(new Rect(_window.width - 26f, 3f, 22f, 18f), "X")) { Close(); return; }
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            return;
        }

        GUILayout.Space(6);

        // ---- pestañas ----
        _tab = GUILayout.Toolbar(_tab, ItemCategories.Names, GUILayout.Height(26));

        // ---- filtro ----
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Filtrar:", _labelStyle, GUILayout.Width(55));
        _filter = GUILayout.TextField(_filter ?? "", GUILayout.Width(300));
        if (GUILayout.Button("Limpiar", GUILayout.Width(70))) _filter = "";
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Soltar todo", GUILayout.Width(110))) DropAll();
        GUILayout.EndHorizontal();

        RefreshMatches();
        var matches = _matches;

        GUILayout.Space(4);
        GUILayout.Label(_catalogue.Length == 0
            ? "No pude leer el catálogo de items (mira el log)."
            : $"{matches.Length} items" +
              (_lastSpawned.Length > 0 ? $"   ·   último: {_lastSpawned}" : ""), _titleStyle);

        // ---- rejilla ----
        GUILayout.Space(2);

        float viewHeight = Mathf.Max(160f, _window.height - 180f);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(viewHeight));

        int perRow = Mathf.Max(1, Mathf.FloorToInt((_window.width - 40f) / (TileSize + 6f)));
        int totalRows = Mathf.CeilToInt(matches.Length / (float)perRow);

        // Solo dibujamos las filas que caben en pantalla. IMGUI no descarta por su cuenta
        // lo que queda fuera de un ScrollView: monta y dibuja TODO el contenido, así que
        // con el catálogo entero eran ~200 botones y ~200 GUIContent nuevos por llamada,
        // y OnGUI corre varias veces por frame. El hueco de las filas que saltamos se
        // rellena con dos Space, para que la barra de desplazamiento siga midiendo igual.
        int firstRow = Mathf.Clamp(Mathf.FloorToInt(_scroll.y / RowHeight) - 1,
                                   0, Mathf.Max(0, totalRows - 1));
        int lastRow = Mathf.Min(totalRows, firstRow + Mathf.CeilToInt(viewHeight / RowHeight) + 2);

        if (firstRow > 0) GUILayout.Space(firstRow * RowHeight);

        for (int row = firstRow; row < lastRow; row++)
        {
            GUILayout.BeginHorizontal();
            for (int c = 0; c < perRow; c++)
            {
                int index = row * perRow + c;
                if (index >= matches.Length) break;

                var entry = matches[index];
                var content = entry.Icon != null
                    ? new GUIContent(entry.Display, entry.Icon, entry.PrefabName)
                    : new GUIContent(entry.Display, entry.PrefabName);

                if (GUILayout.Button(content, _tileStyle,
                                     GUILayout.Width(TileSize), GUILayout.Height(TileSize)))
                {
                    Give(entry.PrefabName);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        if (lastRow < totalRows) GUILayout.Space((totalRows - lastRow) * RowHeight);

        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("Cerrar  (o Esc)", GUILayout.Height(26))) Close();

        GUI.DragWindow(new Rect(0, 0, 10000, 18));
    }

    void Give(string itemName)
    {
        var character = Character.localCharacter;
        if (character == null || character.refs?.items == null)
        {
            Plugin.Log.LogWarning("No hay personaje local al que darle el item.");
            return;
        }

        try
        {
            // Se comprueba ANTES de pedirlo para poder distinguir dos fallos que desde
            // fuera se ven igual: que el item no exista con ese nombre, o que exista y sea
            // el spawn el que revienta.
            bool known = false;
            try
            {
                known = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects
                    .Any(i => i != null && i.name == itemName);
            }
            catch { }

            if (!known)
                Plugin.Log.LogWarning($"'{itemName}' no está en el ItemDatabase con ese nombre.");

            character.refs.items.SpawnItemInHand(itemName);
            _lastSpawned = itemName;
            Plugin.Log.LogInfo($"Item solicitado: {itemName} (en el database: {known})");
        }
        catch (Exception e)
        {
            // Photon invoca los RPC por reflexión, así que lo que llega aquí es un
            // TargetInvocationException cuyo Message no dice nada útil ("Exception has
            // been thrown by the target of an invocation"). La causa está dentro.
            Plugin.Log.LogError($"No pude spawnear '{itemName}':");

            var current = e;
            int depth = 0;
            while (current != null && depth++ < 5)
            {
                Plugin.Log.LogError($"  [{depth}] {current.GetType().Name}: {current.Message}");
                if (!string.IsNullOrEmpty(current.StackTrace))
                    Plugin.Log.LogError($"      {current.StackTrace.Split('\n')[0].Trim()}");
                current = current.InnerException;
            }
        }
    }

    void DropAll()
    {
        var character = Character.localCharacter;
        if (character == null || character.refs?.items == null) return;

        // includeBackpack: false — soltar la mochila entera al probar items sería
        // molesto y además hay que volver a recogerla a mano.
        try { character.refs.items.DropAllItems(includeBackpack: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"No pude soltar los items: {e.Message}"); }
    }

    void Close()
    {
        _open = false;
        KioskUi.Restore();
    }

    void OnDestroy()
    {
        if (_open) Close();
        if (_instance == this) _instance = null;
    }
}
