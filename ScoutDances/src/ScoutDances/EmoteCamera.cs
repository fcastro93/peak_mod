using HarmonyLib;
using UnityEngine;

namespace ScoutDances;

/// <summary>
/// Pasa la cámara a tercera persona mientras el Scout hace un emote, y muestra el
/// cuerpo completo (en primera persona el juego lo oculta y solo se ven las manos).
/// Vuelve a la normalidad en cuanto el emote termina.
/// </summary>
/// <remarks>
/// No hace falta detectar WASD ni el salto: el propio juego corta el emote cuando te
/// mueves (<c>CharacterAnimations.Update</c> apaga <c>emoting</c> si
/// <c>movementInput.magnitude > 0.1</c>, <c>jumpWasPressed</c> o llevas 0,2 s sin suelo).
/// Enganchándonos a <c>emoting</c>, la cámara sigue exactamente la vida del emote.
/// </remarks>
internal static class EmoteCamera
{
    /// 0 = primera persona, 1 = tercera. Se interpola para que no dé un salto seco.
    static float _weight;

    /// Segundos que tarda la transición en cada sentido.
    const float BlendSeconds = 0.25f;

    /// Radio del SphereCast que evita que la cámara se meta en la roca.
    const float CollisionRadius = 0.25f;

    internal static bool IsActive => _weight > 0.001f;

    /// <summary>
    /// Se pone a true mientras el emote en curso del jugador local sea de sonido: esos
    /// no cambian la cámara, solo los bailes.
    /// </summary>
    /// <remarks>
    /// Lo fija <see cref="ScoutDances.Sounds.SoundEmoteTriggerPatch"/> en CADA emote (a
    /// true para sonidos, a false para el resto). Si solo se pusiera a true y se
    /// esperara a que el emote acabe para limpiarlo, encadenar un baile justo después
    /// de un sonido dejaría la cámara suprimida de más.
    /// </remarks>
    internal static bool SuppressForCurrentEmote;

    // --- diagnóstico ---
    static bool _loggedFirstCall;
    static bool _lastWant;
    static string _lastReason = "";
    static string _loggedReason = "\0";

    /// <summary>
    /// Se ejecuta después de que <c>MainCameraMovement.LateUpdate</c> haya colocado la
    /// cámara en primera persona, así que tenemos la última palabra sobre el transform.
    /// </summary>
    internal static void Apply(MainCameraMovement movement)
    {
        if (!_loggedFirstCall && Plugin.CfgVerbose.Value)
        {
            _loggedFirstCall = true;
            Plugin.Log.LogInfo("EmoteCamera: el postfix de MainCameraMovement.LateUpdate SÍ se está ejecutando.");
        }

        var character = Character.localCharacter;
        bool want = ShouldBeThirdPerson(character);

        if ((want != _lastWant || _lastReason != _loggedReason) && Plugin.CfgVerbose.Value)
        {
            _lastWant = want;
            _loggedReason = _lastReason;
            Plugin.Log.LogInfo($"EmoteCamera: tercera persona = {want} (motivo: {_lastReason})");
        }

        _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, Time.deltaTime / BlendSeconds);

        // El cuerpo se muestra durante toda la transición, no solo cuando llega a 1.
        SetBodyVisible(character, IsActive);

        if (!IsActive || character == null) return;

        var focus = character.Center + Vector3.up * Plugin.CfgCamHeight.Value;

        var look = character.data.lookDirection;
        if (look.sqrMagnitude < 0.001f) look = movement.transform.forward;
        look.Normalize();

        var back = -look;
        var right = Vector3.Cross(Vector3.up, look).normalized;
        var desired = focus
                      + back * Plugin.CfgCamDistance.Value
                      + right * Plugin.CfgCamSideOffset.Value;

        desired = PullOutOfGeometry(focus, desired, character);

        var t = movement.transform;
        t.position = Vector3.Lerp(t.position, desired, _weight);

