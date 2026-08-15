using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// La lista de power-ups activos, encima de la barra de vida.
/// </summary>
/// <remarks>
/// <b>Dos estados por entrada.</b> Los primeros segundos se ve el resumen del efecto; luego
/// se encoge a una línea con el nombre y el tiempo. El resumen solo hace falta al principio:
/// cuando recoges "Zancada" por primera vez necesitas saber qué es, y quince segundos
/// después solo te interesa cuánto queda. Sin esto, cuatro power-ups a la vez taparían media
/// pantalla.
///
/// <b>Crece hacia arriba.</b> Así las entradas que ya estaban no se mueven cuando llega una
/// nueva; si creciera hacia abajo estarías leyendo un reloj que se desplaza.
///
/// <b>Orden de recogida, nunca por tiempo restante.</b> Ordenar por "el que menos queda"
/// parece lo lógico y reordenaría la lista cada pocos segundos, que es justo lo que impide
/// leerla. El aviso de que algo se acaba se da parpadeando, sin mover nada de sitio.
/// </remarks>
internal class BuffHud : MonoBehaviour
{
    const int MaxVisible = 5;

    GUIStyle? _name, _summary, _time, _more;
    Texture2D? _panel, _edge;

    void EnsureStyles()
    {
        if (_name != null) return;

        _name = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
        };
        _summary = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
        _summary.normal.textColor = new Color(0.82f, 0.86f, 0.9f, 0.95f);

