using System;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Dispara el arma: traza el tiro, aplica el daño y gasta munición.
/// </summary>
/// <remarks>
/// Calcado del <c>Action_RaycastDart</c> del propio juego (la cerbatana), que es el único
/// arma de proyectil de PEAK. Tres cosas se copian a propósito:
///
/// 1. <b>Doble trazado.</b> Primero un Raycast contra el terreno para saber dónde está la
///    pared, y luego un SphereCastAll SOLO contra la capa "Character" limitado a esa
///    distancia. El radio de la esfera es tolerancia de puntería: sin ella, acertar a un
///    Scout en movimiento sería una tortura.
/// 2. <b>El daño son afflictions</b>, no puntos de vida: PEAK no tiene vida, tiene estados
///    que se acumulan hasta dejarte inconsciente.
/// 3. <b>Autoridad de la víctima.</b> El RPC va a todos para VFX y sonido, pero las
///    afflictions las aplica ÚNICAMENTE el cliente del que recibe el tiro
///    (<c>photonView.IsMine</c>). Si las aplicara el atacante o el host, el estado se
///    desincronizaría, porque en PEAK cada cliente manda sobre su propio personaje.
/// </remarks>
internal class PistolAction : ItemAction
{
    // OJO: estos campos son PUBLIC a propósito, no por descuido. Unity solo copia los
    // campos SERIALIZADOS al instanciar un prefab, y 'internal' no se serializa. Cuando
    // eran internal, los valores que poníamos en el clon se perdían y cada arma nacía
    // con los valores por defecto de la clase: pedías 1 bala y salían 6.

    /// Alcance máximo del disparo, en metros.
    public float maxDistance = 60f;

    /// Radio de tolerancia de puntería.
    public float hitRadius = 0.35f;

    /// Cuánta Injury mete cada impacto (el KO llega al pasar de 0.99 de estado total).
    public float injuryPerHit = 0.5f;

    /// Volumen del disparo de ESTA arma (la pistola suena flojo, el pistolón fuerte).
    public float shotVolume = 0.7f;

    /// Fuerza del empujón que recibe QUIEN RECIBE el disparo.
    public float knockback = 450f;

    /// Retroceso: empujón hacia atrás para QUIEN DISPARA. 0 = sin retroceso.
    public float recoil;

    /// Desde dónde sale el tiro. Si es null usamos la cámara.
    public Transform? muzzle;

    /// Sonido del disparo de ESTA arma. Vacío = el general del config.
    public string shotSound = "";

    /// Partícula en el punto de impacto. Vacío = la sangre de siempre.
    public string hitEffect = "";

    /// Radio del empujón. El del juego para su trampa de flechas es 400.
    public float knockbackRadius;

    /// Segundos que el objetivo se queda sin gravedad. 0 = el arma no lo hace.
    public float floatSeconds;

    /// Aura que acompaña al objetivo mientras flota.
    public string auraEffect = "";

    /// Si es true, el tirador y el objetivo se intercambian el sitio.
    public bool swapPositions;

    RaycastHit[] _hits = new RaycastHit[32];

    public override void RunAction()
    {
        var ammo = GetComponent<PistolAmmo>();
        if (ammo != null && !ammo.TryConsume())
        {
            Plugin.Log.LogInfo("Clic: sin munición.");
            return;
        }

        Fire();
    }

