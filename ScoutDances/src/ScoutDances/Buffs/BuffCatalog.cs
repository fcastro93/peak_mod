using System;
using System.Collections.Generic;
using System.Linq;
using Peak.Afflictions;
using UnityEngine;

namespace ScoutDances.Buffs;

internal enum BuffCategory { Movilidad, Escalada, Supervivencia, Especial }

internal enum BuffRarity { Comun, Raro, Epico }

/// <summary>
/// Un power-up del catálogo: lo que el jugador ve y lo que le pasa al recogerlo.
/// </summary>
internal class BuffEntry
{
    internal string Id = "";
    internal string Name = "";
    internal string Summary = "";
    internal BuffCategory Category;
    internal BuffRarity Rarity;

    /// Segundos que dura. CERO significa que no tiene cuenta atrás.
    internal float Duration;

    /// Texto que sustituye al reloj cuando no hay duración (el escudo).
    internal string? Persistent;

    /// Qué le hace al personaje local.
    internal Action<Character> Apply = _ => { };

    internal bool Instant => Duration <= 0f && Persistent == null;
}

/// <summary>
/// Los power-ups que existen y cómo se sortean.
/// </summary>
/// <remarks>
/// <b>Por qué un catálogo y no un item por bufo.</b> Antes cada power-up era su propio item
/// registrado, con su modelo y su sección de config. Con dieciocho eso serían dieciocho
/// items en el database y dieciocho cajas distintas tiradas por el mapa. Ahora lo que se
/// registra son las CUATRO categorías, y el bufo concreto se sortea al abrir la caja: el
/// jugador ve por el color de qué familia es —y decide si merece el desvío— pero no cuál le
/// va a tocar.
///
/// <b>La rareza es el peso del sorteo</b>, no una etiqueta. Un común sale seis veces más que
/// un épico, que es la regla que ya seguían las cajas de velocidad.
///
/// <b>Dónde vive la duración de cada efecto.</b> Los que se apoyan en un
/// <c>Affliction</c> del juego llevan su propio <c>totalTime</c> y se limpian solos; el
/// contador que guardamos aquí es solo para pintarlo en pantalla. Los que tocan campos del
/// personaje a mano —saltos extra, coste de trepar— sí dependen de que
/// <see cref="ActiveBuffs"/> los devuelva a su sitio al caducar.
/// </remarks>
internal static class BuffCatalog
{
    /// Peso de cada rareza en el sorteo. Los flojos salen mucho; los buenos, poco.
    static readonly Dictionary<BuffRarity, int> Weights = new()
    {
        [BuffRarity.Comun] = 60,
        [BuffRarity.Raro] = 25,
        [BuffRarity.Epico] = 10,
    };

