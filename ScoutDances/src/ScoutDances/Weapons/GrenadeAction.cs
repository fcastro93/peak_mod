using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Granada de estados: se lanza con Q y al chocar reparte efectos al azar.
/// </summary>
/// <remarks>
/// <b>No tiene acción de uso.</b> No se dispara ni se apunta: es un objeto normal que se
/// tira con Q como cualquier otro del juego, y lo único que añadimos es qué pasa cuando
/// aterriza. Por eso vive en un <c>MonoBehaviour</c> suelto y no en un <c>ItemAction</c>.
///
/// <b>Solo explota si la han LANZADO.</b> El juego usa <c>lastThrownCharacter</c> y
/// <c>lastThrownTime</c> para saber quién tiró un objeto y cuándo — es el mismo par que
/// usan sus trampas de suelo para no herirte con lo que acabas de soltar. Sin esa
/// comprobación, la granada reventaría en la mano en cuanto la dejaras caer al suelo.
///
/// <b>Cada víctima se aplica su propio efecto.</b> <c>AddAffliction</c> descarta la
/// llamada si el personaje no es local, así que no hay forma de imponerle un estado a otro
/// desde aquí: el RPC llega a todos y cada cliente mira si SU personaje está dentro del
/// radio. De paso sale gratis que cada uno reciba un efecto distinto, que es justo lo que
/// se busca.
/// </remarks>
internal class Grenade : MonoBehaviour
{
    // PUBLIC a propósito: Unity solo copia los campos serializados al instanciar.

    /// Radio de la explosión, en metros. Debe cuadrar con el tamaño de la partícula.
    public float radius = 9f;

    /// Cuánto se agranda la partícula respecto a su tamaño de fábrica.
    public float effectScale = 4f;

    public string explosionEffect = "PoisonSkullExplosion";
    public string explosionSound = "explosion_large_04";

    /// Margen tras el lanzamiento en el que un choque cuenta como impacto.
    const float ThrowWindow = 8f;

    /// Velocidad mínima del golpe para que cuente, en m/s.
    const float MinImpact = 2f;

    Item? _item;
    bool _spent;

    void Awake() => _item = GetComponent<Item>();

    void OnCollisionEnter(Collision collision)
    {
        if (_spent || _item == null) return;

        // Solo el cliente de quien la lanzó decide que ha explotado. Si lo decidiera cada
        // uno, la misma granada mandaría un RPC por jugador de la sala.
        var thrower = _item.lastThrownCharacter;
        if (thrower == null || !thrower.IsLocal) return;

        if (Time.time - _item.lastThrownTime > ThrowWindow) return;

        // Un roce al rodar no cuenta: tiene que llegar con algo de velocidad.
        if (collision.relativeVelocity.magnitude < MinImpact) return;

        _spent = true;

        var point = collision.contacts.Length > 0
            ? collision.contacts[0].point
            : transform.position;

        _item.photonView.RPC(nameof(RPC_Explode), RpcTarget.All, point);
    }

    /// <remarks>
    /// El efecto se reparte ANTES de consumir la granada. Consumirla destruye el objeto y
    /// con él este componente, así que hacerlo primero se comía el efecto justo en el
    /// cliente de quien la lanzó — que es precisamente al que también tiene que alcanzarle
    /// si está cerca.
    ///
    /// Nadie está excluido a propósito: el RPC llega a todos y cada cliente mira su propio
    /// personaje. Quien la tiró entra en el reparto como cualquiera.
    /// </remarks>
    [PunRPC]
    void RPC_Explode(Vector3 point)
    {
        var local = Character.localCharacter;

        if (local != null && local.data != null && !local.data.dead)
        {
            float distance = (local.Center - point).magnitude;
            bool thrower = _item != null && _item.lastThrownCharacter == local;

            if (distance <= radius)
            {
                var affliction = RandomAffliction();
                local.refs.afflictions.AddAffliction(affliction);

                Plugin.Log.LogInfo(
                    $"Granada: te alcanzó a {distance:0.0} m de {radius:0.0} " +
                    $"({affliction.GetType().Name}){(thrower ? " — y la tiraste tú" : "")}.");
            }
            else
            {
                Plugin.Log.LogInfo($"Granada: te libraste, a {distance:0.0} m de {radius:0.0}.");
            }
        }

        Boom(point);
    }