        var toFocus = focus - t.position;
        if (toFocus.sqrMagnitude > 0.0001f)
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(toFocus.normalized), _weight);
    }

    static bool ShouldBeThirdPerson(Character? character)
    {
        if (!Plugin.CfgThirdPerson.Value) { _lastReason = "desactivado en config"; return false; }
        if (character == null) { _lastReason = "sin localCharacter"; return false; }

        // OJO: NO usar character.IsInitialized. Suena a "el personaje ya existe" pero en
        // realidad es refs.stats.IsInitialized -> 'Time.frameCount >= _frameInitialized',
        // y _frameInitialized vale int.MaxValue hasta que arranca una run. En el
        // aeropuerto es false para siempre, y ahí es justo donde se prueban los bailes.
        if (character.refs == null) { _lastReason = "sin refs"; return false; }
        if (character.data == null) { _lastReason = "sin data"; return false; }
        if (character.data.dead || character.data.fullyPassedOut) { _lastReason = "muerto/KO"; return false; }
        if (character.IsGhost) { _lastReason = "fantasma"; return false; }

        // Si el juego ya está en modo espectador o god cam, no nos metemos.
        if (MainCameraMovement.IsSpectating) { _lastReason = "espectando"; return false; }

        var animations = character.refs?.animations;
        if (animations == null) { _lastReason = "sin CharacterAnimations"; return false; }
        if (!animations.emoting) { _lastReason = "no está emoting"; return false; }
        if (SuppressForCurrentEmote) { _lastReason = "emote de sonido"; return false; }

        _lastReason = "emoting";
        return true;
    }

    /// <summary>
    /// Acerca la cámara si hay geometría entre el personaje y la posición deseada,
    /// para no acabar dentro de una pared.
    /// </summary>
    static Vector3 PullOutOfGeometry(Vector3 focus, Vector3 desired, Character character)
    {
        var delta = desired - focus;
        float distance = delta.magnitude;
        if (distance < 0.01f) return desired;

        var dir = delta / distance;
        var hits = Physics.SphereCastAll(focus, CollisionRadius, dir, distance,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float closest = distance;
        foreach (var hit in hits)
        {
            if (hit.distance <= 0f) continue;                       // ya estábamos dentro
            // Ignoramos al propio Scout y a lo que lleve encima: si no, la cámara
            // se pegaría a su espalda constantemente.
            if (hit.collider.GetComponentInParent<Character>() == character) continue;
            if (hit.collider.GetComponentInParent<Item>() != null) continue;

            if (hit.distance < closest) closest = hit.distance;
        }

        if (closest >= distance) return desired;
        return focus + dir * Mathf.Max(0.4f, closest - 0.1f);
    }

    /// <summary>
    /// Muestra u oculta el cuerpo del jugador local.
    /// </summary>
    /// <remarks>
    /// <c>HideTheBody.Update</c> recalcula el estado cada frame, así que no vale con
    /// llamar una vez a su Toggle (privado): lo revertiría al frame siguiente. En vez de
    /// parchear, movemos la entrada de su propia condición —
    /// <c>flag = !IsLocal || fullyPassedOut || dead || isDummy</c> — poniendo el campo
    /// público <c>isDummy</c>. El juego hace el resto solo, y al revertirlo vuelve a
    /// ocultar el cuerpo sin que tengamos que tocar sus materiales.
    /// </remarks>
    static void SetBodyVisible(Character? character, bool visible)
    {
        // Comparación explícita con null: el '?.' de C# no pasa por el operador ==
        // sobrecargado de UnityEngine.Object, así que un objeto ya destruido se
        // colaría y reventaría al tocar sus campos.
        if (character == null) return;

        var hideTheBody = character.refs?.hideTheBody;
        if (hideTheBody == null) return;
        if (hideTheBody.isDummy != visible) hideTheBody.isDummy = visible;
    }

    /// Devuelve todo a su sitio al descargar el mod.
    internal static void Reset()
    {
        _weight = 0f;
        SetBodyVisible(Character.localCharacter, false);
    }
}

[HarmonyPatch(typeof(MainCameraMovement), "LateUpdate")]
internal static class MainCameraMovementLateUpdatePatch
{
    [HarmonyPostfix]
    static void Postfix(MainCameraMovement __instance) => EmoteCamera.Apply(__instance);
}

/// <summary>
/// Sonda de diagnóstico: confirma si el flag <c>emoting</c> se pone realmente a true en
/// el CharacterAnimations del jugador LOCAL, que es de lo que depende toda la lógica
/// de la cámara.
/// </summary>
[HarmonyPatch(typeof(CharacterAnimations), "RPCA_PlayRemove")]
internal static class EmoteStartProbe
{
    [HarmonyPostfix]
    static void Postfix(CharacterAnimations __instance, string emoteName)
    {
        if (!Plugin.CfgVerbose.Value) return;

        var local = Character.localCharacter;
        bool isLocal = local != null && __instance.character == local;
        bool sameInstance = local != null && local.refs?.animations == __instance;

        Plugin.Log.LogInfo(
            $"EmoteProbe: emote='{emoteName}' esLocal={isLocal} " +
            $"mismaInstanciaQueRefs={sameInstance} emoting={__instance.emoting}");
    }
}
