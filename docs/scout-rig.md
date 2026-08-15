# Rig del Scout (PEAK 2.1.a) — referencia

Extraído de `A_Scout_Emote_Dance2` en `PEAK_Data\resources.assets` con UnityPy.
**62 de 62** hashes de ruta del clip resueltos a rutas reales de transform (match 100%).

El hash es `CRC32(ruta_ascii)`, relativo al GameObject raíz del personaje (`Scout` / `Character_0`).

## Naturaleza del clip

| Propiedad | Valor |
|---|---|
| `m_Legacy` | `False` |
| `m_Compressed` | `False` |
| `m_SampleRate` | `60` |
| `m_MuscleClipSize` | `10520` |
| `m_HasGenericRootTransform` | `False` |
| Curvas sueltas (`m_RotationCurves`, `m_PositionCurves`, …) | todas vacías → clip **horneado** en `m_MuscleClip` |
| Bindings por `typeID` | **45× Transform (4)**, 16× GameObject (1), 4× MonoBehaviour (114) |
| Assets `Avatar` en el juego | **ninguno** |

→ **Rig genérico atado a rutas.** No hay retargeting humanoide automático.
Los bindings de Transform usan `attribute 1` (posición) y `attribute 4` (rotación).
Los bindings de GameObject usan `attribute 2086281974` = `m_IsActive`.

## Jerarquía de huesos

```
Armature/Hip
├── Hip_L
│   └── Leg_L / Knee_L / Foot_L
├── Hip_R
│   └── Leg_R / Knee_R / Foot_R
└── Mid
    └── AimJoint                       (driven por la mirada — cuidado al animar)
        └── Torso
            ├── Head
            ├── S_Shoulder_L           (NO animado en emotes)
            │   └── Arm_L / Elbow_L / Hand_L
            │       ├── Hand_Upper_L
            │       │   ├── Index_1_L / Index_2_L / Index_3_L
            │       │   ├── Middle_1_L / Middle_2_L / Middle_3_L
            │       │   └── Pinky_1_L / Pinky_2_L / Pinky_3_L
            │       └── Thumb_1_L / Thumb_2_L / Thumb_3_L
            └── S_Shoulder_R           (NO animado en emotes)
                └── Arm_R / Elbow_R / Hand_R
                    ├── Hand_Upper_R
                    │   ├── Index_1_R / Index_2_R / Index_3_R
                    │   ├── Middle_1_R / Middle_2_R / Middle_3_R
                    │   └── Pinky_1_R / Pinky_2_R / Pinky_3_R
                    └── Thumb_R_1 / Thumb_R_2 / Thumb_R_3   ← ¡asimetría!
```

**Gotchas:**
- Sin cuello, sin dedos de los pies, sin cadena de columna larga.
- El pulgar derecho se llama `Thumb_R_1/2/3` y el izquierdo `Thumb_1_L/2_L/3_L`.
  Cualquier script de mirroring ingenuo falla aquí.
- `AimJoint` está entre `Mid` y `Torso` y lo controla el sistema de mirada del juego.
- `Hand_Upper_*` es un hueso intermedio entre la mano y los dedos (excepto el pulgar,
  que cuelga directo de `Hand_*`).

## Mapeo sugerido a Unity Humanoid

| Hueso Humanoid | Hueso Scout |
|---|---|
| Hips | `Hip` |
| Spine | `Mid` |
| Chest | `Torso` |
| Head | `Head` |
| LeftUpperArm | `Arm_L` |
| LeftLowerArm | `Elbow_L` |
| LeftHand | `Hand_L` |
| RightUpperArm | `Arm_R` |
| RightLowerArm | `Elbow_R` |
| RightHand | `Hand_R` |
| LeftUpperLeg | `Leg_L` |
| LeftLowerLeg | `Knee_L` |
| LeftFoot | `Foot_L` |
| RightUpperLeg | `Leg_R` |
| RightLowerLeg | `Knee_R` |
| RightFoot | `Foot_R` |
| (opcional) Shoulders | `S_Shoulder_L` / `S_Shoulder_R` |
| (opcional) dedos | cadenas `Index/Middle/Pinky/Thumb` |

Cubre el mínimo obligatorio de Unity Humanoid → **se puede crear un Avatar Humanoid válido**.

## SFX animados

El clip también togglea `m_IsActive` de estos nodos — así es como las animaciones
disparan sonido. Podemos usar el mismo mecanismo en emotes custom:

