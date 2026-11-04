#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The real heights of the bodies in the game, in metres, as built: a standard enemy's crown and head joint, and the
    /// player's. No rendering. Run after any change to a body's model or scale.
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitBodyMeasure.Batch
    /// </summary>
    public static class FPSKitBodyMeasure
    {
        public static void Batch()
        {
            try
            {
                Measure("enemy prefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab"));
                Measure("player body", PlayerBodySettings.Active != null ? PlayerBodySettings.Active.bodyPrefab : null);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] body measure FAILED: " + e); EditorApplication.Exit(1); }
        }

        static void Measure(string label, GameObject prefab)
        {
            if (prefab == null) { Debug.Log($"[FPSKit] body measure: {label}: none"); return; }
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            try
            {
                var animator = go.GetComponentInChildren<Animator>();
                var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var eyeL = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
                float top = float.MinValue, bottom = float.MaxValue;
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!r.gameObject.activeInHierarchy || r.name.ToLowerInvariant().Contains("cap") || r.name.ToLowerInvariant().Contains("helmet")) continue;
                    var baked = new Mesh();
                    r.BakeMesh(baked, true);
                    foreach (var v in baked.vertices)
                    {
                        float y = r.transform.TransformPoint(v).y;
                        if (y > top) top = y;
                        if (y < bottom) bottom = y;
                    }
                    Object.DestroyImmediate(baked);
                }
                Debug.Log($"[FPSKit] body measure: {label}: rig root scale {go.transform.localScale.x:0.000}, feet {bottom:0.000}, crown {top:0.000}, " +
                          $"head joint {(head != null ? head.position.y : -1f):0.000}, eye joint {(eyeL != null ? eyeL.position.y : -1f):0.000}, height {top - bottom:0.000}");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
#endif
