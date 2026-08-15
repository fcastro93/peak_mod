# PEAK — Investigación técnica para nuestro mod custom

> Fecha: 2026-08-13 · Instalación analizada: `D:\SteamLibrary\steamapps\common\PEAK`

---

## 1. Qué es PEAK

Juego cooperativo de escalada (hasta 4 jugadores en vanilla) de **Landfall Games + Aggro Crab**.
Encarnas a un **Scout** (explorador) que debe subir una montaña generada proceduralmente por
biomas (Shore → Tropics → Alpine → Caldera → Peak). El bucle de juego gira sobre:

- **Stamina** y **estados negativos** (*afflictions*), no sobre "vida" clásica.
- **Items** consumibles y de escalada (cuerdas, pitones, comida, antídotos, mochilas).
- **Física ragdoll** compartida: los jugadores se estorban, se cargan, se lanzan.
- Partidas cortas con un mapa diario, run permadeath-ish con checkpoints en hogueras.

### Datos técnicos de la build instalada

| Dato | Valor |
|---|---|
| Versión del juego | **2.1.a** (buildid Steam `24720181`, AppID `3527290`) |
| Motor | **Unity 6000.3.15** (Unity 6.3), URP |
| Backend de scripting | **Mono** (no IL2CPP) → `Assembly-CSharp.dll` es legible/decompilable |
| Networking | **Photon PUN 2** + **Photon Voice** |
| Serialización/UI | Odin (Sirenix), DOTween, TextMeshPro, Unity Localization, Addressables |
| Librerías propias del estudio | `Zorro.*` (Core, UI, Settings, JiggleBones, PhotonUtility, ControllerSupport) |
| Empaquetado de assets | Ficheros serializados clásicos (`resources.assets`, `levelN`, `sharedassetsN`), **sin bundles en StreamingAssets** |

`Assembly-CSharp.dll` contiene **1.566 tipos**. Es el objetivo principal de nuestro modding.

---

## 2. El mod instalado: PEAK Unlimited v4.0.0

