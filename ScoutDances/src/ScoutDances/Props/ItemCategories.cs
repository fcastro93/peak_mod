using System;
using System.Linq;

namespace ScoutDances.Props;

/// <summary>
/// Clasifica los items del juego en pestañas para la caja de pruebas.
/// </summary>
/// <remarks>
/// PEAK no tiene categorías de verdad: <c>Item.itemTags</c> es un enum de flags
/// mayormente MECÁNICOS (<c>ThrowWithoutTorque</c>, <c>NonCloneable</c>,
/// <c>CloneWithOffset</c>) y solo unos pocos describen qué es el objeto. Así que
/// aprovechamos esos y completamos con heurística sobre el nombre del prefab.
///
/// Es aproximado a propósito: sirve para encontrar cosas rápido en un menú de pruebas,
/// no para ninguna lógica de juego. Un item que no encaje en nada cae en "Otros".
/// </remarks>
internal static class ItemCategories
{
    internal static readonly string[] Names =
    {
        "Todos", "Comida", "Escalada", "Amuletos", "Místicos", "Buffs", "Otros",
    };

    const int All = 0, Food = 1, Climbing = 2, Amulets = 3, Mystical = 4, Buffs = 5;

    /// Cajón de sastre. Internal porque la caja de pruebas lo usa como respaldo si no
    /// consigue leer el ItemDatabase; con el índice a pelo, añadir una pestaña lo rompía.
    internal const int Other = 6;

    static readonly string[] ClimbingHints =
    {
        "rope", "piton", "spike", "anchor", "chalk", "vine", "ladder", "shooter", "bolt",
    };

    static readonly string[] FoodHints =
    {
        "food", "berry", "fruit", "apple", "mushroom", "shroom", "meat", "egg", "marshmallow",
        "hotdog", "juice", "milk", "honey", "snack", "candy", "coconut", "banana", "cure",
        "antidote", "medicine", "bandage", "heal",
    };

    /// <summary>Índice de pestaña al que pertenece un item (sin contar "Todos").</summary>
    internal static int Of(Item item)
    {
        if (item == null) return Other;

        // Lo primero, los nuestros: llevan marca propia, así que no hay que adivinar por
        // el nombre como con los del juego.
        if (item.GetComponent<ScoutDances.Buffs.BuffTag>() != null) return Buffs;

        var tags = item.itemTags;

        if (tags.HasFlag(Item.ItemTags.ScoutAmulet)) return Amulets;
        if (tags.HasFlag(Item.ItemTags.Mystical) ||
            tags.HasFlag(Item.ItemTags.GoldenIdol) ||
            tags.HasFlag(Item.ItemTags.BookOfBones)) return Mystical;

        if (tags.HasFlag(Item.ItemTags.PackagedFood) ||
            tags.HasFlag(Item.ItemTags.Berry) ||
            tags.HasFlag(Item.ItemTags.Mushroom) ||
            tags.HasFlag(Item.ItemTags.GourmandRequirement)) return Food;

        var name = item.name.ToLowerInvariant();
        if (ClimbingHints.Any(h => name.Contains(h, StringComparison.Ordinal))) return Climbing;
        if (FoodHints.Any(h => name.Contains(h, StringComparison.Ordinal))) return Food;

        return Other;
    }

    /// <summary>¿Este item se muestra en esa pestaña?</summary>
    internal static bool Matches(int tab, int category) => tab == All || tab == category;
}
