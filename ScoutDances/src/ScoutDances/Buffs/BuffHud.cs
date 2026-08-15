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

    void OnGUI()
    {
        if (!Plugin.CfgBuffHud.Value) return;

        var live = ActiveBuffs.Current;
        if (live.Count == 0) return;

        EnsureStyles();

        float width = Plugin.CfgBuffHudWidth.Value;
        float x = Plugin.CfgBuffHudX.Value;
        float bottom = Screen.height - Plugin.CfgBuffHudBottom.Value;

        float expandedFor = Plugin.CfgBuffSummarySeconds.Value;
        float y = bottom;
        int drawn = 0;

        // De la más reciente a la más antigua, apilando hacia arriba: la de abajo es
        // siempre la última que cogiste, que es donde está mirando el jugador.
        for (int i = live.Count - 1; i >= 0 && drawn < MaxVisible; i--, drawn++)
        {
            var item = live[i];
            bool expanded = Time.time - item.Shown < expandedFor;
            float height = expanded ? 38f : 21f;

            y -= height + 3f;
            Draw(new Rect(x, y, width, height), item, expanded);
        }

        int hidden = live.Count - drawn;
        if (hidden > 0)
        {
            y -= 15f;
            GUI.Label(new Rect(x + 6f, y, width, 15f), $"+{hidden} más", _more);
        }
    }

    void Draw(Rect rect, ActiveBuffs.Live item, bool expanded)
    {
        var color = BuffCatalog.RarityColor(item.Entry.Rarity);

        GUI.DrawTexture(rect, _panel!);

        // La rareza es el borde de color, no una palabra repetida en cada línea.
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), _edge!);
        GUI.color = previous;

        var inner = new Rect(rect.x + 9f, rect.y, rect.width - 18f, 20f);

        _name!.normal.textColor = color;
        GUI.Label(inner, item.Entry.Name, _name);

        GUI.Label(inner, RightText(item), TimeStyle(item));

        if (expanded)
        {
            GUI.Label(new Rect(rect.x + 9f, rect.y + 18f, rect.width - 18f, 18f),
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
