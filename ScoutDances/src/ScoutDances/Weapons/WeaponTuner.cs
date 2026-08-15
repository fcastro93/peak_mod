using UnityEngine;
using UnityEngine.InputSystem;

namespace ScoutDances.Weapons;

/// <summary>
/// Panel para colocar el arma en la mano en tiempo real, sin recompilar.
/// </summary>
/// <remarks>
/// Encajar un modelo de la Asset Store en la mano del Scout no se puede calcular: el
/// pivote está donde lo dejó el artista y "dónde va la empuñadura" es distinto en cada
/// arma. Centrarlo sobre su caja envolvente deja el centro del cañón en la mano, que
/// tampoco es. Con esto se ajusta mirándolo y se guarda al config de una vez, en lugar
/// de encadenar ciclos de compilar-mirar-repetir.
///
/// Vale para las 12 armas del pack, no solo la pistola.
/// </remarks>
internal class WeaponTuner : MonoBehaviour
{
    bool _open;
    Rect _window;
    static Texture2D? _bg;
    static GUIStyle? _label, _titleStyle;

    Vector3 _offset;
    Vector3 _rotation;
    float _length;
    bool _loaded;

    // Valores "en vivo" que está editando el panel. WeaponAim los prefiere sobre los del
    // config: si no, reescribiría la posición cada frame con el valor guardado y los
    // sliders no moverían nada (el tamaño sí funcionaba porque WeaponAim no lo toca).
    internal static string LiveFor = "";
    internal static Vector3 LiveOffset;
    internal static Vector3 LiveRotation;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[Key.F3].wasPressedThisFrame) Toggle();
    }

    /// <remarks>
    /// El cursor se libera en LateUpdate, NO en Update: el juego lo vuelve a bloquear en
    /// su propio LateUpdate, que corre después, y se comía nuestro desbloqueo. Los
    /// kioscos ya lo hacían así; el ajustador se quedó en Update y salía sin ratón.
    /// </remarks>
    void LateUpdate()
    {
        if (_open) Apply();
    }

    void Toggle()
    {
        _open = !_open;

        if (_open)
        {
            LoadFromHeld();
            Props.KioskUi.UseSystemCursor();

            float w = Mathf.Min(430f, Screen.width - 40f);
            _window = new Rect(30f, 80f, w, 330f);
        }
        else
        {
            LiveFor = "";              // deja de mandar; vuelve a valer el config
            Props.KioskUi.Restore();
        }
    }

    /// <summary>Modelo del arma que el jugador tiene ahora mismo en la mano.</summary>
    static Transform? HeldModel()
    {
        var item = Character.localCharacter?.data?.currentItem;
        return item != null ? item.transform.Find("WeaponModel") : null;
    }

    /// <summary>Definición del arma en la mano, para saber a qué sección del config escribir.</summary>
    static IWeaponPlacement? HeldDefinition()
    {
        var item = Character.localCharacter?.data?.currentItem;
        var tag = item != null ? item.GetComponent<WeaponTag>() : null;
        return tag != null ? Plugin.FindWeapon(tag.DefinitionId) : null;
    }

    string _loadedFor = "";

    /// Carga los valores del arma que tengas ahora, si es distinta de la última.
    void LoadFromHeld()
    {
        var definition = HeldDefinition();
        if (definition == null || definition.Id == _loadedFor) return;

        _offset = definition.Offset.Value;
        _rotation = definition.Rotation.Value;
        _length = definition.Length.Value;
        _loadedFor = definition.Id;
        _loaded = true;
    }

    void Apply()
    {
        LoadFromHeld();

        Props.KioskUi.Free();

        var model = HeldModel();
        if (model == null) return;

        var definition = HeldDefinition();
        LiveFor = definition?.Id ?? "";
        LiveOffset = _offset;
        LiveRotation = _rotation;

        WeaponFactory.PlaceModel(model.gameObject, _offset, _rotation, _length);
    }

    void OnGUI()
    {
        if (!_open) return;

        if (_bg == null)
        {
            _bg = new Texture2D(1, 1);
            _bg.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.13f, 0.97f));
            _bg.Apply();
            _bg.hideFlags = HideFlags.HideAndDontSave;
            _label = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.92f, 0.93f, 0.96f) },
                fontSize = 12,
            };
            _titleStyle = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        }

        _window = GUI.Window(GetInstanceID(), _window, Draw, "Ajustar arma  (F3)");
    }

    void Draw(int id)
    {
        GUI.DrawTexture(new Rect(0f, 18f, _window.width, _window.height - 18f), _bg!);
        GUILayout.Space(6);

        if (HeldModel() == null)
        {
            GUILayout.Label("Saca un arma del mod y tenla en la mano.", _label);
            GUI.DragWindow(new Rect(0, 0, 10000, 18));
            return;
        }

        var held = HeldDefinition();
        GUILayout.Label($"Ajustando: {held?.DisplayName.Value ?? "?"}", _titleStyle ?? _label);
        GUILayout.Space(4);

        _length = Row("Tamaño", _length, 0.1f, 5f, "0.00");
        GUILayout.Space(4);

        _offset.x = Row("Offset X", _offset.x, -2f, 2f, "0.000");
        _offset.y = Row("Offset Y", _offset.y, -2f, 2f, "0.000");
        _offset.z = Row("Offset Z", _offset.z, -2f, 3f, "0.000");
        GUILayout.Space(4);

        _rotation.x = Row("Rot X", _rotation.x, 0f, 360f, "0");
        _rotation.y = Row("Rot Y", _rotation.y, 0f, 360f, "0");
        _rotation.z = Row("Rot Z", _rotation.z, 0f, 360f, "0");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Guardar al config", GUILayout.Height(26)))
        {
            var definition = HeldDefinition();
            if (definition != null)
            {
                definition.Offset.Value = _offset;
                definition.Rotation.Value = _rotation;
                definition.Length.Value = _length;
                Plugin.Log.LogInfo($"'{definition.Id}' guardada: offset {_offset}, " +
                                   $"rot {_rotation}, tamaño {_length:0.00}");
            }
        }

        if (GUILayout.Button("Reiniciar", GUILayout.Height(26)))
        {
            _offset = Vector3.zero;
            _rotation = Vector3.zero;
            _length = 0.6f;
        }

        GUILayout.EndHorizontal();

        GUILayout.Label("Se aplica en vivo al arma que tengas en la mano. Guardar lo deja " +
                        "fijo para las próximas partidas.", _label);

        GUI.DragWindow(new Rect(0, 0, 10000, 18));
    }

    float Row(string caption, float value, float min, float max, string format)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(caption, _label, GUILayout.Width(70));
        float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(240));
        GUILayout.Label(result.ToString(format), _label, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        return result;
    }
}
