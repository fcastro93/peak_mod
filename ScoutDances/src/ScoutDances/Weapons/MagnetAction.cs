using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Imán: mientras mantienes el disparo, arrastra hacia ti a quien caiga en el cono.
/// </summary>
/// <remarks>
/// <b>Es un cono, no un rayo.</b> Las demás armas del mod trazan una línea y aciertan a
/// uno; esta afecta a todo el que quede dentro del chorro, que es lo que la hace legible:
/// lo que ves dibujado es lo que te va a arrastrar. Por eso el alcance y el ángulo salen
/// del config y hay que dejarlos parecidos al efecto visual.
///
/// <b>Un solo componente para encender, tirar y apagar.</b> <c>ItemAction</c> encamina
/// todos sus eventos al mismo <c>RunAction()</c>, así que no se puede distinguir "empezó a
/// disparar" de "sigue disparando" con banderas. Se resuelve al revés: <c>RunAction</c>
/// marca la hora de la última vez que llegó, y un <c>Update</c> detecta que dejó de llegar
/// y apaga. Sin eso, soltar el gatillo dejaría el chorro y el zumbido encendidos para
/// siempre.
///
/// <b>El tirón lo lanza el cliente del tirador.</b> <c>AddForceAtPosition</c> ya reparte
/// el impulso a todos por dentro; si lo mandara cada cliente se multiplicaría por el
/// número de jugadores. Va limitado a unas pocas veces por segundo: a 60 fps serían 60
/// mensajes de red por segundo y por víctima, y la sensación de arrastre no mejora nada.
/// </remarks>
internal class MagnetAction : ItemAction
{
    // PUBLIC a propósito: Unity solo copia los campos serializados al instanciar.

    /// Hasta dónde llega el chorro, en metros.
    public float range = 18f;

    /// Apertura del cono, en grados desde el centro.
    public float halfAngle = 22f;

    /// Fuerza del tirón en cada pulso.
    public float pullForce = 130f;

    /// Cuántas veces por segundo se tira.
    public float pullsPerSecond = 8f;

    /// Segundos de uso que da cada punto de durabilidad.
    public float secondsPerCharge = 1f;

    public string beamEffect = "";
    public string humSound = "";

    /// Hasta dónde debe llegar el chorro, en metros. 0 = usar el alcance del tirón.
    public float beamLength;

    bool _active;
    float _lastHeld;
    float _nextPull;
    float _nextDrain;

    GameObject? _beam;
    AudioSource? _hum;

    public override void RunAction()
    {
        if (!_active && !Begin()) return;

        _lastHeld = Time.time;

        // La durabilidad se va sola con el tiempo, no de golpe al apretar: el arma se
        // gasta según cuánto la uses, que es lo que uno espera de un chorro continuo.
        if (Time.time >= _nextDrain && !Drain()) return;

        if (Time.time < _nextPull) return;

        _nextPull = Time.time + 1f / Mathf.Max(1f, pullsPerSecond);
        Pull();
    }

    /// <summary>Enciende el imán. Devuelve false si ya no le queda nada.</summary>
    bool Begin()
    {
        if (!Drain()) return false;

        _active = true;
        photonView.RPC(nameof(RPC_Beam), RpcTarget.All, true);
        return true;
    }

    /// <summary>Descuenta un punto de durabilidad. False si se acabó.</summary>
    bool Drain()
    {
        var ammo = GetComponent<PistolAmmo>();
        if (ammo != null && !ammo.TryConsume())
        {
            Plugin.Log.LogInfo("Imán: agotado.");
            End();
            return false;
        }

        _nextDrain = Time.time + Mathf.Max(0.1f, secondsPerCharge);
        return true;
    }

    /// <remarks>
    /// Aquí está el truco del apagado: si <c>RunAction</c> lleva un par de frames sin
    /// llegar es que soltaste el gatillo, porque mientras lo mantienes llega cada frame.
    /// </remarks>
    void Update()
    {
        if (!_active) return;

        if (Time.time - _lastHeld > 0.2f) End();
    }

    void End()
    {
        if (!_active) return;
        _active = false;

        photonView.RPC(nameof(RPC_Beam), RpcTarget.All, false);
    }

    void Pull()
    {
        var eye = MainCamera.instance;
        if (eye == null || character == null) return;

        var origin = eye.transform.position;
        var forward = eye.transform.forward;
        float cosLimit = Mathf.Cos(halfAngle * Mathf.Deg2Rad);

        foreach (var victim in Character.AllCharacters)
        {
            if (victim == null || victim == character) continue;
            if (victim.data == null || victim.data.dead) continue;

            var toVictim = victim.Center - origin;
            float distance = toVictim.magnitude;
            if (distance > range || distance < 0.5f) continue;

            // Dentro del cono: el coseno del ángulo entre la mirada y el objetivo tiene
            // que superar el del borde. Es la misma comprobación que un "está en pantalla",
            // pero contra el chorro dibujado.
            if (Vector3.Dot(toVictim / distance, forward) < cosLimit) continue;

            // Hacia el tirador, con un poco de elevación: un tirón horizontal contra el
            // suelo se lo come el rozamiento y apenas se nota.
            var pull = (origin - victim.Center).normalized + Vector3.up * 0.25f;

            victim.AddForceAtPosition(pull.normalized * pullForce, victim.Center,
                                      Plugin.CfgKnockbackRadius.Value);
        }
    }

