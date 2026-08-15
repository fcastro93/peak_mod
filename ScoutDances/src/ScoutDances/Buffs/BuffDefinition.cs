using BepInEx.Configuration;
using UnityEngine;

namespace ScoutDances.Buffs;

/// <summary>
/// Ajustes de un power-up concreto. Cada uno tiene su sección en el config.
/// </summary>
/// <remarks>
/// Mismo patrón que <see cref="Weapons.WeaponDefinition"/>: las tres cajas de velocidad
/// solo se diferencian en el modelo y en cuánto aceleran, así que en vez de tres clases
/// hay una definición parametrizada y tres instancias.
/// </remarks>
internal class BuffDefinition
{
    internal string Id { get; }

    internal ConfigEntry<string> DisplayName = null!;
    internal ConfigEntry<string> Model = null!;
    internal ConfigEntry<string> BaseItem = null!;
    internal ConfigEntry<float> PickupRadius = null!;
    internal ConfigEntry<float> Length = null!;
    internal ConfigEntry<Vector3> Offset = null!;
    internal ConfigEntry<Vector3> Rotation = null!;

    /// Familia de power-ups que salen de esta caja.
    internal BuffCategory Category { get; private set; }

    internal BuffDefinition(string id) => Id = id;

    internal static BuffDefinition Create(
        ConfigFile config, string id, string displayName, string model,
        BuffCategory category, float length)
    {
        var section = "Buff." + id;
        return new BuffDefinition(id)
        {
            Category = category,
            DisplayName = config.Bind(section, "Name", displayName,
                "Nombre visible. OJO: el itemID sale de un hash de este nombre, así que si " +
                "lo cambias TODOS los jugadores tienen que cambiarlo igual."),

            Model = config.Bind(section, "Model", model,
                "Prefab del bundle. Los de las cajas son PowerboxColSpeed 1 (azul), " +
                "PowerboxColLightning, PowerboxColHealth y PowerboxColStar."),

            BaseItem = config.Bind(section, "BaseItem", "Bugle_Scoutmaster",
                "Item del juego que se clona; de él solo se aprovecha la pose de agarre."),

            PickupRadius = config.Bind(section, "PickupRadius", 1.6f,
                new ConfigDescription("A qué distancia se recoge al pasarle por encima, en metros.",
                    new AcceptableValueRange<float>(0.3f, 6f))),

            Length = config.Bind(section, "ModelLength", length,
                new ConfigDescription("Tamaño de la caja en metros. El modelo se mide solo.",
                    new AcceptableValueRange<float>(0.05f, 6f))),

            Offset = config.Bind(section, "ModelOffset", Vector3.zero,
                "Posición respecto a la mano, si queda descolocada."),

            Rotation = config.Bind(section, "ModelRotation", Vector3.zero,
                "Rotación extra del modelo, en grados."),
        };
    }
}

/// <summary>Marca un item como power-up del mod, y de cuál.</summary>
/// <remarks>
/// Sirve para dos cosas: que F3 sepa qué sección del config tocar, y que la caja de
/// pruebas los meta en la pestaña "Buffs" sin adivinar por el nombre.
///
/// Público a propósito: Unity solo serializa campos públicos al instanciar.
/// </remarks>
internal class BuffTag : MonoBehaviour
{
    public string DefinitionId = "";
}
