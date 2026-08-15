using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Lleva la cuenta de los power-ups que tienes encima y sostiene los que no se sostienen solos.
/// </summary>
/// <remarks>
/// <b>Dos clases de efecto, y solo una necesita esta clase.</b> Los que se apoyan en un
/// <c>Affliction</c> del juego se limpian solos cuando expira su <c>totalTime</c>; aquí solo
/// se anotan para poder pintarlos. Los que tocan campos del personaje —saltos extra, coste
/// de trepar, alcance del brazo— no tienen quien los revierta, y de eso se encarga esto.
///
/// <b>Por qué en LateUpdate y no al aplicar.</b> El juego recalcula parte de esos campos en
/// su propio <c>Update</c>: <c>CharacterAfflictions.UpdateExtraJumps</c> reescribe los
/// saltos cada frame. Ponerlos una vez al recoger el power-up no serviría de nada, porque el
/// siguiente frame los borra. Reafirmándolos en <c>LateUpdate</c> —después de que el juego
/// haya hecho lo suyo— ganamos siempre, sin parchear nada.
///
/// <b>Solo en el cliente del dueño.</b> Cada uno simula su propio Scout, así que tocar estos
/// campos en los demás no haría nada salvo ensuciar su estado.
/// </remarks>
internal class ActiveBuffs : MonoBehaviour
{
    internal class Live
    {
        internal BuffEntry Entry = null!;
        internal float Until;          // Time.time en que caduca; 0 = no caduca
        internal float Shown;          // cuándo se recogió, para el aviso ampliado

        internal float Remaining => Until <= 0f ? 0f : Mathf.Max(0f, Until - Time.time);
    }

    static readonly List<Live> _live = new();

    /// <summary>Lo que el jugador lleva encima ahora mismo, para pintarlo.</summary>
    internal static IReadOnlyList<Live> Current => _live;

    static ActiveBuffs? _instance;

    Character? _character;

    internal static ActiveBuffs Ensure(Character character)
    {
        if (_instance == null)
        {
            var go = new GameObject("ScoutDancesActiveBuffs");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ActiveBuffs>();
        }

        _instance._character = character;
        return _instance;
    }

    // ------------------------------------------------------------------ alta

    /// <summary>Anota un power-up recogido y lo aplica.</summary>
    internal static void Take(Character character, BuffEntry entry)
    {
        if (character == null || entry == null) return;

        Ensure(character);

        // Recoger el mismo dos veces refresca el tiempo en vez de crear otra entrada: con
        // dos "Termo" tendrías dos relojes contando exactamente lo mismo.
        var existing = _live.FirstOrDefault(l => l.Entry.Id == entry.Id);
        if (existing == null)
        {
            existing = new Live { Entry = entry };
            _live.Add(existing);
        }

        existing.Shown = Time.time;
        existing.Until = entry.Instant
            ? Time.time + Plugin.CfgBuffInstantMessage.Value   // solo para el aviso
            : entry.Duration > 0f ? Time.time + entry.Duration : 0f;

        try
        {
            entry.Apply(character);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"El power-up '{entry.Name}' falló al aplicarse: {e.Message}");
        }

        Announce(entry);

