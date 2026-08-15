using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PEAKLib.Core;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Caja repartida por el mapa que entrega un item del mod.
/// </summary>
/// <remarks>
/// <b>Por qué existe.</b> Las armas del mod salían dentro de las maletas normales, y cada
/// una que tocaba era una cura, una comida o una cuerda que NO salía. Con once armas
/// compitiendo por los mismos huecos, el botín útil se resentía. Sacándolas a su propio
/// contenedor, las maletas vuelven a ser lo que eran y las armas siguen apareciendo.
///
/// <b>Se usa el mismo modelo que la caja de pruebas del aeropuerto</b>, que ya se reconoce
/// de un vistazo: si ves esa caja, sabes que ahí hay algo del mod.
///
/// <b>Una por maleta.</b> No se colocan por su cuenta sino junto a las maletas ya puestas,
/// que es la forma de heredar el trabajo que el juego ya hizo repartiéndolas por sitios
/// alcanzables. Colocarlas por nuestra cuenta habría significado resolver otra vez dónde
/// hay suelo, dónde se puede llegar y dónde no estorban.
/// </remarks>
internal class ModCrate : MonoBehaviour, IInteractible
{
    /// Id del prefab en el pool. Idéntico en todas las máquinas.
    internal const string PrefabId = "ScoutDancesModCrate";

    bool _taken;

    PhotonView? View => GetComponent<PhotonView>();

    public bool IsInteractible(Character interactor) => !_taken;
    public Vector3 Center() => transform.position + Vector3.up * 0.4f;
    public Transform GetTransform() => transform;
    public string GetInteractionText() => "Abrir caja";
    public string GetName() => "Caja del mod";
    public bool IsConstantlyInteractable(Character interactor) => false;
    public float GetInteractTime(Character interactor) => 0.6f;
    public void HoverEnter() { }
    public void HoverExit() { }
    public void ReleaseInteract() { }
    public void CancelCast() { }

    public void Interact(Character interactor)
    {
        if (_taken) return;
        _taken = true;

        var item = PickItem();
        if (item.Length > 0) Give(item);

        // Se avisa a todos ANTES de destruirla: si solo la destruyera el anfitrión, quien la
        // abrió seguiría viéndola un instante y podría pulsarla otra vez.
        var view = View;
        if (view != null) view.RPC(nameof(RPC_Taken), RpcTarget.All);
        else gameObject.SetActive(false);
    }

    [PunRPC]
    void RPC_Taken()
    {
        _taken = true;
        gameObject.SetActive(false);

        // Solo el dueño del objeto de sala puede retirarlo de la red.
        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(gameObject);
    }

    /// <summary>Un arma del mod al azar.</summary>
    /// <remarks>
    /// Solo armas: los power-ups de velocidad ya se reparten por su cuenta junto a las
    /// maletas, y meterlos también aquí los duplicaría.
    /// </remarks>
    static string PickItem()
    {
        var pool = new List<string>();

        foreach (var weapon in Plugin.Weapons) pool.Add(weapon.DisplayName.Value);
        if (Plugin.Blaster != null) pool.Add(Plugin.Blaster.DisplayName.Value);

        return pool.Count == 0 ? "" : pool[Random.Range(0, pool.Count)];
    }

    void Give(string itemName)
    {
        var character = Character.localCharacter;
        if (character?.refs?.items == null) return;

        try
        {
            character.refs.items.SpawnItemInHand(itemName);
            Plugin.Log.LogInfo($"Caja del mod: te dio '{itemName}'.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude darte '{itemName}': {e.Message}");
        }
    }
}

/// <summary>
/// Reparte las cajas del mod por el mapa, una por maleta.
/// </summary>
internal class ModCrateSpawner : MonoBehaviour
{
    internal static ModDefinition? Mod;

    static GameObject? _prefab;
    static bool _registered;

    int _doneSegment = -999;

    void Start() => StartCoroutine(Watch());

