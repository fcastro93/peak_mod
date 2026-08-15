# PEAK — mods propios

Mods custom para **PEAK** (Landfall / Aggro Crab). Uso privado.

| Carpeta | Qué es |
|---|---|
| [docs/](docs/) | Investigación técnica del juego y su ecosistema de modding |
| [unity-tools/](unity-tools/) | Scripts de editor de Unity para convertir animaciones compradas en emotes de PEAK |
| [ScoutDances/](ScoutDances/) | El plugin BepInEx: registra los bailes y los ata a Ctrl+1…Ctrl+9 |

---

## Estado

| Pieza | Estado |
|---|---|
| Investigación del juego y del rig del Scout | ✅ |
| Toolchain (.NET SDK 10, ilspycmd) | ✅ instalado |
| Dependencias en el juego (PEAKEmoteLib + PEAKLib) | ✅ instaladas |
| Plugin `ScoutDances` (registro + hotkeys) | ✅ compila y se despliega |
| Herramientas de Unity (rig, horneado, bundle) | ✅ ejecutadas en batchmode, avatar humanoide **válido** |
| AssetBundle con 12 bailes | ✅ generado y verificado (51/51 rutas de hueso correctas) |
| Carga en el juego | ✅ 12 emotes registrados, `Ctrl+1..9` activos, 0 errores |
| **Cómo se ven los bailes en el Scout** | ⏳ falta mirarlo en partida |

Proyecto de Unity: `C:\Users\fcast\Peak_MOD` (los scripts viven en
`Assets/_PeakEmotes/Editor/`, copiados desde `unity-tools/`).

---

## El problema que resuelve `unity-tools/`

El Scout de PEAK usa un **rig genérico**: sus clips están atados a rutas de huesos
(`Armature/Hip/Mid/AimJoint/Torso/Head`) y **no existe ningún Avatar humanoide en el juego**.
Una animación humanoide de la Asset Store no se retargetea sola.

La solución de este repo, sin necesidad de AssetRipper:

```
resources.assets ──(UnityPy)──> scout_skeleton.json     52 huesos con pose de reposo real
                                        │
                     ScoutRigBuilder.cs  │  reconstruye el esqueleto en Unity
                                         ▼  + AvatarBuilder.BuildHumanAvatar
                                  Avatar HUMANOIDE del Scout
                                         │
   Dance01.fbx (humanoide) ──────────────┤  AnimationMode.SampleAnimationClip = retarget
                                         ▼
                        EmoteBaker.cs  ──> GameObjectRecorder
                                         ▼
                            Dance01.anim  (genérico, curvas por path)
                                         │
                     BuildEmoteBundle.cs  ▼
                                   scoutdances (AssetBundle)
```

---

## Workflow completo

### 1. Proyecto de Unity (una vez)

1. Crea un proyecto 3D nuevo con **Unity 6000.4.4f1** (o el que tengas).
2. Copia dentro:
   - `unity-tools/Editor/*.cs` → `Assets/_PeakEmotes/Editor/`
   - `unity-tools/scout_skeleton.json` → `Assets/_PeakEmotes/`
3. Importa el pack **Human Dance Animations FREE** (Kevin Iglesias) desde
   Package Manager → My Assets.
4. En cada FBX de `Animations/Male/Social/Dance/Steps/`, ponte en el inspector:
   `Rig → Animation Type: **Humanoid**`, y aplica.

### 2. Construir el rig y hornear

**Con Unity cerrado**, todo el pipeline de una sentada:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" `
    -batchmode -quit -nographics -projectPath "C:\Users\fcast\Peak_MOD" `
    -executeMethod PeakEmotes.EditorTools.BatchPipeline.Run -logFile batch.log
```

O desde el editor abierto, con los menús: **PEAK Emotes → 1 / 2 / 3**
(el paso 2 hornea lo que tengas seleccionado en el Project window).

⚠️ En el log tiene que aparecer **"Avatar humanoide VÁLIDO"**. Si no, el retargeting
no funciona y hay que revisar `HumanMap` en `ScoutRigBuilder.cs`.

