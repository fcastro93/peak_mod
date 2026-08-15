using Peak.Afflictions;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Deja a un Scout sin gravedad durante unos segundos.
/// </summary>
/// <remarks>
/// <b>Usa la afflicción del propio juego.</b> PEAK ya tiene ingravidez montada: es lo que
/// hace su orbe, que aplica esto a quien esté dentro:
///
/// <code>
/// new Affliction_LowGravity(3, 15f)
/// </code>
///
/// Trae el recálculo de la gravedad, el remolino visual (<c>StartWhirlwind</c>), su
/// temporizador, su acumulación si te cae dos veces y su limpieza al acabar. Aquí solo se
/// aplica al que recibe el disparo en vez de a todo un área.
///
/// <b>Antes intenté forzar <c>balloonFloatMultiplier</c> a cero</b> desde un parche, tras
/// leer la fórmula de <c>CharacterBalloons</c> y deducir que <c>lowGravAmount</c> hacía lo
/// contrario de lo que sugería su nombre. No funcionaba, y la prueba de que estaba mal es
/// que el juego usa esa misma afflicción justo para esto. La aritmética engañaba; el uso
/// real, no.
///
/// <b>La afflicción la aplica el cliente de la VÍCTIMA.</b> <c>AddAffliction</c> descarta
/// la llamada si el personaje no es local, así que no hay forma de imponérsela desde
/// fuera. El aura sí se crea en todos, para que se vea quién está afectado.
/// </remarks>
internal class AntiGravity : MonoBehaviour
{
    Character? _character;
    GameObject? _aura;
    float _until;

    /// <summary>Aplica la ingravidez y engancha el aura.</summary>
    /// <param name="local">
    /// True solo en el cliente dueño del personaje. La afflicción se aplica ahí; el aura,
    /// en todos.
    /// </param>
    internal static void Apply(Character character, float seconds, string auraPrefab, bool local)
    {
        if (character == null || seconds <= 0f) return;

        var effect = character.GetComponent<AntiGravity>();
        if (effect == null) effect = character.gameObject.AddComponent<AntiGravity>();

        effect.Begin(character, seconds, auraPrefab, local);
    }

    void Begin(Character character, float seconds, string auraPrefab, bool local)
    {
        _character = character;
        _until = Time.time + seconds;

        SpawnAura(auraPrefab);

        if (!local) return;

        // Amount 3 es el que usa el orbe del juego. Con menos apenas se nota.
        character.refs.afflictions.AddAffliction(
            new Affliction_LowGravity(Plugin.CfgAntiGravAmount.Value, seconds));

        Plugin.Log.LogInfo($"Sin gravedad durante {seconds:0.#}s " +
                           $"(nivel {Plugin.CfgAntiGravAmount.Value}).");
    }

    /// <summary>
    /// Cuelga el aura del personaje para que se vea quién está afectado.
    /// </summary>
    /// <remarks>
    /// Va colgada de la CADERA y no soltada en el mundo: el afectado flota a la deriva, así
    /// que un efecto quieto se quedaría atrás en cuanto empezara a moverse.
    /// </remarks>
    void SpawnAura(string auraPrefab)
    {
        if (_aura != null) Destroy(_aura);

        var prefab = Plugin.FindPrefab(auraPrefab);
        if (prefab == null || _character == null) return;

        var hip = _character.GetBodypart(BodypartType.Hip)?.transform;
        if (hip == null) return;

        _aura = Instantiate(prefab, hip.position, Quaternion.identity);
        _aura.transform.SetParent(hip, worldPositionStays: true);

        // Sin esto el aura sale lavada: los materiales de partículas del bundle pierden su
        // mezcla aditiva si se les reenlaza el shader.
        Props.PropBuilder.RebindShaders(_aura);
    }

    void Update()
    {
        if (_until <= 0f) return;

        bool gone = _character == null || _character.data == null || _character.data.dead;

        if (gone || Time.time >= _until) Clear();
    }

    void Clear()
    {
        _until = 0f;

        if (_aura != null) Destroy(_aura);
        _aura = null;

        // La afflicción se retira sola al cumplir su tiempo; no hay que deshacer nada.
    }

    void OnDestroy() => Clear();
}
