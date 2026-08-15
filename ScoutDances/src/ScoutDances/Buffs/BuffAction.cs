using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Un power-up: se recoge pisándolo, o se usa desde la mano.
/// </summary>
/// <remarks>
/// Las dos vías acaban en el mismo <see cref="Trigger"/>, que avisa por red y consume el
/// item. La de la mano existe para poder probarlos desde la caja del aeropuerto; en el
/// juego lo normal será pisarlos.
///
/// <b>Autoridad de quien lo recoge.</b> El RPC va a todos, pero el modificador de
/// velocidad lo aplica ÚNICAMENTE el cliente dueño del personaje. En PEAK cada uno simula
/// su propio Scout, así que tocar <c>movementModifier</c> en los demás clientes no
/// aceleraría a nadie y dejaría el estado sucio. El efecto visual sí va en todos, para
/// que se vea quién lo cogió. Mismo reparto que en <see cref="Weapons.PistolAction"/>.
/// </remarks>
internal class BuffAction : ItemAction
{
    // PUBLIC a propósito. Unity solo copia los campos serializados al instanciar un
    // prefab, e 'internal' no se serializa: con internal, cada copia nacía con los
    // valores por defecto de la clase en vez de los de su definición.

    /// Categoría de la caja, como int porque Unity no serializa enums propios de forma fiable.
    public int category;

    /// A qué distancia se recoge al pasar por encima.
    public float pickupRadius = 1.6f;

    /// <summary>
    /// Margen tras soltarlo durante el que no se puede recoger.
    /// </summary>
    /// <remarks>
    /// Sin esto es imposible tirar uno al suelo: lo sueltas, sigues encima, y se recoge
    /// solo en el mismo frame. El propio juego usa este mismo guard (0,5 s comparando
    /// <c>lastThrownCharacter</c>) para que sus peligros de suelo no te hieran con lo que
    /// acabas de lanzar.
    /// </remarks>
    const float ThrowGrace = 0.6f;

    Item? _item;

    void Awake() => _item = GetComponent<Item>();

    /// <summary>Uso desde la mano, al terminar el cast.</summary>
    public override void RunAction() => Trigger(Character.localCharacter);

    /// <summary>Recogida al pasarle por encima.</summary>
    /// <remarks>
    /// Por distancia y no con un trigger collider: el item ya trae sus propios colliders
    /// configurados por el juego para el agarre y las físicas, y añadir uno en modo
    /// trigger se pelea con ellos. Una comprobación de distancia contra un único
    /// personaje —el local— es más barata que cualquier collider y no toca nada existente.
    /// </remarks>
    void Update()
    {
        if (_item == null || _item.itemState != ItemState.Ground || _item.consuming) return;

        var local = Character.localCharacter;
        if (local == null || local.data == null || local.data.dead) return;

        if (_item.lastThrownCharacter == local && Time.time - _item.lastThrownTime < ThrowGrace)
            return;

        // Se mide contra la CAJA QUE SE VE, no contra el objeto.
        //
        // El modelo se coloca con la misma función que las armas, que lo separa del item
        // para que quede bien agarrado en la mano: 1,26 m adelante y 0,44 m abajo. En la
        // mano da igual, pero en el suelo significa que el jugador camina hacia la caja
        // que ve mientras la recogida se mide desde un punto a metro y medio de allí. Con
        // un radio de 1,6 m, unas veces rozaba y otras no llegaba nunca.
        var box = VisiblePosition();

        // Contra el torso Y contra los pies: el power-up se apoya en el suelo y el centro
        // del cuerpo queda casi un metro por encima.
        var feet = local.Center - Vector3.up * 0.9f;
        float distance = Mathf.Min((local.Center - box).magnitude, (feet - box).magnitude);

        if (distance > pickupRadius)
        {
            Report(distance);
            return;
        }

        Trigger(local);
    }

    Transform? _model;

