#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Audits the C# statics that survive a play session.
    ///
    /// Domain reload is disabled in this project (EnterPlayModeOptions 3), so no
    /// static is ever cleared between play sessions. A gate left false or a cached
    /// reference to a destroyed object makes the game work the first time Play is
    /// pressed and fail the second, while every compile and build check still
    /// passes. The convention that prevents it is a SubsystemRegistration hook on
    /// every class that owns state-carrying statics.
    ///
    /// This enforces that convention two ways:
    ///
    ///   Audit()    -- structural. Finds every state-carrying static in the runtime
    ///                 assembly and reports the ones whose class has no reset hook.
    ///                 Valid in edit mode, headless, and CI. This is the check that
    ///                 catches a newly added static that nobody wired a hook to.
    ///
    ///   Snapshot() -- value level. Prints what every tracked static currently holds.
    ///                 Run it right after entering play mode to confirm the hooks
    ///                 actually fired; run it in edit mode after a session to see the
    ///                 poisoned state the next session would otherwise inherit.
    ///
    /// Audit deliberately proves only that a hook exists, not that it clears every
    /// field -- that would mean reading method bodies. Snapshot is how the values
    /// themselves get checked.
    /// </summary>
    public static class FPSKitStaticProbe
    {
        /// <summary>
        /// Gameplay code has no .asmdef, so all of Assets/Scripts/Runtime lands in
        /// Unity's default assembly. Scanning it by name is what keeps editor tooling
        /// and package code out of the audit.
        /// </summary>
        private const string RuntimeAssembly = "Assembly-CSharp";

        /// <summary>
        /// Statics that look like carried state but are not, with the reason they are
        /// safe. Anything listed here is a judgement call being recorded rather than
        /// repeated: add to it only when a static genuinely cannot leak between
        /// sessions, and say why.
        /// </summary>
        private static readonly (string Type, string Member, string Why)[] Exempt =
        {
            ("UITheme", "Signals",
             "a readonly array of colours, written once where it is declared and never " +
             "again: SignalFor only indexes it, so there is nothing to go stale"),

            ("Achievements", "All",
             "the catalogue, built once and never written to. Every entry derives its " +
             "progress from PlayerStats when it is asked, so what it reports is live even " +
             "though the array itself survives a play session"),

            ("PlayerRank", "Titles",
             "a readonly array of rank names, written once where it is declared and " +
             "never again: TitleFor only indexes it, and the rank itself is derived from " +
             "PlayerStats every time it is asked"),

            ("EnemyAI", "NeighbourBuffer",
             "scratch buffer: OverlapSphereNonAlloc rewrites it before every read and " +
             "the loop is bounded by the returned count, so nothing stale is ever read"),
        };

        // ==================================================================
        // Menu
        // ==================================================================
        [MenuItem("FPSKit/Diagnostics/Audit Static Reset Hooks", false, 80)]
        private static void AuditMenu()
        {
            var problems = Audit(out string report);
            Debug.Log(report);

            if (problems.Count == 0)
                EditorUtility.DisplayDialog("Static reset audit",
                    "Every state-carrying static has a reset hook on its own class.", "OK");
            else
                EditorUtility.DisplayDialog("Static reset audit",
                    $"{problems.Count} static(s) have no reset hook.\n\n" +
                    string.Join("\n", problems) +
                    "\n\nSee the console for the full report.", "OK");
        }

        [MenuItem("FPSKit/Diagnostics/Log Static State", false, 81)]
        private static void SnapshotMenu() => Debug.Log(Snapshot());

        // ==================================================================
        // Structural audit
        // ==================================================================
        /// <summary>
        /// Returns one entry per state-carrying static whose declaring class has no
        /// SubsystemRegistration hook. An empty list means the convention holds.
        /// </summary>
        public static List<string> Audit(out string report)
        {
            var problems = new List<string>();
            var sb = new StringBuilder("[FPSKitStaticProbe] reset-hook audit\n");

            foreach (var group in Scan())
            {
                bool hooked = HasResetHook(group.Key);

                // A class whose every static is exempt needs no hook, so labelling it
                // [NONE] would read as a failure that the problem count contradicts.
                bool allExempt = true;
                foreach (var member in group.Value)
                    if (ExemptReason(group.Key.Name, member.Name) == null) allExempt = false;

                sb.Append(hooked ? "  [hook]   " : allExempt ? "  [exempt] " : "  [NONE]   ")
                  .Append(group.Key.Name).Append('\n');

                foreach (var member in group.Value)
                {
                    string exemptWhy = ExemptReason(group.Key.Name, member.Name);

                    if (exemptWhy != null)
                        sb.Append("      - ").Append(member.Name)
                          .Append("  (exempt: ").Append(exemptWhy).Append(")\n");
                    else
                        sb.Append("      - ").Append(member.Name).Append('\n');

                    if (!hooked && exemptWhy == null)
                        problems.Add($"{group.Key.Name}.{member.Name} has no reset hook");
                }
            }

            sb.Append(problems.Count == 0
                ? "  OK: every state-carrying static is covered."
                : $"  {problems.Count} static(s) missing a reset hook.");

            report = sb.ToString();
            return problems;
        }

        // ==================================================================
        // Value snapshot
        // ==================================================================
        /// <summary>
        /// Current value of every tracked static, plus the engine globals that persist
        /// the same way. Time.timeScale is not a static but survives a session exactly
        /// like one, which is why GameDirector resets it in its own hook.
        /// </summary>
        public static string Snapshot()
        {
            var sb = new StringBuilder("[FPSKitStaticProbe] static state\n");
            sb.Append($"  isPlaying = {Application.isPlaying}\n")
              .Append($"  Time.timeScale = {Time.timeScale}\n")
              .Append($"  Cursor.lockState = {Cursor.lockState}\n");

            foreach (var group in Scan())
            {
                sb.Append("  ").Append(group.Key.Name).Append('\n');

                foreach (var member in group.Value)
                    sb.Append("      ").Append(member.Name).Append(" = ")
                      .Append(Describe(ReadValue(member.Member))).Append('\n');
            }

            return sb.ToString();
        }

        // ==================================================================
        // Reflection
        // ==================================================================
        private struct Tracked
        {
            public string Name;
            public MemberInfo Member;
        }

        /// <summary>
        /// Every state-carrying static in the runtime assembly, grouped by declaring
        /// type. Consts are compile-time and cannot drift; compiler-generated closure
        /// and cache types hold no gameplay state. An auto-property is reported under
        /// its property name rather than its mangled backing field.
        /// </summary>
        private static List<KeyValuePair<Type, List<Tracked>>> Scan()
        {
            var results = new List<KeyValuePair<Type, List<Tracked>>>();

            Assembly assembly;
            try
            {
                assembly = Assembly.Load(RuntimeAssembly);
            }
            catch (Exception)
            {
                // No gameplay scripts compiled yet. Nothing to audit is not a failure.
                return results;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                var found = new List<Tracked>();

                foreach (var field in type.GetFields(BindingFlags.Static |
                                                     BindingFlags.Public |
                                                     BindingFlags.NonPublic |
                                                     BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral) continue;   // const

                    // <b>A readonly field of an immutable type is not state.</b> A colour,
                    // a number or a string assigned where it is declared cannot change
                    // after that, so there is nothing to go stale between play sessions and
                    // a reset hook would have nothing to reset. Color cannot be const --
                    // it is not a compile-time constant -- so UITheme's palette is
                    // static readonly and was being reported as nineteen missing hooks.
                    //
                    // Deliberately only value types and strings. A readonly *array* or list
                    // is still state: the reference cannot change and its contents very much
                    // can, which is exactly why EnemyAI.NeighbourBuffer is exempted by name
                    // below rather than by this rule.
                    if (field.IsInitOnly &&
                        (field.FieldType.IsValueType || field.FieldType == typeof(string)))
                        continue;
                    found.Add(new Tracked { Name = FriendlyName(field.Name), Member = field });
                }

                if (found.Count > 0)
                {
                    found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    results.Add(new KeyValuePair<Type, List<Tracked>>(type, found));
                }
            }

            results.Sort((a, b) => string.CompareOrdinal(a.Key.Name, b.Key.Name));
            return results;
        }

        /// <summary>Unwraps "&lt;Instance&gt;k__BackingField" back to "Instance".</summary>
        private static string FriendlyName(string fieldName)
        {
            int open = fieldName.IndexOf('<');
            int close = fieldName.IndexOf('>');

            return open == 0 && close > 1
                ? fieldName.Substring(1, close - 1)
                : fieldName;
        }

        private static bool HasResetHook(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Static |
                                                   BindingFlags.Public |
                                                   BindingFlags.NonPublic |
                                                   BindingFlags.DeclaredOnly))
            {
                var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
                    method, typeof(RuntimeInitializeOnLoadMethodAttribute));

                if (attribute != null &&
                    attribute.loadType == RuntimeInitializeLoadType.SubsystemRegistration)
                    return true;
            }

            return false;
        }

        private static string ExemptReason(string type, string member)
        {
            foreach (var entry in Exempt)
                if (entry.Type == type && entry.Member == member)
                    return entry.Why;

            return null;
        }

        private static object ReadValue(MemberInfo member)
        {
            try
            {
                return ((FieldInfo)member).GetValue(null);
            }
            catch (Exception e)
            {
                return "<unreadable: " + e.GetType().Name + ">";
            }
        }

        /// <summary>
        /// A destroyed UnityEngine.Object is the exact shape of this bug class -- the
        /// reference is non-null to C# but null to Unity -- so it gets its own label
        /// rather than being flattened to "null".
        /// </summary>
        private static string Describe(object value)
        {
            if (value == null) return "null";

            if (value is UnityEngine.Object unityObject)
                return unityObject == null ? "null (destroyed object still referenced)" : unityObject.name;

            if (value is ICollection collection) return $"count={collection.Count}";

            if (value is Delegate handler)
                return $"{handler.GetInvocationList().Length} subscriber(s)";

            return value.ToString();
        }
    }
}
#endif