Los clips salen en `Assets/_PeakEmotes/Baked/` y el bundle en
`Assets/AssetBundles/scoutdances`. Cópialo a
`D:\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\`
(o menú **PEAK Emotes → 4**).

(Opcional) PNGs 256×256 con fondo transparente en `Assets/_PeakEmotes/Icons/`, con
**el mismo nombre que el clip**, para que salgan en la rueda. Sin iconos, el plugin
genera discos de colores.

#### Detalles del horneado que importan

Tres cosas que el horneado tiene que corregir. Las tres se descubrieron probando en
partida y **ninguna es obvia**:

**1. Forma de las curvas.** `GameObjectRecorder` graba posición + rotación + escala de
cada hueso. Un emote vanilla solo tiene **rotaciones + la posición de `Armature/Hip`**
(45 bindings en `A_Scout_Emote_Dance2`, de los cuales uno es de posición). Grabar
posiciones sobra y además **envenena el estado**: el Animator solo escribe las
propiedades que anima alguna clip del estado activo, y las animaciones vanilla de
idle/andar solo tocan rotaciones — así que al acabar el emote nadie devuelve las
posiciones a su sitio y el Scout se queda deformado *para siempre*.

**2. Los ejes locales están permutados.** El hueso `Armature` está rotado −90° en X:

| Eje local | Eje de mundo |
|---|---|
| X | X (horizontal) |
| Y | −Z (horizontal, profundidad) |
| **Z** | **+Y (vertical)** |

Se comprueba en la pose de reposo: `Hip` local `(0, 0.1696, 0.0127)` acaba en mundo
`(0, 2.013, −0.170)`. Dar por hecho que "Y es arriba" aplana el rebote del baile y deja
la deriva hacia delante — exactamente lo contrario de lo que quieres.

**3. El retargeting no respeta la altura de reposo.** Unity deja la cadera en local
z ≈ −1.4 cuando en reposo vale +0.013: **~0,47 m por debajo**, o sea el Scout bailando
en cuclillas permanentes. Hay que desplazar la curva vertical para centrarla en la
altura de reposo.

#### Otros ajustes

- Los clips del pack son loops de ~2 s. El baker los **repite hasta ~6 s**
  (`MinSeconds`), porque como `OneShot` PEAKEmoteLib para el emote al acabar el clip
  y un baile de 2 s no se ve como un baile. Tope duro de PEAKEmoteLib: 10 s.
- Los clips del pack son loops de ~2 s. El baker los **repite hasta ~6 s**
  (`MinSeconds`), porque como `OneShot` PEAKEmoteLib para el emote al acabar el clip
  y un baile de 2 s no se ve como un baile. Tope duro de PEAKEmoteLib: 10 s.
- Huesos excluidos del grabado (`ExcludedBones`):
  - **`AimJoint`** — lo controla el sistema de mirada del juego.
  - **`S_Toe_1_L/R`, `S_Heel_L/R`** — no son articulaciones: están en la **misma
    posición local** que el tobillo `(0, 0.0837, 0)` y solo se diferencian por una
    rotación de ±86°. Son pivotes de squash/stretch para el rodado del pie.
    Mapear `S_Toe_1_*` como `LeftToes/RightToes` en el Avatar hace que Unity deduzca
    mal el eje frontal del pie y los tobillos roten solos.
- Se anula el desplazamiento **horizontal** de la cadera para que el baile no te mueva
  del sitio. La altura sí se conserva.
- Los nombres se normalizan: `HumanM@Dance01 - Loop` → `Dance01_M`.

#### Verificar un horneado

```bash
python scratchpad/verify_bundle.py   # comprueba hashes de ruta contra el rig real
```

Un clip correcto tiene ~48 bindings: **1 de posición** (`Armature/Hip`) y el resto
**rotaciones**. Si aparecen curvas de escala o posiciones en otros huesos, algo va mal.

### 3. Compartir el mod

```bash
cd ScoutDances
dotnet build -c Release        # -> artifacts/thunderstore/fcastro-ScoutDances-X.Y.Z.zip
```

Ese zip sigue el formato Thunderstore: sirve para "importar mod local" en **r2modman**
o **Gale**. El amigo tendrá que instalar aparte las dependencias (están declaradas en el
paquete y son públicas en Thunderstore).

Para quien no use gestor de mods, hay un zip **todo incluido** en `dist/`, con BepInEx,
las dependencias y el mod. Se genera con `scratchpad/pack_allinone.ps1` y se instala
descomprimiendo sobre la carpeta de PEAK.

> ⚠️ El script de empaquetado **excluye los `.cfg` y la carpeta `soundcache`** a
> propósito: el config lleva tus sonidos y volúmenes personales.

> ⚠️ **Todos los jugadores necesitan el mod.** Quien no lo tenga verá el `Dance2`
> vanilla y no oirá ningún sonido.

#### El AssetBundle tiene que ir en el paquete

El `.csproj` declara dos `ModFile`: el `.dll` y `assets/scoutdances`. Sin el bundle el
mod carga sin errores pero **no registra ni un emote**, así que hay que acordarse de
copiar el bundle nuevo a `ScoutDances/assets/` cada vez que se rehornea:

```powershell
Copy-Item "C:\Users\fcast\Peak_MOD\Assets\AssetBundles\scoutdances" `
          "ScoutDances\assets\" -Force
```

