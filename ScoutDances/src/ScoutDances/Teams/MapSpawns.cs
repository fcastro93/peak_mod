using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Multiplica las maletas del mapa según cuántos equipos jueguen.
/// </summary>
/// <remarks>
/// Con varios equipos compitiendo por el mismo botín, la cantidad de fábrica se queda
/// corta: el primero que pasa lo vacía todo. Aquí se multiplica el número de intentos de
/// colocación por equipo.
///
/// <b>Lo delicado es que la generación del nivel es determinista.</b> Todos los clientes
/// construyen el mismo mapa a partir de la misma semilla, sin mandarse nada por red; si
/// uno multiplicara y otro no, cada uno vería un mapa distinto. Por eso el factor sale del
/// número de equipos, que Photon ya tiene replicado en las propiedades de los jugadores
/// antes de empezar la partida — y no de nada que decida cada máquina por su cuenta.
///
/// Se toca <c>nrOfSpawns</c>, que son INTENTOS y no maletas garantizadas: el propio
/// generador descarta los que caen en mal sitio. Así se respetan sus restricciones de
/// terreno en vez de forzar objetos donde no caben.
/// </remarks>
[HarmonyPatch(typeof(PropSpawner), nameof(PropSpawner.ExecuteImmediately))]
internal static class LuggageCountPatch
{
    [HarmonyPrefix]
    static void Prefix(PropSpawner __instance) => MapSpawns.Boost(__instance);
}

/// <summary>Misma multiplicación para el camino asíncrono de la generación.</summary>
[HarmonyPatch(typeof(PropSpawner), nameof(PropSpawner.Execute))]
internal static class LuggageCountPatchAsync
{
    [HarmonyPrefix]
    static void Prefix(PropSpawner __instance) => MapSpawns.Boost(__instance);
}

internal class MapSpawns : MonoBehaviour
{
    /// Generadores ya retocados, para no multiplicar dos veces el mismo.
    static readonly HashSet<int> Boosted = new();

    /// Tramo del último reparto de power-ups, solo para que el log lo diga.
    int _lastPlacedSegment;

    /// <summary>Cuántos equipos hay ahora mismo, mínimo 1.</summary>
    internal static int TeamCount => Mathf.Max(1, TeamState.Roster().Count);

    /// <summary>Multiplica los intentos de un generador si es de maletas.</summary>
    internal static void Boost(PropSpawner spawner)
    {
        if (spawner == null || !Plugin.CfgTeams.Value || !Plugin.CfgMoreLoot.Value) return;
        if (!Boosted.Add(spawner.GetInstanceID())) return;
        if (!PlacesLuggage(spawner)) return;

        int teams = TeamCount;

        // El aumento base se aplica SIEMPRE, haya equipos o no. Antes salía por aquí en
        // cuanto no había rivales, así que en una partida normal el mapa se quedaba con
        // las maletas de fábrica y los power-ups —que se calculan a partir de ellas—
        // escaseaban. Era lo que os pasaba.
        float factor = Plugin.CfgLuggageBoost.Value;
        if (teams > 1) factor *= Plugin.CfgLootPerTeam.Value * teams;

        int before = spawner.nrOfSpawns;
        spawner.nrOfSpawns = Mathf.RoundToInt(before * factor);

        Plugin.Log.LogInfo($"Maletas: {before} -> {spawner.nrOfSpawns} intentos " +
                           $"(x{factor:0.00}, {teams} equipo(s)).");
    }