    void Fire()
    {
        var camera = MainCamera.instance;
        if (camera == null) return;

        // La trayectoria sale SIEMPRE de la cámara: si partiera de la boca del cañón,
        // el tiro no coincidiría con la mirilla. La boca solo sirve para el fogonazo.
        var eye = camera.transform.position;
        var direction = camera.transform.forward;
        var origin = muzzle != null ? muzzle.position : eye;

        // 1. ¿Dónde acaba el disparo? Contra el terreno.
        float distance = maxDistance;
        var endpoint = eye + direction * maxDistance;

        if (Physics.Raycast(eye, direction, out var wall, maxDistance,
                            HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            distance = wall.distance;
            endpoint = wall.point;
        }

        // 2. ¿Le hemos dado a alguien antes de esa pared?
        int count = Physics.SphereCastNonAlloc(eye, hitRadius, direction, _hits, distance,
                                               LayerMask.GetMask("Character"),
                                               QueryTriggerInteraction.Ignore);

        Character? victim = null;
        float closest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null) continue;

            var hitCharacter = hit.collider.GetComponentInParent<Character>();
            if (hitCharacter == null || hitCharacter == character) continue;   // no a uno mismo
            if (hitCharacter.data != null && hitCharacter.data.dead) continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                victim = hitCharacter;
                endpoint = hit.point;
            }
        }

        // El espejo se resuelve AQUÍ, antes de mandar nada: si el objetivo lo lleva
        // puesto, el tirador se apunta a sí mismo y el resto del disparo sigue igual. Se
        // decide en este lado porque los estados solo los aplica el dueño de cada
        // personaje: la víctima no tendría permiso para devolvérselo al atacante.
        int reflectedFrom = -1;
        if (victim != null && character != null && victim != character &&
            MirrorShield.IsShielded(victim))
        {
            reflectedFrom = victim.photonView.ViewID;
            victim = character;
            endpoint = character.Center;
        }

        int viewId = victim != null ? victim.photonView.ViewID : -1;
        if (swapPositions && victim != null) Swap(victim);

        photonView.RPC(nameof(RPC_Hit), RpcTarget.All, viewId, origin, endpoint,
                       injuryPerHit, shotVolume, knockback,
                       string.Join("|", shotSound, hitEffect,
                           knockbackRadius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                           floatSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                           auraEffect,
                           reflectedFrom.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        ApplyRecoil(direction);
    }

    /// <summary>
    /// Intercambia el sitio del tirador y del objetivo.
    /// </summary>
    /// <remarks>
    /// Usa <c>WarpPlayerRPC</c>, el teletransporte del propio juego: ya trae el efecto de
    /// humo, arregla el ragdoll al llegar y se reparte a todos los clientes por dentro. Se
    /// lanza desde el cliente del tirador, que es el único que ejecuta <c>Fire()</c>.
    ///
    /// Las dos posiciones se leen ANTES de mover a nadie. Si se moviera primero al tirador
    /// y luego se leyera el destino del otro, el segundo salto usaría una posición que ya
    /// ha cambiado y ambos acabarían en el mismo sitio.
    ///
    /// Se sube medio metro cada destino: el centro de un Scout está a la altura del torso,
    /// y aterrizar exactamente ahí mete los pies en el suelo.
    /// </remarks>
    void Swap(Character victim)
    {
        if (character == null) return;

        var mine = character.Center + Vector3.up * 0.5f;
        var theirs = victim.Center + Vector3.up * 0.5f;

        character.photonView.RPC("WarpPlayerRPC", RpcTarget.All, theirs, true);
        victim.photonView.RPC("WarpPlayerRPC", RpcTarget.All, mine, true);

        Plugin.Log.LogInfo($"Intercambio de posiciones con '{victim.name}'.");
    }

    /// <summary>
    /// Empuja hacia atrás a quien dispara.
    /// </summary>
    /// <remarks>
    /// Va aquí, en Fire(), y no en el RPC: Fire() solo corre en el cliente del que aprieta
    /// el gatillo, y AddForceAtPosition ya reparte el impulso a todos por dentro. Si lo
    /// pusiéramos en el RPC se aplicaría una vez por jugador conectado.
    ///
    /// Se le mezcla algo de componente vertical porque un impulso puramente horizontal
    /// contra el suelo se lo come el rozamiento y casi no se nota.
    /// </remarks>
    void ApplyRecoil(Vector3 direction)
    {
        if (recoil <= 0f || character == null) return;

        var push = (-direction + Vector3.up * Plugin.CfgKnockbackUp.Value).normalized;
        character.AddForceAtPosition(push * recoil, character.Center, Plugin.CfgKnockbackRadius.Value);

        Plugin.Log.LogInfo($"Retroceso de {recoil:0} sobre el tirador.");
    }

    /// <summary>
    /// Clip de disparo, buscado entre los que el juego ya tiene cargados.
    /// </summary>
    /// <remarks>
    /// PEAK trae sonidos de arma de fuego de verdad (Au_Garand_Fire1/3, Au_Gundog_Fire3,
    /// Au_Harpoon_Shoot1/2), así que no hace falta meter audio propio en el bundle.
    /// <c>FindObjectsOfTypeAll</c> los encuentra porque ya están cargados con el juego.
    /// </remarks>
    static AudioClip? _shotClip;
    static bool _shotSearched;

    static AudioClip? ShotClip()
    {
        if (_shotSearched) return _shotClip;
        _shotSearched = true;

        var wanted = Plugin.CfgWeaponShotSound.Value;
        if (string.IsNullOrWhiteSpace(wanted)) return null;

        _shotClip = Resources.FindObjectsOfTypeAll<AudioClip>()
            .FirstOrDefault(c => c != null && c.name.Equals(wanted, StringComparison.OrdinalIgnoreCase));

        if (_shotClip != null)
            Plugin.Log.LogInfo($"Sonido de disparo: '{_shotClip.name}' ({_shotClip.length:0.00}s).");
        else
            Plugin.Log.LogWarning($"No encontré el clip '{wanted}'; el arma sonará muda.");

        return _shotClip;
    }

    /// <summary>
    /// Suelta una partícula en el mundo y programa su destrucción.
    /// </summary>
    /// <remarks>
    /// Se llama desde el RPC, así que corre en TODOS los clientes: la ve todo el mundo
    /// sin necesidad de instanciarla en red.
    ///
    /// El Destroy con retardo no es opcional: los prefabs de Epic Toon FX no traen
    /// "stop action: destroy", así que cada disparo dejaría un GameObject muerto en la
    /// escena para el resto de la partida.
    /// </remarks>
    static void SpawnEffect(GameObject? prefab, Vector3 position, Quaternion rotation,
                            float scale, float lifetime)
    {
        if (prefab == null) return;

        var effect = Instantiate(prefab, position, rotation);
        effect.transform.localScale = Vector3.one * scale;

        // Imprescindible: los materiales que viajan en el bundle pasan por aquí para que
        // los de partículas se queden INTACTOS (su modo de mezcla aditiva se perdía al
        // reenlazarlos y salían transparentes y sin brillo) y los demás se reenlacen.
        Props.PropBuilder.RebindShaders(effect);

        Destroy(effect, lifetime);
    }

    [PunRPC]
    void RPC_Hit(int viewId, Vector3 origin, Vector3 endpoint,
                 float injury, float volume, float pushForce, string payload)
    {
        // Los ajustes propios del arma viajan en una sola cadena: Photon serializa cada
        // argumento por separado y la firma ya iba larga.
        var parts = payload.Split('|');
        var sound = parts.Length > 0 ? parts[0] : "";
        var effect = parts.Length > 1 ? parts[1] : "";
        var aura = parts.Length > 4 ? parts[4] : "";

        // Quién reflejó, si alguien lo hizo. Viaja en el RPC para que el destello lo vean
        // TODOS y quede claro de dónde salió el rebote.
        int reflectedFrom = parts.Length > 5 && int.TryParse(parts[5], out var mv) ? mv : -1;

        if (reflectedFrom != -1)
        {
            var mirrorView = PhotonNetwork.GetPhotonView(reflectedFrom);
            var reflector = mirrorView != null ? mirrorView.GetComponent<Character>() : null;

            if (reflector != null)
            {
                MirrorShield.PlayReflect(reflector);

                // Se lo quita quien lo llevaba: el espejo es de un solo uso.
                if (reflector.IsLocal) MirrorShield.Clear(reflector);
            }
        }
        float floating = parts.Length > 3 &&
                         float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var seconds)
            ? seconds
            : 0f;

        float radius = parts.Length > 2 &&
                       float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture,
                                      out var parsed) && parsed > 0f
            ? parsed
            : Plugin.CfgKnockbackRadius.Value;

        // El disparo suena dentro del RPC, así que lo oyen TODOS los clientes y se
        // posiciona en 3D donde salió el tiro.
        var clip = sound.Length > 0 ? Plugin.FindClip(sound) : ShotClip();
        if (clip != null) AudioSource.PlayClipAtPoint(clip, origin, volume);

        SpawnEffect(Plugin.MuzzleFlashPrefab, origin, Quaternion.identity,
                    Plugin.CfgMuzzleFlashScale.Value, Plugin.CfgMuzzleFlashLifetime.Value);

        if (viewId != -1)
        {
            // Sangre en el punto de impacto, orientada hacia quien disparó. Va fuera del
            // check de IsMine: la tiene que ver todo el mundo, no solo la víctima.
            var towardsShooter = origin - endpoint;
            var bloodRotation = towardsShooter.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(towardsShooter.normalized)
                : Quaternion.identity;

            var impact = effect.Length > 0 ? Plugin.FindPrefab(effect) : Plugin.BloodPrefab;
            SpawnEffect(impact, endpoint, bloodRotation,
                        Plugin.CfgBloodScale.Value, Plugin.CfgBloodLifetime.Value);

            // En un intercambio los dos jugadores cambian de sitio, así que el destello
            // tiene que salir también en el tirador. 'swapPositions' es un campo del
            // componente y viaja serializado, así que aquí vale en todos los clientes sin
            // mandarlo por el RPC.
            if (swapPositions && character != null)
            {
                SpawnEffect(impact, character.Center, Quaternion.identity,
                            Plugin.CfgBloodScale.Value, Plugin.CfgBloodLifetime.Value);
            }

            var view = PhotonNetwork.GetPhotonView(viewId);
            var hitCharacter = view != null ? view.GetComponent<Character>() : null;

            // El daño y el empujón los lanza SOLO el cliente de la víctima. AddForceAtPosition
            // ya hace por dentro un RPC a todos, así que si lo llamaran los cuatro clientes
            // el impulso se aplicaría cuatro veces y saldría disparado.
            // El aura la ve TODO el mundo; la ingravidez solo la aplica el cliente de la
            // víctima, porque AddAffliction descarta lo que no sea su propio personaje.
            if (hitCharacter != null && floating > 0f)
                AntiGravity.Apply(hitCharacter, floating, aura, hitCharacter.photonView.IsMine);

            if (hitCharacter != null && hitCharacter.photonView.IsMine)
            {
                hitCharacter.refs.afflictions.AddStatus(
                    CharacterAfflictions.STATUSTYPE.Injury, injury, fromRPC: false);

                var push = (endpoint - origin).normalized + Vector3.up * Plugin.CfgKnockbackUp.Value;
                hitCharacter.AddForceAtPosition(
                    push.normalized * pushForce, endpoint, radius);

                Plugin.Log.LogInfo($"Impacto: +{injury:0.00} de Injury y empujón de " +
                                   $"{pushForce:0} en {endpoint}.");
            }
        }

        // Esto sí en todas las máquinas: el feedback lo ve y oye todo el mundo.
        if (GamefeelHandler.instance != null)
            GamefeelHandler.instance.AddPerlinShakeProximity(endpoint, 4f);
    }
}