        Plugin.Log.LogInfo($"Power-up '{entry.Name}' ({entry.Category}, " +
                           $"{BuffCatalog.RarityName(entry.Rarity)}) recogido.");
    }

    /// <summary>
    /// El aviso discreto de la esquina.
    /// </summary>
    /// <remarks>
    /// <b>NO se usa <c>SetHeroTitle</c>.</b> Es el del "Eres un fantasma" y parecía la
    /// elección obvia, pero al probarlo resulta que pinta un cartel a pantalla completa: te
    /// tapa la montaña entera para decirte que has cogido una Zancada. Sirve para los
    /// momentos que el juego considera dignos de parar la partida, y recoger un power-up no
    /// lo es.
    ///
    /// La lista de la esquina ya cuenta el nombre y el efecto durante unos segundos, así que
    /// esto es solo un refuerzo; si el juego no tiene el panel de avisos a mano, no pasa nada
    /// por quedarse sin él.
    /// </remarks>
    static void Announce(BuffEntry entry)
    {
        try
        {
            var notifications = Object.FindFirstObjectByType<UI_Notifications>();
            notifications?.AddNotification($"{entry.Name} — {entry.Summary}");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"No pude mostrar el aviso del power-up: {e.Message}");
        }
    }

    // ------------------------------------------- efectos que hay que sostener a mano

    static int _extraJumps;
    static float _extraJumpsUntil;

    static float _grabReach;
    static float _grabReachUntil;

    static float _warmUntil;

    internal static void SetJumps(Character c, int jumps, float seconds)
    {
        Ensure(c);
        _extraJumps = Mathf.Max(_extraJumps, jumps);
        _extraJumpsUntil = Mathf.Max(_extraJumpsUntil, Time.time + seconds);
    }

    internal static void SetGrabReach(Character c, float multiplier, float seconds)
    {
        Ensure(c);
        _grabReach = Mathf.Max(_grabReach, multiplier);
        _grabReachUntil = Mathf.Max(_grabReachUntil, Time.time + seconds);
    }

    internal static void KeepWarm(Character c, float seconds)
    {
        Ensure(c);
        _warmUntil = Mathf.Max(_warmUntil, Time.time + seconds);
    }

    /// <summary>Catapulta al jugador hacia arriba.</summary>
    /// <remarks>
    /// Con la fuerza del propio juego en vez de moviendo el transform: el Scout es un
    /// ragdoll, y teletransportarlo hacia arriba lo dejaría peleándose con la física en vez
    /// de saliendo despedido.
    /// </remarks>
    internal static void Launch(Character c, float force)
    {
        if (c == null) return;

        try
        {
            // El ajuste del config multiplica, no sustituye: así se puede subir o bajar el
            // conjunto sin perder la diferencia entre Impulso y Catapulta.
            c.AddForce(Vector3.up * force * Plugin.CfgBuffLaunchForce.Value, 1f);

            // Un poco de gravedad baja para que el salto no acabe en un golpe seco.
            c.refs.afflictions.AddAffliction(new Peak.Afflictions.Affliction_LowGravity(2, 3f));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude catapultarte: {e.Message}");
        }
    }

    /// <summary>Multiplicador de alcance de brazo activo, o 1 si no hay ninguno.</summary>
    /// <remarks>
    /// Lo lee el parche de <c>CharacterGrabbing</c>. Vive aquí y no allí para que la
    /// duración se gestione en un solo sitio.
    /// </remarks>
    internal static float GrabReach =>
        Time.time < _grabReachUntil && _grabReach > 0f ? _grabReach : 1f;

    void LateUpdate()
    {
        // Caducados fuera. Se limpia aquí y no en cada efecto para que la lista que se
        // pinta y los efectos que se sostienen no puedan discrepar.
        _live.RemoveAll(l => l.Until > 0f && Time.time > l.Until);

        DropFinishedShield();

        var character = _character != null ? _character : Character.localCharacter;
        if (character?.data == null) return;

        // Se reafirman DESPUÉS del Update del juego, que reescribe algunos cada frame.
        if (Time.time < _extraJumpsUntil)
            character.data.extraJumps = Mathf.Max(character.data.extraJumps, _extraJumps);
        else
            _extraJumps = 0;

        // Los saltos también en jumpsRemaining, no solo en extraJumps: el juego rellena el
        // primero desde el segundo al pisar suelo, así que sin esto un power-up cogido en
        // el aire no servía hasta aterrizar — justo cuando ya no hace falta.
        if (Time.time < _extraJumpsUntil)
            character.data.jumpsRemaining = Mathf.Max(character.data.jumpsRemaining, _extraJumps);

        if (Time.time >= _grabReachUntil) _grabReach = 0f;

        // El frío se resta a cada frame en vez de limpiarlo una vez: sigue subiendo
        // mientras estés en la nieve, así que quitarlo al recoger el termo no serviría de
        // nada. Se resta en lugar de fijarlo a cero para no pisar el sistema de estados.
        if (Time.time < _warmUntil)
        {
            try
            {
                character.refs.afflictions.SubtractStatus(
                    CharacterAfflictions.STATUSTYPE.Cold, 1f);
            }
            catch { }
        }
    }

    /// <summary>
    /// Quita el escudo de la lista cuando ya se ha roto.
    /// </summary>
    /// <remarks>
    /// El escudo no tiene reloj —dura "hasta que te den"— y por eso su entrada no caducaba
    /// nunca: se quedaba en pantalla el resto de la partida aunque hubiera saltado al primer
    /// golpe. Se le pregunta al juego si el efecto sigue puesto en vez de intentar adivinar
    /// cuándo se rompió.
    /// </remarks>
    static void DropFinishedShield()
    {
        var shield = _live.FirstOrDefault(l => l.Entry.Persistent != null);
        if (shield == null) return;

        // Un margen antes de mirar: el efecto tarda un instante en registrarse, y sin esto
        // se borraría la entrada en el mismo frame en que se crea.
        if (Time.time - shield.Shown < 1f) return;

        try
        {
            var character = Character.localCharacter;
            if (character?.refs?.afflictions == null) return;

            // La sobrecarga devuelve además la instancia por parámetro de salida; aquí solo
            // interesa el sí o no.
            if (!character.refs.afflictions.HasAfflictionType(
                    Peak.Afflictions.Affliction.AfflictionType.BingBongShield, out _))
            {
                _live.Remove(shield);
                Plugin.Log.LogInfo("El escudo se rompió.");
            }
        }
        catch { /* si no se puede consultar, se deja como está */ }
    }
}
