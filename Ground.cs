using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhxCore
{
    /// <summary>
    /// Gives the map solid hitboxes for walking. Surfaces the game already made solid are left alone. Every other map
    /// surface gets a collider that matches it as closely as the game allows:
    ///  - readable mesh: a MeshCollider with its exact shape (ramps, stairs, uneven ground),
    ///  - otherwise, thin or compact pieces: a BoxCollider fitted to the mesh and rotated with it,
    ///  - otherwise (big pieces of unknown shape, e.g. a whole room in one mesh): skipped, because a solid box would trap you.
    /// A wide safety slab under the whole map catches anything that still falls through.
    /// </summary>
    internal static class Ground
    {
        const float SlabThickness = 2f;
        const float ThinPiece = 0.35f;
        const float CompactPiece = 3f;
        const int MaxMeshVertices = 150000;

        static readonly string[] Decor =
        {
            "hologram", "decal", "vfx", "effect", "particle", "ui", "grass", "foliage", "leaf", "leaves", "wire", "cable",
            "rope", "light", "lamp", "glow", "flare", "beam", "fog", "smoke", "water", "glass", "sky", "shadow", "trigger",
            "blood", "splat", "gore"
        };

        static readonly List<Collider> Added = new List<Collider>();
        static readonly HashSet<int> AddedIds = new HashSet<int>();
        static GameObject _root;

        internal static float FloorY;
        internal static bool Ready;

        internal static bool IsOurs(Collider collider)
        {
            return collider != null && AddedIds.Contains(collider.GetInstanceID());
        }

        internal static void Build(Transform player, int bodyLayer, bool generate)
        {
            Clear();

            int gameSolid = 0;
            int meshes = 0;
            int boxes = 0;
            int unknown = 0;
            int decor = 0;

            bool any = false;
            Bounds all = default;
            float floorY = float.PositiveInfinity;

            MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    MeshRenderer renderer = renderers[i];
                    if (!IsMapPiece(renderer, player))
                        continue;

                    Bounds bounds = renderer.bounds;
                    if (!any)
                    {
                        all = bounds;
                        any = true;
                    }
                    else
                    {
                        all.Encapsulate(bounds);
                    }

                    bool floorLike = bounds.size.y <= 2.5f && bounds.size.x >= 1.5f && bounds.size.z >= 1.5f;
                    if (floorLike && bounds.max.y < floorY)
                        floorY = bounds.max.y;

                    if (HasSolidCollider(renderer.gameObject))
                    {
                        gameSolid++;
                        continue;
                    }
                    if (!generate)
                        continue;
                    if (LooksLikeDecor(renderer))
                    {
                        decor++;
                        continue;
                    }

                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null)
                        continue;

                    if (TryAddMesh(renderer.gameObject, mesh))
                    {
                        meshes++;
                        continue;
                    }
                    if (TryAddFittedBox(renderer.gameObject, mesh))
                    {
                        boxes++;
                        continue;
                    }
                    unknown++;
                }
            }

            if (float.IsPositiveInfinity(floorY))
                floorY = any ? all.min.y : 0f;
            FloorY = floorY;

            _root = new GameObject("PhxCore Ground");
            float minX = any ? all.min.x - 8f : -40f;
            float maxX = any ? all.max.x + 8f : 40f;
            float minZ = any ? all.min.z - 8f : -40f;
            float maxZ = any ? all.max.z + 8f : 40f;
            float slabTop = (any ? all.min.y : floorY) - 0.5f;
            var slab = new GameObject("Safety slab");
            slab.layer = bodyLayer;
            slab.transform.SetParent(_root.transform, false);
            slab.transform.position = new Vector3((minX + maxX) * 0.5f, slabTop - SlabThickness * 0.5f, (minZ + maxZ) * 0.5f);
            BoxCollider net = slab.AddComponent<BoxCollider>();
            net.size = new Vector3(Mathf.Max(20f, maxX - minX), SlabThickness, Mathf.Max(20f, maxZ - minZ));
            Track(net);

            AllowBodyToHitSolids(bodyLayer);
            Ready = true;
            MelonLogger.Msg("Ground: " + gameSolid + " surfaces already solid, added " + meshes + " exact + " + boxes
                + " fitted box hitboxes, skipped " + unknown + " unknown-shape and " + decor + " decor pieces.");
        }

        internal static void Clear()
        {
            for (int i = 0; i < Added.Count; i++)
            {
                // Immediate, so a rebuild in the same frame does not mistake the old hitboxes for the game's own.
                if (Added[i] != null)
                    Object.DestroyImmediate(Added[i]);
            }
            Added.Clear();
            AddedIds.Clear();
            if (_root != null)
                Object.DestroyImmediate(_root);
            _root = null;
            Ready = false;
        }

        static bool IsMapPiece(MeshRenderer renderer, Transform player)
        {
            if (renderer == null || !renderer.enabled)
                return false;
            Transform transform = renderer.transform;
            if (player != null && (transform == player || transform.IsChildOf(player)))
                return false;
            if (renderer.GetComponentInParent<Rigidbody>() != null)
                return false;
            if (transform.name.StartsWith("PhxCore_", StringComparison.Ordinal))
                return false;

            Vector3 size = renderer.bounds.size;
            if (size.x > 400f || size.y > 400f || size.z > 400f)
                return false;
            return size.sqrMagnitude >= 0.04f;
        }

        static bool LooksLikeDecor(MeshRenderer renderer)
        {
            string name = renderer.gameObject.name.ToLowerInvariant();
            for (int i = 0; i < Decor.Length; i++)
            {
                if (name.Contains(Decor[i]))
                    return true;
            }
            string layer = LayerMask.LayerToName(renderer.gameObject.layer);
            if (!string.IsNullOrEmpty(layer))
            {
                layer = layer.ToLowerInvariant();
                if (layer.Contains("ui") || layer.Contains("water") || layer.Contains("ignore") || layer.Contains("fx"))
                    return true;
            }
            Vector3 size = renderer.bounds.size;
            return Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < 0.3f;
        }

        static bool HasSolidCollider(GameObject gameObject)
        {
            Collider[] colliders = gameObject.GetComponentsInParent<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null && collider.enabled && !collider.isTrigger && !AddedIds.Contains(collider.GetInstanceID()))
                    return true;
            }
            return false;
        }

        static bool TryAddMesh(GameObject target, Mesh mesh)
        {
            if (!mesh.isReadable || mesh.vertexCount < 3 || mesh.vertexCount > MaxMeshVertices)
                return false;
            try
            {
                MeshCollider collider = target.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                collider.isTrigger = false;
                Track(collider);
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Mesh hitbox failed on " + target.name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>A box in the mesh's own space, so it turns and scales with the object instead of being a world-aligned block.</summary>
        static bool TryAddFittedBox(GameObject target, Mesh mesh)
        {
            Bounds local = mesh.bounds;
            Vector3 scale = target.transform.lossyScale;
            Vector3 world = new Vector3(Mathf.Abs(local.size.x * scale.x), Mathf.Abs(local.size.y * scale.y), Mathf.Abs(local.size.z * scale.z));
            float smallest = Mathf.Min(world.x, Mathf.Min(world.y, world.z));
            float largest = Mathf.Max(world.x, Mathf.Max(world.y, world.z));
            if (smallest > ThinPiece && largest > CompactPiece)
                return false;

            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = new Vector3(Mathf.Max(local.size.x, 0.02f), Mathf.Max(local.size.y, 0.02f), Mathf.Max(local.size.z, 0.02f));
            box.isTrigger = false;
            Track(box);
            return true;
        }

        static void Track(Collider collider)
        {
            Added.Add(collider);
            AddedIds.Add(collider.GetInstanceID());
        }

        static void AllowBodyToHitSolids(int bodyLayer)
        {
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (colliders == null)
                return;
            bool[] seen = new bool[32];
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger)
                    continue;
                int layer = collider.gameObject.layer;
                if (layer < 0 || layer > 31 || seen[layer])
                    continue;
                seen[layer] = true;
                Physics.IgnoreLayerCollision(bodyLayer, layer, false);
            }
        }
    }
}