        _time = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold,
        };
        _more = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        _more.normal.textColor = new Color(0.7f, 0.75f, 0.8f, 0.8f);

        _panel = Solid(new Color(0.05f, 0.07f, 0.09f, 0.72f));
        _edge = Solid(Color.white);
    }

    static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    /// Sitio calculado a partir de la barra de aguante, y cuándo se calculó.
    static Rect _anchor;
    static float _anchorAt = -99f;

    /// <summary>
    /// Dónde empieza la lista, medido sobre la barra de aguante de verdad.
    /// </summary>
    /// <remarks>
    /// <b>Píxeles fijos no valen.</b> La interfaz del juego escala con la resolución, así que
    /// un "150 píxeles desde abajo" que queda perfecto en 1080p se monta encima de la barra
    /// en 1440p y flota despegado en 720p. Se busca la barra, se mide dónde ha quedado y se
    /// coloca la lista justo encima: si el juego la mueve, nos movemos con ella.
    ///
    /// <b>El resultado se cachea medio segundo.</b> <c>OnGUI</c> corre dos o más veces por
    /// frame, y recorrer la escena buscando la barra en cada pasada sería trabajo repetido
    /// para un dato que solo cambia al redimensionar la ventana.
    ///
    /// Si no aparece la barra —otro mod la ha quitado, o aún no ha cargado— se cae a una
    /// posición proporcional a la altura de pantalla, que al menos escala.
    /// </remarks>
    static Rect Anchor()
    {
        if (Time.unscaledTime - _anchorAt < 0.5f) return _anchor;
        _anchorAt = Time.unscaledTime;

        float margin = Plugin.CfgBuffHudGap.Value;

        try
        {
            var bar = Object.FindFirstObjectByType<StaminaBar>();
            var rect = bar != null ? bar.GetComponent<RectTransform>() : null;

            if (rect != null)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);

                // Las esquinas vienen en espacio de mundo; en un Canvas en modo Overlay eso
                // ya son píxeles de pantalla, pero con la Y al revés que la de OnGUI.
                float left = Mathf.Min(corners[0].x, corners[1].x);
                float right = Mathf.Max(corners[2].x, corners[3].x);
                float top = Mathf.Max(corners[1].y, corners[2].y);

                float width = Mathf.Max(180f, right - left);
                float bottom = Screen.height - top - margin;

                _anchor = new Rect(left, bottom, width, 0f);
                return _anchor;
            }
        }
        catch { /* si no se puede medir, se usa el respaldo */ }

        // Respaldo proporcional: no es exacto, pero al menos no depende de la resolución.
        _anchor = new Rect(Screen.width * 0.02f,
                           Screen.height * 0.86f - margin,
                           Mathf.Max(200f, Screen.width * 0.18f), 0f);
        return _anchor;
    }

    /// <summary>Cuánto agrandar el texto y las filas según la pantalla.</summary>
    /// <remarks>
    /// Referencia 1080p. Sin esto, en 4K la lista se lee con lupa y en 720p tapa media
    /// esquina. Se limita por arriba y por abajo para que en pantallas raras no se
    /// desmadre.
    /// </remarks>
    static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.2f);

    void OnGUI()
    {
        if (!Plugin.CfgBuffHud.Value) return;

        var live = ActiveBuffs.Current;
        if (live.Count == 0) return;

        EnsureStyles();

        var anchor = Anchor();
        float scale = Scale;

        float width = anchor.width;
        float x = anchor.x;
        float bottom = anchor.y;

        float expandedFor = Plugin.CfgBuffSummarySeconds.Value;
        float y = bottom;
        int drawn = 0;

        // De la más reciente a la más antigua, apilando hacia arriba: la de abajo es
        // siempre la última que cogiste, que es donde está mirando el jugador.
        for (int i = live.Count - 1; i >= 0 && drawn < MaxVisible; i--, drawn++)
        {
            var item = live[i];
            bool expanded = Time.time - item.Shown < expandedFor;
            float height = (expanded ? 38f : 21f) * scale;

            y -= height + 3f * scale;
            Draw(new Rect(x, y, width, height), item, expanded, scale);
        }

        int hidden = live.Count - drawn;
        if (hidden > 0)
        {
            y -= 15f * scale;
            GUI.Label(new Rect(x + 6f * scale, y, width, 15f * scale), $"+{hidden} más", _more);
        }
    }

    void Draw(Rect rect, ActiveBuffs.Live item, bool expanded, float scale)
    {
        var color = BuffCatalog.RarityColor(item.Entry.Rarity);

        GUI.DrawTexture(rect, _panel!);

        // La rareza es el borde de color, no una palabra repetida en cada línea.
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, 3f * scale, rect.height), _edge!);
        GUI.color = previous;

        float pad = 9f * scale;
        var inner = new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, 20f * scale);

        _name!.fontSize = Mathf.RoundToInt(14f * scale);
        _summary!.fontSize = Mathf.RoundToInt(12f * scale);
        _time!.fontSize = Mathf.RoundToInt(13f * scale);
        _more!.fontSize = Mathf.RoundToInt(11f * scale);

        _name.normal.textColor = color;
        GUI.Label(inner, item.Entry.Name, _name);

        GUI.Label(inner, RightText(item), TimeStyle(item));

        if (expanded)
        {
            GUI.Label(new Rect(rect.x + pad, rect.y + 18f * scale,
                               rect.width - pad * 2f, 18f * scale),
                      item.Entry.Summary, _summary);
        }
    }

    /// <summary>Lo que va a la derecha: reloj, texto fijo, o nada.</summary>
    static string RightText(ActiveBuffs.Live item)
    {
        if (item.Entry.Persistent != null) return item.Entry.Persistent;
        if (item.Entry.Instant) return "";

        float left = item.Remaining;
        return left >= 10f ? $"{left:0} s" : $"{left:0.0} s";
    }

    GUIStyle TimeStyle(ActiveBuffs.Live item)
    {
        // Los últimos segundos parpadea. Es el aviso de que se acaba SIN reordenar la
        // lista, que es lo que la haría ilegible.
        bool ending = !item.Entry.Instant && item.Entry.Persistent == null &&
                      item.Remaining < 3f;

        _time!.normal.textColor = ending && Mathf.PingPong(Time.time * 4f, 1f) > 0.5f
            ? new Color(1f, 0.55f, 0.45f)
            : new Color(0.93f, 0.95f, 0.97f);

        return _time;
    }
}
