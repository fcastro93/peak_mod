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
/// <b>Por qué un catálogo y no un item por bufo.</b> Lo que se registra en el juego son las
/// CUATRO cajas —una por categoría— y el power-up concreto se sortea al abrirla. El jugador
/// ve de qué familia es, y decide si merece el desvío, pero no cuál le va a tocar.
///
/// <b>Casi todo viene en tres fuerzas.</b> Es el mismo patrón que ya tenían las cajas de
/// velocidad con Turbo, Turbo Plus y Turbo Max, y hace que abrir una caja siga teniendo
/// gracia cuando ya conoces los efectos: no es "me ha tocado salto", es "me ha tocado el
/// salto bueno". Se declaran con <see cref="Family"/> en vez de a mano porque son la misma
/// idea tres veces, y escribirlas por separado invita a que se desajusten entre sí.
///
/// <b>Los que no escalan se quedan solos.</b> Una curación total no puede ser más total, y
/// una purga no limpia más que todo. Inventarles niveles sería relleno.
///
/// <b>La rareza es el peso del sorteo</b>, no una etiqueta: un común sale seis veces más que
/// un épico.
/// </remarks>
internal static class BuffCatalog
{
    static readonly Dictionary<BuffRarity, int> Weights = new()
    {
        [BuffRarity.Comun] = 60,
        [BuffRarity.Raro] = 25,
        [BuffRarity.Epico] = 10,
    };

    internal static readonly List<BuffEntry> All = new();

    /// <summary>Un power-up en sus tres fuerzas: común, raro y épico.</summary>
    static void Family(string id, BuffCategory category,
                       (string Name, string Summary, float Duration)[] tiers,
                       Func<int, Action<Character>> apply)
    {
        var rarities = new[] { BuffRarity.Comun, BuffRarity.Raro, BuffRarity.Epico };

        for (int i = 0; i < tiers.Length && i < rarities.Length; i++)
        {
            All.Add(new BuffEntry
            {
                Id = $"{id}{i + 1}",
                Name = tiers[i].Name,
                Summary = tiers[i].Summary,
                Category = category,
                Rarity = rarities[i],
                Duration = tiers[i].Duration,
                Apply = apply(i),
            });
        }
    }

    static void Single(string id, string name, string summary, BuffCategory category,
                       BuffRarity rarity, float duration, Action<Character> apply,
                       string? persistent = null)
    {
        All.Add(new BuffEntry
        {
            Id = id, Name = name, Summary = summary, Category = category,
            Rarity = rarity, Duration = duration, Persistent = persistent, Apply = apply,
        });
    }

