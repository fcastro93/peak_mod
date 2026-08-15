using System.Collections.Generic;
using HarmonyLib;
using PEAKEmoteLib;
using UnityEngine;
using UnityEngine.Audio;

namespace ScoutDances.Sounds;

/// <summary>
/// Los emotes de sonido (7) más el de corte: ocupan una página entera de la rueda y
/// disparan el audio desde el personaje que los usa.
/// </summary>
internal static class SoundEmotes
{
    /// Sufijo del nombre del emote. El número de slot va detrás (1..7).
    internal const string NamePrefix = "Sound";

    internal static readonly List<Emote> Emotes = new();

    /// <summary>Nombres completos tal y como los registra PEAKEmoteLib.</summary>
    internal static readonly HashSet<string> FullNames = new();

    /// <summary>Nombre completo del emote que corta el sonido.</summary>
    /// Nombres de los botones de corte, uno por página.
    internal static readonly HashSet<string> StopNames = new();

    /// <summary>
    /// Registra los 7 emotes de sonido y el de corte. Todos comparten el mismo clip de
    /// animación (un idle): lo que cambia entre ellos es el sonido, no la pose.
    /// </summary>
    internal static void Register(Plugin plugin, AnimationClip idleClip, string emotePrefix)
    {
        // Se registran POR PÁGINA —7 sonidos y su botón de cortar— y en ese orden, porque
        // la rueda coloca los emotes tal como llegan. Registrar los 14 seguidos y los dos
        // cortes al final dejaría ambos botones juntos en la última página.
        for (int i = 0; i < SoundSlots.Count; i++)
        {
            int slot = i;
            var emote = new Emote(
                emotePrefix + NamePrefix + (slot + 1),
                idleClip,
                IconFor(slot),
                type: Emote.EmoteType.OneShot,
                disableIK: false);

            // Texto de reserva: el nombre real del sonido lo pinta EmoteWheelHoverPatch,
            // porque cambia cada vez que reasignas el slot y aquí solo se registra una vez.
            var label = $"Sonido {slot + 1}";
            emote.AddLocalization(label, LocalizedText.Language.English);
            emote.AddLocalization(label, LocalizedText.Language.SpanishSpain);
            emote.AddLocalization(label, LocalizedText.Language.SpanishLatam);

            plugin.RegisterEmote(emote);
            Emotes.Add(emote);
            FullNames.Add(emote.Name);

            // Al completar una página, su octava ranura es el botón de cortar.
            if ((slot + 1) % SoundSlots.PerPage != 0) continue;

            int page = (slot + 1) / SoundSlots.PerPage;
            var stop = new Emote(
                emotePrefix + "SoundStop" + page,
                idleClip,
                StopIcon(),
                type: Emote.EmoteType.OneShot,
                disableIK: false);

            stop.AddLocalization("Stop sound", LocalizedText.Language.English);
            stop.AddLocalization("Cortar sonido", LocalizedText.Language.SpanishSpain);
            stop.AddLocalization("Cortar sonido", LocalizedText.Language.SpanishLatam);

            plugin.RegisterEmote(stop);
            Emotes.Add(stop);
            StopNames.Add(stop.Name);

            // Tiene que entrar en FullNames: es el conjunto con el que
            // EmoteWheelSoundPagePatch decide qué va a las páginas de sonidos. Sin esto los
            // botones de corte se quedaban con los bailes.
            FullNames.Add(stop.Name);
        }

        Plugin.Log.LogInfo($"Registrados {SoundSlots.Count} emotes de sonido en " +
                           $"{SoundSlots.Count / SoundSlots.PerPage} páginas, " +
                           $"cada una con su botón de corte.");
    }

    /// <summary>Devuelve el slot (0..6) del emote, o -1 si no es un emote de sonido.</summary>
    internal static int SlotOf(string emoteName)
    {
        if (!FullNames.Contains(emoteName) || StopNames.Contains(emoteName)) return -1;

        // Se leen TODOS los dígitos finales, no solo el último: con 14 ranuras hay nombres
        // de dos cifras y quedarse con el último convertía el "Sonido12" en el slot 1.
        int end = emoteName.Length;
        int start = end;
        while (start > 0 && emoteName[start - 1] >= '0' && emoteName[start - 1] <= '9') start--;

        if (start == end) return -1;

        return int.TryParse(emoteName.Substring(start, end - start), out var number) &&
               number >= 1 && number <= SoundSlots.Count
            ? number - 1
            : -1;
    }

