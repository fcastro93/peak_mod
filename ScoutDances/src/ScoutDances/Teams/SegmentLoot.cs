using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Las maletas del tramo en el que estamos AHORA, no las del anterior.
/// </summary>
/// <remarks>
/// <b>El fallo que arregla.</b> Las cajas y los power-ups se colocaban junto a las maletas
/// encontradas con <c>FindObjectsByType&lt;Luggage&gt;</c>, sin comprobar de qué tramo eran.
/// Al encender la hoguera y avanzar, mirábamos demasiado pronto: el tramo nuevo todavía no
/// tenía sus maletas colocadas y seguíamos viendo las del anterior. Todo se repartía en la
/// zona que acabábamos de dejar atrás.
///
/// En el log se veía sin querer: el tramo 0 y el tramo 1 daban <b>exactamente 26 maletas</b>
/// los dos. Dos tramos distintos no traen el mismo número por casualidad; era el mismo
/// conjunto contado dos veces. Y por eso todo aparecía correctamente colocado según el
/// registro y no había nada en el mapa: estaba en el tramo apagado.
///
/// <b>Cómo se sabe.</b> <c>MapHandler.CurrentMapSegment</c> da el tramo activo y su
/// <c>segmentParent</c> es el objeto que cuelga de la escena; una maleta pertenece a este
/// tramo si está por debajo de él. Filtrar por eso es exacto y no depende de esperar el
/// tiempo justo, que era lo frágil del enfoque anterior.
/// </remarks>
internal static class SegmentLoot
{
    /// <summary>Maletas del tramo actual. Vacío si aún no están puestas.</summary>
    internal static List<Luggage> Current()
    {
        var all = Object.FindObjectsByType<Luggage>(FindObjectsSortMode.None)
                        .Where(l => l != null && l.GetComponent<RespawnChest>() == null)
                        .ToList();

        var root = SegmentRoot();
        if (root == null) return all;      // sin dato, mejor lo de siempre que nada

        var mine = all.Where(l => l.transform.IsChildOf(root)).ToList();

        // Si el tramo aún no tiene maletas, se devuelve VACÍO a propósito para que quien
        // llame vuelva a intentarlo más tarde. Devolver las del tramo viejo es justo el
        // error que esto viene a evitar.
        return mine;
    }

    static Transform? SegmentRoot()
    {
        try
        {
            var segment = MapHandler.CurrentMapSegment;
            var parent = segment?.segmentParent;

            return parent != null ? parent.transform : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Número del tramo actual, o int.MinValue si no se puede saber.</summary>
    internal static int Number()
    {
        try
        {
            return (int)MapHandler.CurrentSegmentNumber;
        }
        catch
        {
            return int.MinValue;
        }
    }
}
