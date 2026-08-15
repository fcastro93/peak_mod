using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil;

namespace ScoutDances.Patcher;

/// <summary>
/// Actualiza el mod ANTES de que el juego lo cargue, para no tener que arrancar dos veces.
/// </summary>
/// <remarks>
/// <b>Por qué existe esta pieza aparte.</b> El actualizador que vive dentro del mod no puede
/// aplicarse a sí mismo: cuando corre, su propio DLL ya está cargado y Windows no deja
/// sobrescribir un fichero en uso. Por eso deja el nuevo apartado y avisa de reiniciar, y de
/// ahí el "abre, cierra, abre" que molestaba.
///
/// BepInEx arranca en dos fases —primero el <i>preloader</i>, que corre lo que haya en
/// <c>BepInEx/patchers</c>, y después el <i>chainloader</i>, que carga los plugins—. Aquí
/// estamos en la primera: el DLL del mod todavía no lo ha abierto nadie, así que se
/// sobrescribe sin trucos y el juego carga la versión nueva en ESTE mismo arranque.
///
/// <b>Falla hacia el lado bueno.</b> Todo va con límite de tiempo y envuelto en try/catch:
/// si GitHub no responde, si no hay red o si algo sale mal, el arranque sigue con la versión
/// que haya. Un mod desactualizado es un incordio; un juego que no arranca es otra cosa.
///
/// <b>Nunca se escribe encima de un fichero bueno hasta tener el nuevo entero.</b> Se
/// descarga a un temporal, se comprueba que el tamaño coincide con el que anuncia la release
/// y solo entonces se mueve a su sitio, guardando el anterior como <c>.bak</c>. Sin eso, una
/// descarga cortada a la mitad dejaría el mod sin cargar — que es peor que no actualizar.
/// </remarks>
public static class Patcher
{
    /// El preloader lo exige aunque no parcheemos ningún ensamblado.
    public static IEnumerable<string> TargetDLLs => Array.Empty<string>();

    public static void Patch(AssemblyDefinition _) { }

    static readonly ManualLogSource Log =
        Logger.CreateLogSource("ScoutDances.Patcher");

    /// Ficheros que se actualizan, tal como se llaman en la release.
    static readonly string[] Assets = { "fcastro.ScoutDances.dll", "scoutdances" };

    const string Repo = "fcastro93/peak_mod";

    /// Cuánto se le concede a GitHub antes de rendirse. El arranque está parado mientras.
    const int TimeoutMs = 4000;

