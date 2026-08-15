using BepInEx.Configuration;
using UnityEngine;

namespace ScoutDances.Weapons;

/// <summary>
/// Todos los ajustes de un arma concreta. Cada una tiene su propia sección en el config.
/// </summary>
/// <remarks>
/// Antes esto eran variables sueltas en <c>Plugin</c> porque solo había una pistola. Con
/// dos (y las 12 del pack esperando) cada arma necesita sus propios valores, así que van
/// agrupados aquí y en secciones separadas del fichero de configuración.
/// </remarks>
/// <summary>
/// Lo que hace falta para COLOCAR un arma en la mano, sea del tipo que sea.
/// </summary>
/// <remarks>
/// <see cref="WeaponAim"/> y <see cref="WeaponTuner"/> solo necesitan esto, no el arma
/// entera. Sacarlo a una interfaz permite que el blaster —que tiene ajustes propios por
/// sus dos modos y no cabe en <see cref="WeaponDefinition"/>— se coloque y se ajuste con
/// F3 igual que las demás.
///
/// Sin esto el blaster no aparecía en la lista de armas, <c>Plugin.FindWeapon</c> devolvía
/// null, y el arma se anclaba con offset cero: pegada a la cámara, parpadeando, invisible
/// en el espejo y con los deslizadores de F3 sin efecto.
/// </remarks>
internal interface IWeaponPlacement
{
    string Id { get; }
    ConfigEntry<string> DisplayName { get; }
    ConfigEntry<float> Length { get; }
    ConfigEntry<Vector3> Offset { get; }
    ConfigEntry<Vector3> Rotation { get; }
}

internal class WeaponDefinition : IWeaponPlacement
{
    internal string Id { get; }
    string IWeaponPlacement.Id => Id;
    ConfigEntry<string> IWeaponPlacement.DisplayName => DisplayName;
    ConfigEntry<float> IWeaponPlacement.Length => Length;
    ConfigEntry<Vector3> IWeaponPlacement.Offset => Offset;
    ConfigEntry<Vector3> IWeaponPlacement.Rotation => Rotation;

    internal ConfigEntry<string> DisplayName = null!;
    internal ConfigEntry<string> Model = null!;
    internal ConfigEntry<string> BaseItem = null!;
    internal ConfigEntry<int> Ammo = null!;
    internal ConfigEntry<float> Damage = null!;
    internal ConfigEntry<float> Range = null!;
    internal ConfigEntry<float> CastMultiplier = null!;
    internal ConfigEntry<float> ShotVolume = null!;
    internal ConfigEntry<float> Knockback = null!;
    internal ConfigEntry<float> Recoil = null!;
    internal ConfigEntry<float> Length = null!;
    internal ConfigEntry<Vector3> Offset = null!;
    internal ConfigEntry<Vector3> Rotation = null!;
    internal ConfigEntry<string> ShotSound = null!;
    internal ConfigEntry<string> HitEffect = null!;
    internal ConfigEntry<float> KnockbackRadius = null!;
    internal ConfigEntry<bool> MuteBase = null!;
    internal ConfigEntry<float> FloatSeconds = null!;
    internal ConfigEntry<string> AuraEffect = null!;
    internal ConfigEntry<bool> SwapPositions = null!;

    /// Qué acción monta esta arma. "Disparo" es la de toda la vida.
    internal ConfigEntry<string> Kind = null!;

    internal WeaponDefinition(string id) => Id = id;