/// <summary>
/// Munición del arma, guardada por instancia y sincronizada por red.
/// </summary>
/// <remarks>
/// Hereda de <c>ModItemComponent</c> de PEAKLib, que serializa el estado a JSON y lo
/// replica. Así la munición viaja CON el arma: si la sueltas y la coge otro, se la
/// encuentra tal y como la dejaste, en vez de tener cada cliente su propia cuenta.
/// </remarks>
internal class PistolAmmo : PEAKLib.Items.ModItemComponent
{
    internal class Data
    {
        public int remaining = -1;   // -1 = recién creada, hay que inicializarla
    }

    /// Public para que Unity lo serialice y sobreviva al Instantiate del prefab.
    public int MaxAmmo = 1;

    void Start()
    {
        var data = Read();
        if (data.remaining < 0) Write(MaxAmmo);

        ReportModel();
    }

    /// <summary>
    /// Vuelca el estado real del modelo en el arma ya spawneada.
    /// </summary>
    /// <remarks>
    /// El arma sale invisible en la mano y hay varias explicaciones posibles (renderer
    /// apagado, escala minúscula, posición dentro de la cámara). En vez de probar
    /// arreglos a ciegas, medimos: esto dice si el modelo existe, si está encendido,
    /// dónde está y qué tamaño ocupa de verdad.
    /// </remarks>
    void ReportModel()
    {
        var model = transform.Find("WeaponModel");
        if (model == null)
        {
            Plugin.Log.LogWarning("[arma] no hay hijo 'WeaponModel' en el objeto spawneado.");
            return;
        }

        var renderers = model.GetComponentsInChildren<Renderer>(true);
        int on = renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy);

