#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Authors the first-person arms' movement clips (<see cref="FirstPersonMoves"/>): one stride
    /// each of walk, run, sprint, run-while-firing and aim-walk, as keyframed curves for the
    /// rifle (the hands follow it by IK). The two footfalls are at 0 and 0.5 of the stride:
    ///
    /// - **up**: lowest on each footfall (the weight landing), highest between;
    /// - **right / roll / yaw**: once across and back over the stride, towards the stepping side;
    /// - **pitch**: the muzzle nods down on each footfall;
    /// - **ahead**: a small push forward between footfalls, back on them.
    ///
    /// The sprint is the big one: the rifle across the chest (Weapon's sprint pose) swung with the
    /// arms' pump. Run-while-firing is the run steadied; aim-walk barely moves.
    ///
    ///   FPSKit > Player > Rebuild First-Person Moves   (overwrites the asset's curves)
    ///   Ensure() makes the asset if missing and leaves an existing one alone.
    /// </summary>
    public static class FPSKitFirstPersonMoves
    {
        const string Path = "Assets/FPSKit_Generated/Resources/FirstPersonMoves.asset";

        /// <summary>Makes the asset with the authored clips if there is none.</summary>
        public static FirstPersonMoves Ensure()
        {
            var moves = AssetDatabase.LoadAssetAtPath<FirstPersonMoves>(Path);
            if (moves != null) return moves;
            moves = ScriptableObject.CreateInstance<FirstPersonMoves>();
            Author(moves);
            AssetDatabase.CreateAsset(moves, Path);
            AssetDatabase.SaveAssets();
            return moves;
        }

        [MenuItem("FPSKit/Player/Rebuild First-Person Moves", false, 50)]
        public static void Rebuild()
        {
            var moves = Ensure();
            Author(moves);
            EditorUtility.SetDirty(moves);
            AssetDatabase.SaveAssets();
            Debug.Log("[FPSKit] First-person moves written: " + Path);
        }

        /// <summary>Sizes per clip: dip, sway (m), roll, nod, swing (deg), push (m).</summary>
        static void Author(FirstPersonMoves m)
        {
            m.walk = Stride(3.2f, dip: 0.005f, sway: 0.004f, roll: 1.0f, nod: 0.6f, swing: 0.6f, push: 0.002f);
            m.run = Stride(6.0f, dip: 0.011f, sway: 0.009f, roll: 2.2f, nod: 1.4f, swing: 1.2f, push: 0.004f);
            m.sprint = Stride(9.5f, dip: 0.018f, sway: 0.022f, roll: 5.0f, nod: 3.0f, swing: 4.5f, push: 0.012f);
            m.runFire = Stride(6.4f, dip: 0.006f, sway: 0.004f, roll: 1.0f, nod: 0.8f, swing: 0.5f, push: 0.002f);
            m.aimWalk = Stride(2.4f, dip: 0.0018f, sway: 0.0012f, roll: 0.35f, nod: 0.25f, swing: 0.2f, push: 0.0006f);
        }

        static FirstPersonMoves.Move Stride(float speed, float dip, float sway, float roll, float nod, float swing, float push)
        {
            return new FirstPersonMoves.Move
            {
                speed = speed,
                up = Loop((0f, -dip), (0.25f, dip * 0.6f), (0.5f, -dip), (0.75f, dip * 0.6f), (1f, -dip)),
                right = Loop((0f, 0f), (0.25f, sway), (0.5f, 0f), (0.75f, -sway), (1f, 0f)),
                ahead = Loop((0f, -push), (0.25f, push), (0.5f, -push), (0.75f, push), (1f, -push)),
                pitch = Loop((0f, nod), (0.25f, -nod * 0.5f), (0.5f, nod), (0.75f, -nod * 0.5f), (1f, nod)),
                yaw = Loop((0f, 0f), (0.25f, -swing), (0.5f, 0f), (0.75f, swing), (1f, 0f)),
                roll = Loop((0f, 0f), (0.25f, roll), (0.5f, 0f), (0.75f, -roll), (1f, 0f)),
            };
        }

        /// <summary>A looping curve through the keys, smooth, with matching slopes where it wraps.</summary>
        static AnimationCurve Loop(params (float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            foreach (var (t, v) in keys) c.AddKey(new Keyframe(t, v));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            // Same slope at both ends, so the stride joins itself without a kink.
            var first = c[0];
            var last = c[c.length - 1];
            float slope = (c[1].value - c[c.length - 2].value) / ((c[1].time + 1f) - c[c.length - 2].time);
            first.inTangent = first.outTangent = slope;
            last.inTangent = last.outTangent = slope;
            c.MoveKey(0, first);
            c.MoveKey(c.length - 1, last);
            c.preWrapMode = c.postWrapMode = WrapMode.Loop;
            return c;
        }
    }
}
#endif
