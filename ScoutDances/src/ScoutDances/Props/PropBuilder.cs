using System;
using UnityEngine;

namespace ScoutDances.Props;

/// <summary>
/// Coloca props del mod en el aeropuerto: orientación, escala, apoyo en el suelo,
/// materiales y visibilidad. Concentra todo lo que costó averiguar con el primer kiosco.
/// </summary>
/// <remarks>
/// Las cuatro trampas que resuelve, todas descubiertas a base de que el objeto saliera
/// mal en partida:
///
/// 1. <b>Rotación del ancla.</b> Los kioscos vanilla están autorizados con su propia
///    inclinación. Copiar su rotación entera dejaba nuestros props tumbados, así que solo
///    se hereda el giro horizontal.
/// 2. <b>Medir con Renderer.bounds.</b> Devuelve la caja en MUNDO, o sea ya girada por el
///    padre. Con ella detectábamos el eje largo equivocado y "enderezábamos" un modelo que
///    ya estaba derecho. Se mide con las mallas y sus matrices.
/// 3. <b>Alpha clipping.</b> Los materiales de la Asset Store suelen traer
///    <c>_ALPHATEST_ON</c>; con mipmaps el alfa se promedia a distancia y el objeto se
///    recorta entero.
/// 4. <b>Culling por tamaño en pantalla.</b> URP trae
///    <c>smallMeshScreenPercentage = 0.5</c> con el GPU Resident Drawer activo y descarta
///    lo que ocupe menos que eso.
/// </remarks>
internal static class PropBuilder
{
    /// <summary>
    /// Crea el objeto raíz del prop junto a un ancla, ya orientado y apoyado en el suelo.
    /// </summary>
    /// <param name="name">Nombre del GameObject raíz.</param>
    /// <param name="anchor">Objeto vanilla que usamos de referencia (posición y capa).</param>
    /// <param name="sideOffset">Desplazamiento lateral respecto al ancla, en metros.</param>
    /// <param name="prefab">Modelo del bundle. Si es null se usa un cubo.</param>
    /// <param name="targetHeight">Altura final del modelo, en metros.</param>
    internal static GameObject Spawn(string name, Transform anchor, float sideOffset,
                                     GameObject? prefab, float targetHeight)
    {
        var spawn = anchor.position + anchor.right * sideOffset;

        if (Physics.Raycast(spawn + Vector3.up * 3f, Vector3.down, out var ground, 12f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            spawn.y = ground.point.y;
        }

        // Solo el giro horizontal del ancla (ver trampa 1).
        var yaw = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);

        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(spawn, yaw);

        var model = BuildModel(root.transform, prefab, targetHeight);
        FixMaterials(model);
        EnsureCollider(root, model);
        SetLayerRecursive(root, anchor.gameObject.layer);
        RenderingTweaks.ExcludeFromResidentDrawer(root);

        return root;
    }

    /// <summary>
    /// Apoya el modelo en el suelo. Llamar UN FRAME después de <see cref="Spawn"/>:
    /// los bounds no reflejan al instante la rotación y la escala recién puestas.
    /// </summary>
    internal static void SnapToGround(GameObject root)
    {
        var model = root.transform.Find("Model");
        if (model == null) return;

        var local = LocalBounds(model.gameObject);
        float lowest = LowestPoint(local, model.localRotation, model.localScale.x);
        model.localPosition = new Vector3(model.localPosition.x, -lowest, model.localPosition.z);
    }

