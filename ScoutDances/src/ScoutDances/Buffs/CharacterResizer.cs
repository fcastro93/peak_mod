using System.Collections.Generic;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Cambia el tamaño REAL de un Scout: lo que se ve y lo que choca.
/// </summary>
/// <remarks>
/// PEAK no tiene ninguna API para esto. El Scout es un ragdoll completo: cada
/// <c>Bodypart</c> lleva su <c>Rigidbody</c>, sus colliders y un <c>ConfigurableJoint</c>
/// que lo cuelga del anterior. Escalar solo la malla es fácil pero engaña — la hitbox se
/// queda igual — así que hay que mover la física.
///
/// <b>La clave está en cómo Unity trata los anclajes.</b> Los <c>anchor</c> de un joint
/// viven en espacio LOCAL, y Unity los multiplica por el <c>lossyScale</c> del transform.
/// Así que basta escalar el transform raíz del ragdoll para que, de una sola vez:
///
/// <list type="bullet">
/// <item>los colliders de todas las partes cambien de tamaño (hitbox real), y</item>
/// <item>los anclajes de todos los joints se acerquen o se separen, con lo que los
///       drives del propio juego arrastran el cuerpo hacia la nueva postura de reposo.</item>
/// </list>
///
/// Es decir: no hay que recalcular hueso por hueso, hay que dejar que el sistema de
/// joints que ya existe haga el trabajo con las distancias nuevas.
///
/// <b>Lo que sí hay que hacer a mano</b> es acercar los <c>Rigidbody</c> al centro en el
/// momento del cambio. Los cuerpos se simulan en espacio de mundo y NO los mueve el
/// escalado del padre, así que sin esto el cuerpo se queda desperdigado con las partes
/// pequeñas y los joints tiran de golpe: un latigazo que manda al Scout por los aires.
/// Reposicionar y poner las velocidades a cero convierte ese salto en un cambio limpio.
///
/// <b>Las masas no se tocan.</b> El movimiento usa <c>ForceMode.Acceleration</c>, que es
/// independiente de la masa, así que dejarlas igual mantiene al personaje controlable en
/// los dos tamaños. Escalarlas por el cubo sería más "realista" y haría al pequeño
/// ingobernable.
/// </remarks>
internal class CharacterResizer : MonoBehaviour
{
    Character? _character;

    /// Escala aplicada ahora mismo. 1 = tamaño de fábrica.
    float _current = 1f;

    /// Escalas originales de los transforms que tocamos, para restaurar EXACTO.
    readonly List<(Transform transform, Vector3 scale)> _originals = new();

    /// Estado original de cada joint. Guardarlo es obligatorio: le desactivamos el
    /// autoconfigurado y hay que devolvérselo tal cual estaba.
    readonly List<(ConfigurableJoint joint, Vector3 anchor, Vector3 connected, bool auto)> _joints = new();

    internal float Current => _current;

    /// Cuándo hay que devolverlo a su tamaño. 0 = no hay nada puesto.
    float _until;

    /// <summary>Deja al personaje a la escala pedida durante unos segundos.</summary>
    /// <remarks>
    /// El temporizador vive AQUÍ y no en <see cref="PlayerBuff"/> a propósito. El tamaño
    /// se aplica en todos los clientes —cambia colliders, y cada máquina necesita la misma
    /// hitbox— mientras que PlayerBuff solo existe en el del dueño del personaje. Si la
    /// vuelta atrás dependiera de él, en las demás pantallas el jugador se quedaría
    /// encogido para siempre.
    /// </remarks>
    internal static void Apply(Character character, float scale, float seconds)
    {
        if (character == null) return;

        var resizer = character.GetComponent<CharacterResizer>();
        if (resizer == null) resizer = character.gameObject.AddComponent<CharacterResizer>();

        resizer.Resize(character, scale);
        resizer._until = seconds > 0f ? Time.time + seconds : 0f;
    }

    void Update()
    {
        if (_until <= 0f) return;

        // Al morir también se deshace: el juego reinicia cosas por dentro y quedarnos con
        // los anclajes tocados sobre un ragdoll reiniciado no puede acabar bien.
        bool dead = _character == null || _character.data == null || _character.data.dead;

        if (dead || Time.time >= _until)
        {
            _until = 0f;
            Reset();
        }
    }

    void Resize(Character character, float scale)
    {
        _character = character;
        scale = Mathf.Clamp(scale, 0.15f, 5f);

        if (Mathf.Approximately(scale, _current)) return;

        var roots = ScaleRoots(character);
        if (roots.Count == 0)
        {
            Plugin.Log.LogWarning("No encontré la raíz del ragdoll; no puedo redimensionar.");
            return;
        }

        // La primera vez guardamos las escalas de fábrica. Se guardan UNA sola vez: si
        // reescalamos estando ya escalados, lo que hay puesto no es el original.
        if (_originals.Count == 0)
            foreach (var root in roots)
                _originals.Add((root, root.localScale));

        foreach (var (transform, original) in _originals)
            if (transform != null) transform.localScale = original * scale;

        ScaleJoints(character, scale);
        CompactBodies(character, scale / _current);

        _current = scale;
        Plugin.Log.LogInfo($"Escala del personaje -> x{scale:0.00} " +
                           $"({_originals.Count} transform(s), hitbox incluida).");
    }