    /// Iconos de reserva: nota musical simple sobre disco de color.
    static Texture2D IconFor(int slot)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var color = Color.HSVToRGB((0.55f + slot * 0.08f) % 1f, 0.6f, 0.95f);
        var pixels = new Color[size * size];
        float c = size / 2f, r = size * 0.40f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01((r - d) / 2f);

            // Cabeza de la nota (elipse) y plica, en blanco sobre el disco.
            float hx = (x - size * 0.42f) / (size * 0.16f);
            float hy = (y - size * 0.36f) / (size * 0.12f);
            bool head = hx * hx + hy * hy < 1f;
            bool stem = x > size * 0.55f && x < size * 0.60f && y > size * 0.36f && y < size * 0.74f;
            bool flag = y > size * 0.66f && y < size * 0.74f && x >= size * 0.60f && x < size * 0.74f;

            var rgb = (head || stem || flag) ? Color.white : color;
            pixels[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.name = $"ScoutDancesSoundIcon{slot}";
        return tex;
    }

    /// Icono del botón de corte: cuadrado rojo, el símbolo universal de "stop".
    static Texture2D StopIcon()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c = size / 2f, r = size * 0.40f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01((r - d) / 2f);
            bool square = Mathf.Abs(x - c) < size * 0.17f && Mathf.Abs(y - c) < size * 0.17f;
            var rgb = square ? Color.white : new Color(0.78f, 0.18f, 0.18f);
            pixels[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.name = "ScoutDancesStopIcon";
        return tex;
    }
}

/// <summary>
/// Intercepta el RPC del emote ANTES que PEAKEmoteLib para poder leer el nombre original.
/// </summary>
/// <remarks>
/// PEAKEmoteLib reescribe <c>emoteName</c> a "A_Scout_Emote_Dance2" en su propio prefix
/// (es como mete el clip en el AnimatorOverrideController), así que en un postfix el
/// nombre real ya se ha perdido. De ahí el <c>HarmonyBefore</c>.
///
/// Como es un RPC a todos, esto se ejecuta en TODOS los clientes con el nombre original
/// — que es justo lo que necesitamos para que cada uno reproduzca el audio localmente.
/// </remarks>
[HarmonyPatch(typeof(CharacterAnimations), "RPCA_PlayRemove")]
[HarmonyBefore("PEAKEmoteLib")]
internal static class SoundEmoteTriggerPatch
{
    [HarmonyPrefix]
    static void Prefix(CharacterAnimations __instance, string emoteName)
    {
        var character = __instance.character;
        if (character == null) return;

        int slot = SoundEmotes.SlotOf(emoteName);
        bool isStop = SoundEmotes.StopNames.Contains(emoteName);

        // Fijamos la supresión de cámara en CADA emote, no solo en los de sonido: si
        // solo la activáramos aquí y esperásemos a que el emote acabe para limpiarla,
        // encadenar un baile justo después de un sonido lo dejaría en primera persona.
        if (character.IsLocal) EmoteCamera.SuppressForCurrentEmote = slot >= 0 || isStop;

        // El corte también llega por RPC a todos, así que el sonido para en todas las
        // máquinas a la vez y no solo en la de quien pulsa.
        if (isStop) { CharacterSoundPlayer.StopFor(character); return; }

        if (slot < 0) return;
        CharacterSoundPlayer.PlayFor(character, slot);
    }
}

/// <summary>
/// Reproduce el sonido de un slot desde un personaje, enrutado igual que su voz para
/// heredar distancia, reverb y oclusión.
/// </summary>
internal class CharacterSoundPlayer : MonoBehaviour
{
    Character _character = null!;
    CharacterAnimations _animations = null!;
    AudioSource _source = null!;
    CharacterVoiceHandler? _voice;
    int _slot = -1;