    /// <summary>Lo llama el preloader antes de cargar ningún plugin.</summary>
    public static void Initialize()
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Log.LogWarning($"No pude comprobar actualizaciones: {e.Message}. Sigo con lo que hay.");
        }
    }

    static void Run()
    {
        var plugins = PluginsFolder();
        if (plugins == null)
        {
            Log.LogInfo("No encuentro la carpeta de plugins; no actualizo.");
            return;
        }

        var current = InstalledVersion(Path.Combine(plugins, Assets[0]));
        if (current == null)
        {
            Log.LogInfo("El mod no está instalado todavía; no hay nada que actualizar.");
            return;
        }

        var json = Fetch($"https://api.github.com/repos/{Repo}/releases/latest");
        if (json == null) return;

        var latest = Extract(json, "\"tag_name\"")?.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(latest))
        {
            Log.LogInfo("La release no trae etiqueta; no actualizo.");
            return;
        }

        if (latest == current)
        {
            Log.LogInfo($"Mod al día (v{current}).");
            return;
        }

        Log.LogWarning($"Versión nueva: {latest} (tienes {current}). Actualizando antes de cargar…");

        int done = 0;
        foreach (var asset in Assets)
        {
            if (Install(plugins, asset, json)) done++;
        }

        Log.LogWarning(done > 0
            ? $"Listo: {latest} aplicada en este mismo arranque."
            : "No pude aplicar la actualización; sigo con la versión instalada.");
    }

    /// <summary>La carpeta de plugins, a partir de dónde está este patcher.</summary>
    /// <remarks>
    /// Se deduce de la ruta propia en vez de darla por hecha: este fichero vive en
    /// <c>BepInEx/patchers</c>, así que <c>../plugins</c> es la carpeta hermana. Así funciona
    /// aunque BepInEx esté instalado en otra unidad o con la carpeta renombrada.
    /// </remarks>
    static string? PluginsFolder()
    {
        try
        {
            var here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var bepinex = Path.GetDirectoryName(here);
            if (bepinex == null) return null;

            var plugins = Path.Combine(bepinex, "plugins");
            return Directory.Exists(plugins) ? plugins : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Lee la versión del DLL instalado SIN cargarlo.
    /// </summary>
    /// <remarks>
    /// Con Mono.Cecil, que BepInEx ya trae para su propio trabajo. Cargarlo con Reflection
    /// para leer un número lo dejaría bloqueado, que es justo lo que hay que evitar aquí.
    /// </remarks>
    static string? InstalledVersion(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var version = assembly.Name.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception e)
        {
            Log.LogInfo($"No pude leer la versión instalada: {e.Message}");
            return null;
        }
    }

    static string? Fetch(string url)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "ScoutDances";
            request.Accept = "application/vnd.github+json";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;

            using var response = request.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e)
        {
            Log.LogInfo($"GitHub no respondió ({e.Message}); sigo sin actualizar.");
            return null;
        }
    }

    /// <summary>Descarga un adjunto y lo pone en su sitio, si de verdad cambió.</summary>
    static bool Install(string folder, string fileName, string json)
    {
        try
        {
            var (url, size) = FindAsset(json, fileName);
            if (url == null) return false;

            var target = Path.Combine(folder, fileName);

            // El bundle son 23 MB: si pesa lo mismo, no se toca.
            if (File.Exists(target) && new FileInfo(target).Length == size)
            {
                Log.LogInfo($"'{fileName}' no ha cambiado.");
                return false;
            }

            var temp = target + ".new";
            Download(url, temp);

            // La comprobación que evita dejar el mod inservible: si la descarga se cortó,
            // el tamaño no cuadra y no se toca lo que ya funcionaba.
            var got = new FileInfo(temp).Length;
            if (size > 0 && got != size)
            {
                File.Delete(temp);
                Log.LogWarning($"'{fileName}' llegó incompleto ({got} de {size} bytes); lo descarto.");
                return false;
            }

            var backup = target + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            if (File.Exists(target)) File.Move(target, backup);

            File.Move(temp, target);

            Log.LogWarning($"'{fileName}' actualizado ({got / 1024} KB).");
            return true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"No pude actualizar '{fileName}': {e.Message}");
            return false;
        }
    }

    static void Download(string url, string destination)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "ScoutDances";
        request.Timeout = TimeoutMs;
        request.ReadWriteTimeout = TimeoutMs * 8;   // el bundle son 23 MB

        using var response = request.GetResponse();
        using var stream = response.GetResponseStream();
        using var file = File.Create(destination);
        stream.CopyTo(file);
    }

    // ------------------------------------------------------------------ JSON a mano

    static string? Extract(string json, string key)
    {
        int at = json.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;

        int start = json.IndexOf('"', json.IndexOf(':', at) + 1) + 1;
        int end = json.IndexOf('"', start);

        return start > 0 && end > start ? json.Substring(start, end - start) : null;
    }

    /// <summary>Enlace y tamaño del adjunto con ese nombre.</summary>
    static (string? Url, long Size) FindAsset(string json, string name)
    {
        int at = json.IndexOf($"\"name\":\"{name}\"", StringComparison.Ordinal);
        if (at < 0) at = json.IndexOf($"\"name\": \"{name}\"", StringComparison.Ordinal);
        if (at < 0) return (null, 0);

        long size = 0;
        int sizeAt = json.IndexOf("\"size\"", at, StringComparison.Ordinal);
        if (sizeAt >= 0)
        {
            int from = json.IndexOf(':', sizeAt) + 1;
            int to = from;
            while (to < json.Length && (char.IsDigit(json[to]) || json[to] == ' ')) to++;
            long.TryParse(json.Substring(from, to - from).Trim(), out size);
        }

        int link = json.IndexOf("browser_download_url", at, StringComparison.Ordinal);
        if (link < 0) return (null, size);

        int start = json.IndexOf('"', json.IndexOf(':', link) + 1) + 1;
        int end = json.IndexOf('"', start);

        return (start > 0 && end > start ? json.Substring(start, end - start) : null, size);
    }
}