### 4. Compilar el plugin

```bash
cd ScoutDances
dotnet build -c Debug        # compila y copia el .dll a BepInEx/plugins automáticamente
```

La ruta del juego está en `Config.Build.user.props` (gitignorado).

### 5. Sonidos de myinstants.com

Además de los bailes, el mod añade **7 sonidos + un botón de corte** en su propia página
de la rueda (8 ranuras exactas). Se configuran en un **kiosco del aeropuerto** — un pie
de micro junto al kiosco de invitar amigos.

En el kiosco: buscador de myinstants, ▶ para escuchar, botones `1..7` para asignar,
un volumen por sonido, y ■ para cortar la previsualización.

#### Cómo viaja el audio

Por la red **solo viaja la ruta del MP3** (`/media/sounds/vine-boom.mp3`), nunca el
audio. Cada cliente lo descarga de myinstants y lo cachea en `plugins/soundcache/`.
Es lo que permite que cada jugador elija sus propios sonidos sin inundar Photon.

La configuración se sincroniza con **Photon Player Custom Properties**, que son por
jugador y Photon replica solas incluso a quien entre a la sala más tarde.

#### Espacialización

El `AudioSource` cuelga del mismo transform que la voz del personaje y se enruta a **su
mismo grupo de mixer** (`Voice1..4`, que Photon asigna por jugador). De ahí salen gratis
la atenuación por distancia, el reverb y el amortiguado por geometría.

Detalle: `CharacterVoiceHandler` solo asigna grupo de mixer `if (!m_character.IsLocal)`,
así que **tu propio sonido lo oyes limpio**, sin eco ni oclusión — igual que no te oyes
a ti mismo por el chat de voz. Los demás sí lo oyen con todo el tratamiento.

#### Dos detalles que no son obvios

- **El nombre en la rueda** no se puede fijar al registrar el emote: `AddLocalization`
  corre una vez al arrancar, pero el sonido de cada ranura cambia cuando lo reasignas.
  Se resuelve con un postfix sobre `EmoteWheel.Hover`, que es donde el juego escribe
  `selectedEmoteName.text`.
- **El corte va por RPC**, no en local: si solo parase tu copia, tú dejarías de oírlo
  y los demás seguirían aguantándolo.

#### El kiosco desaparecía de lejos

URP trae `smallMeshScreenPercentage = 0.5` con el GPU Resident Drawer activo: descarta
cualquier malla por debajo del 0,5 % de la pantalla, y un pie de micro fino cruza ese
umbral a pocos metros. Se desactiva desde el mod (`DisableSmallMeshCulling`, por
defecto `true`).

