using System.Collections;
using System.Collections.Generic;
using ScoutDances.Props;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScoutDances.Sounds;

/// <summary>
/// Kiosco del aeropuerto donde cada jugador pega sus 3 enlaces de myinstants.com.
/// </summary>
/// <remarks>
/// Implementa <c>IInteractible</c> igual que los kioscos del propio juego
/// (<c>AirportInviteFriendsKiosk</c>). El sistema de interacción raycastea con la máscara
/// <c>AllPhysical</c> a 2 m y luego hace <c>GetComponentInParent&lt;IInteractible&gt;()</c>,
/// así que hace falta un collider en la capa correcta — la copiamos de un kiosco vanilla
/// en vez de adivinarla.
/// </remarks>
internal class SoundKiosk : MonoBehaviour, IInteractible
{
    internal const string AirportScene = "Airport";

    static SoundKiosk? _instance;
    static readonly string[] Draft = new string[SoundSlots.Count];

    bool _open;
    Rect _window = new(0, 0, 760, 720);

    string _query = "";
    List<InstantSearch.Result> _results = new();
    Vector2 _scroll;
    bool _showManual;
    string _preview = "";
    MaterialPropertyBlock? _mpb;
    Renderer[] _renderers = System.Array.Empty<Renderer>();

    // ---------------------------------------------------------------- spawn

