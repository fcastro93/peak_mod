using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScoutDances.Sounds;

/// <summary>
/// Diagnóstico del kiosco: distingue si desaparece de lejos por CULLING o por MATERIAL.
/// </summary>
/// <remarks>
/// Si <c>isVisible</c> se va a false al alejarse, alguien lo está descartando (frustum,
/// occlusion, o el culling por tamaño en pantalla del GPU Resident Drawer de URP).
/// Si sigue en true pero no se ve, el problema está en el shader o el material.
/// Se activa con VerboseLog en la config.
/// </remarks>
internal class KioskDiagnostics : MonoBehaviour
{
    Renderer[] _renderers = System.Array.Empty<Renderer>();
    float _nextLog;
    int _lastVisible = -1;

    void Start()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        LogPipelineSettings();
    }

    /// <summary>
    /// Vuelca los ajustes de URP que pueden descartar objetos pequeños o lejanos.
    /// Por reflexión: los nombres cambian entre versiones de URP y no queremos que un
    /// rename rompa la compilación del mod.
    /// </summary>
    static void LogPipelineSettings()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
        {
            Plugin.Log.LogInfo("[diag] no hay render pipeline asset activo.");
            return;
        }

        Plugin.Log.LogInfo($"[diag] pipeline: {asset.GetType().Name}");

        foreach (var name in new[]
                 {
                     "smallMeshScreenPercentage",
                     "gpuResidentDrawerMode",
                     "gpuResidentDrawerEnableOcclusionCullingInCameras",
                     "shadowDistance",
                 })
        {
            var property = asset.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null) continue;

            try { Plugin.Log.LogInfo($"[diag]   {name} = {property.GetValue(asset)}"); }
            catch { /* alguna propiedad puede lanzar según el estado */ }
        }

        Plugin.Log.LogInfo($"[diag] QualitySettings.lodBias = {QualitySettings.lodBias}, " +
                           $"maximumLODLevel = {QualitySettings.maximumLODLevel}");
    }

    void Update()
    {
        if (!Plugin.CfgVerbose.Value) return;
        if (Time.time < _nextLog) return;
        _nextLog = Time.time + 1f;

        var camera = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (camera == null || _renderers.Length == 0) return;

        int visible = _renderers.Count(r => r != null && r.isVisible);
        int enabled = _renderers.Count(r => r != null && r.enabled && r.gameObject.activeInHierarchy);
        float distance = Vector3.Distance(camera.transform.position, transform.position);

        // Solo cuando cambia el recuento de visibles, para no llenar el log.
        if (visible == _lastVisible) return;
        _lastVisible = visible;

        var bounds = _renderers[0].bounds;
        Plugin.Log.LogInfo(
            $"[diag] dist={distance:0.0} m  visibles={visible}/{_renderers.Length}  " +
            $"activos={enabled}  boundsSize={bounds.size} " +
            $"-> {(visible == 0 ? "CULLING (alguien lo descarta)" : "renderizando")}");
    }
}
