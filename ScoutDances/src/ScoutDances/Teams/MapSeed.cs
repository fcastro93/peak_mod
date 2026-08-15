using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Hace que cada partida caiga en un mapa distinto, elegido entre los que el juego ya trae.
/// </summary>
/// <remarks>
/// <b>Qué es en realidad la "semilla".</b> PEAK no genera terreno al azar: escoge un mapa de
/// un catálogo por índice, y ese índice lo reparte su servidor —el mismo para todo el mundo
/// cada día—. Así que no hace falta inventar un generador: basta con desplazar el índice.
///
/// <b>Por qué vale cualquier número.</b> Las dos funciones que convierten el índice en
/// contenido lo pasan por módulo:
///
/// <code>
/// GetLevel(i)   -> ScenePaths[i % ScenePaths.Length]
/// GetBiomeID(i) -> BiomeIDs[i % BiomeIDs.Count]
/// </code>
///
/// Cualquier entero da la vuelta y cae dentro de la lista. No hay índices inválidos, y todo
/// resultado es un mapa que el estudio probó: no nos salimos de lo comprobado.
///
/// <b>El desplazamiento es la palanca del propio juego.</b>
/// <c>NextLevelService.debugLevelIndexOffset</c> es un <c>static int</c> que PEAK usa en su
/// propio <c>MapDebugUI</c>. No estamos forzando nada raro.
///
/// <b>Y por qué hay que sincronizarlo a mano.</b> El anfitrión resuelve la escena y la manda
/// por RPC, así que ESA parte viaja sola. Pero los biomas los calcula CADA cliente por su
/// cuenta en <c>TrySetBiomes</c>, con su propio número. Si a alguien no le llega el sorteo,
/// carga el terreno bueno con el recorrido del día. Por eso el número va en una propiedad de
/// sala y se aplica constantemente, no una sola vez.
/// </remarks>
internal class MapSeed : MonoBehaviour
{
    /// Clave de la propiedad de sala con la semilla de la partida.
    const string SeedKey = "sd_seed";

    /// Última semilla aplicada al juego, para no repetir el registro cada frame.
    static int _applied = int.MinValue;

    /// <summary>La semilla de esta sala, o 0 si no hay ninguna.</summary>
    internal static int Current
    {
        get
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room?.CustomProperties == null) return 0;

            return room.CustomProperties.TryGetValue(SeedKey, out var value) && value is int seed
                ? seed
                : 0;
        }
    }

    /// <summary>Pone una semilla concreta. Solo el anfitrión.</summary>
    internal static void Set(int seed)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        seed = Mathf.Max(0, seed);

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [SeedKey] = seed });
        Plugin.Log.LogInfo($"Semilla del mapa fijada en {seed}.");
    }

    /// <summary>Sortea una nueva. Solo el anfitrión.</summary>
    internal static void Roll() => Set(Random.Range(1, 1000000));

    void Update()
    {
        if (!Plugin.CfgRandomMap.Value) return;
        if (!PhotonNetwork.InRoom) return;

        // El anfitrión sortea en cuanto la sala no tiene ninguna, para que el número esté
        // repartido MUCHO antes de que nadie pulse empezar. Si se sorteara al arrancar la
        // partida, a los demás les llegaría con la carga ya en marcha.
        if (PhotonNetwork.IsMasterClient && Current == 0) Roll();

        Apply();
    }

    /// <summary>
    /// Vuelca la semilla de la sala en el desplazamiento del juego.
    /// </summary>
    /// <remarks>
    /// En <c>Update</c> y no una vez al entrar: la propiedad de sala puede llegar tarde, el
    /// anfitrión puede cambiar de manos, y quien entra a mitad de lobby tiene que recibirla
    /// igual. Es una asignación a un entero, no cuesta nada repetirla.
    /// </remarks>
    static void Apply()
    {
        int seed = Current;
        if (seed == 0) return;

        try
        {
            if (NextLevelService.debugLevelIndexOffset == seed) return;

            NextLevelService.debugLevelIndexOffset = seed;

            if (_applied != seed)
            {
                _applied = seed;
                Plugin.Log.LogInfo($"Mapa de la partida: semilla {seed} aplicada.");
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude aplicar la semilla del mapa: {e.Message}");
        }
    }

    /// <summary>Devuelve el juego al mapa del día.</summary>
    internal static void Clear()
    {
        try { NextLevelService.debugLevelIndexOffset = 0; } catch { }
        _applied = int.MinValue;
    }
}

/// <summary>
/// Comprueba que la semilla esté puesta justo antes de cargar, y lo deja escrito.
/// </summary>
/// <remarks>
/// Este es el momento que importa: aquí el anfitrión resuelve qué escena manda a todos. Si
/// llegados a este punto el desplazamiento no coincide con el de la sala, algo falló y es
/// mejor saberlo por el log que descubrirlo con medio grupo en un mapa distinto.
///
/// No se aborta la carga: quedarse sin partida es peor que jugar el mapa del día.
/// </remarks>
[HarmonyPatch(typeof(AirportCheckInKiosk), "LoadIslandMaster")]
internal static class MapSeedLoadPatch
{
    [HarmonyPrefix]
    static void Prefix()
    {
        if (!Plugin.CfgRandomMap.Value) return;

        int seed = MapSeed.Current;

        try
        {
            int offset = NextLevelService.debugLevelIndexOffset;

            if (seed == 0)
                Plugin.Log.LogWarning("Se carga sin semilla: saldrá el mapa del día.");
            else if (offset != seed)
                Plugin.Log.LogWarning($"La semilla de la sala es {seed} pero tengo {offset} " +
                                      "puesto; puede que no coincidan los biomas.");
            else
                Plugin.Log.LogInfo($"Cargando con la semilla {seed}.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude comprobar la semilla antes de cargar: {e.Message}");
        }
    }
}