```
SFX/Movement/Climbing/Rope/SFX Rope Move 0 / 1 / 2
SFX/Movement/Climbing/Surface/SFX Climb Fall
SFX/Movement/Climbing/Surface/SFX Climb Getup
SFX/Movement/Climbing/Surface/SFX Climb Grab
SFX/Movement/Climbing/Surface/SFX Climb Jump
SFX/Movement/Climbing/Surface/SFX Climb Move (1) / (2)
SFX/Movement/Misc/Loops/Fall Loop
SFX/Movement/Misc/Loops/Slide Loop
SFX/Movement/SFX Clothing
SFX/Movement/SFX Jump
SFX/Movement/SFX Jump Dash
SFX/Movement/SFX Land
SFX/Movement/Step/Step Manager Run / Sneak / Walk
```

## Tabla completa hash → ruta

| CRC32 | Ruta |
|---:|---|
| 4207688246 | `Armature/Hip` |
| 3260512798 | `Armature/Hip/Hip_L` |
| 2539510540 | `Armature/Hip/Hip_L/Leg_L` |
| 3101995949 | `Armature/Hip/Hip_L/Leg_L/Knee_L` |
| 2881363968 | `Armature/Hip/Hip_L/Leg_L/Knee_L/Foot_L` |
| 945309565 | `Armature/Hip/Hip_R` |
| 1521680892 | `Armature/Hip/Hip_R/Leg_R` |
| 2264122427 | `Armature/Hip/Hip_R/Leg_R/Knee_R` |
| 163227420 | `Armature/Hip/Hip_R/Leg_R/Knee_R/Foot_R` |
| 2569817907 | `Armature/Hip/Mid` |
| 356330424 | `Armature/Hip/Mid/AimJoint/Torso` |
| 4021563979 | `Armature/Hip/Mid/AimJoint/Torso/Head` |
| 2520062636 | `…/Torso/S_Shoulder_L/Arm_L` |
| 2341256716 | `…/Arm_L/Elbow_L` |
| 2625674179 | `…/Elbow_L/Hand_L` |
| 4112984604 | `…/Hand_L/Hand_Upper_L` |
| 534290934 | `…/Hand_Upper_L/Index_1_L` |
| 1489363770 | `…/Index_1_L/Index_2_L` |
| 2918327214 | `…/Index_2_L/Index_3_L` |
| 3118296219 | `…/Hand_Upper_L/Middle_1_L` |
| 185539487 | `…/Middle_1_L/Middle_2_L` |
| 10691758 | `…/Middle_2_L/Middle_3_L` |
| 3060026187 | `…/Hand_Upper_L/Pinky_1_L` |
| 1979930414 | `…/Pinky_1_L/Pinky_2_L` |
| 482899297 | `…/Pinky_2_L/Pinky_3_L` |
| 3425792218 | `…/Hand_L/Thumb_1_L` |
| 2485535769 | `…/Thumb_1_L/Thumb_2_L` |
| 334861744 | `…/Thumb_2_L/Thumb_3_L` |
| 1541128284 | `…/Torso/S_Shoulder_R/Arm_R` |
| 3163594166 | `…/Arm_R/Elbow_R` |
| 2875095885 | `…/Elbow_R/Hand_R` |
| 3392628684 | `…/Hand_R/Hand_Upper_R` |
| 3079344924 | `…/Hand_Upper_R/Index_1_R` |
| 1266008790 | `…/Index_1_R/Index_2_R` |
| 1869165862 | `…/Index_2_R/Index_3_R` |
| 3622057715 | `…/Hand_Upper_R/Middle_1_R` |
| 4073882368 | `…/Middle_1_R/Middle_2_R` |
| 1733524813 | `…/Middle_2_R/Middle_3_R` |
| 506984865 | `…/Hand_Upper_R/Pinky_1_R` |
| 1706260162 | `…/Pinky_1_R/Pinky_2_R` |
| 3730007017 | `…/Pinky_2_R/Pinky_3_R` |
| 347950985 | `…/Hand_R/Thumb_R_1` |
| 1415527677 | `…/Thumb_R_1/Thumb_R_2` |
| 2418704892 | `…/Thumb_R_2/Thumb_R_3` |

(Los 16 hashes restantes son los nodos SFX listados arriba.)

## Emotes vanilla disponibles

```
A_Scout_Emote_BackFlip    A_Scout_Emote_Cinema     A_Scout_Emote_Clap
A_Scout_Emote_Crashout    A_Scout_Emote_CrossedArms A_Scout_Emote_Dance1
A_Scout_Emote_Dance2 ←    A_Scout_Emote_Despair    A_Scout_Emote_Fist
A_Scout_Emote_Flex        A_Scout_Emote_ImHere     A_Scout_Emote_Nono
A_Scout_Emote_Panic       A_Scout_Emote_Salute     A_Scout_Emote_Shrug
A_Scout_Emote_Sit         A_Scout_Emote_Think      A_Scout_Emote_ThumbsUp
```

`A_Scout_Emote_Dance2` es el estado que **PEAKEmoteLib sacrifica** como slot de override.
