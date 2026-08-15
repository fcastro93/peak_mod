using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace ScoutDances;

/// <summary>
/// Pone al día los ajustes al actualizar el mod, sin tocar lo que es tuyo.
/// </summary>
/// <remarks>
/// BepInEx guarda en disco todo lo que enlaza una vez, y ese fichero MANDA sobre los
/// valores por defecto del código. Es lo correcto para lo que ha elegido el jugador, pero
/// convierte cada actualización en un problema: cambiamos un valor por defecto, y a quien
/// ya había jugado no le llega. Hasta ahora la única salida era borrar el fichero a mano,
/// lo que se llevaba por delante los 14 sonidos configurados en el kiosco.
///
/// Aquí se guarda un número de versión dentro del propio config. Cuando no coincide con el
/// del mod, se devuelven a su valor por defecto SOLO los ajustes técnicos y se dejan
/// intactas las secciones que contienen decisiones del jugador.
///
/// El criterio para no tocar algo es que no lo hayamos elegido nosotros: los sonidos son
/// suyos, y la colocación de las armas se la ha calibrado él con F3.
/// </remarks>
internal static class ConfigMigration
{
    /// Se sube cuando cambian valores por defecto que la gente debe recibir sí o sí.
    /// 4: el doble de maletas, armas del mod a Epic y power-ups reajustados.
    internal const int Version = 4;

    /// <summary>
    /// Secciones que NUNCA se tocan porque guardan decisiones del jugador.
    /// </summary>
    static readonly string[] Protected =
    {
        "Sonidos",      // los 14 enlaces de myinstants y sus volúmenes
        "Arma.",        // posiciones calibradas a mano con F3
        "Buff.",
    };

    static ConfigEntry<int>? _stamp;

    /// <summary>Llamar ANTES de enlazar el resto de ajustes.</summary>
    internal static void ReadStamp(ConfigFile config)
    {
        _stamp = config.Bind("General", "ConfigVersion", 0,
            "Con qué versión del mod se escribió este fichero. No lo toques: sirve para " +
            "que al actualizar te lleguen los ajustes nuevos sin perder tus sonidos.");
    }

    /// <summary>
    /// Llamar DESPUÉS de enlazar todo. Devuelve cuántos ajustes se pusieron al día.
    /// </summary>
    /// <remarks>
    /// TODO va envuelto en try/catch, y no por prudencia genérica: esto corre dentro del
    /// <c>Awake</c> del plugin, y BepInEx se traga las excepciones de ahí sin decir nada.
    /// Una versión anterior de este método petaba en silencio y se llevaba por delante
    /// todo lo que venía después —los kioscos, el bucle de red— sin un solo error en el
    /// log. Poner al día unos ajustes no puede impedir que el mod arranque.
    /// </remarks>
    internal static int Apply(ConfigFile config)
    {
        try
        {
            return Migrate(config);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No pude poner al día el config ({e.GetType().Name}: " +
                                  $"{e.Message}). Se sigue con los valores que haya.");
            return 0;
        }
    }

    static int Migrate(ConfigFile config)
    {
        if (_stamp == null || _stamp.Value == Version) return 0;

        int previous = _stamp.Value;
        var updated = new List<string>();

        foreach (var definition in config.Keys.ToList())
        {
            if (definition.Section == "General") continue;

            if (Protected.Any(p => definition.Section.StartsWith(p, System.StringComparison.Ordinal)))
                continue;

            // Por entrada, y con red: el indexador da la entrada sin exigir saber su tipo.
            // Pedirla como ConfigEntry<object> reventaba, porque BepInEx comprueba el tipo
            // real y ninguna entrada es de tipo object.
            try
            {
                var entry = config[definition];
                var fallback = entry?.DefaultValue;

                if (entry == null || fallback == null) continue;
                if (Equals(entry.BoxedValue, fallback)) continue;

                updated.Add($"{definition.Section}/{definition.Key}: " +
                            $"{entry.BoxedValue} -> {fallback}");
                entry.BoxedValue = fallback;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Me salto {definition.Section}/{definition.Key}: {e.Message}");
            }
        }

        _stamp.Value = Version;
        config.Save();

        Plugin.Log.LogInfo($"Config actualizado de la versión {previous} a la {Version}: " +
                           $"{updated.Count} ajuste(s) al día. Tus sonidos y las posiciones " +
                           "de las armas se han respetado.");

        foreach (var line in updated.Take(20)) Plugin.Log.LogInfo($"  {line}");

        return updated.Count;
    }
}