⚠️ Es un ajuste **global** de render: también dejan de descartarse los props pequeños y
lejanos del juego. Si notas caída de FPS, ponlo en `false`.

(Antes de dar con esto se probaron y descartaron dos hipótesis: `LODGroup` en el prefab
—no tiene— y el occlusion culling horneado. La causa se midió con `KioskDiagnostics`,
que vuelca los ajustes de la pipeline al log con `VerboseLog = true`.)

### 4. Jugar

Los bailes salen en páginas nuevas de la **rueda de emotes** (rueda del ratón para
pasar página). Mientras bailas, la cámara pasa **a tercera persona** y se ve el cuerpo
entero; vuelve a primera persona al terminar o en cuanto te mueves.

Config en `BepInEx/config/fcastro.ScoutDances.cfg`:

| Clave | Qué hace |
|---|---|
| `WheelOrder` | Orden de los bailes en la rueda, por nombre de clip |
| `ThirdPersonOnEmote` | Activa/desactiva la cámara en tercera persona |
| `Distance` | Distancia de la cámara (1.5 – 8) |
| `HeightOffset` | Altura respecto al torso (-1 – 3) |
| `SideOffset` | Desplazamiento lateral; negativo = izquierda |

> Los demás jugadores **necesitan el mod** para ver los bailes. Sin él verán
> el `Dance2` vanilla, porque así funciona el override de animaciones.

---

## Cómo funciona el plugin

### Los bailes

La rueda llama a `Character.localCharacter.refs.animations.PlayEmote(nombre)`, que
internamente lanza `view.RPC("RPCA_PlayRemove", RpcTarget.All, ...)` — o sea, **la
sincronización multijugador es gratis**. PEAKEmoteLib intercepta ese RPC y sustituye
el clip del estado `A_Scout_Emote_Dance2` mediante un `AnimatorOverrideController`
(los AnimatorController de Unity están precompilados: no se pueden añadir estados
nuevos en runtime, solo pisar los existentes).

Se registran como `EmoteType.OneShot` para que PEAKEmoteLib deje correr el clip
entero: el juego vanilla corta los emotes **a los 2 segundos**.

### La cámara en tercera persona

`EmoteCamera.cs`, dos piezas:

**Posición** — postfix sobre `MainCameraMovement.LateUpdate`. Ese método coloca la
cámara en primera persona cada frame, así que corriendo después tenemos la última
palabra sobre el transform. Interpolamos hacia una órbita detrás del Scout con un peso
0→1 en 0,25 s, con `SphereCastAll` para no meter la cámara en la roca (ignorando al
propio Scout y a los items que lleve).

**Cuerpo visible** — en primera persona el juego fantasmea el cuerpo con
`HideTheBody`, y su `Update()` recalcula el estado **cada frame**: llamar una vez a su
`Toggle` no serviría, lo revertiría al frame siguiente. En vez de parchearlo, movemos
la entrada de su propia condición:

```csharp
// HideTheBody.Update (vanilla)
bool flag = !character.IsLocal || fullyPassedOut || dead || isDummy;
if (flag != isShowing) Toggle(flag);
```

Poniendo el campo **público** `isDummy = true`, el juego muestra el cuerpo él solo; al
revertirlo, lo vuelve a ocultar. Sin tocar materiales ni parchear nada.
(Verificado sobre el ensamblado decompilado: `isDummy` no se lee en ningún otro sitio.)

**El retorno a primera persona no detecta WASD.** No hace falta: el propio
`CharacterAnimations.Update` apaga `emoting` cuando `movementInput.magnitude > 0.1`,
`jumpWasPressed`, o llevas 0,2 s sin tocar suelo. Enganchando la cámara a `emoting`,
sigue exactamente la vida del emote y no hay dos fuentes de verdad que se desincronicen.

## Dependencias instaladas en el juego

`PEAKEmoteLib 1.0.0`, `PEAKLib_Core 1.7.2`, `PEAKLib_UI 1.6.1`,
`SoftDependencyFix 1.0.0`, `MonoDetour 0.6.7` (+ su patcher para BepInEx 5).