    /// <summary>Enciende o apaga el chorro y el zumbido en todos los clientes.</summary>
    [PunRPC]
    void RPC_Beam(bool on)
    {
        if (!on)
        {
            if (_beam != null) Destroy(_beam);
            _beam = null;

            if (_hum != null) { _hum.Stop(); Destroy(_hum); }
            _hum = null;
            return;
        }

        var muzzle = transform.Find("Muzzle") ?? transform;

        var prefab = Plugin.FindPrefab(beamEffect);
        if (prefab != null)
        {
            _beam = Instantiate(prefab, muzzle.position, muzzle.rotation, muzzle);

            // El chorro se orienta hacia donde MIRA el jugador, no hacia donde apunta el
            // cañón. El arma va rígida en la mano, así que su rotación es la del brazo y
            // el cono salía disparado de lado. Además el tirón se calcula desde la mirada:
            // si el dibujo apuntara a otro sitio, arrastraría a gente que el cono no toca.
            var aim = _beam.AddComponent<BeamAim>();
            aim.Holder = (GetComponent<Item>())?.holderCharacter;
            aim.Muzzle = muzzle;

            // Sin esto el chorro sale lavado: los materiales de partículas del bundle
            // pierden su mezcla aditiva si se les reenlaza el shader.
            Props.PropBuilder.RebindShaders(_beam);

            // Y en bucle: los efectos de Epic Toon FX son ráfagas de un solo uso, así que
            // el chorro se apagaría al segundo aunque sigas apretando. Igual que con la
            // bola del blaster, se fuerza en runtime para que valga con cualquier efecto.
            foreach (var system in _beam.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;

                // Cada sistema se ajusta para que TODOS lleguen a la misma distancia, en
                // vez de multiplicarles la velocidad por igual. Una partícula llega hasta
                // velocidad x vida, así que la velocidad que hace falta es longitud/vida.
                //
                // El multiplicador común no valía: los cinco sistemas del prefab traen
                // velocidades y vidas muy distintas, y escalarlos todos por cinco dejaba el
                // humo a 176 metros y la llama inicial a 9. Eso no es un cono, son cinco
                // chorros de largos distintos. Fijando el destino en vez del factor, el
                // efecto entero acaba donde acaba el tirón: lo que ves es lo que arrastra.
                float lifetime = Mathf.Max(0.01f, main.startLifetime.constantMax);
                float wanted = beamLength > 0.1f ? beamLength : range;
                float needed = wanted / lifetime;

                var speed = main.startSpeed;
                float original = speed.constantMax;
                float ratio = original > 0.01f ? needed / original : 1f;

                speed.constantMin *= ratio;
                speed.constantMax = needed;
                main.startSpeed = speed;

                system.Play(withChildren: false);

                Plugin.Log.LogInfo(
                    $"[iman] '{system.name}': {original:0.0} -> {needed:0.0} m/s " +
                    $"(vida {lifetime:0.00}s) = alcance {wanted:0.0} m, " +
                    $"igual que el tirón ({range:0.0} m)");
            }
        }

        var clip = Plugin.FindClip(humSound);
        if (clip == null) return;

        // La fuente va EN el arma para que el zumbido acompañe al tirador y se oiga por
        // cercanía, como el proyectil del blaster.
        _hum = gameObject.AddComponent<AudioSource>();
        _hum.clip = clip;
        _hum.loop = true;
        _hum.volume = Plugin.CfgBlasterVolume.Value;
        _hum.spatialBlend = 1f;
        _hum.rolloffMode = AudioRolloffMode.Linear;
        _hum.minDistance = Plugin.CfgOrbSoundNear.Value;
        _hum.maxDistance = Plugin.CfgOrbSoundFar.Value;
        _hum.Play();
    }

    void OnDestroy()
    {
        if (_beam != null) Destroy(_beam);
    }
}

/// <summary>
/// Mantiene el chorro pegado a la boca del arma y mirando a donde mira el jugador.
/// </summary>
/// <remarks>
/// Va en LateUpdate por el mismo motivo que <see cref="WeaponAim"/>: la animación del
/// personaje escribe en los huesos durante Update, así que orientar antes lo pisaría en el
/// mismo frame.
///
/// Se usa <c>lookDirection</c> del personaje y no la cámara para que valga igual en los
/// clientes de los demás, que no tienen nuestra cámara pero sí su mirada sincronizada.
/// </remarks>
internal class BeamAim : MonoBehaviour
{
    internal Character? Holder;
    internal Transform? Muzzle;

    float _nextLog;

    void LateUpdate()
    {
        if (Muzzle != null) transform.position = Muzzle.position;

        var look = Holder?.data?.lookDirection ?? Vector3.zero;
        if (look.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

        Report(look.normalized);
    }

    /// <summary>
    /// Deja en el log dónde está el cono y hacia dónde va, comparado con el jugador.
    /// </summary>
    /// <remarks>
    /// Lo que importa de verdad es el ÁNGULO entre el eje del cono y la mirada: el tirón se
    /// calcula desde la mirada, así que cualquier desviación ahí significa que lo que ves
    /// dibujado no es lo que arrastra. A cero grados van de la mano.
    /// </remarks>
    void Report(Vector3 look)
    {
        if (!Plugin.CfgMagnetDiagnostics.Value || Time.time < _nextLog) return;
        _nextLog = Time.time + 1f;

        var player = Holder != null ? Holder.Center : Vector3.zero;
        var forward = transform.forward;

        float drift = Vector3.Angle(forward, look);
        var offset = transform.position - player;

        Plugin.Log.LogInfo(
            $"[iman] cono en {transform.position} · jugador en {player} · " +
            $"separación {offset.magnitude:0.00} m ({offset})");
        Plugin.Log.LogInfo(
            $"[iman] eje del cono {forward} · mirada {look} · desvío {drift:0.0}°");
    }
}
