using System;
using System.Collections.Generic;
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
        "Todos", "Comida", "Escalada", "Amuletos", "Místicos", "Buffs", "Del mod", "Otros",
    };

    const int All = 0, Food = 1, Climbing = 2, Amulets = 3, Mystical = 4, Buffs = 5, Mod = 6;

    /// Cajón de sastre. Internal porque la caja de pruebas lo usa como respaldo si no
    /// consigue leer el ItemDatabase; con el índice a pelo, añadir una pestaña lo rompía.
    internal const int Other = 7;

    /// <summary>
    /// Nombres de los items del mod, para reconocerlos sin adivinar.
    /// </summary>
    /// <remarks>
    /// Hace falta porque nuestras armas son clones del cuerno del Scoutmaster y heredan su
    /// etiqueta <c>Mystical</c>: sin esto, la pistola y el imán aparecían mezclados con los
    /// ídolos y las calaveras del juego. Se compara por nombre y no por la etiqueta porque
    /// la etiqueta es justo la que miente.
    ///
    /// Se construye una vez y se guarda: la caja de pruebas clasifica doscientos items cada
    /// vez que se abre, y recorrer la lista del mod en cada uno sería trabajo repetido.
    /// </remarks>
    static HashSet<string>? _modNames;

    static HashSet<string> ModNames =>
        _modNames ??= new HashSet<string>(Plugin.ModItemNames(), StringComparer.OrdinalIgnoreCase);

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

        // El resto de lo nuestro —armas y trastos— a su propia pestaña. Va ANTES de mirar
        // las etiquetas del juego a propósito: son clones del cuerno del Scoutmaster y
        // arrastran su 'Mystical', así que preguntar por las etiquetas primero los mandaría
        // a la pestaña equivocada.
        if (ModNames.Contains(item.name) ||
            (item.UIData != null && ModNames.Contains(item.UIData.itemName ?? ""))) return Mod;

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