    internal static readonly List<BuffEntry> All = new()
    {
        // ---------------------------------------------------------------- Movilidad
        new BuffEntry
        {
            Id = "turbo", Name = "Turbo", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Movilidad, Duration = 4f,
            Summary = "El doble de rápido",
            Apply = c => PlayerBuff.Grant(c, 2f, 1f, 4f),
        },
        new BuffEntry
        {
            Id = "turbo_plus", Name = "Turbo Plus", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Movilidad, Duration = 4f,
            Summary = "Cuatro veces más rápido",
            Apply = c => PlayerBuff.Grant(c, 4f, 1f, 4f),
        },
        new BuffEntry
        {
            Id = "turbo_max", Name = "Turbo Max", Rarity = BuffRarity.Epico,
            Category = BuffCategory.Movilidad, Duration = 4f,
            Summary = "Seis veces más rápido",
            Apply = c => PlayerBuff.Grant(c, 6f, 1f, 4f),
        },
        new BuffEntry
        {
            Id = "salto_doble", Name = "Salto doble", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Movilidad, Duration = 20f,
            Summary = "Un segundo salto en el aire",
            Apply = c => ActiveBuffs.SetJumps(c, 1, 20f),
        },
        new BuffEntry
        {
            Id = "salto_triple", Name = "Salto triple", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Movilidad, Duration = 15f,
            Summary = "Dos saltos extra encadenados",
            Apply = c => ActiveBuffs.SetJumps(c, 2, 15f),
        },
        new BuffEntry
        {
            Id = "pluma", Name = "Pluma", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Movilidad, Duration = 10f,
            Summary = "Caes despacio y sin hacerte daño",
            Apply = c => c.refs.afflictions.AddAffliction(new Affliction_LowGravity(2, 10f)),
        },
        new BuffEntry
        {
            Id = "impulso", Name = "Impulso", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Movilidad, Duration = 0f,
            Summary = "Te catapulta hacia arriba",
            Apply = ActiveBuffs.Launch,
        },
        new BuffEntry
        {
            Id = "zancada", Name = "Zancada", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Movilidad, Duration = 20f,
            Summary = "Correr no gasta aguante",
            Apply = c => PlayerBuff.Grant(c, 1f, 0.15f, 20f),
        },

        // ---------------------------------------------------------------- Escalada
        new BuffEntry
        {
            Id = "tiza", Name = "Manos de tiza", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Escalada, Duration = 25f,
            Summary = "Trepar cuesta mucho menos",
            Apply = c => c.refs.afflictions.AddAffliction(
                new Affliction_ClimbingChalk { totalTime = 25f }),
        },
        new BuffEntry
        {
            Id = "brazos", Name = "Brazos largos", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Escalada, Duration = 25f,
            Summary = "Agarras compañeros desde el triple de lejos",
            Apply = c => ActiveBuffs.SetGrabReach(c, 3f, 25f),
        },
        new BuffEntry
        {
            Id = "agarre", Name = "Agarre firme", Rarity = BuffRarity.Epico,
            Category = BuffCategory.Escalada, Duration = 12f,
            Summary = "Trepar es gratis",
            // Aguante infinito y no 'staticClimbCost': ese campo resultó ser un booleano
            // que elige CÓMO se cobra el trepado, no cuánto. Trepar gasta aguante, así que
            // aguante infinito es exactamente "trepar gratis" — y lo gestiona el juego.
            Apply = c => c.refs.afflictions.AddAffliction(
                new Affliction_InfiniteStamina { totalTime = 12f }),
        },

        // ------------------------------------------------------------ Supervivencia
        new BuffEntry
        {
            Id = "purga", Name = "Purga", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Supervivencia, Duration = 0f,
            Summary = "Te quita veneno, frío y esporas",
            Apply = c => c.refs.afflictions.AddAffliction(new Affliction_ClearAllStatus()),
        },
        new BuffEntry
        {
            Id = "termo", Name = "Termo", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Supervivencia, Duration = 40f,
            Summary = "Inmune al frío",
            Apply = c => ActiveBuffs.KeepWarm(c, 40f),
        },
        new BuffEntry
        {
            Id = "comido", Name = "Bien comido", Rarity = BuffRarity.Comun,
            Category = BuffCategory.Supervivencia, Duration = 60f,
            Summary = "No pasas hambre",
            Apply = c => c.refs.afflictions.AddAffliction(
                new Affliction_NoHunger { totalTime = 60f }),
        },
        new BuffEntry
        {
            Id = "escudo", Name = "Escudo", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Supervivencia, Duration = 0f,
            Persistent = "hasta que te den",
            Summary = "Aguanta un golpe y se rompe",
            Apply = c => c.refs.afflictions.AddAffliction(new Affliction_BingBongShield()),
        },
        new BuffEntry
        {
            Id = "curacion", Name = "Curación total", Rarity = BuffRarity.Raro,
            Category = BuffCategory.Supervivencia, Duration = 0f,
            Summary = "Vuelves a estar entero",
            Apply = c => c.refs.afflictions.AddAffliction(new Affliction_HealAll()),
        },
        new BuffEntry
        {
            Id = "invencible", Name = "Invencible", Rarity = BuffRarity.Epico,
            Category = BuffCategory.Supervivencia, Duration = 5f,
            Summary = "Nada te hace daño",
            Apply = c => c.refs.afflictions.AddAffliction(
                new Affliction_Invincibility { totalTime = 5f }),
        },

        // ---------------------------------------------------------------- Especial
        new BuffEntry
        {
            Id = "tormenta", Name = "Llamar a la tormenta", Rarity = BuffRarity.Epico,
            Category = BuffCategory.Especial, Duration = 0f,
            Summary = "Desatas el viento sobre toda la montaña",
            Apply = _ => Storm.Summon(),
        },
    };

    internal static BuffEntry? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>Sortea un power-up de esa categoría, con la rareza como peso.</summary>
    internal static BuffEntry? Roll(BuffCategory category)
    {
        var pool = All.Where(b => b.Category == category).ToList();
        if (pool.Count == 0) return null;

        int total = pool.Sum(b => Weights[b.Rarity]);
        int pick = UnityEngine.Random.Range(0, total);

        foreach (var entry in pool)
        {
            pick -= Weights[entry.Rarity];
            if (pick < 0) return entry;
        }

        return pool[pool.Count - 1];
    }

    internal static string RarityName(BuffRarity rarity) => rarity switch
    {
        BuffRarity.Comun => "COMÚN",
        BuffRarity.Raro => "RARO",
        _ => "ÉPICO",
    };

    /// <summary>Color de cada rareza, el mismo en el aviso y en la lista.</summary>
    internal static Color RarityColor(BuffRarity rarity) => rarity switch
    {
        BuffRarity.Comun => new Color(0.42f, 0.82f, 0.55f),
        BuffRarity.Raro => new Color(0.96f, 0.75f, 0.36f),
        _ => new Color(0.78f, 0.58f, 0.90f),
    };
}