    static GameObject BuildModel(Transform parent, GameObject? prefab, float targetHeight)
    {
        if (prefab == null)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Model";
            box.transform.SetParent(parent, false);
            box.transform.localPosition = new Vector3(0f, targetHeight / 2f, 0f);
            box.transform.localScale = new Vector3(0.7f, targetHeight, 0.5f);

            // CreatePrimitive asigna el material por defecto de Unity, que apunta al
            // shader Standard del pipeline built-in. PEAK va con URP y ese shader no
            // está en su build, así que el cubo existía pero no se dibujaba: se veía
            // el prompt de interacción flotando sobre la nada. Hay que darle uno del juego.
            var shader = Shader.Find("W/Peak_Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var renderer = box.GetComponent<Renderer>();
            if (shader != null && renderer != null)
            {
                renderer.material = new Material(shader) { color = new Color(0.55f, 0.36f, 0.18f) };
            }
            else
            {
                Plugin.Log.LogWarning("No encuentro ningún shader válido para el modelo de reserva.");
            }

            Plugin.Log.LogInfo($"Prop sin modelo en el bundle: uso un cubo de reserva ({targetHeight:0.00} m).");
            return box;
        }

        // Cualificado: con 'using System' presente, 'Object' es ambiguo.
        var model = UnityEngine.Object.Instantiate(prefab, parent);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        // Medición determinista (ver trampa 2): una sola vez, en reposo.
        var size = LocalBounds(model).size;
        int longest = (size.x >= size.y && size.x >= size.z) ? 0 : (size.y >= size.z ? 1 : 2);
        float length = size[longest];

        // Solo enderezamos si el modelo viene claramente tumbado. Un objeto ancho y bajo
        // (una caja, una mesa) tiene su eje largo en horizontal y está perfectamente bien
        // así: girarlo lo estropearía. Por eso exigimos que sea bastante alargado.
        bool elongated = length > 2.5f * Mathf.Min(size.x, Mathf.Min(size.y, size.z));
        if (elongated && longest == 0) model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        else if (elongated && longest == 2) model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        float reference = elongated ? length : Mathf.Max(size.y, 0.01f);
        model.transform.localScale = Vector3.one * (targetHeight / reference);

        Plugin.Log.LogInfo(
            $"Prop '{prefab.name}': {size.x:0.00}x{size.y:0.00}x{size.z:0.00} m, " +
            $"{(elongated ? $"alargado (eje {"XYZ"[longest]})" : "compacto")} -> " +
            $"escala {model.transform.localScale.x:0.00}");

        return model;
    }