    internal static WeaponDefinition Create(
        ConfigFile config, string id, string displayName, string model,
        int ammo, float damage, float castMultiplier, float shotVolume,
        float knockback, float recoil, float length, Vector3 offset,
        string shotSound = "", string hitEffect = "", float knockbackRadius = 0f,
        bool muteBase = false, float floatSeconds = 0f, string auraEffect = "",
        bool swapPositions = false, string kind = "Disparo")
    {
        var section = "Arma." + id;
        var definition = new WeaponDefinition(id)
        {
            DisplayName = config.Bind(section, "Name", displayName,
                "Nombre visible. OJO: el itemID sale de un hash de este nombre, así que si " +
                "lo cambias TODOS los jugadores tienen que cambiarlo igual."),

            Model = config.Bind(section, "Model", model,
                "Prefab del bundle. Disponibles: pistol_001, rifle_001, shotgun_001, " +
                "sniper_rifle_001, axe_001, baseball_bat_001, hockey_stick_001, knife_001."),

            BaseItem = config.Bind(section, "BaseItem", "Bugle_Scoutmaster",
                "Item del juego que se clona; de él solo se aprovecha la pose de agarre."),

            Ammo = config.Bind(section, "Ammo", ammo,
                new ConfigDescription("Balas. Al gastarse, el arma desaparece.",
                    new AcceptableValueRange<int>(1, 100))),

            Damage = config.Bind(section, "Damage", damage,
                new ConfigDescription("Injury por impacto. El KO llega al pasar de 0.99.",
                    new AcceptableValueRange<float>(0f, 2f))),

            Range = config.Bind(section, "Range", 60f,
                new ConfigDescription("Alcance en metros.",
                    new AcceptableValueRange<float>(5f, 300f))),

            CastMultiplier = config.Bind(section, "CastTimeMultiplier", castMultiplier,
                new ConfigDescription("Multiplica el tiempo de carga del item base. " +
                    "CERO es un caso especial y no 'muy rápido': el juego se salta la carga " +
                    "entera, no dibuja el círculo de progreso y avisa cada frame mientras " +
                    "mantengas el botón. Es lo que necesitan las armas de chorro continuo.",
                    new AcceptableValueRange<float>(0f, 3f))),

            ShotVolume = config.Bind(section, "ShotVolume", shotVolume,
                new ConfigDescription("Volumen del disparo.",
                    new AcceptableValueRange<float>(0f, 1f))),

            Knockback = config.Bind(section, "KnockbackForce", knockback,
                new ConfigDescription("Fuerza del empujón. De referencia, el emote de " +
                    "voltereta del juego usa 200.",
                    new AcceptableValueRange<float>(0f, 3000f))),

            Recoil = config.Bind(section, "Recoil", recoil,
                new ConfigDescription("Empujón hacia atrás que recibe QUIEN DISPARA. " +
                    "0 = sin retroceso.",
                    new AcceptableValueRange<float>(0f, 5000f))),

            Length = config.Bind(section, "ModelLength", length,
                new ConfigDescription("Longitud del arma en metros. El modelo se mide solo.",
                    new AcceptableValueRange<float>(0.05f, 6f))),

            Offset = config.Bind(section, "ModelOffset", offset,
                "Posición del arma respecto a la vista. Ajústalo en vivo con F3."),

            Rotation = config.Bind(section, "ModelRotation", Vector3.zero,
                "Rotación extra del modelo, en grados."),

            ShotSound = config.Bind(section, "ShotSound", shotSound,
                "Sonido del disparo de ESTA arma, del bundle. Vacío = el general de [Armas]."),

            HitEffect = config.Bind(section, "HitEffect", hitEffect,
                "Partícula en el punto de impacto. Vacío = la sangre de siempre."),

            KnockbackRadius = config.Bind(section, "KnockbackRadius", knockbackRadius,
                new ConfigDescription("Radio del empujón. 0 = el general de [Armas]. El " +
                    "juego usa 400 en su trampa de flechas, que reparte el impulso por " +
                    "todo el cuerpo y lo manda volando en vez de doblarlo por la mitad.",
                    new AcceptableValueRange<float>(0f, 800f))),

            FloatSeconds = config.Bind(section, "FloatSeconds", floatSeconds,
                new ConfigDescription("Segundos que el objetivo se queda sin gravedad. " +
                    "0 = esta arma no lo hace.",
                    new AcceptableValueRange<float>(0f, 60f))),

            AuraEffect = config.Bind(section, "AuraEffect", auraEffect,
                "Aura que acompaña al objetivo mientras flota. Vacío = ninguna."),

            Kind = config.Bind(section, "Kind", kind,
                new ConfigDescription("Qué hace el arma.",
                    new AcceptableValueList<string>("Disparo", "Iman", "Portales", "Granada",
                                                    "Varita", "Espejo"))),

            SwapPositions = config.Bind(section, "SwapPositions", swapPositions,
                "El tirador y quien recibe el disparo se cambian el sitio."),

            MuteBase = config.Bind(section, "MuteBase", muteBase,
                "Silencia los sonidos del item que se clona. Sirve para quitar el corneteo " +
                "mientras se carga el disparo."),
        };

        return definition;
    }
}

/// <summary>Marca un arma con la definición de la que salió, para que F3 sepa cuál tocar.</summary>
internal class WeaponTag : MonoBehaviour
{
    public string DefinitionId = "";
}

/// <summary>
/// Guarda cuánto hay que descontar al offset para compensar el pivote del modelo.
/// </summary>
/// <remarks>
/// El pivote de un modelo de la Asset Store está donde lo dejó el artista, casi nunca en
/// el centro del arma. <c>PlaceModel</c> lo compensa restando el centro de la caja
/// envolvente, y esa resta es la que hace que el offset signifique "dónde va el arma"
/// y no "dónde va un punto arbitrario del fichero .fbx".
///
/// Va en un componente y no en una variable suelta porque el valor depende de la escala
/// y la rotación, cambia al mover los sliders de F3, y tiene que viajar con cada copia
/// instanciada del arma.
///
/// Público a propósito: Unity solo serializa campos públicos o [SerializeField] al
/// instanciar, y con "internal" llegaba a cero en las copias.
/// </remarks>
internal class WeaponPivot : MonoBehaviour
{
    public Vector3 Compensation;
}
