using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Un portal. Manda a quien lo pise al sitio de su pareja.
/// </summary>
/// <remarks>
/// <b>El portal no es un objeto de red.</b> Cada cliente crea su propia copia de los dos
/// portales a partir del mismo RPC, con las mismas posiciones y el mismo momento de
/// apertura. No hace falta sincronizar nada más: no tienen estado que pueda divergir —ni
/// se abren, ni se gastan, ni se rompen— solo existen cinco segundos y desaparecen.
///
/// El teletransporte sí es autoridad de quien lo pisa: cada uno vigila SU personaje y se
/// manda a sí mismo con <c>WarpPlayerRPC</c>, el del propio juego. Si el que disparó
/// vigilara a todos, un jugador con lag se vería teletransportado desde otra máquina.
/// </remarks>
internal class Portal : MonoBehaviour
{
    internal Portal? Twin;
    internal float Radius = 2.2f;

    /// <summary>
    /// Hasta cuándo este portal ignora a quien acaba de salir de él.
    /// </summary>
    /// <remarks>
    /// Imprescindible: al llegar apareces ENCIMA del portal de destino, así que sin este
    /// margen te devolvería al instante y te quedarías rebotando entre los dos.
    /// </remarks>
    float _ignoreUntil;

    /// <summary>Segundos que los dos portales ignoran a quien acaba de cruzar.</summary>
    /// <remarks>
    /// Tiene que cubrir de sobra lo que tarda el teletransporte del juego en completarse,
    /// o el de origen te reengancha antes de que te hayas ido.
    /// </remarks>
    const float Cooldown = 1.2f;

    void Update()
    {
        if (Twin == null) return;                 // todavía sin pareja: no lleva a ningún sitio

        var local = Character.localCharacter;
        if (local == null || local.data == null || local.data.dead) return;
        if (Time.time < _ignoreUntil) return;

        // Se mide contra los PIES y no contra el centro del cuerpo. El portal se apoya en
        // el suelo y el torso queda un metro por encima, así que medir desde el centro te
        // obligaba a meterte más de la cuenta para que contara.
        var feet = local.Center - Vector3.up * 0.9f;
        float distance = Mathf.Min((local.Center - transform.position).magnitude,
                                   (feet - transform.position).magnitude);

        if (distance > Radius) return;

        // Se cierran LOS DOS un momento, no solo el de llegada. Es el detalle que faltaba:
        // WarpPlayer no te mueve en el mismo frame —tiene su propio estado interno y hasta
        // un reintento— así que durante unos cuantos frames sigues encima del portal de
        // origen, y sin enfriarlo también volvía a lanzarte una y otra vez. En el log
        // salían ciento sesenta cruces seguidos al mismo sitio.
        _ignoreUntil = Time.time + Cooldown;
        Twin._ignoreUntil = Time.time + Cooldown;

        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All,
                             Twin.transform.position + Vector3.up * 0.5f, true);

        Plugin.Log.LogInfo($"Portal: cruzado a {Twin.transform.position}.");
    }
}

/// <summary>
/// Pistola de portales: abre una pareja y se agota.
/// </summary>
/// <remarks>
/// El azul queda donde estabas y el dorado donde apuntaste, así que un disparo te deja un
/// atajo de ida y vuelta durante unos segundos.
/// </remarks>
internal class PortalAction : ItemAction
{
    // PUBLIC a propósito: Unity solo copia los campos serializados al instanciar.
    public float maxDistance = 60f;
    public float seconds = 5f;
    public float radius = 2.2f;
    public string entryPrefab = "SimplePortalBlue";
    public string exitPrefab = "SimplePortalGold";
    public string openSound = "";

    /// <summary>
    /// El primer portal, mientras espera pareja.
    /// </summary>
    /// <remarks>
    /// Lo llevan TODOS los clientes, no solo el que dispara: cada uno lo apunta al ejecutar
    /// el RPC del primer disparo, así que cuando llega el segundo todos saben a quién
    /// emparejarlo sin mandar identificadores por la red.
    /// </remarks>
    Portal? _pending;

    public override void RunAction()
    {
        var ammo = GetComponent<PistolAmmo>();
        if (ammo != null && !ammo.TryConsume())
        {
            Plugin.Log.LogInfo("Portales: sin carga.");
            return;
        }

        var target = AimPoint();
        if (target == null) return;

        // El primer disparo deja el portal esperando; el segundo lo empareja y arranca la
        // cuenta atrás. Antes se abrían los dos de una vez, uno bajo tus pies, y no había
        // forma de elegir el otro extremo.
        photonView.RPC(_pending == null ? nameof(RPC_First) : nameof(RPC_Second),
                       RpcTarget.All, target.Value);
    }

    /// <summary>Dónde cae el portal: contra el terreno, o al final del alcance.</summary>
    Vector3? AimPoint()
    {
        var eye = MainCamera.instance;
        if (eye == null) return null;

        var origin = eye.transform.position;
        var direction = eye.transform.forward;

        // Separado de la pared por su normal, para que no quede medio embutido en ella.
        return Physics.Raycast(origin, direction, out var wall, maxDistance,
                               HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
            ? wall.point + wall.normal * 0.6f
            : origin + direction * maxDistance;
    }

    [PunRPC]
    void RPC_First(Vector3 position)
    {
        PlayOpen(position);

        // La cuenta atrás de verdad no empieza hasta que el par está completo, pero se le
        // pone un tope generoso: si sueltas el arma entre disparo y disparo, el portal
        // huérfano se quedaría plantado en el mapa el resto de la partida.
        _pending = Build(entryPrefab, position, seconds * 8f);

        Plugin.Log.LogInfo(_pending != null
            ? $"Primer portal en {position}; esperando el segundo disparo."
            : "El primer portal NO se pudo crear (falta el prefab en el bundle).");
    }

    [PunRPC]
    void RPC_Second(Vector3 position)
    {
        PlayOpen(position);

        var second = Build(exitPrefab, position, seconds);
        if (second == null || _pending == null) return;

        _pending.Twin = second;
        second.Twin = _pending;

        Plugin.Log.LogInfo($"Portales emparejados: {_pending.transform.position} <-> " +
                           $"{second.transform.position}. Activos {seconds:0.#}s.");

        // Ahora sí: los dos se van a la vez cuando se acabe el tiempo.
        Destroy(_pending.gameObject, seconds);

        _pending = null;
    }

    void PlayOpen(Vector3 position)
    {
        var clip = Plugin.FindClip(openSound);
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, position, Plugin.CfgBlasterVolume.Value);
    }

    Portal? Build(string prefabName, Vector3 position, float lifetime)
    {
        var prefab = Plugin.FindPrefab(prefabName);
        if (prefab == null)
        {
            Plugin.Log.LogWarning($"Sin prefab de portal '{prefabName}' en el bundle.");
            return null;
        }

        var instance = Instantiate(prefab, position, Quaternion.identity);

        // Sin esto los portales salen lavados: los materiales de partículas del bundle
        // pierden su mezcla aditiva si se les reenlaza el shader.
        Props.PropBuilder.RebindShaders(instance);

        var portal = instance.AddComponent<Portal>();
        portal.Radius = radius;

        if (lifetime > 0f) Destroy(instance, lifetime);

        return portal;
    }
}
