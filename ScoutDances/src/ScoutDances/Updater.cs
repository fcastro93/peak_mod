using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ScoutDances;

/// <summary>
/// Se actualiza solo desde las releases de GitHub.
/// </summary>
/// <remarks>
/// <b>Por qué basta con el plugin y no hace falta un patcher.</b> Un DLL cargado no se
/// puede sobrescribir, y esa es la razón habitual para meter una segunda pieza que corra
/// antes que los plugins. Pero probándolo sobre el juego en marcha resulta que en Windows
/// el fichero <b>sí se puede RENOMBRAR</b> aunque esté en uso: lo que está bloqueado es
/// escribir encima, no moverlo. Así que se aparta el viejo, se escribe el nuevo en su
/// sitio, y en el siguiente arranque BepInEx carga el nuevo. Una pieza en vez de dos.
///
/// <b>Se aplica pero no tiene efecto hasta reiniciar</b>, y eso es deseable: cambiar el
/// mod a mitad de partida sería mucho peor que esperar al siguiente arranque.
///
/// <b>El bundle se descarga aparte y solo si cambia.</b> Son 23 MB; bajarlos en cada
/// actualización del DLL sería absurdo, así que se compara su tamaño con el publicado
/// antes de tocarlo.
/// </remarks>
internal class Updater : MonoBehaviour
{
    /// Ficheros que se actualizan, tal como se llaman en la release.
    static readonly string[] Assets = { "fcastro.ScoutDances.dll", "scoutdances" };

    /// <summary>
    /// El patcher, que va en otra carpeta y sirve para no tener que arrancar dos veces.
    /// </summary>
    /// <remarks>
    /// Se instala desde aquí a propósito: nadie va a copiarlo a mano, y una vez puesto es él
    /// quien actualiza el mod en el mismo arranque en que descarga. O sea que esta clase
    /// existe, entre otras cosas, para dejar instalado a su relevo.
    ///
    /// Va a <c>BepInEx/patchers</c>, no a <c>plugins</c>: es la carpeta que lee el preloader,
    /// que corre antes de que se cargue ningún mod.
    /// </remarks>
    const string PatcherAsset = "fcastro.ScoutDances.Patcher.dll";

    void Start() => StartCoroutine(Run());

    IEnumerator Run()
    {
        // Lo primero, limpiar lo que quedó de una actualización anterior: esos ficheros ya
        // no los tiene cargados nadie, así que ahora sí se pueden borrar.
        CleanLeftovers();

        if (!Plugin.CfgAutoUpdate.Value) yield break;

        // Un respiro para no competir con la carga del juego.
        yield return new WaitForSeconds(5f);

        var repo = Plugin.CfgUpdateRepo.Value;
        if (string.IsNullOrWhiteSpace(repo)) yield break;

        string url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var request = UnityWebRequest.Get(url);

        // GitHub rechaza las peticiones sin User-Agent.
        request.SetRequestHeader("User-Agent", "ScoutDances");
        request.SetRequestHeader("Accept", "application/vnd.github+json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Log.LogInfo($"No pude consultar actualizaciones: {request.error}");
            yield break;
        }

        var json = request.downloadHandler.text;
        var latest = Extract(json, "\"tag_name\"");

        if (string.IsNullOrEmpty(latest))
        {
            Plugin.Log.LogInfo("La release no trae etiqueta de versión; no actualizo.");
            yield break;
        }

        // Lo primero, dejar puesto el patcher aunque el mod esté al día: es lo que hace que
        // la PRÓXIMA actualización entre en un solo arranque. Si se hiciera solo al detectar
        // versión nueva, quien ya estuviera actualizado no lo recibiría nunca.
        yield return EnsurePatcher(json);

        var current = Plugin.Instance.Info.Metadata.Version.ToString();

        if (Normalize(latest) == Normalize(current))
        {
            Plugin.Log.LogInfo($"Mod al día (v{current}).");
            yield break;
        }

        Plugin.Log.LogInfo($"Hay una versión nueva: {latest} (tienes {current}). Descargando…");

        int done = 0;
        foreach (var asset in Assets)
        {
            var link = FindAsset(json, asset);
            if (link == null)
            {
                Plugin.Log.LogWarning($"La release no incluye '{asset}'.");
                continue;
            }

            yield return Download(link, asset, r => { if (r) done++; });
        }

        Plugin.Log.LogWarning(done > 0
            ? $"Actualización a {latest} lista ({done} fichero(s)). REINICIA EL JUEGO para aplicarla."
            : "No pude descargar la actualización; sigue la versión actual.");
    }

    /// <summary>Deja el patcher instalado en BepInEx/patchers si falta o si cambió.</summary>
    IEnumerator EnsurePatcher(string json)
    {
        string? folder = null;

        try
        {
            var plugins = Path.GetDirectoryName(Plugin.Instance.Info.Location)!;
            var bepinex = Path.GetDirectoryName(plugins)!;
            folder = Path.Combine(bepinex, "patchers");
            Directory.CreateDirectory(folder);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"No pude preparar la carpeta de patchers: {e.Message}");
            yield break;
        }

