using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ExitGames.Client.Photon;
using Photon.Pun;

namespace ScoutDances.Sounds;

/// <summary>
/// Los 3 sonidos de myinstants.com que cada jugador elige en el kiosco del aeropuerto.
/// </summary>
/// <remarks>
/// Por la red viaja solo la ruta del MP3 (<c>/media/sounds/vine-boom.mp3</c>), nunca el
/// audio: cada cliente lo descarga por su cuenta (ver <see cref="InstantAudioCache"/>).
/// Usamos Player Custom Properties de Photon porque son <b>por jugador</b> y Photon las
/// replica solo, incluso a quien entre a la sala más tarde.
///
/// Guardamos también la ruta ya resuelta en la config para no volver a pedir la página
/// de myinstants en cada arranque.
/// </remarks>
internal static class SoundSlots
{
    /// <summary>
    /// 7 sonidos: con el botón de silencio hacen exactamente 8, que es lo que cabe en
    /// una página de la rueda de emotes. Ni hueco ni desbordamiento.
    /// </summary>
    /// Sonidos por jugador. Se reparten en páginas de <see cref="PerPage"/> en la rueda.
    public const int Count = 14;

    /// Cuántos caben en una página de la rueda. La octava ranura la ocupa el botón de
    /// cortar, así que son 7 sonidos por página y no 8.
    public const int PerPage = 7;

    /// Claves de las Custom Properties. Cortas a propósito: viajan en cada sync.
    static readonly string[] PropKeys = BuildKeys("sd_s");
    static readonly string[] VolumeKeys = BuildKeys("sd_v");

    static string[] BuildKeys(string prefix)
    {
        var keys = new string[Count];
        for (int i = 0; i < Count; i++) keys[i] = prefix + i;
        return keys;
    }

    static ConfigEntry<string>[] _link = null!;
    static ConfigEntry<string>[] _resolved = null!;
    static ConfigEntry<float>[] _volume = null!;