    internal static void Hook()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    internal static void Unhook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance != null) Destroy(_instance.gameObject);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != AirportScene) return;
        Plugin.Instance.StartCoroutine(SpawnWhenReady());
    }

    static System.Collections.IEnumerator SpawnWhenReady()
    {
        // El kiosco vanilla nos sirve de ancla: nos da posición, orientación y —lo más
        // importante— la capa correcta para que el raycast de interacción nos vea.
        AirportInviteFriendsKiosk? anchor = null;
        for (int i = 0; i < 120 && anchor == null; i++)
        {
            anchor = Object.FindFirstObjectByType<AirportInviteFriendsKiosk>();
            if (anchor == null) yield return null;
        }

        if (anchor == null)
        {
            Plugin.Log.LogWarning("No encontré el kiosco de invitar amigos; no puedo colocar el de sonidos.");
            yield break;
        }

        if (_instance != null) Destroy(_instance.gameObject);
        _instance = Build(anchor);

        // El apoyo en el suelo va un frame después: Renderer.bounds todavía no refleja
        // la rotación y la escala que acabamos de aplicar.
        yield return null;
        _instance.SnapModelToGround();
    }

    /// <summary>Baja el modelo hasta que su base toque el suelo detectado.</summary>
    void SnapModelToGround()
    {
        var model = transform.Find("Model");
        if (model == null) return;

        var local = LocalBounds(model.gameObject);
        float lowest = LowestPoint(local, model.localRotation, model.localScale.x);
        model.localPosition = new Vector3(model.localPosition.x, -lowest, model.localPosition.z);

        Plugin.Log.LogInfo($"Kiosco apoyado en el suelo (base a {lowest:0.00} del pivote).");
    }

    static SoundKiosk Build(AirportInviteFriendsKiosk anchor)
    {
        var anchorTf = anchor.transform;

        var spawn = anchorTf.position + anchorTf.right * Plugin.CfgKioskOffset.Value;

        // No damos por hecho dónde está el pivote del kiosco vanilla (el nuestro se
        // construye hacia arriba desde su origen): buscamos el suelo y nos apoyamos ahí.
        if (Physics.Raycast(spawn + Vector3.up * 3f, Vector3.down, out var ground, 12f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            spawn.y = ground.point.y;
        }
        spawn.y += Plugin.CfgKioskHeight.Value;

        // Solo copiamos el GIRO HORIZONTAL del kiosco vanilla, no su rotación completa:
        // ese prefab está autorizado con una inclinación propia, y heredarla entera
        // dejaba nuestro pie de micro tumbado en el suelo por mucho que el modelo
        // estuviera bien orientado en su propio espacio.
        var yaw = Quaternion.Euler(0f, anchorTf.eulerAngles.y, 0f);

        var root = new GameObject("ScoutDancesSoundKiosk");
        root.transform.SetPositionAndRotation(spawn, yaw);

        var model = BuildModel(root.transform);
        FixMaterials(model);
        EnsureCollider(root, model);

        // La capa importa: si no coincide con la máscara AllPhysical, el raycast de
        // interacción nunca nos encuentra y el kiosco es un adorno.
        SetLayerRecursive(root, anchor.gameObject.layer);

        // URP descarta mallas por debajo del 0,5 % de pantalla; sin esto el kiosco solo
        // se ve al entrar en rango de interacción.
        RenderingTweaks.ExcludeFromResidentDrawer(root);
        if (Plugin.CfgDisableSmallMeshCulling.Value) RenderingTweaks.DisableSmallMeshCulling();

        root.AddComponent<KioskDiagnostics>();

        var kiosk = root.AddComponent<SoundKiosk>();
        kiosk._renderers = root.GetComponentsInChildren<Renderer>();
        kiosk._mpb = new MaterialPropertyBlock();

        Plugin.Log.LogInfo($"Kiosco de sonidos colocado en {root.transform.position} " +
                           $"(capa {LayerMask.LayerToName(root.layer)}).");
        return kiosk;
    }

    /// <summary>
    /// Instancia el modelo del kiosco desde el bundle. Si no está, cae a un cubo para
    /// que el kiosco siga siendo usable en vez de desaparecer.
    /// </summary>
    static GameObject BuildModel(Transform parent)
    {
        var prefab = Plugin.KioskPrefab;
        if (prefab != null)
        {
            var model = Instantiate(prefab, parent);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // Los prefabs de la Asset Store vienen con la escala y el pivote que le
            // apeteciera al autor, así que los normalizamos aquí.
            //
            // Medimos con LocalBounds (mallas + matrices), NO con Renderer.bounds: ese
            // devuelve la caja en mundo, o sea ya girada por la rotación del padre. Con
            // él detectábamos "eje largo = X" en un pie de micro que en su propio
            // espacio está perfectamente vertical (0.30 x 1.44 x 0.30), y acabábamos
            // tumbándolo nosotros al "corregirlo".
            var local = LocalBounds(model);
            var size = local.size;
            int longest = (size.x >= size.y && size.x >= size.z) ? 0 : (size.y >= size.z ? 1 : 2);
            float length = size[longest];

            // Enderezar: en un pie de micro, el eje más largo es su altura.
            if (longest == 0) model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (longest == 2) model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Escalar a partir de esa longitud medida, sin volver a consultar bounds.
            float scale = length > 0.01f
                ? Plugin.CfgKioskTargetHeight.Value / length * Plugin.CfgKioskScale.Value
                : Plugin.CfgKioskScale.Value;
            model.transform.localScale = Vector3.one * scale;

            Plugin.Log.LogInfo(
                $"Modelo '{prefab.name}': {size.x:0.00}x{size.y:0.00}x{size.z:0.00} m, " +
                $"eje largo={"XYZ"[longest]} ({length:0.00} m) -> escala {scale:0.00}");
            return model;
        }

        Plugin.Log.LogWarning($"No encontré el prefab '{Plugin.CfgKioskModel.Value}' en el bundle; " +
                              "uso un cubo de reserva.");

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Model";
        box.transform.SetParent(parent, false);
        box.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        box.transform.localScale = new Vector3(0.7f, 1.2f, 0.5f);
        return box;
    }

    /// <summary>
    /// Caja envolvente del modelo en SU PROPIO espacio local, calculada a partir de las
    /// mallas y sus matrices.
    /// </summary>
    /// <remarks>
    /// No usamos <c>Renderer.bounds</c> a propósito: no refleja una rotación aplicada
    /// por script (ni siquiera un frame después, comprobado), y eso hacía que el modelo
    /// se escalara como si siguiera tumbado. Esto es determinista.
    /// </remarks>
    static Bounds LocalBounds(GameObject model)
    {
        var root = model.transform;
        bool started = false;
        Bounds bounds = default;

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;

            // worldToLocalMatrix del root deshace su propia rotación/escala, así que
            // obtenemos la forma "cruda" del prefab.
            var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var mb = mesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? mb.min.x : mb.max.x,
                    (corner & 2) == 0 ? mb.min.y : mb.max.y,
                    (corner & 4) == 0 ? mb.min.z : mb.max.z));

                if (!started) { bounds = new Bounds(point, Vector3.zero); started = true; }
                else bounds.Encapsulate(point);
            }
        }

        return started ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    /// <summary>Punto más bajo del modelo (en local del padre) tras aplicar rotación y escala.</summary>
    static float LowestPoint(Bounds local, Quaternion rotation, float scale)
    {
        float min = float.MaxValue;
        for (int corner = 0; corner < 8; corner++)
        {
            var point = rotation * Vector3.Scale(new Vector3(
                (corner & 1) == 0 ? local.min.x : local.max.x,
                (corner & 2) == 0 ? local.min.y : local.max.y,
                (corner & 4) == 0 ? local.min.z : local.max.z), Vector3.one * scale);
            if (point.y < min) min = point.y;
        }
        return min;
    }

    /// <summary>
    /// Los materiales del pack usan un ShaderGraph propio. Suele sobrevivir al bundle
    /// porque nuestro proyecto y PEAK van ambos con URP 17.x, pero si el shader no
    /// resuelve, Unity lo pinta de rosa; en ese caso caemos al shader del propio juego
    /// conservando la textura.
    /// </summary>
    static void FixMaterials(GameObject model)
    {
        var fallback = Shader.Find("W/Peak_Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
        if (fallback == null) return;

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            // El aeropuerto trae occlusion culling horneado. Nuestro kiosco se crea en
            // runtime, así que no está en esos datos y Unity lo tapaba desde lejos:
            // solo aparecía al acercarte mucho. Con esto se salta esa prueba.
            renderer.allowOcclusionWhenDynamic = false;

            var materials = renderer.materials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                // El material del pack viene con _ALPHATEST_ON. Con mipmaps, el alfa se
                // promedia con la distancia, cae bajo el umbral de corte y el objeto se
                // recorta ENTERO: por eso el kiosco solo se veía de cerca. Es opaco, así
                // que el alpha test aquí no aporta nada.
                if (material.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    material.DisableKeyword("_ALPHATEST_ON");
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0f);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    Plugin.Log.LogInfo($"Alpha clipping desactivado en '{material.name}'.");
                }

                bool broken = material.shader == null ||
                              material.shader.name == "Hidden/InternalErrorShader";
                if (!broken) continue;

                var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap")
                            : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex")
                            : null;

                var replacement = new Material(fallback);
                if (texture != null)
                {
                    if (replacement.HasProperty("_BaseMap")) replacement.SetTexture("_BaseMap", texture);
                    if (replacement.HasProperty("_MainTex")) replacement.SetTexture("_MainTex", texture);
                }
                materials[i] = replacement;
                changed = true;
            }

            if (changed)
            {
                renderer.materials = materials;
                Plugin.Log.LogInfo($"Shader del kiosco sustituido por '{fallback.name}' en '{renderer.name}'.");
            }
        }
    }

    /// <summary>
    /// El prefab del pack es decorativo y no trae collider; sin uno, el raycast de
    /// interacción nunca lo encuentra. Se lo ajustamos a los bounds reales del modelo.
    /// </summary>
    static void EnsureCollider(GameObject root, GameObject model)
    {
        if (model.GetComponentInChildren<Collider>() != null) return;

        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            root.AddComponent<BoxCollider>();
            return;
        }

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        var box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(bounds.center);
        // Ensanchamos un poco para que sea cómodo apuntarle: el pie del micro es fino.
        box.size = new Vector3(
            Mathf.Max(bounds.size.x, 0.5f),
            bounds.size.y,
            Mathf.Max(bounds.size.z, 0.5f));
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    // ---------------------------------------------------------- IInteractible

    public bool IsInteractible(Character interactor) => !_open;

    public void Interact(Character interactor)
    {
        for (int i = 0; i < SoundSlots.Count; i++) Draft[i] = SoundSlots.GetRaw(i);
        _open = true;
        // Clamp a la pantalla: con resoluciones bajas la ventana se salía por abajo y
        // el botón de cerrar quedaba fuera de alcance.
        float w = Mathf.Min(760f, Screen.width - 40f);
        float h = Mathf.Min(720f, Screen.height - 40f);
        _window = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        Props.KioskUi.UseSystemCursor();
    }

    public void HoverEnter() => SetHighlight(1f);
    public void HoverExit() => SetHighlight(0f);

    void SetHighlight(float value)
    {
        if (_mpb == null) return;
        _mpb.SetFloat(Item.PROPERTY_INTERACTABLE, value);
        foreach (var renderer in _renderers)
            if (renderer != null) renderer.SetPropertyBlock(_mpb);
    }

    public Vector3 Center() => transform.position + Vector3.up * 0.8f;
    public Transform GetTransform() => transform;
    public string GetInteractionText() => "Configurar sonidos";
    public string GetName() => "Kiosco de sonidos";

    // ------------------------------------------------------------------- UI

    void LateUpdate()
    {
        if (!_open) return;

        // Mecanismo propio del juego: mientras 'lastBlockedInput' sea reciente,
        // GUIManager.windowBlockingInput queda a true y Character.CanDoInput() da false.
        // Así el jugador no camina ni abre la rueda mientras escribe.
        Props.KioskUi.Free();
    }

    void OnGUI()
    {
        if (!_open) return;

        Props.KioskUi.Begin();
        _window = GUI.Window(GetInstanceID(), _window, DrawWindow, "Sonidos del Scout");
        Props.KioskUi.End("kiosco de sonidos");
    }

    void DrawWindow(int id)
    {
        // Cierre siempre alcanzable, pase lo que pase con el alto del contenido.
        if (GUI.Button(new Rect(_window.width - 26f, 3f, 22f, 18f), "X")) { Close(); return; }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            return;
        }

        GUILayout.Space(4);

        // ---- buscador ----
        GUILayout.BeginHorizontal();
        GUILayout.Label("Buscar en myinstants:", GUILayout.Width(150));

        GUI.SetNextControlName("sd_query");
        _query = GUILayout.TextField(_query, GUILayout.Width(380));

        bool enter = Event.current.type == EventType.KeyDown &&
                     (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                     GUI.GetNameOfFocusedControl() == "sd_query";

        if ((GUILayout.Button("Buscar", GUILayout.Width(90)) || enter) &&
            !InstantSearch.Busy && _query.Trim().Length > 0)
        {
            Plugin.Instance.StartCoroutine(
                InstantSearch.Search(_query.Trim(), r => _results = r));
        }
        GUILayout.EndHorizontal();

        // ---- resultados ----
        GUILayout.Space(6);
        if (InstantSearch.Busy) GUILayout.Label("Buscando…");
        else if (InstantSearch.LastError.Length > 0) GUILayout.Label($"Error: {InstantSearch.LastError}");
        else if (_results.Count > 0) GUILayout.Label($"{_results.Count} resultados — ▶ escuchar, 1/2/3 asignar:");
        else GUILayout.Label("Escribe algo y pulsa Buscar.");

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Max(110f, _window.height - 460f)));
        foreach (var result in _results)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(result.Name, GUILayout.Width(430));

            if (GUILayout.Button("▶", GUILayout.Width(34))) Preview(result.MediaPath, 0.5f);

            for (int slot = 0; slot < SoundSlots.Count; slot++)
            {
                bool assigned = SoundSlots.GetLocalPath(slot) == result.MediaPath;
                var label = assigned ? $"✓{slot + 1}" : (slot + 1).ToString();
                if (GUILayout.Button(label, GUILayout.Width(34)))
                    SoundSlots.AssignDirect(slot, result.MediaPath);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        // ---- tus 3 slots ----
        GUILayout.Space(6);
        GUILayout.Label("Tus sonidos (última página de la rueda de emotes) — cada uno con su volumen:");
        for (int slot = 0; slot < SoundSlots.Count; slot++)
        {
            var path = SoundSlots.GetLocalPath(slot);
            var name = path.Length == 0 ? "(vacío)" : InstantAudioCache.PrettyName(path);
            var state = path.Length == 0 ? "" : (InstantAudioCache.IsCached(path) ? "" : "  (descargando…)");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{slot + 1}.  {name}{state}", GUILayout.Width(300));

            if (path.Length > 0)
            {
                float current = SoundSlots.GetLocalVolume(slot);
                float updated = GUILayout.HorizontalSlider(current, 0f, 1f, GUILayout.Width(230));
                SoundSlots.SetLocalVolume(slot, updated);   // solo escribe si cambió

                GUILayout.Label($"{updated * 100f:0} %", GUILayout.Width(45));
                if (GUILayout.Button("▶", GUILayout.Width(30))) Preview(path, updated);
                if (GUILayout.Button("■", GUILayout.Width(30))) StopPreview();
                if (GUILayout.Button("X", GUILayout.Width(28))) SoundSlots.Clear(slot);
            }
            GUILayout.EndHorizontal();
        }

        // ---- pegar enlace a mano ----
        GUILayout.Space(6);
        _showManual = GUILayout.Toggle(_showManual, " Pegar un enlace a mano");
        if (_showManual)
        {
            for (int i = 0; i < SoundSlots.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Sonido {i + 1}", GUILayout.Width(70));
                Draft[i] = GUILayout.TextField(Draft[i] ?? "", GUILayout.Width(470));
                GUILayout.Label(StatusFor(i), GUILayout.Width(130));
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Guardar enlaces escritos", GUILayout.Height(24)))
            {
                Plugin.Instance.StartCoroutine(SoundSlots.ApplyAndSync((string[])Draft.Clone()));
            }
        }

        GUILayout.Space(6);
        if (GUILayout.Button("Cerrar  (o Esc)", GUILayout.Height(26))) Close();

        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    /// <summary>
    /// Escucha un sonido dentro del kiosco, en 2D y sin pasar por el personaje.
    /// </summary>
    /// <summary>Corta la previsualización del kiosco (solo suena para ti).</summary>
    void StopPreview()
    {
        _preview = "";
        if (_previewSource != null) _previewSource.Stop();
    }

    void Preview(string mediaPath, float volume)
    {
        _preview = mediaPath;
        _previewVolume = volume;
        InstantAudioCache.Request(mediaPath);
        Plugin.Instance.StartCoroutine(PlayPreviewWhenReady(mediaPath));
    }

    IEnumerator PlayPreviewWhenReady(string mediaPath)
    {
        // Damos un margen para que termine de bajar; si tarda más, el usuario reintenta.
        for (int i = 0; i < 300; i++)
        {
            var ready = InstantAudioCache.Get(mediaPath);
            if (ready != null)
            {
                if (_preview != mediaPath) yield break;   // pidió otro mientras tanto
                if (_previewSource == null)
                {
                    _previewSource = gameObject.AddComponent<AudioSource>();
                    _previewSource.spatialBlend = 0f;      // 2D: es la UI, no el mundo
                    _previewSource.playOnAwake = false;
                }
                _previewSource.volume = _previewVolume * Plugin.CfgSoundVolume.Value;
                _previewSource.clip = ready;
                _previewSource.Play();
                yield break;
            }
            yield return null;
        }
    }

    AudioSource? _previewSource;
    float _previewVolume = 0.5f;

    static string StatusFor(int slot)
    {
        var typed = Draft[slot];
        if (string.IsNullOrWhiteSpace(typed)) return "vacío";
        if (!InstantAudioCache.LooksLikeInstantLink(typed)) return "no es myinstants";

        // Si no cambió respecto a lo guardado, podemos decir si ya está descargado.
        if (typed.Trim() == SoundSlots.GetRaw(slot))
        {
            var path = SoundSlots.GetLocalPath(slot);
            if (path.Length > 0)
                return InstantAudioCache.IsCached(path) ? "descargado" : "se descargará";
        }
        return "se resolverá";
    }

    void Close()
    {
        _open = false;
        StopPreview();
        Props.KioskUi.Restore();
    }

    void OnDestroy()
    {
        if (_open) Close();
        if (_instance == this) _instance = null;
    }
}