    internal static void PlayFor(Character character, int slot)
    {
        var player = Get(character);
        if (player == null) return;

        var mediaPath = SoundSlots.GetPathFor(character.refs?.view?.Owner, slot);
        if (mediaPath.Length == 0)
        {
            Plugin.Log.LogInfo($"{character.characterName} no tiene nada en el slot {slot + 1}.");
            return;
        }

        var clip = InstantAudioCache.Get(mediaPath);
        if (clip == null)
        {
            // Aún no está en memoria: lo pedimos para que la próxima vez sí suene.
            InstantAudioCache.Request(mediaPath);
            Plugin.Log.LogInfo($"'{mediaPath}' todavía no está listo; se descargará para la próxima.");
            return;
        }

        player._slot = slot;
        player.Play(clip);
    }

    /// <summary>Corta lo que esté sonando en ese personaje.</summary>
    internal static void StopFor(Character character)
    {
        var existing = character.GetComponentInChildren<CharacterSoundPlayer>();
        if (existing == null || existing._source == null) return;

        existing._source.Stop();
        Plugin.Log.LogInfo($"Sonido cortado en {character.characterName}.");
    }

    static CharacterSoundPlayer? Get(Character character)
    {
        var existing = character.GetComponentInChildren<CharacterSoundPlayer>();
        if (existing != null) return existing;

        // Colgamos el AudioSource del mismo objeto que la voz para heredar su posición
        // (CharacterVoiceTransformProvider mueve ese transform a la boca del Scout).
        var voice = character.refs?.voice;
        var host = voice != null ? voice.gameObject : character.gameObject;

        var go = new GameObject("ScoutDancesSound");
        go.transform.SetParent(host.transform, false);

        var player = go.AddComponent<CharacterSoundPlayer>();
        player.Setup(character, voice);
        return player;
    }

    void Setup(Character character, CharacterVoiceHandler? voice)
    {
        _character = character;
        _animations = character.refs.animations;
        _voice = voice;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 1f;                       // 3D puro
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 3f;
        _source.maxDistance = 40f;
        _source.dopplerLevel = 0f;                       // sin efecto doppler en música

        // Copiamos la configuración espacial y el grupo de mixer de la voz del propio
        // personaje: así el sonido se atenúa, reverbera y se amortigua igual que si
        // estuviera hablando. Photon asigna un grupo distinto (Voice1..4) por jugador.
        var voiceSource = voice != null ? voice.GetComponent<AudioSource>() : null;
        if (voiceSource != null)
        {
            _source.outputAudioMixerGroup = voiceSource.outputAudioMixerGroup;
            _source.rolloffMode = voiceSource.rolloffMode;
            _source.minDistance = voiceSource.minDistance;
            _source.maxDistance = voiceSource.maxDistance;
            _source.spatialBlend = Mathf.Max(voiceSource.spatialBlend, 0.9f);
            if (voiceSource.rolloffMode == AudioRolloffMode.Custom)
            {
                _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                    voiceSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            }
        }

        // Recortamos el alcance respecto al de la voz. Heredarlo tal cual hacía que los
        // sonidos se oyeran desde demasiado lejos: la voz está pensada para coordinarse
        // a distancia, y un meme sonando a 40 m es ruido para media montaña.
        //
        // Escalamos min y max a la vez para no deformar la curva de caída; si el rolloff
        // es Custom, esa curva está normalizada sobre min..max y se reescala sola.
        float scale = Mathf.Max(0.05f, Plugin.CfgSoundDistanceScale.Value);
        _source.minDistance *= scale;
        _source.maxDistance *= scale;

        Plugin.Log.LogInfo(
            $"Audio de {character.characterName}: alcance {_source.minDistance:0.0}–" +
            $"{_source.maxDistance:0.0} m (x{scale:0.00}), rolloff {_source.rolloffMode}.");
    }

    void Play(AudioClip clip)
    {
        // El enrutado se refresca en cada uso, no solo al crear el reproductor. El grupo de
        // mixer de la voz (Voice1..4) se lo asigna Photon cuando el jugador entra al canal
        // de voz, que puede ser DESPUÉS de que alguien use la rueda por primera vez: si solo
        // se copiara al crearlo, ese jugador se quedaba fuera del bus de voz para siempre y
        // bajarle el volumen no le hacía nada.
        AdoptVoiceRouting();

        _source.Stop();
        _source.clip = clip;
        _source.volume = CurrentVolume();
        _source.time = 0f;
        _source.Play();
        Plugin.Log.LogInfo($"Sonando '{clip.name}' desde {_character.characterName} " +
                           $"(grupo {_source.outputAudioMixerGroup?.name ?? "ninguno"}, " +
                           $"alcance {_source.minDistance:0.#}–{_source.maxDistance:0.#} m).");
    }

