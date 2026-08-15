using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Orbe de fuego: viaja, quema a quien roza y se apaga al chocar o al agotar su tiempo.
/// </summary>
/// <remarks>
/// <b>Quemar está copiado de <c>HotSun</c>, el sol del desierto.</b> Su mecánica no usa
/// colliders ni triggers: cada frame comprueba si el jugador local está dentro de un
/// volumen y, cada <c>rate</c> segundos, le suma calor. Aquí es lo mismo con una esfera en
/// vez de una caja.
///
/// <c>HotSun</c> además tira una línea hacia el sol para saber si estás a la sombra.
/// Nosotros la tiramos hacia el ORBE, que da algo mejor: te quema si te ve, y puedes
/// cubrirte poniendo una roca de por medio.
///
/// <b>Cada cliente quema a su propio personaje.</b> Los estados solo los puede aplicar el
/// dueño del personaje, así que el orbe existe en todas las máquinas y cada una mira si el
/// suyo está dentro. Mismo reparto que la granada.
///
/// <b>El choque se detecta con un rayo, no con un collider.</b> El efecto es puro sistema
/// de partículas: no tiene cuerpo con el que chocar. Se lanza un rayo desde donde estaba
/// hasta donde va a estar, así que tampoco puede atravesar una pared fina por ir rápido.
/// </remarks>
internal class FireOrb : MonoBehaviour
{
    // PUBLIC a propósito: Unity solo copia los campos serializados al instanciar.

    public Vector3 direction = Vector3.forward;
    public float speed = 10f;
    public float lifetime = 5f;

    /// Radio en el que quema, en metros.
    public float radius = 4f;

    /// Cada cuánto pica, en segundos. Igual que el rate de HotSun.
    public float rate = 0.4f;

    /// Cuánto calor suma cada pico.
    public float amount = 0.06f;

    public string loopSound = "";

    float _dieAt;
    float _nextBurn;
    Vector3 _lastPosition;
    AudioSource? _loop;

    void Start()
    {
        _dieAt = Time.time + lifetime;
        _lastPosition = transform.position;

        LoopParticles();
        StartLoopSound();
    }

    /// <remarks>
    /// Los efectos de Epic Toon FX son ráfagas de un solo uso: sin esto el orbe se apagaría
    /// al segundo aunque le quedaran cuatro de vida.
    /// </remarks>
    void LoopParticles()
    {
        foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
            system.Play(withChildren: false);
        }
    }

    /// <remarks>
    /// La fuente va EN el orbe para que el fuego se oiga por cercanía y acompañe al
    /// proyectil, igual que la bola del blaster. <c>spatialBlend = 1</c> es lo que lo
    /// convierte en sonido 3D; por defecto sonaría igual de fuerte en todo el mapa.
    /// </remarks>
    void StartLoopSound()
    {
        var clip = Plugin.FindClip(loopSound);
        if (clip == null) return;

        _loop = gameObject.AddComponent<AudioSource>();
        _loop.clip = clip;
        _loop.loop = true;
        _loop.volume = Plugin.CfgBlasterVolume.Value;
        _loop.spatialBlend = 1f;
        _loop.rolloffMode = AudioRolloffMode.Linear;
        _loop.minDistance = Plugin.CfgOrbSoundNear.Value;
        _loop.maxDistance = Plugin.CfgOrbSoundFar.Value;
        _loop.Play();
    }

    void Update()
    {
        if (Time.time >= _dieAt) { Die(); return; }

        var step = direction.normalized * speed * Time.deltaTime;
        var next = transform.position + step;

        // Contra la pared: el rayo cubre TODO el tramo de este frame, así que a velocidad
        // alta no se cuela por una pared fina como haría una simple comprobación de punto.
        if (Physics.Raycast(_lastPosition, step.normalized, out _, step.magnitude + 0.3f,
                            HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            Die();
            return;
        }

        transform.position = next;
        _lastPosition = next;

        Burn();
    }

    void Burn()
    {
        if (Time.time < _nextBurn) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null || local.data.dead) return;

        var toPlayer = local.Center - transform.position;
        if (toPlayer.magnitude > radius) return;

        // ¿Le ve el orbe? Igual que HotSun mira si estás a la sombra, pero respecto al
        // propio orbe: una roca de por medio te protege.
        if (Physics.Raycast(transform.position, toPlayer.normalized, out _, toPlayer.magnitude - 0.5f,
                            HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        _nextBurn = Time.time + rate;

        // AddStatus directo y no AddSunHeat: ese respeta la sombrilla y la crema solar, que
        // no pintan nada contra una bola de fuego que te han lanzado.
        local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount);
    }

    void Die()
    {
        if (_loop != null) _loop.Stop();

        // Se apagan las partículas y se deja un momento para que las que están en el aire
        // terminen su vida; destruir de golpe corta el fuego en seco.
        foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
            system.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);

        Destroy(gameObject, 1.5f);
        Destroy(this);            // deja de moverse y de quemar ya
    }
}

/// <summary>
/// Varita de fuego: lanza un orbe que quema por donde pasa.
/// </summary>
/// <remarks>
/// Gasta munición como el resto de las armas y desaparece al agotarse: un solo orbe por
/// varita. El campo de recarga sigue ahí por si alguna futura quiere varios usos
/// espaciados, pero a cero no estorba.
/// </remarks>
internal class WandAction : ItemAction
{
    /// Recarga entre usos. 0 = sin recarga; con 1 sola carga da igual.
    public float cooldown;
    public float orbSpeed = 10f;
    public float orbLifetime = 5f;
    public float orbRadius = 4f;
    public float burnRate = 0.4f;
    public float burnAmount = 0.06f;

    public string orbPrefab = "";
    public string loopSound = "";

    float _readyAt;

    public override void RunAction()
    {
        var ammo = GetComponent<PistolAmmo>();
        if (ammo != null && !ammo.TryConsume())
        {
            Plugin.Log.LogInfo("Varita: gastada.");
            return;
        }

        if (cooldown > 0f && Time.time < _readyAt)
        {
            Plugin.Log.LogInfo($"Varita: recargando ({_readyAt - Time.time:0.0}s).");
            return;
        }

        var eye = MainCamera.instance;
        if (eye == null) return;

        _readyAt = Time.time + cooldown;

        // Sale un poco por delante de la cara para no nacer dentro del propio jugador.
        var origin = eye.transform.position + eye.transform.forward * 1.5f;

        photonView.RPC(nameof(RPC_Cast), RpcTarget.All, origin, eye.transform.forward);
    }

    [PunRPC]
    void RPC_Cast(Vector3 origin, Vector3 direction)
    {
        var prefab = Plugin.FindPrefab(orbPrefab);
        if (prefab == null)
        {
            Plugin.Log.LogWarning($"Sin orbe '{orbPrefab}' en el bundle.");
            return;
        }

        var orb = Instantiate(prefab, origin, Quaternion.LookRotation(direction));

        // Sin esto el fuego sale lavado: los materiales de partículas del bundle pierden su
        // mezcla aditiva si se les reenlaza el shader.
        Props.PropBuilder.RebindShaders(orb);

        var fire = orb.AddComponent<FireOrb>();
        fire.direction = direction;
        fire.speed = orbSpeed;
        fire.lifetime = orbLifetime;
        fire.radius = orbRadius;
        fire.rate = burnRate;
        fire.amount = burnAmount;
        fire.loopSound = loopSound;

        Plugin.Log.LogInfo($"Orbe de fuego lanzado ({orbLifetime:0.#}s, radio {orbRadius:0.#} m).");
    }
}