    /// <summary>Devuelve al tamaño de fábrica.</summary>
    internal void Reset()
    {
        if (Mathf.Approximately(_current, 1f)) return;

        foreach (var (transform, original) in _originals)
            if (transform != null) transform.localScale = original;

        foreach (var (joint, anchor, connected, auto) in _joints)
        {
            if (joint == null) continue;
            joint.anchor = anchor;
            joint.connectedAnchor = connected;
            joint.autoConfigureConnectedAnchor = auto;
        }

        if (_character != null) CompactBodies(_character, 1f / _current);

        _current = 1f;
    }

    /// <summary>
    /// Escala los anclajes de los joints del ragdoll.
    /// </summary>
    /// <remarks>
    /// Esto es lo que hace que el cuerpo encoja de VERDAD en vez de deformarse. Las partes
    /// del ragdoll se recolocan cada frame desde sus Rigidbody, así que el cuerpo solo se
    /// hace pequeño si los joints tiran de ellas más cerca unas de otras — y esa distancia
    /// es justo lo que dicen los anclajes.
    ///
    /// El problema es <c>autoConfigureConnectedAnchor</c>: con él activo Unity recalcula
    /// el ancla del otro extremo a partir de dónde están los cuerpos AHORA, lo que anula
    /// nuestro escalado. Quedaba un lado del joint escalado y el otro no, y el resultado
    /// era el Scout con los miembros estirados. Hay que apagarlo y poner las dos puntas a
    /// mano.
    /// </remarks>
    void ScaleJoints(Character character, float scale)
    {
        var ragdoll = character.refs?.ragdoll;
        if (ragdoll == null) return;

        if (_joints.Count == 0)
        {
            foreach (var joint in ragdoll.GetComponentsInChildren<ConfigurableJoint>(true))
            {
                if (joint == null) continue;

                // OJO al orden: leer connectedAnchor con el autoconfigurado puesto da el
                // valor que Unity calculó, que es el bueno de fábrica. Se guarda ANTES de
                // apagarlo.
                _joints.Add((joint, joint.anchor, joint.connectedAnchor,
                             joint.autoConfigureConnectedAnchor));
            }
        }

        foreach (var (joint, anchor, connected, _) in _joints)
        {
            if (joint == null) continue;

            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = anchor * scale;
            joint.connectedAnchor = connected * scale;
        }
    }

    /// <summary>
    /// Transforms cuya escala hay que tocar, sin solaparse.
    /// </summary>
    /// <remarks>
    /// El ragdoll y el rig visual pueden estar anidados o ser hermanos según cómo esté
    /// montado el prefab. Si escaláramos los dos estando anidados, el hijo saldría al
    /// cuadrado (x4 en vez de x2). Por eso descartamos cualquier candidato que ya cuelgue
    /// de otro de la lista.
    /// </remarks>
    static List<Transform> ScaleRoots(Character character)
    {
        var candidates = new List<Transform>();

        void Add(Transform? t)
        {
            if (t != null && !candidates.Contains(t)) candidates.Add(t);
        }

        Add(character.refs?.ragdoll != null ? character.refs.ragdoll.transform : null);
        Add(character.refs?.rigCreator != null ? character.refs.rigCreator.transform : null);
        Add(character.refs?.animator != null ? character.refs.animator.transform : null);

        var roots = new List<Transform>();
        foreach (var candidate in candidates)
        {
            bool nested = false;
            foreach (var other in candidates)
                if (other != candidate && candidate.IsChildOf(other)) { nested = true; break; }

            if (!nested) roots.Add(candidate);
        }

        return roots;
    }

    /// <summary>
    /// Acerca (o separa) los cuerpos rígidos respecto al centro del personaje.
    /// </summary>
    /// <remarks>
    /// Los Rigidbody se simulan en mundo: escalar el transform padre les cambia los
    /// colliders y los anclajes, pero no los mueve. Sin este paso el cuerpo queda con
    /// las piezas donde estaban y los joints tiran de golpe hacia una postura mucho más
    /// pequeña, que es exactamente cómo se dispara un ragdoll por los aires.
    ///
    /// Las velocidades a cero por lo mismo: reposicionar sin limpiarlas conserva el
    /// impulso que llevaban a la escala anterior.
    /// </remarks>
    static void CompactBodies(Character character, float factor)
    {
        var ragdoll = character.refs?.ragdoll;
        if (ragdoll == null || Mathf.Approximately(factor, 1f)) return;

        // La cadera es el ancla natural del cuerpo: encoger hacia ella deja al personaje
        // de pie donde estaba, mientras que hacerlo hacia el torso lo hunde en el suelo.
        var pivot = character.refs?.hip != null
            ? character.refs.hip.Rig.position
            : character.Center;

        foreach (var part in ragdoll.partList)
        {
            if (part == null) continue;

            var rig = part.Rig;
            if (rig == null || rig.isKinematic) continue;

            rig.position = pivot + (rig.position - pivot) * factor;
            rig.linearVelocity = Vector3.zero;
            rig.angularVelocity = Vector3.zero;
        }
    }

    /// <remarks>
    /// Imprescindible: si nos destruyen con el personaje encogido y no restauramos, se
    /// queda deforme hasta que reinicie el juego.
    /// </remarks>
    void OnDestroy() => Reset();
}
