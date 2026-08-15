using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScoutDances.Props;

/// <summary>
/// Evita que el GPU Resident Drawer de URP descarte el kiosco por ser pequeño en pantalla.
/// </summary>
/// <remarks>
/// Medido en el juego: el asset de URP trae <c>smallMeshScreenPercentage = 0.5</c> con
/// <c>gpuResidentDrawerMode = InstancedDrawing</c>. Cualquier malla que ocupe menos del
/// 0,5 % de la pantalla se descarta entera, y un pie de micro fino cruza ese umbral a
/// pocos metros: por eso solo aparecía al entrar en rango de interacción.
///
/// Se ataca en dos frentes, del más barato al más invasivo:
///
/// 1. <see cref="ExcludeFromResidentDrawer"/> — un MaterialPropertyBlock por renderer.
///    El GPU Resident Drawer no gestiona renderers con overrides por instancia, así que
///    esos vuelven al camino de render normal. Solo afecta a nuestro objeto.
///
/// 2. <see cref="DisableSmallMeshCulling"/> — pone el umbral global a 0. Funciona
///    siempre, pero es un cambio de render para TODO el juego: los props pequeños y
///    lejanos dejan de descartarse y eso cuesta algo de rendimiento. Reversible.
/// </remarks>
internal static class RenderingTweaks
{
    static float? _originalPercentage;

    /// <summary>
    /// Marca los renderers con un MaterialPropertyBlock para sacarlos del
    /// GPU Resident Drawer sin tocar ajustes globales.
    /// </summary>
    internal static void ExcludeFromResidentDrawer(GameObject root)
    {
        var block = new MaterialPropertyBlock();
        block.SetFloat(Item.PROPERTY_INTERACTABLE, 0f);

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.SetPropertyBlock(block);
    }

    /// <summary>Pone el umbral de descarte por tamaño a 0 en el asset de URP.</summary>
    internal static void DisableSmallMeshCulling()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null) return;

        // Por reflexión: el setter no es público y el nombre podría cambiar entre
        // versiones de URP; preferimos no hacer nada antes que romper el arranque.
        var property = asset.GetType().GetProperty("smallMeshScreenPercentage",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (property == null || !property.CanRead)
        {
            Plugin.Log.LogWarning("No encuentro smallMeshScreenPercentage; no toco el culling.");
            return;
        }

        var current = (float)property.GetValue(asset);
        if (Mathf.Approximately(current, 0f)) return;

        _originalPercentage ??= current;

        if (property.CanWrite)
        {
            property.SetValue(asset, 0f);
        }
        else
        {
            var field = asset.GetType().GetField("m_SmallMeshScreenPercentage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Plugin.Log.LogWarning("smallMeshScreenPercentage es de solo lectura y no veo su campo.");
                return;
            }
            field.SetValue(asset, 0f);
        }

        Plugin.Log.LogInfo($"Culling de mallas pequeñas desactivado ({current} % -> 0 %) " +
                           "para que el kiosco se vea de lejos.");
    }

    /// <summary>Restaura el umbral original al descargar el mod.</summary>
    internal static void Restore()
    {
        if (_originalPercentage == null) return;

        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null) return;

        var property = asset.GetType().GetProperty("smallMeshScreenPercentage",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (property != null && property.CanWrite)
        {
            property.SetValue(asset, _originalPercentage.Value);
        }
        else
        {
            asset.GetType()
                .GetField("m_SmallMeshScreenPercentage", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(asset, _originalPercentage.Value);
        }

        _originalPercentage = null;
    }
}