    static BuffCatalog()
    {
        // ------------------------------------------------------------------ Movilidad

        Family("turbo", BuffCategory.Movilidad, new[]
        {
            ("Turbo",      "El doble de rápido",      4f),
            ("Turbo Plus", "Cuatro veces más rápido", 4f),
            ("Turbo Max",  "Seis veces más rápido",   4f),
        }, tier =>
        {
            float[] speed = { 2f, 4f, 6f };
            return c => PlayerBuff.Grant(c, speed[tier], 1f, 4f);
        });

        Family("salto", BuffCategory.Movilidad, new[]
        {
            ("Salto doble",     "Un segundo salto en el aire", 20f),
            ("Salto triple",    "Dos saltos extra",            18f),
            ("Salto cuádruple", "Tres saltos extra",           15f),
        }, tier =>
        {
            int[] jumps = { 1, 2, 3 };
            float[] seconds = { 20f, 18f, 15f };
            return c => ActiveBuffs.SetJumps(c, jumps[tier], seconds[tier]);
        });

        Family("pluma", BuffCategory.Movilidad, new[]
        {
            ("Pluma",     "Caes despacio",              10f),
            ("Pluma Max", "Caes muy despacio",          14f),
            ("Ingrávido", "Casi no te pesa el cuerpo",  18f),
        }, tier =>
        {
            int[] amount = { 2, 3, 5 };
            float[] seconds = { 10f, 14f, 18f };
            return c => c.refs.afflictions.AddAffliction(
                new Affliction_LowGravity(amount[tier], seconds[tier]));
        });

        Family("impulso", BuffCategory.Movilidad, new[]
        {
            ("Impulso",       "Te empuja hacia arriba",       0f),
            ("Impulso Plus",  "Te lanza bastante arriba",     0f),
            ("Catapulta",     "Te dispara hacia el cielo",    0f),
        }, tier =>
        {
            float[] force = { 700f, 1100f, 1700f };
            return c => ActiveBuffs.Launch(c, force[tier]);
        });

        Family("zancada", BuffCategory.Movilidad, new[]
        {
            ("Zancada",      "Correr gasta la mitad",     20f),
            ("Zancada Plus", "Correr casi no gasta",      22f),
            ("Incansable",   "Correr no gasta nada",      25f),
        }, tier =>
        {
            float[] cost = { 0.5f, 0.2f, 0.02f };
            float[] seconds = { 20f, 22f, 25f };
            return c => PlayerBuff.Grant(c, 1f, cost[tier], seconds[tier]);
        });

        // ------------------------------------------------------------------- Escalada

        Family("tiza", BuffCategory.Escalada, new[]
        {
            ("Manos de tiza", "Trepar cuesta menos",       18f),
            ("Tiza Plus",     "Trepar cuesta mucho menos", 30f),
            ("Tiza infinita", "Trepar apenas cuesta",      45f),
        }, tier =>
        {
            float[] seconds = { 18f, 30f, 45f };
            return c => c.refs.afflictions.AddAffliction(
                new Affliction_ClimbingChalk { totalTime = seconds[tier] });
        });

        Family("brazos", BuffCategory.Escalada, new[]
        {
            ("Brazos largos",  "Agarras compañeros del doble de lejos", 25f),
            ("Brazos de mono", "Del triple de lejos",                   25f),
            ("Brazos de grúa", "Del quíntuple de lejos",                25f),
        }, tier =>
        {
            float[] reach = { 2f, 3f, 5f };
            return c => ActiveBuffs.SetGrabReach(c, reach[tier], 25f);
        });

        Family("agarre", BuffCategory.Escalada, new[]
        {
            ("Agarre firme",  "Trepar es gratis un rato",   8f),
            ("Agarre férreo", "Trepar es gratis",           14f),
            ("Manos de acero","Trepar es gratis, y mucho",  22f),
        }, tier =>
        {
            float[] seconds = { 8f, 14f, 22f };
            return c => c.refs.afflictions.AddAffliction(
                new Affliction_InfiniteStamina { totalTime = seconds[tier] });
        });

        // -------------------------------------------------------------- Supervivencia

        Family("termo", BuffCategory.Supervivencia, new[]
        {
            ("Termo",       "Inmune al frío",             25f),
            ("Termo Plus",  "Inmune al frío, más rato",   50f),
            ("Hoguera",     "Inmune al frío mucho rato",  90f),
        }, tier =>
        {
            float[] seconds = { 25f, 50f, 90f };
            return c => ActiveBuffs.KeepWarm(c, seconds[tier]);
        });

        Family("comido", BuffCategory.Supervivencia, new[]
        {
            ("Bien comido", "No pasas hambre",             45f),
            ("Banquete",    "No pasas hambre en un rato",  90f),
            ("Despensa",    "Olvídate del hambre",         160f),
        }, tier =>
        {
            float[] seconds = { 45f, 90f, 160f };
            return c => c.refs.afflictions.AddAffliction(
                new Affliction_NoHunger { totalTime = seconds[tier] });
        });

        Family("invencible", BuffCategory.Supervivencia, new[]
        {
            ("Coraza",     "Nada te hace daño",              3f),
            ("Invencible", "Nada te hace daño, más rato",    6f),
            ("Inmortal",   "Nada te hace daño en un buen rato", 10f),
        }, tier =>
        {
            float[] seconds = { 3f, 6f, 10f };
            return c => c.refs.afflictions.AddAffliction(
                new Affliction_Invincibility { totalTime = seconds[tier] });
        });

        // Los que no escalan: una curación total no puede ser más total.
        Single("purga", "Purga", "Te quita veneno, frío y esporas",
               BuffCategory.Supervivencia, BuffRarity.Comun, 0f,
               c => c.refs.afflictions.AddAffliction(new Affliction_ClearAllStatus()));

        Single("curacion", "Curación total", "Vuelves a estar entero",
               BuffCategory.Supervivencia, BuffRarity.Raro, 0f,
               c => c.refs.afflictions.AddAffliction(new Affliction_HealAll()));

        Single("escudo", "Escudo", "Aguanta un golpe y se rompe",
               BuffCategory.Supervivencia, BuffRarity.Raro, 0f,
               c => c.refs.afflictions.AddAffliction(new Affliction_BingBongShield()),
               persistent: "hasta que te den");

        // ------------------------------------------------------------------- Especial

        Single("tormenta", "Llamar a la tormenta",
               "Desatas el viento sobre toda la montaña",
               BuffCategory.Especial, BuffRarity.Epico, 0f, _ => Storm.Summon());
    }

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
