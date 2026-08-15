using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Espejo: el siguiente efecto que te lancen se lo lleva quien te lo lanzó.
/// </summary>
/// <remarks>
/// <b>El reflejo lo decide el cliente del ATACANTE, no el de la víctima.</b> Parece del
/// revés, pero es la única forma que funciona: los estados solo los puede aplicar el dueño
/// de cada personaje, así que si la víctima intentara devolver el efecto no tendría
/// permiso para tocar al atacante. Decidiéndolo antes de disparar, el atacante simplemente
/// se apunta a sí mismo y todo lo demás sigue igual.
///
/// <b>Por eso el escudo viaja en las propiedades de jugador de Photon.</b> El atacante
/// necesita saber si su objetivo lo lleva puesto, y esas propiedades las replica Photon
/// sola y las mantiene entre escenas — el mismo mecanismo que los equipos. Un componente
/// local no le serviría de nada a la otra máquina.
///
/// <b>No se ve nada hasta que refleja.</b> Es lo que lo hace divertido: el que dispara no
/// sabe que va a comérselo hasta que ya es tarde, y el destello sale en quien llevaba el
/// espejo para que quede claro de dónde vino.
/// </remarks>
internal class MirrorShield : MonoBehaviour
{
    const string Key = "sd_mirror";

    float _until;

    /// <summary>¿Este jugador lleva el espejo puesto ahora mismo?</summary>
    internal static bool IsShielded(Character? character)
    {
        var owner = character?.photonView?.Owner;
        if (owner?.CustomProperties == null) return false;

        return owner.CustomProperties.TryGetValue(Key, out var value) && value is bool on && on;
    }

    /// <summary>Pone el espejo al jugador local.</summary>
    internal static void Grant(Character character, float seconds)
    {
        if (character == null || !character.IsLocal) return;

        var shield = character.GetComponent<MirrorShield>();
        if (shield == null) shield = character.gameObject.AddComponent<MirrorShield>();

        shield._until = Time.time + seconds;
        Announce(true);

        Plugin.Log.LogInfo($"Espejo puesto ({seconds:0.#}s o hasta que refleje).");
    }

    /// <summary>Lo quita, tanto al reflejar como al agotarse el tiempo.</summary>
    internal static void Clear(Character? character)
    {
        if (character == null || !character.IsLocal) return;

        var shield = character.GetComponent<MirrorShield>();
        if (shield != null) shield._until = 0f;

        Announce(false);
    }

    static void Announce(bool on)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [Key] = on });
    }

    void Update()
    {
        if (_until <= 0f) return;
        if (Time.time < _until) return;

        _until = 0f;
        Announce(false);
        Plugin.Log.LogInfo("El espejo se agotó sin reflejar nada.");
    }

    /// <summary>
    /// Suelta el destello y el tintineo en quien reflejó. Corre en todos los clientes.
    /// </summary>
    internal static void PlayReflect(Character victim)
    {
        if (victim == null) return;

        var clip = Plugin.FindClip(Plugin.CfgMirrorSound.Value);
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, victim.Center, Plugin.CfgBlasterVolume.Value);

        var prefab = Plugin.FindPrefab(Plugin.CfgMirrorEffect.Value);
        if (prefab == null) return;

        var hip = victim.GetBodypart(BodypartType.Hip)?.transform;
        var effect = Object.Instantiate(prefab, hip != null ? hip.position : victim.Center,
                                        Quaternion.identity);

        // Colgado del cuerpo: dura poco, pero si el reflejado sale corriendo el destello
        // debe irse con él y no quedarse flotando donde estaba.
        if (hip != null) effect.transform.SetParent(hip, worldPositionStays: true);

        effect.transform.localScale = Vector3.one * Plugin.CfgMirrorScale.Value;

        // Sin esto el escudo sale lavado: los materiales de partículas del bundle pierden
        // su mezcla aditiva si se les reenlaza el shader.
        Props.PropBuilder.RebindShaders(effect);

        Object.Destroy(effect, Plugin.CfgMirrorEffectTime.Value);
    }
}

/// <summary>Usar el espejo te lo pone encima y consume el objeto.</summary>
internal class MirrorAction : ItemAction
{
    public float duration = 60f;

    public override void RunAction()
    {
        var local = Character.localCharacter;
        if (local == null) return;

        MirrorShield.Grant(local, duration);

        // El mismo tintineo que al reflejar, para que se oiga que lo tienes puesto. Va por
        // RPC porque suena en el mundo: lo oyen también los que estén cerca, aunque el
        // icono solo lo veas tú.
        photonView.RPC(nameof(RPC_Ding), Photon.Pun.RpcTarget.All, local.Center);

        // Se gasta al usarlo: el espejo es el efecto, no el objeto.
        if (item != null && !item.consuming) item.StartCoroutine(item.ConsumeDelayed());
    }

    [Photon.Pun.PunRPC]
    void RPC_Ding(Vector3 at)
    {
        var clip = Plugin.FindClip(Plugin.CfgMirrorSound.Value);
        if (clip != null) AudioSource.PlayClipAtPoint(clip, at, Plugin.CfgBlasterVolume.Value);
    }
}
