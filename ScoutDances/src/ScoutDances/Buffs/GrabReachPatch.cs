using HarmonyLib;

namespace ScoutDances.Buffs;

/// <summary>
/// Alarga el brazo para agarrar compañeros, sobre el valor que calcula el juego.
/// </summary>
/// <remarks>
/// <b>Por qué hace falta un parche y no bastaba con escribir el campo.</b> "Brazos largos"
/// ponía <c>grabFriendDistance</c> desde <c>LateUpdate</c>, y no servía de nada: el juego lo
/// recalcula en <c>CharacterGrabbing.FixedUpdate</c> y en <c>Reach</c>, o sea que nuestro
/// valor duraba hasta el siguiente paso de física. El power-up estaba puesto, se veía en la
/// lista, y no alargaba el brazo ni un centímetro.
///
/// <b>Multiplica, no fija.</b> El valor del juego cambia según lo que estés haciendo —no es
/// una constante— así que poner un número fijo rompería esos casos. Multiplicando lo que él
/// acaba de calcular, "el doble de lejos" significa el doble de lo que te tocara en ese
/// momento, que es lo que promete el nombre.
///
/// Se parchean los dos sitios que lo escriben. Con uno solo, el otro volvería a pisarlo.
/// </remarks>
[HarmonyPatch(typeof(CharacterGrabbing))]
internal static class GrabReachPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("FixedUpdate")]
    static void AfterFixedUpdate(CharacterGrabbing __instance) => Apply(__instance);

    [HarmonyPostfix]
    [HarmonyPatch("Reach")]
    static void AfterReach(CharacterGrabbing __instance) => Apply(__instance);

    static void Apply(CharacterGrabbing grabbing)
    {
        float reach = ActiveBuffs.GrabReach;
        if (reach <= 1f) return;

        var character = grabbing != null ? grabbing.character : null;
        if (character == null || character.data == null) return;

        // Solo el personaje local: el power-up lo lleva quien lo recogió, y tocar los datos
        // de los demás no alargaría su brazo —lo simula su propia máquina— sino que dejaría
        // su estado sucio.
        if (!character.IsLocal) return;

        character.data.grabFriendDistance *= reach;
    }
}