    /// <summary>Copia de la voz el grupo de mixer y la curva de distancia.</summary>
    void AdoptVoiceRouting()
    {
        var voiceSource = _voice != null ? _voice.GetComponent<AudioSource>() : null;
        if (voiceSource == null) return;

        _source.outputAudioMixerGroup = voiceSource.outputAudioMixerGroup;
        _source.rolloffMode = voiceSource.rolloffMode;
        _source.spatialBlend = Mathf.Max(voiceSource.spatialBlend, 0.9f);

        if (voiceSource.rolloffMode == AudioRolloffMode.Custom)
        {
            _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                voiceSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }

        float scale = Mathf.Max(0.05f, Plugin.CfgSoundDistanceScale.Value);
        _source.minDistance = voiceSource.minDistance * scale;
        _source.maxDistance = voiceSource.maxDistance * scale;
    }

    /// <summary>
    /// La misma atenuación por distancia y obstáculos que el juego aplica a la voz.
    /// </summary>
    /// <remarks>
    /// Copiar los ajustes del <c>AudioSource</c> de la voz no bastaba, y esto explica por qué
    /// un meme se oía por medio mapa mientras que hablando no llegas ni de lejos: el
    /// <c>AudioSource</c> de la voz está configurado a 10–1000 m, pero PEAK NO se conforma
    /// con la atenuación de Unity. En <c>CharacterVoiceHandler.Update</c> calcula un factor
    /// propio —distancia, obstáculos, si estás amordazado— y lo aplica a mano sobre las
    /// muestras en <c>OnAudioFilterRead</c>. Nuestro audio se saltaba ese segundo filtro y
    /// conservaba el alcance bruto de 500 m.
    ///
    /// Se comprobó en el ensamblado que <c>Update</c> escribe ese campo en cada frame,
    /// hable el jugador o no, así que el valor sirve aunque nadie esté transmitiendo.
    /// </remarks>
    float VoiceFalloff()
    {
        if (_voice == null) return 1f;

        try
        {
            float falloff = _voice._lastMeasuredFalloff;

            // Si el juego aún no lo ha medido, no silenciamos por las dudas.
            return falloff <= 0f ? 1f : Mathf.Clamp01(falloff);
        }
        catch { return 1f; }
    }

    /// <summary>
    /// Volumen del sonido que elige SU DUEÑO, por el multiplicador local de quien escucha.
    /// </summary>
    /// <remarks>
    /// El nivel por slot lo manda el dueño (los clips de myinstants vienen con volúmenes
    /// muy dispares y quien lo elige es quien sabe cómo suena). El multiplicador local
    /// queda para bajarlo todo si alguien te revienta los oídos.
    /// </remarks>
    float CurrentVolume()
    {
        var owner = _character != null ? _character.refs?.view?.Owner : null;
        return SoundSlots.GetVolumeFor(owner, _slot) * Plugin.CfgSoundVolume.Value;
    }

    void Update()
    {
        if (!_source.isPlaying) return;

        // El sonido NO está atado al emote: la animación se corta en cuanto te mueves
        // (lo hace el propio juego), pero el audio sigue hasta el final. Cortarlo con el
        // emote obligaba a quedarse quieto para oír un clip de 10 segundos.
        if (_character == null || _character.data == null || _character.data.dead)
        {
            _source.Stop();
            return;
        }

        // Seguimos el slider en vivo: si lo mueves mientras algo suena, se nota ya. Y por la
        // atenuación de la voz, que cambia cada frame según te acerques o te tapes.
        _source.volume = CurrentVolume() * VoiceFalloff();

        float max = Plugin.CfgSoundMaxSeconds.Value;
        if (max > 0f && _source.time > max) _source.Stop();
    }

    void OnDestroy()
    {
        if (_source != null) _source.Stop();
    }
}
