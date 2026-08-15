using HarmonyLib;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Permite recibir daño en el aeropuerto y añade regeneración, para poder probar las
/// armas en el lobby sin entrar a una partida.
/// </summary>
/// <remarks>
/// PEAK corta en seco cualquier estado en el aeropuerto:
///
/// <code>
/// public bool AddStatus(STATUSTYPE statusType, float amount, ...)
/// {
///     ...
///     if (m_inAirport) return false;
/// </code>
///
/// Por eso allí no hay barra de vida: no está oculta, es que no puede pasarte nada. Para
/// el banco de pruebas apagamos ese guard durante la llamada y lo devolvemos a su sitio
/// justo después, en vez de dejar el flag tocado (lo leen otros sistemas, como el hambre).
/// </remarks>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
internal static class AirportDamagePatch
{
    [HarmonyPrefix]
    static void Prefix(CharacterAfflictions __instance, ref bool __state)
    {
        __state = false;
        if (!Plugin.CfgLobbyCombat.Value || !__instance.m_inAirport) return;

        __state = true;                 // recordamos que hay que restaurarlo
        __instance.m_inAirport = false;
    }

    [HarmonyFinalizer]
    static void Finalizer(CharacterAfflictions __instance, bool __state)
    {
        // Finalizer y no Postfix: si el método original lanza, el flag tiene que volver
        // a su sitio igualmente. Dejarlo en false haría que el juego creyera que no
        // estás en el aeropuerto, con lo que eso arrastra (hambre, frío, etc.).
        if (__state) __instance.m_inAirport = true;
    }
}

/// <summary>
/// Vigila la vida del jugador local: la mantiene entre 0 y el máximo, y la regenera
/// cuando lleva un rato sin recibir daño.
/// </summary>
internal class LobbyHealth : MonoBehaviour
{
    /// El juego deja KO cuando la suma de estados pasa de 0.99, así que ese es el tope.
    const float KnockOutThreshold = 1f;

    /// Cuánto margen dejamos por debajo del KO en el aeropuerto.
    const float DeathMargin = 0.05f;

    float _lastInjury;
    float _lastDamageAt;

    /// Momento en que toca levantar al jugador caído. 0 = no hay nadie esperando.
    float _reviveAt;

    void Update()
    {
        if (!Plugin.CfgLobbyCombat.Value) return;

        var character = Character.localCharacter;
        if (character == null || character.data == null) return;

        // TODO esto es del banco de pruebas del aeropuerto y NO debe correr en partida.
        // Corriendo en la montaña recortaba statusSum justo al umbral del KO cada frame:
        // el juego intentaba dejarte inconsciente y nosotros te levantábamos, una y otra
        // vez. Eso es lo que hacía que los pies dieran saltos raros sin parar, y de paso
        // te volvía casi inmortal en una partida de verdad.
        if (!character.inAirport) return;

        if (Plugin.CfgLobbyRespawn.Value) CheckRespawn(character);
        if (character.data.dead) return;

        var afflictions = character.refs?.afflictions;
        if (afflictions == null) return;

        float injury = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury);

        // ¿Nos han dado desde el último frame?
        if (injury > _lastInjury + 0.0001f) _lastDamageAt = Time.time;

        // Techo de daño. En el aeropuerto lo dejamos JUSTO por debajo del KO: allí no
        // existe el sistema de reaparición de una partida, así que si te desmayas te
        // conviertes en fantasma y te quedas así — no hay hoguera ni compañero que te
        // reviva. Mejor aguantar en pie con la vida al mínimo.
        float ceiling = KnockOutThreshold;
        if (Plugin.CfgLobbyPreventDeath.Value && character.inAirport)
            ceiling = KnockOutThreshold - DeathMargin;

        float excess = afflictions.statusSum - ceiling;
        if (excess > 0f && injury > 0f)
        {
            injury = Mathf.Max(0f, injury - excess);
            afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Injury, injury);
        }

        // Regeneración tras el respiro.
        if (injury > 0f && Time.time - _lastDamageAt >= Plugin.CfgRegenDelay.Value)
        {
            injury = Mathf.Max(0f, injury - Plugin.CfgRegenPerSecond.Value * Time.deltaTime);
            afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Injury, injury);
        }

        _lastInjury = injury;
    }

    /// <summary>
    /// Te levanta en el aeropuerto como si acabaras de entrar.
    /// </summary>
    /// <remarks>
    /// El aeropuerto no tiene sistema de reaparición: allí no hay hogueras ni estatuas, así
    /// que al caer te quedabas de fantasma sin forma de volver. Se resuelve reviviendo en el
    /// punto de entrada del propio jugador, el mismo que usa el juego al llegar.
    ///
    /// Se pasa <c>applyStatus: false</c> a propósito. Ese parámetro mete maldición y hambre
    /// como penalización por haber revivido, lo cual tiene sentido en la montaña pero no en
    /// un vestíbulo donde estáis probando armas: saldrías penalizado antes de empezar.
    ///
    /// Lo lanza el cliente de quien cayó, que es el dueño de ese personaje; el RPC ya se
    /// reparte a todos por dentro.
    /// </remarks>
    void CheckRespawn(Character character)
    {
        bool down = character.data.dead || character.data.fullyPassedOut;

        if (!down)
        {
            _reviveAt = 0f;
            return;
        }

        // Un respiro antes de levantarlo: reaparecer en el mismo frame en que caes se ve
        // como un fallo, no como una mecánica.
        if (_reviveAt == 0f)
        {
            _reviveAt = Time.time + Plugin.CfgLobbyRespawnDelay.Value;
            return;
        }

        if (Time.time < _reviveAt) return;
        _reviveAt = 0f;

        var spawn = SpawnPoint.LocalSpawnPoint;
        var position = spawn != null
            ? spawn.transform.position + Vector3.up * 0.5f
            : character.Center + Vector3.up * 2f;

        character.photonView.RPC("RPCA_ReviveAtPosition", Photon.Pun.RpcTarget.All,
                                 position, false, -1);

        Plugin.Log.LogInfo($"Reaparecido en el aeropuerto en {position}.");
    }
}