    /// <summary>Caja envolvente en el espacio local del modelo, desde las mallas.</summary>
    internal static Bounds LocalBounds(GameObject model)
    {
        var root = model.transform;
        bool started = false;
        Bounds bounds = default;

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;

            var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var mb = mesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? mb.min.x : mb.max.x,
                    (corner & 2) == 0 ? mb.min.y : mb.max.y,
                    (corner & 4) == 0 ? mb.min.z : mb.max.z));

                if (!started) { bounds = new Bounds(point, Vector3.zero); started = true; }
                else bounds.Encapsulate(point);
            }
        }

        return started ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    internal static float LowestPoint(Bounds local, Quaternion rotation, float scale)
    {
        float min = float.MaxValue;
        for (int corner = 0; corner < 8; corner++)
        {
            var point = rotation * (new Vector3(
                (corner & 1) == 0 ? local.min.x : local.max.x,
                (corner & 2) == 0 ? local.min.y : local.max.y,
                (corner & 4) == 0 ? local.min.z : local.max.z) * scale);
            if (point.y < min) min = point.y;
        }
        return min;
    }

    static void FixMaterials(GameObject model)
    {
        // URP/Lit primero: usa las propiedades estándar (_BaseMap, _BaseColor) y sabemos
        // exactamente cómo alimentarlo. W/Peak_Standard es el shader propio del juego y
        // no expone la textura con esos nombres, así que la caja salía sin color.
        var fallback = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("W/Peak_Standard");

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.allowOcclusionWhenDynamic = false;

            var materials = renderer.materials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                // Ver trampa 3: opacos, el alpha test solo los hace desaparecer de lejos.
                if (material.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    material.DisableKeyword("_ALPHATEST_ON");
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0f);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                }

                if (!NeedsUrpShader(material) || fallback == null) continue;

                materials[i] = Reshade(material, fallback);
                changed = true;
            }

            if (changed) renderer.materials = materials;
        }
    }

    /// <summary>
    /// Vuelve a enlazar los shaders de un modelo del bundle con los compilados en el juego.
    /// </summary>
    /// <remarks>
    /// Un AssetBundle se lleva SU PROPIA copia del shader, distinta de la que el juego
    /// tiene compilada. Esa copia suele venir sin las variantes que hacen falta y el
    /// resultado es un objeto que existe, está activo, bien colocado… y no se dibuja.
    /// Fue justo lo que pasó con el arma: el diagnóstico decía "2 renderers activos,
    /// escala y posición correctas" y aun así era invisible.
    ///
    /// <c>Shader.Find</c> devuelve la instancia del juego, que sí está completa.
    /// </remarks>
    internal static void RebindShaders(GameObject model)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");

        // Las partículas necesitan un shader transparente sin iluminar. Con URP/Lit salen
        // como cuadrados opacos y grises en vez de destellos, que es peor que no verlas.
        var particle = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Universal Render Pipeline/Particles/Simple Lit")
                    ?? lit;

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var fallback = renderer is ParticleSystemRenderer ? particle : lit;
            var materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                var name = material.shader != null ? material.shader.name : "(null)";
                string what;

                // Las partículas NO se reenlazan si su shader ya funciona.
                //
                // Un material de partículas no es solo un shader: es un shader MÁS su modo
                // de mezcla (aditiva, premultiplicada…), que vive en propiedades y palabras
                // clave del propio material. Al cambiarle la instancia del shader por la del
                // juego, esa configuración deja de cuadrar y el resultado es lo que se veía:
                // partículas presentes pero lavadas, como reflejos transparentes en vez del
                // rayo azul que se ve en el Editor.
                //
                // Reenlazar existe para los shaders GRANDES que el juego ya trae, cuya copia
                // en el bundle llega sin variantes. Los de partículas van con lo que
                // necesitan y no hay que tocarlos.
                if (renderer is ParticleSystemRenderer && !NeedsUrpShader(material))
                {
                    Plugin.Log.LogInfo($"[shader] '{material.name}' usaba '{name}' -> " +
                                       "intacto (partícula)");
                    continue;
                }

                if (NeedsUrpShader(material) && fallback != null)
                {
                    // Roto de verdad: el shader de error, o uno del pipeline built-in que
                    // carga sin quejarse pero no tiene pase válido en URP.
                    materials[i] = Reshade(material, fallback);
                    what = "sustituido por " + fallback.name;
                }
                else
                {
                    var ingame = material.shader != null ? Shader.Find(name) : null;
                    if (ingame != null)
                    {
                        material.shader = ingame;
                        what = "reenlazado al del juego";
                    }
                    else if (name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)
                             && fallback != null)
                    {
                        // Es de la FAMILIA de URP, no del asset. Que Shader.Find falle solo
                        // significa que PEAK no compiló esa variante concreta —le pasa a
                        // los shaders opcionales de URP, como Autodesk Interactive— y la
                        // copia del bundle llega sin variantes, igual que URP/Lit. Dejarla
                        // intacta la vuelve invisible: al fallback.
                        materials[i] = Reshade(material, fallback);
                        what = "familia URP sin variantes -> " + fallback.name;
                    }
                    else
                    {
                        // Shader PROPIO del asset, que el juego no tiene. Se queda como
                        // está: viaja entero dentro del bundle y funciona.
                        //
                        // Sustituirlo era destructivo. El de las cajas de power-up compone
                        // el cuerpo (_Background) con el símbolo (_Icon) y lo multiplica
                        // por el color de vértice de cada prefab; al cambiarlo por URP/Lit
                        // se perdían el icono y el color de golpe, y las tres cajas salían
                        // iguales, azules y sin chevrons. El problema de las variantes que
                        // motivó todo esto solo afecta a los shaders GRANDES que el juego
                        // ya trae (URP/Lit), no a uno pequeño y autocontenido como este.
                        what = "intacto (shader propio del asset)";
                    }
                }

                Plugin.Log.LogInfo($"[shader] '{material.name}' usaba '{name}' -> {what}");
            }

            renderer.materials = materials;
        }
    }

    /// <summary>
    /// ¿Este material usa un shader que no pinta nada bajo URP?
    /// </summary>
    /// <remarks>
    /// No basta con mirar si el shader es null o el de error: un asset viejo puede traer
    /// el <c>Standard</c> del pipeline built-in, que se empaqueta y CARGA sin problema
    /// pero no tiene pase válido en URP. La caja de madera salía blanca y desaparecía
    /// según el ángulo justo por eso.
    /// </remarks>
    static bool NeedsUrpShader(Material material)
    {
        var shader = material.shader;
        if (shader == null) return true;

        var name = shader.name;
        return name == "Hidden/InternalErrorShader"
            || name == "Standard"
            || name == "Standard (Specular setup)"
            || name.StartsWith("Legacy Shaders/", StringComparison.Ordinal)
            || name.StartsWith("Mobile/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Nombres bajo los que un shader puede guardar su textura principal, en orden de
    /// preferencia. _Background va antes que _Icon porque es el cuerpo de la caja, que es
    /// lo que le da el color; el icono es solo el símbolo estampado encima.
    /// </summary>
    static readonly string[] AlbedoProperties =
    {
        "_MainTex", "_BaseMap", "_Background", "_Icon", "_Albedo", "_Texture",
    };

    /// <summary>Rehace el material sobre un shader de URP conservando texturas y color.</summary>
    static Material Reshade(Material source, Shader target)
    {
        var result = new Material(target) { name = source.name + " (URP)" };

        // El built-in usa _MainTex/_Color; URP usa _BaseMap/_BaseColor. Escribimos en
        // ambos porque el shader del propio juego (W/Peak_Standard) no sigue siempre
        // la nomenclatura de URP.
        // Cada shader bautiza su textura como quiere. Los de Epic Toon FX no declaran
        // _MainTex: la caja de power-up usa _Background para el cuerpo (que es donde va
        // el color) y _Icon para el símbolo. Sin buscarlos, salía "albedo=ninguno" y la
        // caja quedaba blanca, igual que nos pasó con la de madera.
        Texture? albedo = null;
        string albedoFrom = "ninguno";

        foreach (var candidate in AlbedoProperties)
        {
            if (!source.HasProperty(candidate)) continue;
            var texture = source.GetTexture(candidate);
            if (texture == null) continue;

            albedo = texture;
            albedoFrom = candidate;
            break;
        }

        if (albedo != null)
        {
            if (result.HasProperty("_BaseMap")) result.SetTexture("_BaseMap", albedo);
            if (result.HasProperty("_MainTex")) result.SetTexture("_MainTex", albedo);
        }

        if (source.HasProperty("_BumpMap") && result.HasProperty("_BumpMap"))
        {
            var normal = source.GetTexture("_BumpMap");
            if (normal != null)
            {
                result.SetTexture("_BumpMap", normal);
                result.EnableKeyword("_NORMALMAP");
            }
        }

        var color = source.HasProperty("_Color") ? source.GetColor("_Color")
                  : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                  : Color.white;

        if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
        if (result.HasProperty("_Color")) result.SetColor("_Color", color);

        Plugin.Log.LogInfo($"Material '{source.name}': shader '{source.shader?.name ?? "null"}' " +
                           $"no vale en URP -> '{target.name}' " +
                           $"(albedo={(albedo != null ? albedo.name : "ninguno")} vía {albedoFrom}).");
        return result;
    }

    static void EnsureCollider(GameObject root, GameObject model)
    {
        if (model.GetComponentInChildren<Collider>() != null) return;

        var local = LocalBounds(model);
        var box = root.AddComponent<BoxCollider>();
        float scale = model.transform.localScale.x;

        box.center = model.transform.localPosition + model.transform.localRotation * (local.center * scale);
        var size = local.size * scale;
        box.size = new Vector3(Mathf.Max(size.x, 0.5f), Mathf.Max(size.y, 0.3f), Mathf.Max(size.z, 0.5f));
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }
}
