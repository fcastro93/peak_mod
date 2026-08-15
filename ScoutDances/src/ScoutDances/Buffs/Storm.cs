using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Desata la tormenta de viento sobre toda la montaña.
/// </summary>
/// <remarks>
/// El único power-up que cambia el mundo en vez de a una persona, y por eso es el único que
/// necesita red: <c>WindChillZone</c> es un objeto por cliente, así que si solo lo tocara
/// quien recoge la caja, el viento le soplaría a él y a nadie más.
///
/// Va por evento de sala y no por RPC porque no hay ningún objeto de red nuestro donde
/// colgarlo — el mismo motivo por el que la puntuación de los equipos también viaja así.
/// </remarks>
internal class Storm : MonoBehaviour, IOnEventCallback
{
    /// Photon reserva del 200 para arriba; el 101 y el 102 ya los usa la puntuación.
    const byte EventStorm = 103;

    void Awake() => PhotonNetwork.AddCallbackTarget(this);
    void OnDestroy() => PhotonNetwork.RemoveCallbackTarget(this);

    /// <summary>Pide a todos que empiece la tormenta.</summary>
    internal static void Summon()
    {
        if (!PhotonNetwork.InRoom)
        {
            Unleash();
            return;
        }

        PhotonNetwork.RaiseEvent(EventStorm, null,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            SendOptions.SendReliable);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == EventStorm) Unleash();
    }

    /// <summary>Adelanta el reloj de la tormenta en este cliente.</summary>
    /// <remarks>
    /// Se toca la cuenta atrás y no el progreso: el juego ya sabe arrancar una tormenta
    /// cuando ese reloj llega a cero, con su viento, su sonido y su aviso. Forzar el
    /// progreso a mano sería saltarse todo eso.
    /// </remarks>
    static void Unleash()
    {
        try
        {
            var zone = WindChillZone.instance;
            if (zone == null)
            {
                Plugin.Log.LogInfo("Aquí no hay tormenta que llamar.");
                return;
            }

            zone.timeUntilStorm = Mathf.Min(zone.timeUntilStorm, 1f);
            Plugin.Log.LogInfo("Tormenta llamada: el viento llega en un segundo.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude desatar la tormenta: {e.Message}");
        }
    }
}
