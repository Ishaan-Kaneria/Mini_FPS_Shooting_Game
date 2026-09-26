using UnityEngine;

/// <summary>
/// Every sound an enemy makes with its own throat, in two voices: a human soldier and
/// a creature. Which one an enemy uses is <see cref="EnemyArchetype.voice"/>, so a new
/// enemy picks its voice the way it picks its colour, as data.
///
/// Each slot is an array picked from at random, because one clip retriggered is the
/// most obviously synthetic sound a firefight can make. Any slot may be empty; the
/// voice falls back along a short chain (a scream to a grunt, a headshot death to a
/// death) rather than going silent.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Voice Bank", fileName = "VoiceBank")]
public class VoiceBank : ScriptableObject
{
    [System.Serializable]
    public class Set
    {
        [Tooltip("The moment it first sees you. A shout, or a roar.")]
        public AudioClip[] alert;

        [Tooltip("A round landing. Short, and cut off.")]
        public AudioClip[] pain;

        [Tooltip("A heavy hit, or a limb going: a scream, or a howl.")]
        public AudioClip[] hurt;

        [Tooltip("Dying from anything but a headshot. Long, falling, running out of air.")]
        public AudioClip[] death;

        [Tooltip("Dying from a headshot. Barely a sound: a choke, a gurgle, or nothing.")]
        public AudioClip[] headshotDeath;

        [Tooltip("Made every few seconds while it hunts you. Shouts and heavy breathing " +
                 "from a soldier, growls from a creature.")]
        public AudioClip[] hunt;

        [Tooltip("The effort of a swing or a lunge.")]
        public AudioClip[] attack;

        [Tooltip("Dragging itself along the floor on a wrecked leg.")]
        public AudioClip[] crawl;
    }

    public Set human = new Set();
    public Set creature = new Set();

    public Set For(EnemyVoice.Kind kind) => kind == EnemyVoice.Kind.Creature ? creature : human;

    /// <summary>A random clip from the first of the slots that has one.</summary>
    public static AudioClip Pick(params AudioClip[][] slots)
    {
        foreach (var slot in slots)
        {
            if (slot == null || slot.Length == 0) continue;

            var clip = slot[Random.Range(0, slot.Length)];
            if (clip != null) return clip;
        }

        return null;
    }
}
