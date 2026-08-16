using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Reparte cosas por el tramo sin que se amontonen unas encima de otras.
/// </summary>
/// <remarks>
/// <b>El problema que resuelve.</b> Las cajas y los power-ups se colocaban pegados a una
/// maleta, porque las maletas son terreno que el juego ya ha validado como alcanzable. Pero
/// eso hacía que los tres aparecieran siempre juntos: encontrabas un montón con todo y
/// media montaña sin nada. Recogías tres cosas de una sentada y luego subías veinte minutos
/// en seco.
///
/// <b>Cómo se separa sin perder esa garantía.</b> En vez de colocar JUNTO a una maleta, se
/// coloca ENTRE dos maletas elegidas al azar, en un punto cualquiera del camino que las une
/// y desplazado a un lado. Las maletas están repartidas por donde de verdad se pasa, así que
/// el espacio entre ellas también lo está; y como el punto no es ninguna de las dos, no
/// hereda su posición.
///
/// <b>Y se comprueba, no se supone.</b> Cada candidato pasa por
/// <see cref="TeamSpawns.TryGround"/>, el mismo que usan las salidas de equipo: que haya
/// suelo, que no sea agua y que no esté ocupado. Además se exige una distancia mínima a las
/// maletas y a lo ya colocado, que es lo que impide que se vuelvan a juntar.
///
/// Si tras varios intentos no encuentra sitio para uno, lo deja pasar: es mejor colocar
/// dieciocho bien repartidos que veinte con dos dentro de una roca.
/// </remarks>
internal static class Scatter
{
    /// Intentos por objeto antes de rendirse con ese.
    const int Tries = 14;

    /// <summary>
    /// Puntos repartidos por el tramo, lejos de las maletas y entre sí.
    /// </summary>
    /// <param name="anchors">Maletas del tramo, que marcan por dónde se pasa.</param>
    /// <param name="count">Cuántos puntos hacen falta.</param>
    /// <param name="minFromAnchor">Metros mínimos a cualquier maleta.</param>
    /// <param name="minApart">Metros mínimos entre dos puntos de esta tanda.</param>
    internal static List<Vector3> Points(List<Vector3> anchors, int count,
                                         float minFromAnchor, float minApart)
    {
        var placed = new List<Vector3>();
        if (anchors == null || anchors.Count == 0 || count <= 0) return placed;

        float anchorSq = minFromAnchor * minFromAnchor;
        float apartSq = minApart * minApart;

        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < Tries; attempt++)
            {
                var candidate = Between(anchors);

                if (!TeamSpawns.TryGround(candidate, out var spot)) continue;

                if (anchors.Any(a => (a - spot).sqrMagnitude < anchorSq)) continue;
                if (placed.Any(p => (p - spot).sqrMagnitude < apartSq)) continue;

                placed.Add(spot);
                break;
            }
        }

        return placed;
    }

    /// <summary>Un punto del camino entre dos maletas, desplazado a un lado.</summary>
    /// <remarks>
    /// Con una sola maleta no hay camino que recorrer, así que se sale en una dirección
    /// cualquiera. Con dos o más se interpola, que es lo que reparte de verdad: el resultado
    /// cae en la franja por la que se sube, no en un corro alrededor de un punto.
    /// </remarks>
    static Vector3 Between(List<Vector3> anchors)
    {
        var from = anchors[Random.Range(0, anchors.Count)];

        if (anchors.Count == 1)
            return from + Random.insideUnitSphere.With(y: 0f).normalized * Random.Range(12f, 40f);

        Vector3 to;
        int guard = 0;
        do { to = anchors[Random.Range(0, anchors.Count)]; }
        while (to == from && ++guard < 8);

        var point = Vector3.Lerp(from, to, Random.Range(0.2f, 0.8f));

        // Un paso a un lado del camino recto, para no dejarlos todos en línea entre maletas.
        var side = Vector3.Cross((to - from).With(y: 0f).normalized, Vector3.up);
        point += side * Random.Range(-8f, 8f);

        return point;
    }
}
