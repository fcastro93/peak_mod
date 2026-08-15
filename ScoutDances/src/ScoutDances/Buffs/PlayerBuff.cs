using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Efecto temporal sobre un Scout: velocidad, gasto de estamina y tamaño.
/// </summary>
/// <remarks>
/// Los tres modificadores viven en la MISMA clase a propósito. Cada uno pisa un campo
/// compartido del personaje, y si nos vamos sin deshacerlo el jugador se queda tocado
/// hasta que reinicie el juego. Teniéndolos juntos hay un solo <see cref="Clear"/> del
/// que acordarse, y un solo sitio donde mirar cuando algo se queda pegado.
///
/// De dónde sale cada uno:
///
/// <list type="bullet">
/// <item><b>Velocidad</b>: <c>movement.movementModifier</c>, el mismo campo que usa la
///       bebida energética del juego (afflicción <c>FasterBoi</c>). Entra en
///       <c>GetMovementForce()</c> antes que el sprint y el agachado, así que vale
///       andando, corriendo y en el aire. No cambia la altura del salto.</item>
/// <item><b>Estamina</b>: <c>movement.sprintStaminaUsage</c>, que el juego consume como
///       <c>UseStamina(sprintStaminaUsage * Time.deltaTime)</c> mientras esprintas.</item>
/// <item><b>Tamaño</b>: ver <see cref="CharacterResizer"/>.</item>
/// </list>
///
/// Velocidad y estamina se restauran distinto, y no por capricho. El juego ESCRIBE en
/// <c>movementModifier</c> por su cuenta, así que ahí guardamos cuánto sumamos nosotros y
/// restamos esa misma cantidad; quedarnos el valor viejo borraría de paso el efecto de
/// una bebida energética que hubieras tomado entretanto. En <c>sprintStaminaUsage</c> no
/// escribe nadie más, así que basta con guardar el valor original y devolverlo.
/// </remarks>
internal class PlayerBuff : MonoBehaviour
{
    Character? _character;

    /// Lo que le hemos SUMADO al modificador de movimiento. Se resta tal cual al acabar.
    float _speedAdded;

    /// Valor de fábrica del gasto de estamina; NaN mientras no lo hayamos tocado.
    float _staminaOriginal = float.NaN;

    float _until;

    /// <summary>Aplica (o refresca) un efecto sobre un personaje.</summary>
    /// <param name="speedMultiplier">2 = el doble de rápido. 1 = sin cambio.</param>
    /// <param name="staminaMultiplier">0.5 = gasta la mitad al correr. 1 = sin cambio.</param>
    /// <remarks>
    /// El TAMAÑO no se gestiona aquí: lo lleva <see cref="CharacterResizer"/> con su propio
    /// contador, porque se aplica en todos los clientes y esta clase solo existe en el del
    /// dueño del personaje.
    /// </remarks>
    internal static void Grant(Character character, float speedMultiplier, float staminaMultiplier,
                               float seconds)
    {
        if (character == null || seconds <= 0f) return;

        var buff = character.GetComponent<PlayerBuff>();
        if (buff == null) buff = character.gameObject.AddComponent<PlayerBuff>();

        buff.Begin(character, speedMultiplier, staminaMultiplier, seconds);
    }

    void Begin(Character character, float speedMultiplier, float staminaMultiplier,
               float seconds)
    {
        _character = character;

        // Nos quedamos con lo más fuerte de cada cosa y reiniciamos el contador: coger un
        // power-up flojo mientras corre uno bueno no debe rebajarte.
        float speedAdd = Mathf.Max(speedMultiplier - 1f, _speedAdded);

        Clear();                       // deshace lo anterior antes de aplicar lo nuevo

        var movement = character.refs?.movement;

        if (speedAdd > 0f && movement != null)
        {
            _speedAdded = speedAdd;
            movement.movementModifier += _speedAdded;
        }

        if (staminaMultiplier > 0f && !Mathf.Approximately(staminaMultiplier, 1f) && movement != null)
        {
            _staminaOriginal = movement.sprintStaminaUsage;
            movement.sprintStaminaUsage = _staminaOriginal * staminaMultiplier;
        }

        _until = Time.time + seconds;

        Plugin.Log.LogInfo($"Efecto: velocidad x{1f + _speedAdded:0.##}, " +
                           $"estamina x{staminaMultiplier:0.##}, {seconds:0.#}s.");
    }

    bool Active => _speedAdded > 0f || !float.IsNaN(_staminaOriginal);

    void Update()
    {
        if (!Active) return;

        // Al morir, el juego reinicia cosas por dentro; soltamos lo nuestro para no dejar
        // una resta pendiente sobre un estado que ya no es el que tocamos.
        if (_character == null || _character.data == null || _character.data.dead)
        {
            Clear();
            return;
        }

        if (Time.time >= _until) Clear();
    }

    void Clear()
    {
        var movement = _character != null ? _character.refs?.movement : null;

        if (_speedAdded > 0f)
        {
            if (movement != null) movement.movementModifier -= _speedAdded;
            _speedAdded = 0f;
        }

        if (!float.IsNaN(_staminaOriginal))
        {
            if (movement != null) movement.sprintStaminaUsage = _staminaOriginal;
            _staminaOriginal = float.NaN;
        }

    }

    /// <remarks>
    /// Imprescindible, no es cortesía: si nos destruyen con el efecto puesto (cambio de
    /// escena, fin de partida) el jugador se queda acelerado, encogido o las dos cosas,
    /// sin forma de deshacerlo salvo reiniciar.
    /// </remarks>
    void OnDestroy() => Clear();
}