    /// <summary>Dónde está de verdad la caja que ve el jugador.</summary>
    Vector3 VisiblePosition()
    {
        if (_model == null) _model = transform.Find("BuffModel");
        return _model != null ? _model.position : transform.position;
    }

    float _nextReport;

    /// <summary>Deja constancia de por qué no se recoge, sin llenar el log.</summary>
    void Report(float distance)
    {
        if (!Plugin.CfgBuffDiagnostics.Value) return;
        if (distance > pickupRadius * 3f || Time.time < _nextReport) return;

        _nextReport = Time.time + 2f;
        Plugin.Log.LogInfo($"[power-up] cerca pero fuera: {distance:0.00} m de {pickupRadius:0.00}.");
    }

    void Trigger(Character? character)
    {
        if (character == null || _item == null || _item.consuming) return;

        // El sorteo lo hace QUIEN LA RECOGE, y el resultado viaja ya decidido. Si cada
        // cliente sorteara al recibir el aviso, a cada uno le saldría un power-up distinto
        // y el efecto no cuadraría con lo que ve el dueño.
        var entry = BuffCatalog.Roll((BuffCategory)category);
        if (entry == null)
        {
            Plugin.Log.LogWarning($"La caja de {(BuffCategory)category} no tiene nada dentro.");
            return;
        }

        photonView.RPC(nameof(RPC_Grant), RpcTarget.All,
                       character.photonView.ViewID, entry.Id);

        // ConsumeDelayed funciona igual en el suelo que en la mano: si no hay portador
        // manda -1 como consumerID y avisa a todos por RPC.
        _item.StartCoroutine(_item.ConsumeDelayed());
    }

    [PunRPC]
    void RPC_Grant(int viewId, string buffId)
    {
        var view = PhotonView.Find(viewId);
        var character = view != null ? view.GetComponent<Character>() : null;
        if (character == null) return;

        SpawnPickupEffect(character);

        // El efecto solo lo aplica el dueño del personaje; a los demás les llega para que
        // vean el destello y sepan quién lo cogió.
        if (!character.IsLocal) return;

        var entry = BuffCatalog.ById(buffId);
        if (entry == null)
        {
            Plugin.Log.LogWarning($"Me pasaron un power-up que no conozco: '{buffId}'. " +
                                  "¿Versiones distintas del mod?");
            return;
        }

        ActiveBuffs.Take(character, entry);
    }

    /// <summary>
    /// Suelta el destello sobre la cabeza de quien lo ha recogido.
    /// </summary>
    /// <remarks>
    /// Se cuelga de la cabeza en vez de soltarlo suelto en el mundo para que acompañe al
    /// jugador mientras dura; si no, te alejas corriendo —que es justo lo que acabas de
    /// ganar— y el efecto se queda atrás.
    ///
    /// El Destroy con retardo no es opcional: los prefabs de Epic Toon FX no traen "stop
    /// action: destroy", así que cada recogida dejaría un GameObject muerto colgado del
    /// esqueleto para el resto de la partida.
    /// </remarks>
    static void SpawnPickupEffect(Character character)
    {
        var prefab = Plugin.FindPrefab(Plugin.CfgBuffPickupEffect.Value);
        if (prefab == null) return;

        var head = character.GetBodypart(BodypartType.Head)?.transform;
        if (head == null) return;

        var effect = Instantiate(prefab, head.position + Vector3.up * Plugin.CfgBuffEffectHeight.Value,
                                 Quaternion.identity);
        effect.transform.SetParent(head, worldPositionStays: true);
        effect.transform.localScale = Vector3.one * Plugin.CfgBuffEffectScale.Value;

        // El shader que viaja dentro del bundle es una copia sin variantes: sin esto la
        // partícula sale invisible o rosa, igual que nos pasó con el arma.
        Props.PropBuilder.RebindShaders(effect);

        Destroy(effect, Plugin.CfgBuffEffectLifetime.Value);
    }
}
