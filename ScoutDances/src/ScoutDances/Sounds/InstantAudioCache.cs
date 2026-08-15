using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace ScoutDances.Sounds;

/// <summary>
/// Resuelve enlaces de myinstants.com a <see cref="AudioClip"/> reproducibles y los
/// cachea en disco.
/// </summary>
/// <remarks>
/// El sitio sirve los sonidos como MP3 directos y son clips cortos, así que no hace
/// falta ninguna herramienta externa: basta con UnityWebRequest. La página de cada
/// sonido incrusta su MP3 en un <c>onclick="play('/media/sounds/&lt;slug&gt;.mp3', …)"</c>,
/// y una página de instant concreta contiene exactamente uno.
///
/// Por la red viaja solo la ruta del MP3 (algo como <c>/media/sounds/vine-boom.mp3</c>),
/// nunca el audio: cada cliente lo descarga por su cuenta.
/// </remarks>
internal static class InstantAudioCache
{
    internal const string Host = "www.myinstants.com";
    const string BaseUrl = "https://" + Host;
    const string CacheFolderName = "soundcache";

    static readonly Regex MediaPattern = new(@"/media/sounds/[^""'\s\\)]+\.mp3", RegexOptions.IgnoreCase);

    static readonly Dictionary<string, AudioClip> Loaded = new();
    static readonly HashSet<string> InFlight = new();
    static readonly HashSet<string> Failed = new();

    /// Resultados de resolver una página a su MP3, para no repetir la petición.
    static readonly Dictionary<string, string> ResolvedPages = new();

    static string _cacheDir = "";

    internal static void Init(string pluginDir)
    {
        _cacheDir = Path.Combine(pluginDir, CacheFolderName);
        Directory.CreateDirectory(_cacheDir);
    }

    // ------------------------------------------------------------- resolución

    /// <summary>
    /// Convierte lo que escribió el jugador en una ruta de MP3 (<c>/media/sounds/x.mp3</c>),
    /// si se puede saber sin pedir nada a la red.
    /// </summary>
    /// <returns>La ruta, o cadena vacía si hay que resolver la página primero.</returns>
    internal static string TryGetDirectPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = raw!.Trim();

        // Ya es la ruta del MP3, con o sin host.
        var match = MediaPattern.Match(text);
        if (match.Success && (text.StartsWith("/") || text.Contains(Host)))
            return match.Value;

        return "";
    }

    /// <summary>¿Es un enlace de myinstants que podamos intentar resolver?</summary>
    internal static bool LooksLikeInstantLink(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw!.Trim();
        return TryGetDirectPath(text).Length > 0 ||
               (text.Contains(Host, StringComparison.OrdinalIgnoreCase) && text.Contains("/instant/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resuelve un enlace a la ruta de su MP3, pidiendo la página si hace falta.
    /// </summary>
    internal static IEnumerator Resolve(string raw, Action<string> onDone)
    {
        var direct = TryGetDirectPath(raw);
        if (direct.Length > 0) { onDone(direct); yield break; }

        var pageUrl = (raw ?? "").Trim();
        if (!LooksLikeInstantLink(pageUrl)) { onDone(""); yield break; }

        if (ResolvedPages.TryGetValue(pageUrl, out var cached)) { onDone(cached); yield break; }

        // Nos limitamos a myinstants a propósito: esto no debe convertirse en un
        // descargador de URLs arbitrarias metido dentro del juego.
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.Log.LogWarning($"'{pageUrl}' no es un enlace de {Host}.");
            onDone("");
            yield break;
        }

        using var request = UnityWebRequest.Get(pageUrl);
        request.timeout = 20;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Log.LogWarning($"No pude abrir '{pageUrl}': {request.error}");
            onDone("");
            yield break;
        }

        var found = MediaPattern.Match(request.downloadHandler.text);
        if (!found.Success)
        {
            Plugin.Log.LogWarning($"'{pageUrl}' no contiene ningún MP3 reconocible.");
            onDone("");
            yield break;
        }

        ResolvedPages[pageUrl] = found.Value;
        Plugin.Log.LogInfo($"Resuelto '{pageUrl}' -> {found.Value}");
        onDone(found.Value);
    }

    // ------------------------------------------------------------- descarga

    static string FileNameFor(string mediaPath) =>
        Path.GetFileName(mediaPath).Replace("%20", "_");

    static string PathFor(string mediaPath) => Path.Combine(_cacheDir, FileNameFor(mediaPath));

    internal static bool IsCached(string mediaPath) =>
        mediaPath.Length > 0 && File.Exists(PathFor(mediaPath));

    internal static AudioClip? Get(string mediaPath)
    {
        if (mediaPath.Length == 0) return null;
        return Loaded.TryGetValue(mediaPath, out var clip) ? clip : null;
    }

    /// <summary>Se asegura de que el clip acabe en memoria. No bloquea.</summary>
    internal static void Request(string mediaPath)
    {
        if (mediaPath.Length == 0) return;
        if (Loaded.ContainsKey(mediaPath) || InFlight.Contains(mediaPath) || Failed.Contains(mediaPath)) return;

        InFlight.Add(mediaPath);
        Plugin.Instance.StartCoroutine(Fetch(mediaPath));
    }

    internal static void Prefetch(IEnumerable<string> mediaPaths)
    {
        foreach (var path in mediaPaths) Request(path);
    }

    static IEnumerator Fetch(string mediaPath)
    {
        var file = PathFor(mediaPath);

        if (!File.Exists(file))
        {
            using var download = UnityWebRequest.Get(BaseUrl + mediaPath);
            download.timeout = 30;
            yield return download.SendWebRequest();

            if (download.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"No pude descargar '{mediaPath}': {download.error}");
                Failed.Add(mediaPath);
                InFlight.Remove(mediaPath);
                yield break;
            }

            try
            {
                File.WriteAllBytes(file, download.downloadHandler.data);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"No pude guardar '{file}': {e.Message}");
                Failed.Add(mediaPath);
                InFlight.Remove(mediaPath);
                yield break;
            }
        }

        yield return Load(mediaPath, file);
        InFlight.Remove(mediaPath);
    }

    static IEnumerator Load(string mediaPath, string file)
    {
        var uri = new Uri(file).AbsoluteUri;   // file:///C:/...
        using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Log.LogWarning($"No pude cargar '{file}': {request.error}");
            Failed.Add(mediaPath);
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null)
        {
            Failed.Add(mediaPath);
            yield break;
        }

        clip.name = FileNameFor(mediaPath);
        Loaded[mediaPath] = clip;
        Plugin.Log.LogInfo($"Sonido listo: '{clip.name}' ({clip.length:0.0}s)");
    }

    /// Nombre legible para la UI del kiosco.
    internal static string PrettyName(string mediaPath) =>
        mediaPath.Length == 0 ? "" : Path.GetFileNameWithoutExtension(mediaPath).Replace('-', ' ');
}
