using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Dibuja un espejito sobre la barra de vida mientras llevas el escudo puesto.
/// </summary>
/// <remarks>
/// El espejo no se ve por ninguna parte hasta que refleja —es lo que lo hace divertido
/// contra los demás— pero eso deja al que lo lleva sin saber si sigue activo. Este icono
/// resuelve solo eso: te lo recuerda a ti, sin delatarte.
///
/// <b>El icono se dibuja, no se carga.</b> Los prefabs del bundle son modelos 3D, no
/// texturas de interfaz; sacar una imagen de uno implicaría renderizarlo a una cámara
/// aparte. Para un óvalo con brillo no compensa: se pinta una vez a mano y queda igual de
/// legible a ese tamaño.
///
/// Va en IMGUI y en coordenadas de pantalla en vez de colgarlo del HUD del juego, porque
/// su barra de vida es un objeto de Unity UI con su propio orden de dibujado: meterse ahí
/// dentro es frágil y esto no necesita tanto.
/// </remarks>
internal class MirrorHud : MonoBehaviour
{
    static Texture2D? _icon;

    void OnGUI()
    {
        if (!Plugin.CfgMirrorHud.Value) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null || local.data.dead) return;
        if (!MirrorShield.IsShielded(local)) return;

        var texture = Icon();
        if (texture == null) return;

        float size = Plugin.CfgMirrorHudSize.Value;

        // Abajo a la izquierda, justo encima de la barra de vida. Se cuenta desde el
        // borde inferior izquierdo para que aguante cualquier resolución.
        var rect = new Rect(Plugin.CfgMirrorHudX.Value,
                            Screen.height - Plugin.CfgMirrorHudY.Value - size,
                            size, size);

        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
    }

    /// <summary>Dibuja el espejito una sola vez y lo reutiliza.</summary>
    static Texture2D? Icon()
    {
        if (_icon != null) return _icon;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];

        float cx = size / 2f, cy = size / 2f;
        float rx = size * 0.30f, ry = size * 0.40f;   // óvalo vertical, como un espejo de mano

        var frame = new Color(0.85f, 0.75f, 0.35f);   // marco dorado
        var glass = new Color(0.70f, 0.90f, 0.85f);   // cristal verdoso
        var shine = new Color(1f, 1f, 1f, 0.95f);     // brillo

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            float d = dx * dx + dy * dy;

            Color color;
            if (d > 1.15f) color = new Color(0f, 0f, 0f, 0f);        // fuera
            else if (d > 0.80f) color = frame;                        // marco
            else
            {
                // Una banda diagonal más clara: es lo que hace que se lea como un cristal
                // y no como una moneda.
                float diagonal = (x - y) / (float)size;
                color = diagonal > 0.05f && diagonal < 0.22f ? shine : glass;
            }

            pixels[y * size + x] = color;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.name = "ScoutDancesMirrorIcon";

        _icon = tex;
        return _icon;
    }
}