    IEnumerator Watch()
    {
        var wait = new WaitForSeconds(3f);

        while (true)
        {
            yield return wait;

            if (!Plugin.CfgModCrates.Value) continue;
            if (!PhotonNetwork.InRoom) continue;

            var local = Character.localCharacter;
            if (local == null || local.inAirport) { _doneSegment = -999; continue; }

            // Todos registran el prefab; solo el anfitrión coloca. Si cada cliente colocara
            // las suyas saldría una tanda por jugador.
            if (!EnsurePrefab()) continue;

            int segment = CurrentSegment();
            if (segment == _doneSegment) continue;

            if (!PhotonNetwork.IsMasterClient) { _doneSegment = segment; continue; }

            // Un respiro para que a los demás les dé tiempo de registrar el prefab: si no,
            // les llega un objeto cuyo id no saben resolver.
            yield return new WaitForSeconds(2f);

            var luggage = Object.FindObjectsByType<Luggage>(FindObjectsSortMode.None)
                                .Where(l => l != null && l.GetComponent<RespawnChest>() == null)
                                .ToList();

            if (luggage.Count == 0) continue;

            _doneSegment = segment;

            try
            {
                Place(luggage, segment);
            }
            catch (System.Exception e)
            {
                // La corrutina tiene que sobrevivir para el siguiente tramo.
                Plugin.Log.LogError($"Fallo colocando las cajas del mod: {e.Message}");
            }
        }
    }

    static int CurrentSegment()
    {
        try
        {
            return Zorro.Core.Singleton<MapHandler>.Instance != null
                ? (int)Zorro.Core.Singleton<MapHandler>.Instance.GetCurrentSegment()
                : 0;
        }
        catch { return 0; }
    }

    void Place(List<Luggage> luggage, int segment)
    {
        int count = Mathf.RoundToInt(luggage.Count * Plugin.CfgCratesPerLuggage.Value);
        if (count <= 0) return;

        int placed = 0;

        for (int i = 0; i < count; i++)
        {
            var spot = luggage[Random.Range(0, luggage.Count)];
            if (spot == null) continue;

            // Apartada de la maleta y algo por encima, para que caiga al suelo en vez de
            // nacer empotrada dentro de ella.
            var offset = Random.insideUnitSphere;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.01f) offset = Vector3.forward;

            var position = spot.transform.position
                         + offset.normalized * Plugin.CfgCrateScatter.Value
                         + Vector3.up * 1f;

            if (Physics.Raycast(position + Vector3.up * 4f, Vector3.down, out var ground, 20f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                position.y = ground.point.y + 0.2f;
            }

            try
            {
                PhotonNetwork.InstantiateRoomObject(PrefabPath, position, Quaternion.identity);
                placed++;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"No pude poner una caja: {e.Message}");
            }
        }

        Plugin.Log.LogInfo($"Cajas del mod en el tramo {segment}: {placed} para " +
                           $"{luggage.Count} maleta(s).");
    }

    static string PrefabPath => ModCrate.PrefabId;

    /// <summary>
    /// Prepara el prefab de red con el modelo de la caja del aeropuerto.
    /// </summary>
    /// <remarks>
    /// Se clona colgado de un objeto desactivado por la misma razón que la estatua: Unity no
    /// ejecuta <c>Awake</c> en algo que nace inactivo, y así el <c>PhotonView</c> no intenta
    /// registrar un ViewID heredado —lo que reventaba con "Duplicate key" y mataba la
    /// corrutina que lo llamaba.
    /// </remarks>
    static bool EnsurePrefab()
    {
        if (_registered) return true;
        if (Mod == null || Plugin.ItemBoxPrefab == null) return false;

        try
        {
            var crib = new GameObject("ScoutDancesCratePrefabCrib");
            crib.SetActive(false);
            DontDestroyOnLoad(crib);

            _prefab = Instantiate(Plugin.ItemBoxPrefab, crib.transform);

            foreach (var view in _prefab.GetComponentsInChildren<PhotonView>(true))
                view.ViewID = 0;

            _prefab.transform.SetParent(null, false);
            _prefab.SetActive(false);
            DontDestroyOnLoad(_prefab);
            _prefab.name = ModCrate.PrefabId;

            // Hace falta un PhotonView propio para que "ya la abrieron" viaje a los demás.
            if (_prefab.GetComponent<PhotonView>() == null)
                _prefab.AddComponent<PhotonView>();

            if (_prefab.GetComponent<ModCrate>() == null)
                _prefab.AddComponent<ModCrate>();

            // Un colisionador para poder apuntarla; el modelo del bundle no trae ninguno.
            if (_prefab.GetComponentInChildren<Collider>(true) == null)
            {
                var box = _prefab.AddComponent<BoxCollider>();
                box.size = new Vector3(0.6f, 0.6f, 0.6f);
                box.center = new Vector3(0f, 0.3f, 0f);
            }

            NetworkPrefabManager.RegisterNetworkPrefab(ModCrate.PrefabId, _prefab);
            _registered = true;

            Plugin.Log.LogInfo($"Caja del mod registrada como prefab de red ('{ModCrate.PrefabId}').");
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"No pude preparar la caja del mod: {e.Message}");
            return false;
        }
    }
}