    internal static void Init(ConfigFile config)
    {
        _link = new ConfigEntry<string>[Count];
        _resolved = new ConfigEntry<string>[Count];
        _volume = new ConfigEntry<float>[Count];

        for (int i = 0; i < Count; i++)
        {
            _link[i] = config.Bind(
                "Sonidos", $"Slot{i + 1}", "",
                $"Enlace de myinstants.com para el sonido {i + 1}. Se configura desde el " +
                "kiosco del aeropuerto, pero también puedes pegarlo aquí a mano.");

            _resolved[i] = config.Bind(
                "Sonidos", $"Slot{i + 1}Resolved", "",
                "Ruta del MP3 ya resuelta. Se rellena sola; no hace falta tocarla.");

            _volume[i] = config.Bind(
                "Sonidos", $"Slot{i + 1}Volume", 0.5f,
                new ConfigDescription(
                    $"Volumen del sonido {i + 1}. Es por sonido porque los clips de " +
                    "myinstants vienen con niveles muy dispares. Se sincroniza: manda el " +
                    "volumen que elige el dueño del sonido, no el de quien escucha.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }

    internal static float GetLocalVolume(int slot) =>
        slot >= 0 && slot < Count ? _volume[slot].Value : 1f;

    internal static void SetLocalVolume(int slot, float value)
    {
        if (slot < 0 || slot >= Count) return;
        if (Mathf.Approximately(_volume[slot].Value, value)) return;

        _volume[slot].Value = value;
        PushToNetwork();
    }

    /// <summary>Volumen que ese jugador eligió para ese slot.</summary>
    internal static float GetVolumeFor(Photon.Realtime.Player? player, int slot)
    {
        if (player == null || slot < 0 || slot >= Count) return 1f;
        if (player.IsLocal) return GetLocalVolume(slot);

        if (player.CustomProperties != null &&
            player.CustomProperties.TryGetValue(VolumeKeys[slot], out var value) &&
            value is float volume)
        {
            return Mathf.Clamp01(volume);
        }
        return 0.5f;   // el que use una versión sin volumen sincronizado
    }

    /// Enlace tal cual lo escribió el jugador.
    internal static string GetRaw(int slot) => _link[slot].Value;

    /// Ruta del MP3 del slot local, o cadena vacía si aún no se ha resuelto.
    internal static string GetLocalPath(int slot) => _resolved[slot].Value;

    internal static string[] GetLocalPaths()
    {
        var paths = new string[Count];
        for (int i = 0; i < Count; i++) paths[i] = GetLocalPath(i);
        return paths;
    }

    /// <summary>
    /// Guarda los 3 enlaces, resuelve los que hagan falta y publica el resultado.
    /// </summary>
    internal static IEnumerator ApplyAndSync(string[] rawLinks)
    {
        for (int i = 0; i < Count && i < rawLinks.Length; i++)
        {
            var raw = (rawLinks[i] ?? "").Trim();
            bool changed = raw != _link[i].Value;
            _link[i].Value = raw;

            if (raw.Length == 0)
            {
                _resolved[i].Value = "";
                continue;
            }

            // Si ya estaba resuelto y el enlace no ha cambiado, no repetimos la petición.
            if (!changed && _resolved[i].Value.Length > 0) continue;

            int slot = i;
            yield return InstantAudioCache.Resolve(raw, path => _resolved[slot].Value = path);
        }

        PushToNetwork();

        var toFetch = new List<string>();
        foreach (var path in GetLocalPaths())
            if (path.Length > 0) toFetch.Add(path);

        InstantAudioCache.Prefetch(toFetch);   // descarga ya, para que suene a la primera
    }

    /// <summary>
    /// Asigna directamente un resultado del buscador a un slot.
    /// </summary>
    /// <remarks>
    /// Aquí ya tenemos la ruta del MP3, así que nos saltamos la resolución: guardamos la
    /// URL directa como enlace y la ruta como resuelta, publicamos y dejamos descargando.
    /// </remarks>
    internal static void AssignDirect(int slot, string mediaPath)
    {
        if (slot < 0 || slot >= Count || mediaPath.Length == 0) return;

        _link[slot].Value = $"https://{InstantAudioCache.Host}{mediaPath}";
        _resolved[slot].Value = mediaPath;

        PushToNetwork();
        InstantAudioCache.Request(mediaPath);
    }

    internal static void Clear(int slot)
    {
        if (slot < 0 || slot >= Count) return;
        _link[slot].Value = "";
        _resolved[slot].Value = "";
        PushToNetwork();
    }

    /// <summary>Publica las 3 rutas en las Custom Properties del jugador local.</summary>
    internal static void PushToNetwork()
    {
        if (!PhotonNetwork.InRoom) return;

        // Hashtable cualificado: System.Collections también define uno y colisionan.
        var props = new ExitGames.Client.Photon.Hashtable();
        for (int i = 0; i < Count; i++)
        {
            props[PropKeys[i]] = GetLocalPath(i);
            props[VolumeKeys[i]] = GetLocalVolume(i);
        }

        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        Plugin.Log.LogInfo($"Sonidos publicados: [{string.Join(", ", GetLocalPaths())}]");
    }

    /// <summary>
    /// Ruta del MP3 que ese jugador tiene en ese slot.
    /// </summary>
    /// <remarks>
    /// Ojo: PEAK tiene su propia clase 'Player' en el namespace global que tapa a
    /// Photon.Realtime.Player, de ahí el nombre completo.
    /// </remarks>
    internal static string GetPathFor(Photon.Realtime.Player? player, int slot)
    {
        if (player == null || slot < 0 || slot >= Count) return "";

        // Para el jugador local no dependemos de la red: en el lobby offline las
        // Custom Properties ni siquiera existen, y ahí también queremos oírnos.
        if (player.IsLocal) return GetLocalPath(slot);

        if (player.CustomProperties != null &&
            player.CustomProperties.TryGetValue(PropKeys[slot], out var value) &&
            value is string path)
        {
            return path;
        }
        return "";
    }
}
