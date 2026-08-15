using System.Collections;
using System.Linq;
using HarmonyLib;
using PEAKLib.Core;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Teams;

/// <summary>
/// Marca una estatua como propiedad de un equipo concreto.
/// </summary>
/// <remarks>
/// El dueño llega en los datos de instanciación de Photon
/// (<c>PhotonView.InstantiationData</c>) y no por una propiedad de sala aparte. Es la
/// diferencia entre que todos los clientes sepan de quién es cada estatua EN EL MISMO
/// INSTANTE en que aparece, o que haya una ventana en la que ya se ve pero todavía no se
/// sabe de quién es — y en esa ventana cualquiera podría abrirla.
/// </remarks>
internal class TeamStatue : MonoBehaviour
{
    internal string Owner = "";

    void Start()
    {
        var view = GetComponent<PhotonView>();
        if (view?.InstantiationData is { Length: > 0 } data && data[0] is string team)
            Owner = team;

        // El SegmentNumber no es de red: viaja en los mismos datos de instanciación.
        if (view?.InstantiationData is { Length: > 1 } more && more[1] is int segment)
        {
            var chest = GetComponent<RespawnChest>();
            if (chest != null) chest.SegmentNumber = (Segment)segment;
        }

        Plugin.Log.LogInfo($"Estatua del equipo '{Owner}' lista.");
    }
}

/// <summary>
/// Pone una estatua de reaparición por equipo en cada etapa.
/// </summary>
/// <remarks>
/// El juego coloca UNA estatua por etapa y al usarla revive a todos los muertos de la
/// partida, sin distinguir bandos. Aquí se clona una por equipo y cada una solo sirve al
/// suyo.
///
/// <b>Por qué se registra como prefab de red.</b> Una copia hecha con <c>Instantiate</c>
/// sin más se vería y se podría pulsar, pero su estado de "ya gastada" no viajaría: tú la
/// usarías y los demás la seguirían viendo intacta. Registrarla en el pool de PEAKLib y
/// crearla con <c>InstantiateRoomObject</c> le da su propio <c>PhotonView</c>, y con él
/// todo el trasiego de abrir y vaciar que el juego ya tiene resuelto.
/// </remarks>
internal class TeamStatues : MonoBehaviour
{
    internal static ModDefinition? Mod;

    /// Id del prefab en el pool. Tiene que ser idéntico en todas las máquinas.
    internal const string PrefabId = "ScoutDancesTeamStatue";

    static GameObject? _prefab;
    static bool _registered;

    /// Etapa cuyas estatuas ya hemos puesto, para no repetir.
    int _doneSegment = -999;

    void Start() => StartCoroutine(Watch());

    IEnumerator Watch()
    {
        var wait = new WaitForSeconds(2f);

        while (true)
        {
            yield return wait;

            if (!Plugin.CfgTeams.Value || !Plugin.CfgTeamStatues.Value) continue;
            if (!PhotonNetwork.InRoom) continue;

            var original = FindOriginal();
            if (original == null) continue;

            EnsurePrefab(original);

            int segment = (int)original.SegmentNumber;
            if (segment == _doneSegment) continue;

            // Todos registran el prefab, pero solo el anfitrión crea las estatuas: si
            // cada cliente creara las suyas tendríamos una por jugador y por equipo.
            if (!PhotonNetwork.IsMasterClient) { _doneSegment = segment; continue; }

            // Un respiro antes de crear, para que a los demás les dé tiempo de registrar
            // el prefab; si no, les llega un objeto cuyo id no saben resolver.
            yield return new WaitForSeconds(2f);

            // Y SE VUELVE A MIRAR: en esos dos segundos alguien puede haber abierto la
            // estatua, y entonces Unity ya la destruyó. Usarla lanzaba una excepción que
            // mataba esta corrutina entera — no solo fallaba una vez, es que dejaba de
            // intentarlo para el resto de la partida.
            if (original == null)
            {
                Plugin.Log.LogInfo("La estatua desapareció mientras esperábamos; lo dejo para el próximo tramo.");
                continue;
            }

            try
            {
                SpawnForTeams(original, segment);
            }
            catch (System.Exception e)
            {
                // Pase lo que pase, la corrutina sigue viva para el siguiente tramo.
                Plugin.Log.LogError($"Fallo poniendo las estatuas de equipo: {e.Message}");
            }

            _doneSegment = segment;
        }
    }

    /// <summary>La estatua que puso el generador de niveles, si sigue disponible.</summary>
    static RespawnChest? FindOriginal() =>
        Object.FindObjectsByType<RespawnChest>(FindObjectsSortMode.None)
              .FirstOrDefault(c => c != null && c.GetComponent<TeamStatue>() == null && !c.IsSpent);