        var bounds = renderers.Length > 0 ? renderers[0].bounds : default;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Plugin.Log.LogInfo(
            $"[arma] modelo: {renderers.Length} renderers ({on} activos), " +
            $"escala local {model.localScale.x:0.000}, " +
            $"posLocal {model.localPosition}, posMundo {model.position}, " +
            $"tamaño {bounds.size}, activo={model.gameObject.activeInHierarchy}");

        // Y los del item base, que deberían estar TODOS apagados.
        var baseOn = GetComponentsInChildren<Renderer>(true)
            .Where(r => r.transform.root == transform.root &&
                        r.GetComponentInParent<Transform>() != null &&
                        !r.transform.IsChildOf(model))
            .Count(r => r.enabled);

        Plugin.Log.LogInfo($"[arma] renderers del item base todavía encendidos: {baseOn}");
    }

    /// <summary>Gasta una bala. Devuelve false si estaba vacía.</summary>
    internal bool TryConsume()
    {
        var data = Read();
        int left = data.remaining < 0 ? MaxAmmo : data.remaining;
        if (left <= 0) return false;

        int remaining = left - 1;
        Write(remaining);
        Plugin.Log.LogInfo($"Munición restante: {remaining}/{MaxAmmo}");

        if (remaining <= 0 && Plugin.CfgWeaponDestroyWhenEmpty.Value && item != null)
        {
            // Misma vía que usa el juego para gastar un consumible. Es justo el
            // Action_Consume que le quité al item base: allí saltaba al PRIMER uso
            // ignorando la munición, aquí lo llamamos nosotros cuando toca.
            item.StartCoroutine(item.ConsumeDelayed());
            Plugin.Log.LogInfo("Arma vacía: se destruye.");
        }

        return true;
    }

    internal int Remaining
    {
        get
        {
            var data = Read();
            return data.remaining < 0 ? MaxAmmo : data.remaining;
        }
    }

    Data Read() => TryGetModItemDataFromJson<Data>(out var data) ? data : new Data();

    void Write(int remaining)
    {
        SetModItemDataFromJson(new Data { remaining = remaining });

        // La barra de uso solo se toca si el item la declara (totalUses > 0). En el
        // nuestro vale -1 a propósito, porque activarla rompe Item.Start(); dejamos la
        // comprobación por si algún día partimos de un item que sí la traiga.
        if (item != null && item.totalUses > 0)
            item.SetUseRemainingPercentage((float)remaining / Mathf.Max(1, MaxAmmo));
    }

    public override void OnInstanceDataSet() { }
}
