using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Mantiene el arma apuntando a donde mira el jugador, ignorando el vaivén de la
/// animación de la mano.
/// </summary>
/// <remarks>
/// Mientras se carga el disparo, el Scout mueve el brazo con la animación de uso del
/// item base. El arma va pegada a la mano, así que acaba mirando a un lado mientras la
/// mira sigue en el centro. La trayectoria del tiro SIEMPRE sale de la cámara —eso ya
/// era correcto— pero visualmente confunde muchísimo.
///
/// La solución es desacoplar rotación de posición: el arma sigue colgada de la mano
/// (posición animada, se mueve natural) pero su orientación la marca la dirección de
/// mirada. Así lo que ves apuntar es lo que de verdad va a recibir el disparo.
///
/// Usamos <c>lookDirection</c> del personaje y no la cámara para que funcione también
/// con los jugadores remotos, que no tienen nuestra cámara pero sí su mirada sincronizada.
/// </remarks>
internal class WeaponAim : MonoBehaviour
{
    Transform? _model;
    Item? _item;
    WeaponPivot? _pivot;

    /// Posición "de reposo" de la mano en espacio de cámara, suavizada. Ver <see cref="Sway"/>.
    Vector3 _rest;
    bool _restReady;

    /// Vaivén ya suavizado que se aplica de verdad.
    Vector3 _sway;

    void Awake()
    {
        _item = GetComponent<Item>();
        _model = transform.Find("WeaponModel");
        _pivot = _model != null ? _model.GetComponent<WeaponPivot>() : null;
    }

    /// <remarks>
    /// LateUpdate y no Update: la animación del personaje escribe en los huesos durante
    /// Update, así que si rotáramos antes nos lo pisaría en el mismo frame.
    /// </remarks>
    void LateUpdate()
    {
        if (!Plugin.CfgWeaponAimAlign.Value || _model == null || _item == null) return;

        // Solo cuando alguien lo lleva en la mano; en el suelo debe caer como un objeto.
        var holder = _item.holderCharacter;
        if (holder == null || holder.data == null)
        {
            _restReady = false;          // al volver a cogerlo, que no dé un salto
            return;
        }

        var id = GetComponent<WeaponTag>()?.DefinitionId ?? "";
        var definition = Plugin.FindWeapon(id);

        // Si el panel F3 está abierto sobre ESTA arma, mandan sus valores en vivo.
        bool tuning = WeaponTuner.LiveFor == id && id.Length > 0;
        var offsetValue = tuning ? WeaponTuner.LiveOffset : (definition?.Offset.Value ?? Vector3.zero);
        var rotationValue = tuning ? WeaponTuner.LiveRotation : (definition?.Rotation.Value ?? Vector3.zero);

        var extraRotation = Quaternion.Euler(rotationValue);

        var camera = MainCamera.instance;

        if (holder.IsLocal && Plugin.CfgWeaponInHand.Value)
        {
            // RÍGIDA EN LA MANO: no tocamos nada. El modelo es hijo del item, y el item ya
            // cuelga de la mano del Scout, así que PlaceModel le dejó su offset local y con
            // eso basta: se mueve exactamente con el brazo, ni más ni menos.
            //
            // Todo lo de abajo —anclar a la cámara y calcular vaivén— existía para que la
            // animación de carga no levantara el arma hasta taparte la pantalla. Pero
            // sustituir el movimiento del brazo por uno calculado siempre iba a delatarse:
            // por muy bien filtrado que esté, no es el movimiento de la mano, y se nota que
            // el arma va por libre. Dejarla pegada al hueso es lo único que no puede
            // desincronizarse, porque no hay dos movimientos que sincronizar.
            //
            // El tiro sigue saliendo de la CÁMARA, así que da donde apunta la mira aunque
            // el cañón mire a otro lado mientras se carga.
            return;
        }

        if (holder.IsLocal && camera != null)
        {
            // Para quien la lleva, el arma se ancla a la CÁMARA, no a la mano. La mano
            // sube y baja con la animación de carga del item base y llegaba a taparte
            // media pantalla. Anclada a la vista se queda quieta, como en cualquier FPS.
            //
            // Se descuenta la compensación del pivote, exactamente igual que en
            // PlaceModel. Sin esto el offset valdría una cosa al construir el arma y otra
            // al llevarla en la mano, y era justo lo que descolocaba los valores
            // calibrados: el arma se iba tanto como despiste tuviera el pivote del .fbx,
            // que además crece con la escala (por eso el Pistolón se iba mucho más).
            var compensation = _pivot != null ? _pivot.Compensation : Vector3.zero;

            _model.position = camera.transform.position
                            + camera.transform.rotation * (offsetValue - compensation + Sway(camera));
            _model.rotation = camera.transform.rotation * extraRotation;
            return;
        }

        // Para los remotos se queda en la mano (desde fuera se ve natural) y solo
        // enderezamos la orientación hacia donde miran.
        var look = holder.data.lookDirection;
        if (look.sqrMagnitude < 0.001f) return;

        _model.rotation = Quaternion.LookRotation(look.normalized, Vector3.up) * extraRotation;
    }

