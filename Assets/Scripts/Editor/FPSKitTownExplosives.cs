#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town's fuel, made to burn and blow up (Docs/DESERT_EXPLOSIVES.md; the runtime is ExplosiveProp and
    /// FuelFire). Runs once at the end of the town's build, over what the town already placed: every jerry can, gas
    /// cylinder and propane tank, a share of the steel drums (the rest hold water or nothing), and the gas station's six
    /// pumps. Petrol drums are painted red, as fuel drums are, so the player can tell which ones will go.
    ///
    /// Which drums hold fuel is decided by a hash of where each stands, never by the town's random sequence: adding this
    /// must not reshuffle the town (CLAUDE.md, "Walls varied").
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>The gas station's pumps, at their base (written by BuildGasStation, cleared in BuildFromTheme).</summary>
        private static readonly List<Vector3> _fuelPumps = new List<Vector3>();

        const string FireMaterialPath = "Assets/Fairground/Materials/Dressed/FX_Flame.mat";
        const string SmokeMaterialPath = "Assets/FPSKit_Generated/Materials/IndustrialWarehouse_Smoke.mat";
        const string FuelDrumMaterialPath = "Assets/FPSKit_Generated/Town/Materials/Town_FuelDrumRed.mat";

        /// <summary>Share of the steel drums that hold petrol, and that hold diesel. The rest are inert.</summary>
        const float PetrolDrumShare = 0.35f, DieselDrumShare = 0.2f;

        private static void ArmTownExplosives(Transform root)
        {
            if (root == null) return;
            var frag = AssetDatabase.LoadAssetAtPath<BombData>(FPSKitStore.StoreFolder + "/Bomb_Frag.asset");
            var fire = AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);
            var smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            if (frag == null) Debug.LogWarning("[FPSKit] explosives: no Bomb_Frag asset; blasts will have no fireball or sound (run the store first)");
            if (fire == null || smoke == null) Debug.LogWarning($"[FPSKit] explosives: fire or smoke material missing ({FireMaterialPath}, {SmokeMaterialPath}); fires draw with the default particle material");

            var counts = new Dictionary<ExplosiveProp.Kind, int>();
            int inert = 0;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t == null || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src == null) continue;
                if (!KindOf(src.name, t.position, out var kind)) { if (IsDrum(src.name)) inert++; continue; }
                Arm(t.gameObject, kind, frag, fire, smoke);
                counts[kind] = counts.TryGetValue(kind, out var c) ? c + 1 : 1;
            }

            // The pumps: a box round each, just outside the station mesh's own faces, so a round meets it first.
            var pumps = new GameObject("FuelPumps").transform;
            pumps.SetParent(root, false);
            for (int i = 0; i < _fuelPumps.Count; i++)
            {
                var p = _fuelPumps[i];
                var go = new GameObject($"FuelPump_{i}");
                go.transform.SetParent(pumps, false);
                go.transform.position = p + Vector3.up * 1.09f;
                go.layer = LayerMask.NameToLayer("Environment");
                go.tag = "Metal";
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(0.94f, 1.86f, 0.66f);
                Arm(go, ExplosiveProp.Kind.FuelPump, frag, fire, smoke);
                counts[ExplosiveProp.Kind.FuelPump] = counts.TryGetValue(ExplosiveProp.Kind.FuelPump, out var c) ? c + 1 : 1;
            }

            var line = new System.Text.StringBuilder();
            foreach (var kv in counts) line.Append($"{kv.Key} {kv.Value}, ");
            Debug.Log($"[FPSKit] explosives: {line}{inert} inert drums");
        }

        static bool IsDrum(string name) => name == "Barrel_01" || name == "barrel_03";

        /// <summary>What a placed prop is, if it is fuel. Drums by a hash of their place.</summary>
        static bool KindOf(string name, Vector3 at, out ExplosiveProp.Kind kind)
        {
            kind = ExplosiveProp.Kind.PetrolDrum;
            switch (name)
            {
                case "plastic_jerrycan": kind = ExplosiveProp.Kind.JerryCan; return true;
                case "small_lpg_tank": kind = ExplosiveProp.Kind.GasCylinder; return true;
                case "propane_tank": kind = ExplosiveProp.Kind.PropaneTank; return true;
            }
            if (!IsDrum(name)) return false;
            float h = PlaceHash(at);
            if (h < PetrolDrumShare) { kind = ExplosiveProp.Kind.PetrolDrum; return true; }
            if (h < PetrolDrumShare + DieselDrumShare) { kind = ExplosiveProp.Kind.DieselDrum; return true; }
            return false;
        }

        /// <summary>0-1 from a position (to the centimetre): the same every build for a prop in the same place.</summary>
        static float PlaceHash(Vector3 p)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (int v in new[] { Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f) })
                {
                    h ^= (uint)v; h *= 16777619u;
                    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                }
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static void Arm(GameObject go, ExplosiveProp.Kind kind, BombData frag, Material fire, Material smoke)
        {
            // Not `??`: in the editor GetComponent hands back a fake null that `??` takes for a component.
            var health = go.GetComponent<Health>();
            if (health == null) health = go.AddComponent<Health>();
            var x = go.GetComponent<ExplosiveProp>();
            if (x == null) x = go.AddComponent<ExplosiveProp>();
            x.kind = kind;
            x.explosionPrefab = frag != null ? frag.explosionPrefab : null;
            x.burstClip = frag != null ? frag.explodeClip : null;
            x.igniteClip = frag != null ? frag.armClip : null;
            x.fireMaterial = fire;
            x.smokeMaterial = smoke;
            health.destroyOnDeath = false;
            health.regenerates = false;
            health.maxShield = 0f;
            switch (kind)
            {
                case ExplosiveProp.Kind.PetrolDrum:
                    health.maxHealth = 45f;
                    Set(x, 170f, 6.5f, new Vector2(2f, 4.5f), 0.35f, 10f, 1.8f, 16f);
                    PaintFuelDrum(go);
                    break;
                case ExplosiveProp.Kind.DieselDrum:
                    health.maxHealth = 70f;
                    Set(x, 140f, 5.5f, new Vector2(8f, 14f), 0.03f, 14f, 1.8f, 12f);
                    break;
                case ExplosiveProp.Kind.JerryCan:
                    health.maxHealth = 12f;
                    Set(x, 80f, 3.5f, new Vector2(0f, 0.15f), 1f, 6f, 1.2f, 14f);
                    break;
                case ExplosiveProp.Kind.GasCylinder:
                    health.maxHealth = 60f;
                    Set(x, 200f, 7f, new Vector2(3f, 6f), 0.6f, 0f, 0.6f, 18f);
                    x.blastColor = new Color(1f, 0.62f, 0.2f);
                    break;
                case ExplosiveProp.Kind.PropaneTank:
                    health.maxHealth = 90f;
                    Set(x, 260f, 8.5f, new Vector2(4f, 7f), 0.6f, 0f, 0.7f, 18f);
                    x.blastColor = new Color(1f, 0.62f, 0.2f);
                    break;
                case ExplosiveProp.Kind.FuelPump:
                    health.maxHealth = 80f;
                    Set(x, 0f, 0f, Vector2.zero, 0.5f, 25f, 1.4f, 16f);
                    break;
            }
        }

        static void Set(ExplosiveProp x, float damage, float radius, Vector2 burn, float ignite, float poolSeconds, float poolRadius, float dps)
        {
            x.blastDamage = damage;
            x.blastRadius = radius;
            x.burnBeforeBurst = burn;
            x.igniteChancePerHit = ignite;
            x.poolFireSeconds = poolSeconds;
            x.poolFireRadius = poolRadius;
            x.fireDamagePerSecond = dps;
        }

        /// <summary>Petrol drums in red, as fuel drums are painted: the drum's own material, its colour multiplied toward red.</summary>
        static void PaintFuelDrum(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || !src.HasProperty("_BaseColor")) continue;
                    string path = FuelDrumMaterialPath.Replace("Town_FuelDrumRed", "Town_FuelDrumRed_" + src.name);
                    var red = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (red == null)
                    {
                        red = new Material(src) { name = "Town_FuelDrumRed_" + src.name };
                        AssetDatabase.CreateAsset(red, path);
                    }
                    else red.CopyPropertiesFromMaterial(src);
                    // A worn red: linear albedo about 0.25 / 0.03 / 0.02 over the drum's own detail (painted metal range).
                    red.SetColor("_BaseColor", new Color(1.0f, 0.16f, 0.11f));
                    red.SetFloat("_Metallic", 0f);
                    EditorUtility.SetDirty(red);
                    mats[i] = red;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
#endif
