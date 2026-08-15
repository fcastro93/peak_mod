using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Decide en qué maletas del juego puede salir un arma del mod.
/// </summary>
/// <remarks>
/// La tabla de botín se construye recorriendo el ItemDatabase y leyendo el
/// <c>LootData</c> de cada item: su rareza y un campo de banderas con las maletas donde
/// aparece. Nuestras armas son clones de un item vanilla, así que venían arrastrando el
/// suyo —el del Bugle del Scoutmaster: rareza mítica y solo en maletas malditas y ataúdes—
/// sin que nadie lo hubiera pedido.
///
/// Aquí se pone a propósito: maletas normales de todos los biomas, y la rareza sale del
/// config para poder afinar cómo de frecuente es encontrarse un arma sin recompilar.
/// </remarks>
internal static class WeaponLoot
{
    /// Todas las maletas corrientes, una por bioma.
    const SpawnPool NormalLuggage =
        SpawnPool.LuggageBeach | SpawnPool.LuggageJungle | SpawnPool.LuggageTundra |
        SpawnPool.LuggageCaldera | SpawnPool.LuggageMesa | SpawnPool.LuggageAncient |
        SpawnPool.LuggageClimber | SpawnPool.LuggageRoots;

    internal static void Apply(GameObject clone)
    {
        var loot = clone.GetComponent<LootData>();
        if (loot == null) return;

        if (!Plugin.CfgWeaponsInLuggage.Value)
        {
            loot.spawnLocations = SpawnPool.None;
            return;
        }

        loot.spawnLocations = NormalLuggage;
        loot.rarityOverrides?.Clear();

        if (System.Enum.TryParse<Rarity>(Plugin.CfgWeaponRarity.Value, ignoreCase: true,
                                         out var rarity))
        {
            loot.Rarity = rarity;
        }
    }
}
