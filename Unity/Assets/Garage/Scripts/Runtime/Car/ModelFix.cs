using System.Collections.Generic;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Safety net for the generated FBX models when the import postprocessor did not run (on a fresh clone Unity
    /// may import the models before the editor scripts compile): hides the UCX_ collision boxes (and turns them into
    /// convex colliders) and builds the LODGroup from the _LOD0/1/2 children so only one level is drawn.
    /// Idempotent; call it on every instance of a model from Resources.
    /// </summary>
    public static class ModelFix
    {
        /// <summary>Instantiates a model and fixes it.</summary>
        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
            GameObject go = Object.Instantiate(prefab, parent);
            Apply(go);
            return go;
        }

        /// <summary>Fixes an already instantiated model.</summary>
        public static void Apply(GameObject go)
        {
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("UCX_"))
                {
                    continue;
                }

                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.enabled = false;
                }

                if (mf.GetComponent<Collider>() == null && mf.sharedMesh != null)
                {
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
            }

            if (go.GetComponentInChildren<LODGroup>(true) != null)
            {
                return;
            }

            var levels = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                for (int i = 0; i < 3; i++)
                {
                    if (r.name.EndsWith("_LOD" + i))
                    {
                        levels[i].Add(r);
                    }
                }
            }

            if (levels[0].Count == 0 || (levels[1].Count == 0 && levels[2].Count == 0))
            {
                return;
            }

            LODGroup group = go.AddComponent<LODGroup>();
            var lods = new List<LOD> { new LOD(0.25f, levels[0].ToArray()) };
            if (levels[1].Count > 0)
            {
                lods.Add(new LOD(0.08f, levels[1].ToArray()));
            }

            if (levels[2].Count > 0)
            {
                lods.Add(new LOD(0.01f, levels[2].ToArray()));
            }

            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }
    }
}