    /// <summary>
    /// Guarda una copia inactiva de la estatua y la registra como prefab de red.
    /// </summary>
    /// <remarks>
    /// Mismo truco que con las armas: PEAKLib acepta cualquier GameObject como prefab, no
    /// hace falta que sea un asset. La copia se hace inactiva y persistente para que no
    /// participe en la escena ni se la lleve un cambio de nivel.
    /// </remarks>
    static void EnsurePrefab(RespawnChest original)
    {
        if (_registered || Mod == null) return;

        // Se clona DENTRO de un objeto desactivado, y esto no es un detalle de estilo.
        // La estatua lleva un PhotonView, y el Awake de un PhotonView registra su ViewID en
        // una tabla global. Al clonar en caliente, la copia heredaba el ViewID del original
        // e intentaba registrarlo otra vez:
        //
        //     InvalidOperationException: Duplicate key 1593
        //       at Photon.Pun.PhotonNetwork.RegisterPhotonView
        //
        // Esa excepción salía de dentro de Instantiate, así que _prefab se quedaba a nulo y
        // —lo peor— MATABA la corrutina que la llamaba, dejando las estatuas a medias sin un
        // solo mensaje de error en el log de BepInEx.
        //
        // Unity no ejecuta Awake en un objeto que nace inactivo en la jerarquía, así que
        // colgándolo de un padre desactivado el PhotonView no llega a registrarse nunca.
        var crib = new GameObject("ScoutDancesPrefabCrib");
        crib.SetActive(false);
        DontDestroyOnLoad(crib);

        _prefab = Instantiate(original.gameObject, crib.transform);

        // Sin ViewID heredado: se lo asigna Photon al instanciarlo de verdad por la red.
        foreach (var view in _prefab.GetComponentsInChildren<Photon.Pun.PhotonView>(true))
            view.ViewID = 0;

        _prefab.transform.SetParent(null, false);
        _prefab.SetActive(false);
        DontDestroyOnLoad(_prefab);
        _prefab.name = PrefabId;
        _prefab.AddComponent<TeamStatue>();

        NetworkPrefabManager.RegisterNetworkPrefab(PrefabId, _prefab);
        _registered = true;

        Plugin.Log.LogInfo($"Estatua registrada como prefab de red ('{PrefabId}').");
    }

    static void SpawnForTeams(RespawnChest original, int segment)
    {
        var teams = TeamState.Scoreboard().Select(e => e.Team).ToList();
        if (teams.Count == 0) return;

        var origin = original.transform.position;
        var rotation = original.transform.rotation;
        var side = original.transform.right;

        float spacing = Plugin.CfgStatueSpacing.Value;

        for (int i = 0; i < teams.Count; i++)
        {
            // En fila y centradas sobre la original, para que ninguna quede escondida
            // dentro del decorado que había alrededor de la de fábrica.
            float offset = (i - (teams.Count - 1) / 2f) * spacing;

            PhotonNetwork.InstantiateRoomObject(
                PrefabId, origin + side * offset, rotation, 0,
                new object[] { teams[i], segment });
        }

        // La de fábrica se retira: si se quedara, cualquiera podría usarla y saltarse el
        // reparto por equipos. Se abre en vez de destruirla porque el juego ya sincroniza
        // ese estado y no le sienta bien que le desaparezcan objetos del nivel.
        original.Break();

        Plugin.Log.LogInfo($"Puestas {teams.Count} estatuas de equipo en el tramo {segment}.");
    }
}

/// <summary>Solo el equipo dueño puede tocar su estatua.</summary>
[HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.IsInteractible))]
internal static class StatueOwnerPatch
{
    [HarmonyPostfix]
    static void Postfix(RespawnChest __instance, Character interactor, ref bool __result)
    {
        if (!__result || !Plugin.CfgTeams.Value) return;

        var statue = __instance.GetComponent<TeamStatue>();
        if (statue == null || statue.Owner.Length == 0) return;

        var team = TeamState.TeamOf(interactor?.photonView?.Owner);
        if (team != statue.Owner) __result = false;
    }
}

/// <summary>Una estatua de equipo revive solo a los suyos.</summary>
/// <remarks>
/// El método original recorre <c>Character.AllCharacters</c> y levanta a todo el que esté
/// muerto. Lo sustituimos entero en vez de filtrar después porque para cuando termina ya
/// ha mandado los RPC de resurrección: no hay nada que deshacer.
/// </remarks>
[HarmonyPatch(typeof(RespawnChest), "RespawnAllPlayersHere")]
internal static class StatueRevivePatch
{
    [HarmonyPrefix]
    static bool Prefix(RespawnChest __instance)
    {
        var statue = __instance.GetComponent<TeamStatue>();
        if (statue == null || statue.Owner.Length == 0) return true;   // estatua normal

        foreach (var character in Character.AllCharacters)
        {
            if (character == null || character.data == null) continue;
            if (!character.data.dead && !character.data.fullyPassedOut) continue;
            if (TeamState.TeamOf(character.photonView?.Owner) != statue.Owner) continue;

            character.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All,
                                     __instance.RandomRevivePoint, true,
                                     (int)__instance.SegmentNumber);
        }

        Plugin.Log.LogInfo($"Estatua de '{statue.Owner}' usada: revive solo a los suyos.");
        return false;
    }
}