        var link = FindAsset(json, PatcherAsset);
        if (link == null) yield break;

        var target = Path.Combine(folder, PatcherAsset);

        using var request = UnityWebRequest.Get(link);
        request.SetRequestHeader("User-Agent", "ScoutDances");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success) yield break;

        var data = request.downloadHandler.data;

        try
        {
            // El patcher NO está cargado ahora mismo —lo estuvo durante el preloader y ya
            // terminó— así que se puede escribir encima sin apartar nada.
            if (File.Exists(target) && new FileInfo(target).Length == data.Length) yield break;

            File.WriteAllBytes(target, data);
            Plugin.Log.LogWarning(
                $"Instalado el actualizador rápido en patchers/ ({data.Length / 1024} KB). " +
                "A partir del siguiente arranque, las actualizaciones entran sin reiniciar dos veces.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"No pude instalar el patcher: {e.Message}");
        }
    }

    IEnumerator Download(string url, string fileName, System.Action<bool> onDone)
    {
        using var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("User-Agent", "ScoutDances");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Log.LogWarning($"Fallo bajando '{fileName}': {request.error}");
            onDone(false);
            yield break;
        }

        onDone(Install(fileName, request.downloadHandler.data));
    }

    /// <summary>
    /// Aparta el fichero en uso y deja el nuevo en su lugar.
    /// </summary>
    /// <remarks>
    /// El renombrado es la clave: Windows no deja ESCRIBIR sobre un fichero cargado, pero
    /// sí moverlo. Una vez apartado, el hueco queda libre para el nuevo. Si algo falla a
    /// medias se devuelve el viejo a su sitio, porque quedarse sin ninguno de los dos
    /// dejaría el mod sin cargar en el siguiente arranque.
    /// </remarks>
    static bool Install(string fileName, byte[] data)
    {
        try
        {
            var folder = Path.GetDirectoryName(Plugin.Instance.Info.Location)!;
            var target = Path.Combine(folder, fileName);

            // El bundle pesa 23 MB: si es idéntico, no se toca.
            if (File.Exists(target) && new FileInfo(target).Length == data.Length)
            {
                Plugin.Log.LogInfo($"'{fileName}' no ha cambiado; lo dejo como está.");
                return false;
            }

            var parked = target + ".old";
            if (File.Exists(parked)) File.Delete(parked);

            bool moved = false;
            if (File.Exists(target))
            {
                File.Move(target, parked);
                moved = true;
            }

            try
            {
                File.WriteAllBytes(target, data);
            }
            catch
            {
                if (moved) File.Move(parked, target);   // que no se quede sin ninguno
                throw;
            }

            Plugin.Log.LogInfo($"'{fileName}' actualizado ({data.Length / 1024} KB).");
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude instalar '{fileName}': {e.Message}");
            return false;
        }
    }

    /// <summary>Borra los ficheros apartados por una actualización anterior.</summary>
    static void CleanLeftovers()
    {
        try
        {
            var folder = Path.GetDirectoryName(Plugin.Instance.Info.Location)!;

            foreach (var leftover in Directory.GetFiles(folder, "*.old"))
            {
                File.Delete(leftover);
                Plugin.Log.LogInfo($"Limpiado {Path.GetFileName(leftover)} de la actualización anterior.");
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"No pude limpiar restos de actualizaciones: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ JSON a mano

    /// <remarks>
    /// Se lee a mano en vez de con una librería de JSON: solo hacen falta dos campos, y el
    /// mod ya rasca HTML de myinstants con la misma técnica. Meter una dependencia nueva
    /// para esto sería desproporcionado.
    /// </remarks>
    static string? Extract(string json, string key)
    {
        int at = json.IndexOf(key, System.StringComparison.Ordinal);
        if (at < 0) return null;

        int start = json.IndexOf('"', json.IndexOf(':', at) + 1) + 1;
        int end = json.IndexOf('"', start);

        return start > 0 && end > start ? json.Substring(start, end - start) : null;
    }

    /// <summary>Busca el enlace de descarga del asset con ese nombre.</summary>
    static string? FindAsset(string json, string name)
    {
        int at = json.IndexOf($"\"name\":\"{name}\"", System.StringComparison.Ordinal);

        // GitHub a veces devuelve el JSON con espacios; se prueba también así.
        if (at < 0) at = json.IndexOf($"\"name\": \"{name}\"", System.StringComparison.Ordinal);
        if (at < 0) return null;

        int link = json.IndexOf("browser_download_url", at, System.StringComparison.Ordinal);
        if (link < 0) return null;

        int start = json.IndexOf('"', json.IndexOf(':', link) + 1) + 1;
        int end = json.IndexOf('"', start);

        return start > 0 && end > start ? json.Substring(start, end - start) : null;
    }

    static string Normalize(string version) => version.TrimStart('v', 'V').Trim();
}
