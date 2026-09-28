using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhxCore
{
    /// <summary>
    /// Debug view (SETTINGS › SHOW HITBOXES). Outlines every solid collider near the camera:
    /// green = the game's own, orange = added by PhxCore, blue = PhxCore mesh hitbox (outline shows its bounds).
    /// </summary>
    internal static class Hitboxes
    {
        const float Radius = 30f;
        const int MaxShown = 400;
        const float Refresh = 0.5f;
        const float Width = 0.04f;

        static readonly int[] Path = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };
        static readonly List<LineRenderer> Pool = new List<LineRenderer>();
        static readonly Vector3[] Corners = new Vector3[8];
        static GameObject _root;
        static readonly Color GameColor = new Color(0.2f, 1f, 0.3f);
        static readonly Color OursColor = new Color(1f, 0.55f, 0.1f);
        static readonly Color MeshColor = new Color(0.25f, 0.6f, 1f);
        static Material _material;
        static bool _logged;
        static bool _noCamera;
        static bool _counted;
        static float _next;
        static bool _failed;

        internal static void Reset()
        {
            Pool.Clear();
            _root = null;
            _next = 0f;
        }

        internal static void Hide()
        {
            if (_root != null)
                Object.Destroy(_root);
            Reset();
        }

        internal static void Tick()
        {
            if (_failed || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + Refresh;

            try
            {
                if (!EnsureMaterials())
                    return;
                Camera camera = ViewCamera();
                if (camera == null)
                {
                    if (!_noCamera)
                        MelonLogger.Warning("Hitbox view: no active camera found yet.");
                    _noCamera = true;
                    return;
                }
                int layer = VisibleLayer(camera);
                if (_root == null)
                {
                    _root = new GameObject("PhxCore_Hitboxes");
                    Pool.Clear();
                }

                Collider[] hits = Physics.OverlapSphere(camera.transform.position, Radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                int shown = 0;
                if (!_counted)
                {
                    _counted = true;
                    MelonLogger.Msg("Hitbox view: " + (hits == null ? 0 : hits.Length) + " colliders within " + Radius + " m of " + camera.name + ".");
                }
                if (hits != null)
                {
                    for (int i = 0; i < hits.Length && shown < MaxShown; i++)
                    {
                        Collider collider = hits[i];
                        if (collider == null || !collider.enabled)
                            continue;
                        if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                            continue;
                        Bounds world = collider.bounds;
                        if (world.size.x > 300f || world.size.z > 300f)
                            continue;

                        LineRenderer line = Line(shown++);
                        Fill(collider);
                        for (int p = 0; p < Path.Length; p++)
                            line.SetPosition(p, Corners[Path[p]]);
                        Color color = !Ground.IsOurs(collider) ? GameColor : (collider.TryCast<MeshCollider>() != null ? MeshColor : OursColor);
                        line.startColor = color;
                        line.endColor = color;
                        line.gameObject.layer = layer;
                        line.gameObject.SetActive(true);
                    }
                }

                for (int i = shown; i < Pool.Count; i++)
                {
                    if (Pool[i] != null)
                        Pool[i].gameObject.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                _failed = true;
                MelonLogger.Warning("Hitbox view stopped: " + e.Message);
            }
        }

        static void Fill(Collider collider)
        {
            BoxCollider box = collider.TryCast<BoxCollider>();
            if (box != null)
            {
                Vector3 c = box.center;
                Vector3 h = box.size * 0.5f;
                Transform t = box.transform;
                Corners[0] = t.TransformPoint(c + new Vector3(-h.x, -h.y, -h.z));
                Corners[1] = t.TransformPoint(c + new Vector3(h.x, -h.y, -h.z));
                Corners[2] = t.TransformPoint(c + new Vector3(h.x, -h.y, h.z));
                Corners[3] = t.TransformPoint(c + new Vector3(-h.x, -h.y, h.z));
                Corners[4] = t.TransformPoint(c + new Vector3(-h.x, h.y, -h.z));
                Corners[5] = t.TransformPoint(c + new Vector3(h.x, h.y, -h.z));
                Corners[6] = t.TransformPoint(c + new Vector3(h.x, h.y, h.z));
                Corners[7] = t.TransformPoint(c + new Vector3(-h.x, h.y, h.z));
                return;
            }

            Bounds b = collider.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;
            Corners[0] = new Vector3(min.x, min.y, min.z);
            Corners[1] = new Vector3(max.x, min.y, min.z);
            Corners[2] = new Vector3(max.x, min.y, max.z);
            Corners[3] = new Vector3(min.x, min.y, max.z);
            Corners[4] = new Vector3(min.x, max.y, min.z);
            Corners[5] = new Vector3(max.x, max.y, min.z);
            Corners[6] = new Vector3(max.x, max.y, max.z);
            Corners[7] = new Vector3(min.x, max.y, max.z);
        }

        static LineRenderer Line(int index)
        {
            while (Pool.Count <= index)
            {
                var go = new GameObject("PhxCore_Hitbox");
                go.transform.SetParent(_root.transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = Path.Length;
                line.startWidth = Width;
                line.endWidth = Width;
                line.numCornerVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = _material;
                Pool.Add(line);
            }
            return Pool[index];
        }

        /// <summary>The camera the player is looking through. FRUKT's camera is not always tagged MainCamera.</summary>
        static Camera ViewCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled)
                return main;
            Camera best = null;
            Camera[] all = Camera.allCameras;
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                Camera camera = all[i];
                if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
                    continue;
                if (best == null || camera.depth < best.depth)
                    best = camera;
            }
            return best;
        }

        /// <summary>A layer the camera draws, so the outlines are not culled away.</summary>
        static int VisibleLayer(Camera camera)
        {
            int mask = camera.cullingMask;
            if ((mask & 1) != 0)
                return 0;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }
            return 0;
        }

        /// <summary>
        /// An unlit, vertex-coloured material that draws on top of everything, so outlines show through walls and floors.
        /// Hidden/Internal-Colored ships in every Unity build (it is what GL and debug drawing use).
        /// </summary>
        static bool EnsureMaterials()
        {
            if (_material != null)
                return true;

            string[] names = { "Hidden/Internal-Colored", "Sprites/Default", "Universal Render Pipeline/Unlit", "Unlit/Color" };
            Shader shader = null;
            string used = null;
            for (int i = 0; i < names.Length && shader == null; i++)
            {
                shader = Shader.Find(names[i]);
                used = names[i];
            }

            if (shader != null)
            {
                _material = new Material(shader);
                if (_material.HasProperty("_ZTest"))
                    _material.SetInt("_ZTest", 8);
                if (_material.HasProperty("_ZWrite"))
                    _material.SetInt("_ZWrite", 0);
                if (_material.HasProperty("_Cull"))
                    _material.SetInt("_Cull", 0);
                _material.renderQueue = 5000;
            }
            else
            {
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Material source = probe.GetComponent<MeshRenderer>().sharedMaterial;
                Object.Destroy(probe);
                if (source == null)
                {
                    _failed = true;
                    MelonLogger.Warning("Hitbox view has no material to draw with.");
                    return false;
                }
                _material = new Material(source);
                used = source.shader != null ? source.shader.name : "primitive";
            }

            Object.DontDestroyOnLoad(_material);
            if (!_logged)
            {
                _logged = true;
                MelonLogger.Msg("Hitbox view on (shader: " + used + ").");
            }
            return true;
        }
    }
}