    /// <summary>¿Este generador coloca maletas?</summary>
    /// <remarks>
    /// Se mira si alguno de sus prefabs lleva el componente <c>Luggage</c> en vez de fiarse
    /// del nombre del objeto: los nombres de los prefabs del nivel no son estables, pero el
    /// componente sí identifica sin ambigüedad qué es una maleta.
    /// </remarks>
    static bool PlacesLuggage(PropSpawner spawner)
    {
        // Se miran las DOS listas. Esto no es prudencia: 'overrideProps' es un interruptor
        // que hace que el generador use 'overridePropsList' EN VEZ de 'props', y mirando
        // solo la primera no se reconocía ni un generador de maletas. El resultado es que el
        // multiplicador nunca llegó a aplicarse —en el log no aparecía una sola línea de
        // "Maletas:"— y como los power-ups se cuentan a partir de las maletas colocadas,
        // escaseaban por lo mismo.
        foreach (var prop in AllProps(spawner))
        {
            if (prop == null) continue;

            // RespawnChest también es Luggage, y esa no se multiplica: es la estatua de
            // reaparición y ya la gestionamos por equipos.
            if (prop.GetComponentInChildren<RespawnChest>(true) != null) continue;
            if (prop.GetComponentInChildren<Luggage>(true) != null) return true;
        }

        return false;
    }

    /// <summary>Todos los prefabs que puede colocar un generador, de donde sea que salgan.</summary>
    static IEnumerable<GameObject> AllProps(PropSpawner spawner)
    {
        if (spawner.props != null)
            foreach (var prop in spawner.props) yield return prop;

        var list = spawner.overridePropsList;
        if (list?.gameObjects != null)
            foreach (var prop in list.gameObjects) yield return prop;
    }

    // ------------------------------------------------------------------ power-ups

    void Start()
    {
        StartCoroutine(SeedBuffs());
        StartCoroutine(ReportLootData());
    }

    /// <summary>
    /// Deja en el log si nuestros items pueden salir en maletas del juego.
    /// </summary>
    /// <remarks>
    /// La tabla de botín se construye recorriendo el ItemDatabase y leyendo el componente
    /// LootData de cada item: su rareza y en qué maletas aparece. Como nuestras armas son
    /// clones de un item vanilla, arrastran el LootData del original — así que pueden estar
    /// saliendo por el mapa sin que nadie lo haya pedido. Esto lo dice a las claras.
    /// </remarks>
    IEnumerator ReportLootData()
    {
        yield return new WaitForSeconds(12f);

        foreach (var name in Plugin.ModItemNames())
        {
            Item? item = null;
            try
            {
                // PEAKLib antepone el id del mod al registrar, así que en el database el
                // objeto se llama "fcastro.ScoutDances:Pistola" y no "Pistola".
                item = Zorro.Core.SingletonAsset<ItemDatabase>.Instance.Objects
                    .FirstOrDefault(i => i != null &&
                                         (i.name == name || i.name.EndsWith(":" + name,
                                             System.StringComparison.Ordinal)));
            }
            catch { }

            if (item == null) { Plugin.Log.LogInfo($"[botín] '{name}': no está en el database."); continue; }

            var loot = item.GetComponent<LootData>();
            Plugin.Log.LogInfo(loot == null
                ? $"[botín] '{name}': sin LootData -> NO sale en maletas."
                : $"[botín] '{name}': rareza {loot.Rarity}, maletas: {loot.spawnLocations}");
        }
    }

