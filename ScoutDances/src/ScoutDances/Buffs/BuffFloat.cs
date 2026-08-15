using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Mantiene la caja derecha, a la altura del pecho y girando en horizontal.
/// </summary>
/// <remarks>
/// <b>Por qué hace falta.</b> El power-up es un clon de un item del juego, y un item del
/// juego es un objeto físico: nace, cae, rueda y se queda tumbado donde pare. Por eso las
/// cajas salían torcidas y pegadas al suelo aunque el modelo trajera su propia animación de
/// giro — la animación giraba una caja que ya estaba de lado.
///
/// <b>Se congela la física en vez de pelearse con ella.</b> El <c>Rigidbody</c> pasa a
/// cinemático: deja de caer, deja de rodar y deja de reaccionar a golpes. No se pierde nada,
/// porque estos objetos no se recogen chocando con ellos sino por distancia.
///
/// <b>La altura se mide desde el suelo, no desde donde nació.</b> Se lanza un rayo hacia
/// abajo al colocarla; si nace dentro de una cuesta o flotando, igual acaba a la altura
/// pedida sobre el terreno real.
///
/// <b>El giro se impone sobre el del modelo.</b> El prefab de Epic Toon FX trae un Animator
/// que lo rota en varios ejes; aquí se fija la inclinación a cero y se gira solo en Y, que
/// es el «girar horizontal» que se pidió.
/// </remarks>
internal class BuffFloat : MonoBehaviour
{
    float _angle;
    bool _settled;

    void Start()
    {
        Freeze();
        Settle();
    }

    /// <summary>Quita la física para que no ruede ni se caiga.</summary>
    void Freeze()
    {
        foreach (var body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>La deja a la altura pedida sobre el suelo que tenga debajo.</summary>
    void Settle()
    {
        var position = transform.position;

        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var ground, 40f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            position.y = ground.point.y + Plugin.CfgBuffFloatHeight.Value;
            _settled = true;
        }
        else
        {
            // Sin suelo debajo se queda donde está: subirla a ciegas la metería dentro de
            // un techo o la dejaría flotando en el vacío.
            _settled = false;
        }

        transform.position = position;
    }

    void LateUpdate()
    {
        // En LateUpdate para pisar al Animator del prefab, que rota en varios ejes: si se
        // hiciera en Update, su animación se aplicaría después y volvería a torcerla.
        _angle += Plugin.CfgBuffSpinSpeed.Value * Time.deltaTime;
        if (_angle > 360f) _angle -= 360f;

        transform.rotation = Quaternion.Euler(0f, _angle, 0f);

        // Un vaivén suave para que se note que es un objeto a recoger y no decorado.
        if (_settled && Plugin.CfgBuffBob.Value > 0f)
        {
            var position = transform.position;
            position.y += Mathf.Sin(Time.time * 2f) * Plugin.CfgBuffBob.Value * Time.deltaTime;
            transform.position = position;
        }
    }
}
