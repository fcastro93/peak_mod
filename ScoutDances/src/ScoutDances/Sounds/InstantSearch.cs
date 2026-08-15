using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace ScoutDances.Sounds;

/// <summary>
/// Busca sonidos en myinstants.com y devuelve los resultados ya parseados.
/// </summary>
/// <remarks>
/// Evitamos empotrar un navegador (Chromium serían +100 MB junto al mod y la inyección
/// de botones en el DOM es frágil): la página de búsqueda incrusta cada sonido en un
/// <c>onclick="play('/media/sounds/x.mp3', …)" title="Play NOMBRE sound"</c>, así que
/// sacamos nombre y ruta con una expresión regular y pintamos la lista con la UI del mod.
/// </remarks>
internal static class InstantSearch
{
    internal readonly struct Result
    {
        internal readonly string Name;
        internal readonly string MediaPath;

        internal Result(string name, string mediaPath)
        {
            Name = name;
            MediaPath = mediaPath;
        }
    }

    static readonly Regex ResultPattern = new(
        @"onclick=""play\('(?<path>/media/sounds/[^']+\.mp3)'[^""]*""\s+title=""Play (?<name>[^""]*) sound""",
        RegexOptions.IgnoreCase);

    internal static bool Busy { get; private set; }
    internal static string LastError = "";

    internal static IEnumerator Search(string query, Action<List<Result>> onDone)
    {
        Busy = true;
        LastError = "";
        var results = new List<Result>();

        var url = $"https://{InstantAudioCache.Host}/en/search/?name={UnityWebRequest.EscapeURL(query)}";
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 20;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = request.error;
                Plugin.Log.LogWarning($"Búsqueda '{query}' falló: {request.error}");
                Busy = false;
                onDone(results);
                yield break;
            }

            var seen = new HashSet<string>();
            foreach (Match match in ResultPattern.Matches(request.downloadHandler.text))
            {
                var path = match.Groups["path"].Value;
                if (!seen.Add(path)) continue;   // la página repite cada botón
                results.Add(new Result(DecodeEntities(match.Groups["name"].Value), path));
            }
        }

        Plugin.Log.LogInfo($"Búsqueda '{query}': {results.Count} resultados.");
        Busy = false;
        onDone(results);
    }

    /// Los títulos vienen con entidades HTML (&#x27; y compañía).
    static string DecodeEntities(string text)
    {
        if (text.IndexOf('&') < 0) return text;

        text = Regex.Replace(text, @"&#x([0-9A-Fa-f]+);",
            m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        text = Regex.Replace(text, @"&#(\d+);",
            m => ((char)int.Parse(m.Groups[1].Value)).ToString());

        return text.Replace("&amp;", "&")
                   .Replace("&quot;", "\"")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&nbsp;", " ");
    }
}