Único plugin presente en `BepInEx\plugins\PEAKUnlimited.dll`.
Autor: **glarmer** · [GitHub](https://github.com/glarmer/PEAK-Unlimited) · [Thunderstore](https://thunderstore.io/c/peak/p/glarmer/PEAK_Unlimited/)

**Qué hace:** sube el tope de jugadores de 4 a N (configurado a **20**) y arregla todo lo que
se rompe al hacerlo.

### Arquitectura (es un buen molde para copiar)

```
src/PEAKUnlimited/
├── Plugin.cs                  # BaseUnityPlugin: aplica cada parche por separado
├── Configuration/             # ConfigurationHandler + UI de config in-game (tecla F2)
├── Model/GameInfo/            # DTOs de logging
├── Patches/                   # 1 fichero = 1 parche Harmony
└── Util/                      # logger con categorías, utilidades de conteo
```

Puntos clave del diseño:

- **BepInEx 5.4.23.3** + **HarmonyX**, `TargetFramework: netstandard2.1`.
- Usa `[BepInAutoPlugin]` (paquete `Hamunii.BepInEx.AutoPlugin`) para generar el atributo
  `BepInPlugin` desde el `.csproj`.
- Usa **`BepInEx.AssemblyPublicizer.MSBuild`** con `Publicize="true"` sobre `Assembly-CSharp.dll`:
  eso hace públicos todos los campos/métodos privados del juego en tiempo de compilación.
  **Esto es imprescindible** para modding cómodo de PEAK.
- Referencia las DLL del juego directamente desde `PEAK_Data\Managed\*.dll`.
  ⚠️ El `.csproj` tiene la ruta **hardcodeada** a `C:\Program Files (x86)\Steam\...`;
  en esta máquina el juego está en `D:\SteamLibrary\...` → habrá que cambiarla.
- Aplica los parches **uno a uno** (`_harmony.PatchAll(typeof(X))`) en vez de un `PatchAll()`
  global, para poder activarlos/desactivarlos por config. Buena práctica, la copiamos.
- `OnDestroy` hace `_harmony.UnpatchSelf()` → hot-reload limpio.

### Ejemplos de parches representativos

```csharp
// Cambiar una propiedad estática del juego: prefix que sustituye el resultado
[HarmonyPatch(typeof(NetworkingUtilities), nameof(NetworkingUtilities.MAX_PLAYERS), MethodType.Getter)]
[HarmonyPrefix]
static bool Prefix(ref int __result)
{
    __result = ConfigurationHandler.ConfigMaxPlayers.Value;
    return false;   // no ejecutar el original
}
```

```csharp
// Spawnear objetos extra: postfix + guard de "solo el host"
[HarmonyPatch(typeof(Campfire), nameof(Campfire.OnEnable))]
[HarmonyPostfix]
static void Postfix(Campfire __instance)
{
    if (!PhotonNetwork.IsMasterClient) return;   // <-- patrón obligatorio en PEAK
    Utility.SpawnMarshmallows(PhotonNetwork.CurrentRoom.PlayerCount, __instance);
}
```

**Lección de multijugador:** casi todo lo que crea/modifica estado del mundo se hace
**solo en el MasterClient** y se propaga por Photon. Los mods "client-side" (emotes, UI,
cosméticos) no necesitan eso.

### Configuración actual (`BepInEx\config\PEAKUnlimited.cfg`)

`MaxPlayers = 20`, marshmallows y mochilas extra activadas, `VoiceFix = false`,
`AllScoutsInHelicopter = false`, menú en `F2`.

> ⚠️ En el log hay un warning: `VisibleLogTypes` está **vacío** en el .cfg y provoca
> `Invalid enum name found while setting up Logger: ''`. Inofensivo, pero se arregla
> poniendo `VisibleLogTypes = PatchingLogic, NetworkingLogic`.

---

## 3. El ecosistema de modding de PEAK

### Documentación oficial de la comunidad

- **Wiki de modding**: <http://peak.modding-community.com/> (antes `peakmodding.github.io`)
- **Thunderstore** (repo principal de mods): <https://thunderstore.io/c/peak/>
- **Nexus Mods**: <https://www.nexusmods.com/peak>
- Discord: *PEAK Modding* (canal `#peak-lib`)

### PEAKLib — la API comunitaria (lo que vamos a usar)

Repo: <https://github.com/PEAKModding/PEAKLib> · NuGet: `PEAKModding.PEAKLib.*`

| Módulo | Para qué sirve |
|---|---|
| **Core** | Registro de contenido, carga de bundles (`.peakbundle`), Network Prefab API, lista de mods del host |
| **Items** | Items custom + *Item Acceptor API* (dar objetos a otros objetos) |
| **Stats** | **Status Effects API** — crear afflictions nuevas |
| **UI** | UI custom, localización (`MenuAPI.CreateLocalization`) |
| **ModConfig** | Menú de configuración in-game |
| **UnityReferences** | Paquete para meter en `Assets/` del proyecto Unity ripeado |

### PEAKEmoteLib — la API de emotes (clave para "bailes")

Repo: <https://github.com/WaporVave/PEAKEmoteLib> · NuGet: `PEAKEmoteLib`
Mod de ejemplo: *Fortnite Default Dance Emote*. Ya tiene **15 mods dependientes**.

### Mods más descargados (panorama)

r2modman / Gale (managers) · BepInExPack PEAK · loaforcsSoundAPI · MonoDetour ·
**PEAK Unlimited** · **PEAKLib Core/Items** · Piggyback · More Customizations ·
EasyBackpack · SmoreSkinColors · Too Many Hats · MoreCustomHats · PushMod · Everest.

Referencias directas para nuestros objetivos:
- **Armas** → [Knife Item](https://thunderstore.io/c/peak/p/Sapphire009/Knife_Item/)
  (`Knife.dll` + `Knife.peakbundle`, depende de PEAKLib Items/UI/Core). Es el patrón exacto.
- **Bailes** → [PEAKEmoteLib](https://thunderstore.io/c/peak/p/WaporVave/PEAKEmoteLib/),
  [Fortnite Default Dance](https://thunderstore.io/c/peak/p/WaporVave/Fortnite_Default_Dance_Emote/),
  [Twerk Emote](https://thunderstore.io/c/peak/p/WaporVave/Twerk_Emote/),
  [DanceTillYouDrop](https://thunderstore.io/c/peak/p/Elteeb96/DanceTillYouDrop/).

---

## 4. HALLAZGO CRÍTICO: el rig del Scout es **Generic**, no Humanoid

Esto condiciona todo el plan de "usar animaciones compradas en la Unity Asset Store".

Analicé el clip `A_Scout_Emote_Dance2` directamente de `resources.assets`:

- El clip **no tiene curvas humanoides (muscle curves)**. Tiene **62 bindings por *path* de
  transform** (`typeID = 4`, Transform) → es un clip **genérico atado a rutas de huesos**.
- **No existe ningún asset `Avatar`** en los ficheros del juego → el `Animator` del Scout
  corre **sin Avatar humanoide**.

**Consecuencia:** una animación humanoide de la Asset Store (o de Mixamo) **NO se
retargetea automáticamente** al Scout. Hay que convertirla.

### Esqueleto real del Scout (reconstruido, 62/62 rutas verificadas)

```
Armature/Hip
├── Hip_L/Leg_L/Knee_L/Foot_L
├── Hip_R/Leg_R/Knee_R/Foot_R
└── Mid
    └── AimJoint            <- driven por la mirada, ojo al animarlo
        └── Torso
            ├── Head
            ├── S_Shoulder_L/Arm_L/Elbow_L/Hand_L
            │   ├── Hand_Upper_L/{Index,Middle,Pinky}_{1,2,3}_L
            │   └── Thumb_1_L/Thumb_2_L/Thumb_3_L
            └── S_Shoulder_R/Arm_R/Elbow_R/Hand_R
                ├── Hand_Upper_R/{Index,Middle,Pinky}_{1,2,3}_R
                └── Thumb_R_1/Thumb_R_2/Thumb_R_3     <- ¡nomenclatura asimétrica!
```

Notas:
- Sin cuello, sin dedos de los pies, sin cadena de columna larga: `Hip → Mid → AimJoint → Torso → Head`.
- Los hombros (`S_Shoulder_L/R`) **no se animan** en los emotes; solo desde `Arm_*` hacia abajo.
- Ojo con la asimetría del pulgar derecho (`Thumb_R_1` vs `Thumb_1_L`) — rompe scripts ingenuos de mirroring.

### Además: las animaciones disparan sonido

El clip también contiene 16 bindings de `GameObject.m_IsActive` (`typeID = 1`) sobre nodos como
`SFX/Movement/SFX Jump`, `SFX/Movement/Step/Step Manager Walk`... → **las animaciones activan
y desactivan emisores de SFX**. Podemos usar el mismo truco para meter sonido a nuestros bailes.

### Cómo usar entonces las animaciones compradas → pipeline IMPLEMENTADO

**Sin AssetRipper.** No hace falta ripear el juego: la pose de reposo de los huesos
está en `resources.assets` y se puede extraer con UnityPy, así que reconstruimos el
esqueleto en Unity desde cero.

1. `scout_skeleton.json` — 52 huesos con TRS local real, extraído del prefab del Scout.
2. `ScoutRigBuilder.cs` reconstruye el esqueleto en la escena y le monta encima un
   **Avatar Humanoid** con `AvatarBuilder.BuildHumanAvatar`. El rig tiene exactamente
   los huesos mínimos que Unity exige.
3. Se importa el pack comprado como **Humanoid**.
4. `EmoteBaker.cs` usa `AnimationMode.SampleAnimationClip` (que hace el retargeting) y
   graba el resultado con `UnityEditor.Animations.GameObjectRecorder` → sale un
   **clip genérico con curvas de Transform por path**, el formato que PEAK consume.
5. `BuildEmoteBundle.cs` empaqueta los clips en un AssetBundle.
6. El plugin lo carga y registra los emotes con PEAKEmoteLib.

Ver [`unity-tools/`](../unity-tools/) y el [README](../README.md).

> El autor de PEAKEmoteLib hizo sus bailes **keyframe a keyframe a mano** ("me llevó horas
> recrear el Fortnite default dance"). Nuestro pipeline de retarget + bake nos ahorra eso
> y es lo que hace viable usar packs comprados.

---

## 5. Cómo se añaden BAILES (emotes)

### El truco central

Los `AnimatorController` de Unity están **precompilados**: no se pueden añadir estados nuevos
en runtime. PEAKEmoteLib lo resuelve con un **`AnimatorOverrideController`** que **pisa el clip
de un estado vanilla concreto**:

```csharp
public const string OverrideState = "A_Scout_Emote_Dance2";   // el estado sacrificado
```

Flujo completo:

1. `EmoteWheel.Start` → **postfix** que añade páginas nuevas a la rueda de emotes
   (8 slices por página) y crea `EmoteWheelData` con nombre + sprite de cada emote custom.
2. El jugador elige → el juego llama al RPC `CharacterAnimations.RPCA_PlayRemove(emoteName)`.
   **Es un RPC de Photon**, así que la sincronización multijugador es gratis.
3. **Prefix** sobre ese RPC: si `emoteName` empieza por `PEAKEmoteLib_`, mete nuestro
   `AnimationClip` en `overrideController["A_Scout_Emote_Dance2"]` y reescribe `emoteName`
   al nombre del estado vanilla. El resto del código vanilla sigue igual.
4. Estado por personaje guardado en un `ConditionalWeakTable<CharacterAnimations, Holder>`
   (no se puede añadir campos a clases del juego).
5. `GUIManager.UpdateEmoteWheel` → postfix para cambiar de página con la rueda del ratón.

⚠️ **Implicación multijugador:** los demás jugadores necesitan el mod instalado para ver
el baile. Sin él verán el `Dance2` vanilla.

### API para registrar un emote

```csharp
[BepInDependency(PEAKEmoteLib.Plugin.Id)]
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        var bundle = AssetBundle.LoadFromFile(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "mis_bailes_bundle"));

        var clip = bundle.LoadAsset<AnimationClip>("Assets/Mi_Baile.anim");
        var icon = bundle.LoadAsset<Texture2D>("Assets/mi_baile_icon.png");

        var emote = new Emote(
            "fcastro_MiBaile",              // único GLOBALMENTE → prefijo con nuestro nick
            clip,
            icon,
            type: Emote.EmoteType.OneShot,  // OneShot = se reproduce entera 1 vez
                                            // Vanilla = loopea 5s
            disableIK: true);               // desactiva el IK de pies durante el emote

        emote.AddLocalization("Mi Baile", LocalizedText.Language.English);
        emote.AddLocalization("Mi Baile", LocalizedText.Language.Spanish);

        this.RegisterEmote(emote);
    }
}
```

Iconos: PNG ~256×256 con fondo transparente y margen generoso.

---

## 6. Cómo se añaden ARMAS / ITEMS

### Lo primero: PEAK no tiene "armas" ni barra de vida

No hay sistema de daño clásico. El "daño" son **afflictions** que se acumulan y te dejan
inconsciente (*pass out*) o te matan.

`CharacterAfflictions.STATUSTYPE`:
```
Injury, Hunger, Cold, Poison, Crab, Curse, Drowsy, Weight,
Hot, Thorns, Spores, Web, Arrow, Petrify, FlyTrap
```

`AfflictionType` (efectos aplicables por items):
```
PoisonOverTime, InfiniteStamina, FasterBoi, Exhausted, Glowing, ColdOverTime, Chaos,
AdjustStatus, ClearAllStatus, PreventPoisonHealing, AddBonusStamina, DrowsyOverTime,
AdjustStatusOverTime, Sunscreen, BingBongShield, ZombieBite, Invincibility, LowGravity,
Blind, Numb, ClimbingChalk, NoHunger, HealAll, DoubleJumpAmulet, RadiateInfiniteStam,
MassSuperJump
```

Métodos útiles de `CharacterAfflictions`: `AddStatus`, `SubtractStatus`, `AdjustStatus`,
`SetStatus`, `AddPoison`, `AddCold`, `AddCurse`, `AddDrowsy`, `Die`, `DieToRitualDagger`,
`PetrifyMore`, `ClearAllStatus`. Todo con `SyncStatusesRPC` / `PushStatuses` para la red.

Componentes vanilla que ya "hacen daño" y podemos reutilizar como plantilla:
- **`KnockOutPlayerOnImpact`** — campos `knockoutVelocity`, `damage`, `forceMult`.
  Es literalmente el "arma contundente" del juego (objetos lanzados que noquean).
- **`EventOnItemCollision`** — dispara un UnityEvent al chocar con velocidad mínima.
- **`Action_ModifyStatus`** — modifica un `STATUSTYPE` en una cantidad.
- **`Action_LaunchPlayer`**, **`Action_WarpToRandomPlayer`**, **`Action_PlayItemAnimation`**.
- `Item.RPC_SetThrownData`, `Item.lastThrownCharacter` → saber quién lanzó qué.

Así que un **arma** en PEAK = un **Item** con:
`itemActions` (primario/secundario) + un `ItemComponent` propio que, al impactar o al
usarse sobre alguien, llama a `CharacterAfflictions.AddStatus(...)` en el objetivo,
todo dentro de un RPC.

### API de `Item` (lo relevante)

`Item` expone eventos que son los ganchos de nuestra arma:
```
OnPrimaryStarted / OnPrimaryHeld / OnPrimaryFinishedCast / OnPrimaryReleased / OnPrimaryCancelled
OnSecondaryStarted / OnSecondaryHeld / OnSecondaryFinishedCast / OnSecondaryCancelled
OnConsumed, OnStateChange, OnScrolled...
```
Y campos: `mass`, `throwForceMultiplier`, `carryWeight`, `totalUses`, `itemTags`,
`canUseOnFriend`, `mustUseOnFriend`, `showUseProgress`, `blocksSprint`, `rightHandOnly`.

### Flujo de creación con PEAKLib.Items

1. Ripear PEAK a Unity y meter **PEAKLib.UnityReferences** en `Assets/`.
2. Copiar un prefab de Item vanilla a `Assets/_Mod/` y modificarlo
   (el root **debe** llevar el componente `Item`).
3. `Create → PEAKLib → ItemContent`, asignar el prefab.
4. Marcar el AssetBundle como `miarma.peakbundle`
   (o `miarma.autoload_peakbundle` si no queremos plugin en C#).
5. En el plugin:

```csharp
[BepInAutoPlugin]
[BepInDependency(CorePlugin.Id)]
[BepInDependency(ItemsPlugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    void Awake()
    {
        this.LoadBundleWithName("miarma.peakbundle", bundle =>
        {
            var content = bundle.LoadAsset<UnityItemContent>("MiArmaContent");
            content.ItemPrefab.AddComponent<MiArma>();   // los scripts NO van en el bundle
            bundle.Mod.RegisterContent();
        });
    }
}
```

6. El comportamiento custom hereda de **`ModItemComponent`** (PEAKLib), que da
   **datos de item sincronizados por red en JSON**:

```csharp
public class MiArma : ModItemComponent
{
    public class Data { public int usosRestantes = 10; }

    public override void OnInstanceDataSet() { /* refrescar visuales */ }

    void Golpear()
    {
        TryGetModItemDataFromJson<Data>(out var d);
        SetModItemDataFromJson(new Data { usosRestantes = d.usosRestantes - 1 });
    }
}
```

### Estados/afflictions custom (PEAKLib.Stats)

```csharp
var status = new Status {
    Name = "Sangrado", Color = Color.red, MaxAmount = 2f, AllowClear = true,
    ReductionCooldown = 1.5f, ReductionPerSecond = 0.01f,
    Icon = ..., SFX = new SFX_Instance { clips = [clip], settings = new() },
    Update = (afflictions, st) => { /* lógica por frame */ },
};
new StatusContent(status).Register(Definition);
```

---

## 7. Setup necesario en esta máquina

| Requisito | Estado |
|---|---|
| PEAK 2.1.a | ✅ instalado en `D:\SteamLibrary\...` |
| BepInEx 5.4.23.3 | ✅ instalado y funcionando |
| Unity Hub + Editor **6000.4.4f1** | ✅ instalado |
| Git | ✅ |
| Python 3.12 / Node | ✅ |
| **.NET SDK 10+** | ❌ **FALTA** — imprescindible para compilar cualquier mod |
| Visual Studio / MSBuild | ❌ no hay (basta con .NET SDK + VS Code) |
| IDE (Rider / VS Code / VS) | pendiente de elegir |
| Decompilador (ILSpy / dnSpy / dotPeek) | ❌ **FALTA** — muy recomendable |
| **AssetRipper 1.1.13** | ❌ falta, para ripear PEAK a Unity |

### Una vez instalado el SDK

```bash
dotnet new install PEAKModding.BepInExTemplate
dotnet new peakmod --output MiMod --guid fcastro.MiMod --ts-team <equipo-thunderstore>
cd MiMod
dotnet build -c Release -v d        # sale un .zip en ./artifacts/thunderstore/
```

### ⚠️ Aviso sobre la versión de Unity para ripear

La wiki de modding recomienda **Unity 6000.0.36f1 / 6000.0.62f1** para el proyecto ripeado,
pero eso está escrito para el **parche 1.54.a**. Nuestra build es **2.1.a con Unity 6000.3.15**.
Aquí hay **6000.4.4f1** instalado. Habrá que verificar con AssetRipper qué versión pide
realmente y probablemente instalar la 6000.3.x que toque desde Unity Hub.

---

## 8. Sobre usar assets comprados en la Unity Asset Store

Puntos a tener claros antes de publicar nada:

- La **EULA estándar de la Asset Store** permite distribuir los assets **integrados en un
  producto** (un mod es un producto), pero **prohíbe redistribuir el asset en bruto** o de
  forma que se pueda extraer y reutilizar. Un AssetBundle es un formato compilado —
  aceptable en la práctica, pero conviene revisar la licencia concreta de cada pack
  (algunos son "Extension Asset" y tienen restricciones extra).
- Si el mod es **gratuito y público**, con licencia estándar suele estar bien;
  si lleva packs de terceros con licencias raras, mejor mantenerlo privado o
  usar solo assets propios.
- Para **animaciones**, el pipeline de §4 hornea un clip **nuevo** atado al esqueleto del
  Scout: el resultado es una obra derivada, no el asset original. Es la vía más limpia.

---

## 9. Plan — estado actual

### ✅ Fase 0 — Herramientas
.NET SDK 10.0.400 + `ilspycmd` 11.0 instalados. AssetRipper **ya no hace falta**.

### ✅ Fase 1 — Proyecto del plugin
`ScoutDances/` creado con la plantilla oficial `peakmod`, compila y se auto-despliega
a `BepInEx/plugins/`. Dependencias (PEAKEmoteLib, PEAKLib Core/UI, SoftDependencyFix,
MonoDetour) instaladas en el juego.

### ✅ Fase 2 — Herramientas de Unity
`ScoutRigBuilder` + `EmoteBaker` + `BuildEmoteBundle` escritos. Pendiente de probarlos
en el editor.

### ⏳ Fase 3 — Primer baile (objetivo A)
Pack **Human Dance Animations FREE** (Kevin Iglesias) descargado: 6 bailes por género.
Falta: montar el proyecto Unity, hornear y probar en partida.

### Fase 4 — Primera "arma" (nuestro objetivo B)
1. Copiar el prefab de un item vanilla contundente, sustituir el modelo por el nuestro.
2. `ItemContent` + `.peakbundle`.
3. `ModItemComponent` propio: al impactar con velocidad, `AddStatus(Injury, X)` al objetivo
   vía RPC, con guard de MasterClient donde toque.
4. Opcional: status custom propio con PEAKLib.Stats (p. ej. "Sangrado").

### Fase 5 — Empaquetado
`thunderstore.toml` + `tcli`, o distribución privada como zip para el grupo.

### Decisiones pendientes (para hablar)
- ¿Mod público en Thunderstore o privado para el grupo de amigos?
- ¿Qué packs concretos de la Asset Store tienes? (determina cuánto retarget hace falta)
- ¿"Armas" con daño real PvP, o más bien de broma/utilidad? Cambia bastante el diseño.

---

## Anexos

- `docs/scout-rig.md` — esqueleto completo del Scout con hashes de ruta.
- Volcados de metadatos generados durante el análisis (scratchpad de la sesión):
  `AssemblyCSharp.dump.txt` (1.566 tipos), `PEAKUnlimited.dump.txt` (50 tipos).
- Repos clonados para consulta: PEAKLib, PEAKEmoteLib (+ su wiki), PEAK-Unlimited,
  PEAKModding.github.io.