    /// <summary>
    /// Reparte power-ups por el mapa, tomando las maletas como referencia.
    /// </summary>
    /// <remarks>
    /// Los power-ups NO se cuelan en la generación del nivel: son items de red nuestros y
    /// el generador coloca props locales. Se sueltan después, y solo desde el anfitrión,
    /// con <c>InstantiateRoomObject</c> — así existen una sola vez para toda la sala.
    ///
    /// La cantidad se calcula a partir de las maletas YA colocadas, que es lo que hace que
    /// se cumplan las dos condiciones de una vez: escalan con los equipos (porque las
    /// maletas ya escalaron) y siempre son menos que las maletas (porque el ratio es menor
    /// que uno).
    /// </remarks>
    IEnumerator SeedBuffs()
    {
        var wait = new WaitForSeconds(4f);
        int lastSegment = -999;

        while (true)
        {
            yield return wait;

            if (!Plugin.CfgTeams.Value || !Plugin.CfgMapBuffs.Value) continue;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) continue;

            var local = Character.localCharacter;
            if (local == null || local.inAirport) continue;      // en el lobby no

            // Solo las del tramo ACTUAL: ver SegmentLoot. Con las de cualquier tramo, todo
            // se repartía en la zona que acabábamos de dejar atrás.
            var luggage = SegmentLoot.Current();

            if (luggage.Count == 0) continue;

            // Una vez por tramo: al cambiar de etapa el mapa trae maletas nuevas.
            int segment = Zorro.Core.Singleton<MapHandler>.Instance != null
                ? (int)Zorro.Core.Singleton<MapHandler>.Instance.GetCurrentSegment()
                : 0;

            if (segment == lastSegment) continue;
            lastSegment = segment;
            _lastPlacedSegment = segment;

            int count = Mathf.FloorToInt(luggage.Count * Plugin.CfgBuffsPerLuggage.Value);
            // Tope generoso: antes se obligaba a que hubiera MENOS power-ups que maletas, lo
            // que impedía subirlos por encima de 1 por maleta aunque se pidiera.
            count = Mathf.Clamp(count, 0, luggage.Count * 4);
            if (count == 0) continue;

            Place(luggage, count);
        }
    }

    void Place(List<Luggage> luggage, int count)
    {
        int placed = 0;

        for (int i = 0; i < count; i++)
        {
            var spot = luggage[Random.Range(0, luggage.Count)];
            if (spot == null) continue;

            var definition = PickBuff();
            if (definition == null) continue;

            // Un poco separado de la maleta y algo por encima, para que caiga al suelo en
            // vez de nacer empotrado dentro de ella.
            var position = spot.transform.position
                         + Random.insideUnitSphere.With(y: 0f).normalized * Plugin.CfgBuffScatter.Value
                         + Vector3.up * 1.2f;

            try
            {
                PhotonNetwork.InstantiateRoomObject(
                    "0_Items/" + Plugin.Definition.Id + ":" + definition.DisplayName.Value,
                    position, Quaternion.identity);
                placed++;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"No pude soltar un power-up: {e.Message}");
            }
        }

        Plugin.Log.LogInfo($"Tramo {_lastPlacedSegment}: repartidos {placed} power-ups " +
                           $"entre {luggage.Count} maletas.");
    }

    /// <summary>
    /// Elige qué power-up sale, con los fuertes más raros.
    /// </summary>
    /// <remarks>
    /// El peso es <c>1 / (multiplicador - 1)</c>: el x2 sale el doble que el x3, el triple
    /// que el x4, y así. Sale de la propia definición en vez de una tabla aparte, así que
    /// si cambias un multiplicador en el config su rareza se ajusta sola — y si añadimos un
    /// power-up nuevo, entra en el reparto sin tocar esto.
    /// </remarks>
    static Buffs.BuffDefinition? PickBuff()
    {
        var buffs = Plugin.BuffList;
        if (buffs.Count == 0) return null;

        float total = 0f;
        foreach (var buff in buffs)
            total += Weight(buff);

        if (total <= 0f) return null;

        float roll = Random.value * total;
        foreach (var buff in buffs)
        {
            roll -= Weight(buff);
            if (roll <= 0f) return buff;
        }

        return buffs[buffs.Count - 1];
    }

    /// <summary>Peso de cada CAJA en el reparto por el mapa.</summary>
    /// <remarks>
    /// Todas iguales, y a propósito. Antes se pesaba por lo fuerte que era el power-up,
    /// pero ahora la caja no es un power-up: es una familia, y dentro ya se sortea con la
    /// rareza. Volver a pesar aquí sería castigar dos veces a los buenos.
    /// </remarks>
    static float Weight(Buffs.BuffDefinition buff) => 1f;
}

internal static class VectorExtensions
{
    /// <summary>Copia el vector cambiando solo la componente indicada.</summary>
    internal static Vector3 With(this Vector3 v, float? x = null, float? y = null, float? z = null) =>
        new Vector3(x ?? v.x, y ?? v.y, z ?? v.z);
}
