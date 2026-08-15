using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// La bola de energía del blaster: viaja hasta el objetivo y allí aplica el efecto.
/// </summary>
/// <remarks>
/// <b>El impacto ya está decidido antes de que la bola salga.</b> El arma sigue trazando
/// el disparo al instante —doble raycast desde la cámara— y la bola solo VIAJA hacia el
/// punto que ya se calculó. Es lo contrario de un proyectil con física, y a propósito:
/// un proyectil real tendría que colisionar por su cuenta en cada máquina, y en un juego
/// con lag eso significa que a ti te da y al otro no. Aquí todos reciben el mismo origen,
/// el mismo destino y la misma velocidad, así que todos ven el mismo viaje y aplican el
/// mismo efecto en el mismo momento.
///
/// El componente vive en la BOLA y no en el arma porque el arma se consume al disparar:
/// una corrutina suya moriría con ella antes de que la bola llegara.
/// </remarks>
internal class BlasterOrb : MonoBehaviour
{
    // Público: los rellena quien instancia la bola, no vienen del prefab.
    public Vector3 target;
    public float speed = 45f;

    /// Personaje al que hay que aplicar el efecto, o -1 si el tiro no dio a nadie.
    public int targetViewId = -1;

    public float targetScale = 1f;
    public float targetSpeed = 1f;
    public float targetStamina = 1f;
    public float seconds = 15f;

    /// Sonido que suena justo antes de que la bola desaparezca.
    public string impactSound = "";

    /// Zumbido que la bola arrastra durante todo el vuelo.
    public string flightSound = "";

    /// Tope de vida por si algo sale mal y nunca llega.
    float _deadline;

    void Start()
    {
        // El margen sale de lo que debería tardar, no de un número fijo: con la bola lenta
        // un tope de 6 s la mataba a medio vuelo en tiros largos.
        float distance = Vector3.Distance(transform.position, target);
        _deadline = Time.time + Mathf.Clamp(distance / Mathf.Max(1f, speed) * 2f, 1f, 20f);

        LoopParticles();
        StartFlightSound();
    }

    /// <summary>
    /// Cuelga de la bola el zumbido que suena mientras vuela.
    /// </summary>
    /// <remarks>
    /// El AudioSource va EN la bola, no suelto en el mundo con <c>PlayClipAtPoint</c>: al
    /// ser hijo suyo se mueve con ella, y Unity recalcula la distancia al oyente en cada
    /// frame. Es lo que hace que el sonido pase de largo cuando el proyectil te cruza por
    /// delante, en vez de quedarse clavado donde se disparó.
    ///
    /// <c>spatialBlend = 1</c> es la línea que lo convierte en sonido 3D. Por defecto un
    /// AudioSource es 2D y se oiría igual de fuerte estés donde estés, que es justo lo
    /// contrario de lo que se busca aquí.
    /// </remarks>
    void StartFlightSound()
    {
        var clip = Plugin.FindClip(flightSound);
        if (clip == null) return;

        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = Plugin.CfgBlasterVolume.Value;

        source.spatialBlend = 1f;                       // 3D, no plano
        source.rolloffMode = AudioRolloffMode.Linear;   // se apaga de forma previsible
        source.minDistance = Plugin.CfgOrbSoundNear.Value;
        source.maxDistance = Plugin.CfgOrbSoundFar.Value;
        source.dopplerLevel = 0.4f;                     // un punto de silbido al pasar

        source.Play();
    }

    /// <summary>
    /// Pone en bucle las partículas del proyectil.
    /// </summary>
    /// <remarks>
    /// Los efectos de Epic Toon FX son ráfagas de un solo uso: nacen, revientan y se
    /// apagan en menos de un segundo. Para un impacto eso vale, pero una bala que viaja
    /// tiene que seguir brillando todo el trayecto, y más aún ahora que va despacio a
    /// propósito para poder verla. Sin esto la bola se apaga a medio camino y el resto
    /// del viaje es invisible.
    ///
    /// Se fuerza en runtime en vez de pedirlo en el prefab para que valga con cualquier
    /// efecto que se ponga en el config, venga preparado o no.
    /// </remarks>
    void LoopParticles()
    {
        foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.loop = true;

            // Sin esto el sistema se autodestruye al terminar su primera ráfaga y se
            // lleva por delante el bucle que acabamos de poner.
            main.stopAction = ParticleSystemStopAction.None;

            system.Play(withChildren: false);
        }
    }

    void Update()
    {
        var toTarget = target - transform.position;
        float step = Mathf.Max(1f, speed) * Time.deltaTime;

        if (toTarget.sqrMagnitude <= step * step || Time.time >= _deadline)
        {
            Impact();
            return;
        }

        transform.position += toTarget.normalized * step;

        // Mirando hacia donde va: los prefabs de rayo de Epic Toon FX son alargados y de
        // lado se ven planos.
        transform.rotation = Quaternion.LookRotation(toTarget.normalized);
    }

    void Impact()
    {
        // Antes de nada, el chispazo: se sitúa donde está la bola AHORA, que es el punto
        // que el jugador está mirando. Si sonara después de destruirla ya no tendríamos
        // esa posición.
        // El zumbido se corta aquí y no en el Destroy: el objeto muere un instante
        // después y se solaparía con el chispazo del impacto.
        var flight = GetComponent<AudioSource>();
        if (flight != null) flight.Stop();

        var clip = Plugin.FindClip(impactSound);
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, transform.position, Plugin.CfgBlasterVolume.Value);

        var character = TargetCharacter();

        if (character != null)
        {
            SpawnImpact(character.Center);

            // El tamaño, en TODAS las máquinas: cambia colliders y anclajes de joints, y
            // si cada cliente viera un tamaño distinto los disparos no cuadrarían.
            if (!Mathf.Approximately(targetScale, 1f))
                Buffs.CharacterResizer.Apply(character, targetScale, seconds);

            // Los modificadores de movimiento, solo en el dueño: en PEAK cada cliente
            // simula su propio Scout, así que tocarlos en los demás no haría nada.
            if (character.IsLocal)
                Buffs.PlayerBuff.Grant(character, targetSpeed, targetStamina, seconds);
        }
        else
        {
            SpawnImpact(transform.position);
        }

        Destroy(gameObject);
    }

    Character? TargetCharacter()
    {
        if (targetViewId == -1) return null;

        var view = Photon.Pun.PhotonView.Find(targetViewId);
        return view != null ? view.GetComponent<Character>() : null;
    }

    static void SpawnImpact(Vector3 position)
    {
        var name = Plugin.Blaster?.ImpactEffect.Value ?? "";
        if (name.Length == 0) return;

        var prefab = Plugin.FindPrefab(name);
        if (prefab == null) return;

        var effect = Instantiate(prefab, position, Quaternion.identity);
        effect.transform.localScale = Vector3.one * Plugin.CfgBuffEffectScale.Value;
        Props.PropBuilder.RebindShaders(effect);

        // Los prefabs de Epic Toon FX no traen "stop action: destroy": sin esto cada
        // disparo dejaría un GameObject muerto en la escena para el resto de la partida.
        Destroy(effect, Plugin.CfgBuffEffectLifetime.Value);
    }
}
