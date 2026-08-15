using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Cosas comunes a las ventanas del mod: el cursor y la medición de cuánto cuestan.
/// </summary>
internal static class KioskUi
{
    // ------------------------------------------------------------------ cursor

    /// <summary>
    /// Libera el ratón mientras una ventana está abierta.
    /// </summary>
    /// <remarks>
    /// Va en LateUpdate y NO en Update: el juego vuelve a bloquear el cursor en su propio
    /// LateUpdate, que corre después, y se comía nuestro desbloqueo. El ajustador de armas
    /// salía sin ratón justo por esto.
    ///
    /// <c>lastBlockedInput</c> es mecanismo del propio juego: mientras sea reciente,
    /// <c>GUIManager.windowBlockingInput</c> queda a true y <c>Character.CanDoInput()</c>
    /// devuelve false, así que el jugador no camina ni abre la rueda mientras escribe.
    /// </remarks>
    internal static void Free()
    {
        var gui = GUIManager.instance;
        if (gui != null) gui.lastBlockedInput = Time.frameCount;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Pone el puntero normal de Windows. Llamar UNA vez, al abrir.
    /// </summary>
    /// <remarks>
    /// <c>Cursor.visible = true</c> no basta: muestra la textura que el juego haya puesto
    /// con <c>SetCursor</c>, que es su puntero de partida. Pasarle null devuelve el del
    /// sistema operativo, que es lo que uno espera en una ventana con campos de texto y
    /// botones.
    ///
    /// Una sola vez y no cada frame: <c>SetCursor</c> toca el cursor del SO, y repetirlo
    /// 60 veces por segundo es justo el tipo de llamada que provoca tirones.
    /// </remarks>
    internal static void UseSystemCursor() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

    /// <summary>Devuelve el ratón al juego al cerrar la ventana.</summary>
    internal static void Restore()
    {
        // El juego reaplica su propio puntero cuando lo necesita; a nosotros nos basta
        // con volver a esconderlo y bloquearlo.
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ------------------------------------------------------------------ medición

    /// <summary>
    /// Mide cuánto tarda cada ventana en dibujarse y cuántas veces por frame se dibuja.
    /// </summary>
    /// <remarks>
    /// OnGUI no se llama una vez por frame: IMGUI lo invoca al menos dos veces (Layout y
    /// Repaint) y una más por cada evento de teclado o ratón. Un trabajo que parece barato
    /// "por frame" puede estar corriendo seis veces, así que hay que contar las llamadas
    /// además de cronometrarlas.
    /// </remarks>
    static readonly Dictionary<string, Sample> Samples = new();
    static readonly Stopwatch Watch = new();
    static float _nextReport;

    struct Sample
    {
        internal int Calls;
        internal double Milliseconds;
        internal int Frames;
        internal int LastFrame;
    }

    internal static void Begin() => Watch.Restart();

    internal static void End(string window)
    {
        Watch.Stop();

        Samples.TryGetValue(window, out var sample);
        sample.Calls++;
        sample.Milliseconds += Watch.Elapsed.TotalMilliseconds;
        if (sample.LastFrame != Time.frameCount)
        {
            sample.LastFrame = Time.frameCount;
            sample.Frames++;
        }
        Samples[window] = sample;

        if (!Plugin.CfgKioskProfiler.Value || Time.unscaledTime < _nextReport) return;
        _nextReport = Time.unscaledTime + 1f;

        foreach (var entry in Samples)
        {
            var s = entry.Value;
            if (s.Frames == 0) continue;

            Plugin.Log.LogInfo($"[gui] {entry.Key}: {s.Milliseconds / s.Frames:0.00} ms/frame, " +
                               $"{s.Calls / (float)s.Frames:0.0} llamadas/frame " +
                               $"({s.Milliseconds / s.Calls:0.00} ms cada una)");
        }

        Samples.Clear();
    }
}
