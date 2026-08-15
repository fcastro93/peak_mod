using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Blaster que agranda o encoge a quien recibe el disparo. No hace daño.
/// </summary>
/// <remarks>
/// Comparte el trazado con <see cref="PistolAction"/> —doble raycast, primero contra el
/// terreno y luego una esfera contra la capa Character— porque el problema de acertar es
/// el mismo. Lo que cambia es qué pasa al impactar: en vez de afflictions, un efecto
/// temporal sobre el objetivo.
///
/// <b>Dos componentes, no dos ramas.</b> Van dos BlasterAction en el mismo item: uno
/// suscrito al disparo primario y otro al secundario. <c>ItemAction</c> encamina todos sus
/// eventos al mismo <c>RunAction()</c>, así que separar clic izquierdo de derecho dentro
/// de un solo componente no se puede; con dos, cada uno se suscribe a lo suyo y lleva sus
/// propios números.
///
/// <b>El efecto lo aplican TODOS los clientes.</b> A diferencia del empujón o del daño de
/// la pistola —que son autoridad de la víctima— aquí el tamaño cambia colliders y anclajes
/// de joints, y eso tiene que ser igual en todas las máquinas o cada una vería una hitbox
/// distinta. Por eso el RPC redimensiona en todos y solo los modificadores de movimiento
/// se limitan al dueño del personaje, que es quien lo simula.
/// </remarks>
internal class BlasterAction : ItemAction
{
    // PUBLIC a propósito: Unity solo copia los campos serializados al instanciar, e
    // 'internal' no se serializa. Ver la nota en PistolAction.

    /// Tamaño que deja al objetivo (2 = el doble, 0.33 = un tercio).
    public float targetScale = 2f;

    /// Velocidad que le da mientras dura.
    public float targetSpeed = 2f;

    /// Gasto de estamina al correr (0.5 = la mitad).
    public float targetStamina = 1f;

    public float duration = 15f;

    /// Prefab de la bola que sale disparada. Distinto por modo, para distinguirlos.
    public string orbPrefab = "LightningOrbSoftBlue";

    /// Sonido del disparo. Cada modo tiene el suyo.
    public string shotSound = "";

    /// Sonido del impacto, que suena al llegar la bola.
    public string impactSound = "";

    /// Zumbido que la bola arrastra mientras vuela.
    public string flightSound = "";

    public float maxDistance = 60f;
    public float hitRadius = 0.35f;
    public float shotVolume = 0.5f;

    /// Por dónde sale el rayo visual. Si es null, la cámara.
    public Transform? muzzle;

    RaycastHit[] _hits = new RaycastHit[32];

    public override void RunAction()
    {
        var ammo = GetComponent<PistolAmmo>();
        if (ammo != null && !ammo.TryConsume())
        {
            Plugin.Log.LogInfo("Blaster: sin carga.");
            return;
        }

        Fire();
    }

    void Fire()
    {
        var eye = MainCamera.instance;
        if (eye == null) return;

        // El tiro sale de la CÁMARA, no del cañón: es lo que hace que dé donde apunta la
        // mira. El cañón solo se usa como origen de los efectos.
        var origin = eye.transform.position;
        var direction = eye.transform.forward;

        // Primero contra el mundo, para no atravesar paredes.
        float reach = maxDistance;
        if (Physics.Raycast(origin, direction, out var wall, maxDistance,
                            HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            reach = wall.distance;
        }

        Character? victim = null;
        int count = Physics.SphereCastNonAlloc(origin, hitRadius, direction, _hits, reach,
                                               LayerMask.GetMask("Character"), QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var candidate = _hits[i].collider.GetComponentInParent<Character>();
            if (candidate == null || candidate == character) continue;   // no a uno mismo
            if (_hits[i].distance >= best) continue;

            best = _hits[i].distance;
            victim = candidate;
        }

        var endpoint = victim != null ? victim.Center : origin + direction * reach;
        var effectOrigin = muzzle != null ? muzzle.position : origin;

        photonView.RPC(nameof(RPC_Zap), RpcTarget.All,
                       victim != null ? victim.photonView.ViewID : -1,
                       effectOrigin, endpoint,
                       targetScale, targetSpeed, targetStamina, duration,
                       orbPrefab + "|" + shotSound + "|" + impactSound + "|" + flightSound);
    }

    /// <remarks>
    /// Corre en TODOS los clientes: así el fogonazo y la bola los ve todo el mundo sin
    /// instanciar nada por red. El efecto ya no se aplica aquí, sino cuando la bola llega
    /// (ver <see cref="BlasterOrb"/>); como todos reciben el mismo origen, destino y
    /// velocidad, a todos les llega en el mismo momento.
    /// </remarks>
    [PunRPC]
    void RPC_Zap(int viewId, Vector3 origin, Vector3 endpoint,
                 float scale, float speed, float stamina, float seconds, string payload)
    {
        // Los tres nombres viajan en una sola cadena. Photon serializa cada argumento por
        // separado y la lista ya iba larga; empaquetarlos evita ampliar la firma del RPC
        // cada vez que el arma gana un sonido.
        var parts = payload.Split('|');
        var orb = parts.Length > 0 ? parts[0] : "";
        var shot = parts.Length > 1 ? parts[1] : "";
        var impact = parts.Length > 2 ? parts[2] : "";
        var flight = parts.Length > 3 ? parts[3] : "";

        // El disparo suena dentro del RPC, así que lo oyen TODOS los clientes y queda
        // situado en 3D donde salió el tiro.
        var shotClip = Plugin.FindClip(shot);
        if (shotClip != null)
            AudioSource.PlayClipAtPoint(shotClip, origin, Plugin.CfgBlasterVolume.Value);

        // El efecto de boca sale del config y no del RPC: es puramente cosmético, así
        // que cada cliente puede tenerlo a su gusto sin desincronizar nada.
        var muzzleName = Plugin.Blaster?.MuzzleEffect.Value ?? "";
        if (muzzleName.Length > 0)
        {
            SpawnEffect(Plugin.FindPrefab(muzzleName), origin, Plugin.CfgMuzzleFlashScale.Value,
                        Plugin.CfgMuzzleFlashLifetime.Value);
        }

        var prefab = Plugin.FindPrefab(orb);
        if (prefab == null)
        {
            Plugin.Log.LogWarning($"Sin proyectil '{orb}' en el bundle.");
            return;
        }

        var bullet = Instantiate(prefab, origin, Quaternion.identity);
        bullet.transform.localScale = Vector3.one * Plugin.CfgOrbScale.Value;
        Props.PropBuilder.RebindShaders(bullet);

        var mover = bullet.AddComponent<BlasterOrb>();
        mover.target = endpoint;
        mover.speed = Plugin.CfgOrbSpeed.Value;
        mover.targetViewId = viewId;
        mover.targetScale = scale;
        mover.targetSpeed = speed;
        mover.targetStamina = stamina;
        mover.seconds = seconds;
        mover.impactSound = impact;
        mover.flightSound = flight;
    }

    /// <remarks>
    /// El Destroy con retardo no es opcional: los prefabs de Epic Toon FX no traen
    /// "stop action: destroy" y cada disparo dejaría un GameObject muerto en la escena
    /// para el resto de la partida.
    /// </remarks>
    static void SpawnEffect(GameObject? prefab, Vector3 position, float scale, float lifetime)
    {
        if (prefab == null) return;

        var effect = Instantiate(prefab, position, Quaternion.identity);
        effect.transform.localScale = Vector3.one * scale;
        Props.PropBuilder.RebindShaders(effect);
        Destroy(effect, lifetime);
    }
}