    /// <summary>
    /// Devuelve el balanceo de la mano que queremos que el arma acompañe.
    /// </summary>
    /// <remarks>
    /// Anclar el arma a la cámara arregló que apuntara mal, pero la dejó CLAVADA: al
    /// correr o saltar las manos se balancean y el arma no, y se ve como si flotara
    /// aparte del cuerpo.
    ///
    /// Devolverle el movimiento entero de la mano nos llevaría de vuelta al problema
    /// original, porque el tirón grande de la animación de carga volvería a taparte la
    /// pantalla. Así que nos quedamos solo con la parte RÁPIDA del movimiento: mantenemos
    /// una media suavizada de dónde está la mano (su "reposo") y usamos únicamente cuánto
    /// se aparta de ella. El balanceo de correr oscila y sobrevive al filtro; un
    /// desplazamiento sostenido se absorbe en el reposo y desaparece solo.
    ///
    /// El tope de <c>SwayMax</c> es la red de seguridad: distingue por TAMAÑO lo que el
    /// filtro no distingue por frecuencia. El balanceo al correr son pocos centímetros;
    /// el brazo levantándose para disparar, mucho más. Así el arma respira con el cuerpo
    /// pero nunca se sale de sitio.
    ///
    /// Se trabaja en espacio de cámara para que girar la vista no cuente como movimiento
    /// de la mano; si no, mirar alrededor sacudiría el arma.
    /// </remarks>
    Vector3 Sway(MainCamera camera)
    {
        float amount = Plugin.CfgWeaponSway.Value;
        if (amount <= 0f) return Vector3.zero;

        // transform es la RAÍZ del item, que sí sigue colgada de la mano; el que
        // reposicionamos cada frame es el hijo "WeaponModel". Por eso aquí seguimos
        // teniendo el movimiento real del brazo, sin habérnoslo pisado.
        var hand = camera.transform.InverseTransformPoint(transform.position);

        if (!_restReady)
        {
            _rest = hand;
            _restReady = true;
            return Vector3.zero;
        }

        // Suavizado exponencial expresado en segundos, para que no dependa de los FPS:
        // con un Lerp de factor fijo el arma se movería distinto a 60 que a 144.
        float tau = Mathf.Max(0.01f, Plugin.CfgWeaponSwaySmoothing.Value);
        _rest = Vector3.Lerp(_rest, hand, 1f - Mathf.Exp(-Time.deltaTime / tau));

        var raw = Vector3.ClampMagnitude((hand - _rest) * amount, Plugin.CfgWeaponSwayMax.Value);

        // Y ahora se suaviza la SALIDA. Quedarse solo con lo de arriba dejaba el arma
        // dando botes al correr: la mano del Scout no la mueve una animación limpia, es un
        // ragdoll colgado de joints, así que su posición viene con temblor de física. Un
        // filtro que se queda con la parte rápida del movimiento se queda precisamente con
        // ese temblor y lo amplifica.
        //
        // Con las dos etapas queda un pasa-banda: la lenta descarta que el brazo se
        // levante para disparar, la rápida descarta la vibración, y en medio sobrevive el
        // balanceo de correr, que es lo único que queríamos.
        float outTau = Mathf.Max(0.005f, Plugin.CfgWeaponSwayDamping.Value);
        _sway = Vector3.Lerp(_sway, raw, 1f - Mathf.Exp(-Time.deltaTime / outTau));

        return _sway;
    }
}