    void Boom(Vector3 point)
    {
        var clip = Plugin.FindClip(explosionSound);
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, point, Plugin.CfgBlasterVolume.Value);

        var prefab = Plugin.FindPrefab(explosionEffect);
        if (prefab != null)
        {
            var effect = Instantiate(prefab, point, Quaternion.identity);
            effect.transform.localScale = Vector3.one * effectScale;

            // Sin esto la explosión sale lavada: los materiales de partículas del bundle
            // pierden su mezcla aditiva si se les reenlaza el shader.
            Props.PropBuilder.RebindShaders(effect);

            Destroy(effect, 6f);
        }

        // La granada se gasta. Solo lo pide quien la lanzó: ConsumeDelayed ya avisa a
        // todos por su cuenta.
        if (_item != null && _item.lastThrownCharacter != null &&
            _item.lastThrownCharacter.IsLocal && !_item.consuming)
        {
            _item.StartCoroutine(_item.ConsumeDelayed());
        }
    }

    /// <summary>
    /// Elige uno de los efectos temporales del juego al azar.
    /// </summary>
    /// <remarks>
    /// Se usan las afflictions que YA trae PEAK en vez de inventar estados nuestros: traen
    /// su propio temporizador, su icono en la interfaz, su forma de acumularse si te cae
    /// dos veces y su limpieza al terminar. Reimplementarlo sería más código y peor.
    ///
    /// Todos son molestos pero temporales: la granada fastidia, no mata. El más duro
    /// —Blind— dura poco justamente por eso.
    ///
    /// <b>Casi todos van por <c>Affliction_AdjustStatus</c> a propósito.</b> Las versiones
    /// "OverTime" de cada estado parecen la opción natural, pero algunas están hechas para
    /// CONTINUAR algo que ya tienes, no para empezarlo. <c>Affliction_ZombieBite</c> es el
    /// ejemplo: lo primero que hace es
    ///
    /// <code>
    /// if (afflictions.GetCurrentStatus(Spores) &lt; 0.025f) { totalTime = 0f; }
    /// </code>
    ///
    /// o sea que se apaga sola en el primer frame si la víctima está limpia. Aplicada a
    /// alguien sano no hacía absolutamente nada, y como salía en uno de cada seis lanzamientos
    /// parecía que la granada fallaba de vez en cuando. <c>AdjustStatus</c> no tiene ese
    /// requisito: pone la cantidad y la retira al acabar.
    /// </remarks>
    static Affliction RandomAffliction()
    {
        switch (Random.Range(0, 6))
        {
            case 0:   // sueño: te entra el sopor de golpe
                return new Affliction_AdjustStatus(
                    CharacterAfflictions.STATUSTYPE.Drowsy, 0.35f, 10f);

            case 1:   // veneno goteando
                return new Affliction_PoisonOverTime(8f, 0.3f, 0.045f);

            case 2:   // frío
                return new Affliction_AdjustStatus(
                    CharacterAfflictions.STATUSTYPE.Cold, 0.35f, 10f);

            case 3:   // esporas: se aplican directas, no con el mordisco zombi
                return new Affliction_AdjustStatus(
                    CharacterAfflictions.STATUSTYPE.Spores, 0.30f, 10f);

            case 4:   // peso encima: te cuesta moverte
                return new Affliction_AdjustStatus(
                    CharacterAfflictions.STATUSTYPE.Weight, 0.35f, 10f);

            default:  // ceguera, corta porque es la más dura
                return new Affliction_Blind { totalTime = 4f };
        }
    }
}
