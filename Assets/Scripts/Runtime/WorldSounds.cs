using UnityEngine;

/// <summary>
/// The world's synthesised sounds (Tools/generate-world-audio.py), from Resources so props and cars need no field set in
/// a scene. Resources caches them itself: nothing here holds state.
/// </summary>
public static class WorldSounds
{
    static AudioClip Clip(string name) => Resources.Load<AudioClip>("Audio/" + name);
    static AudioClip Any(string name, int count) => Clip($"{name}_{Random.Range(0, count)}");

    public static AudioClip Car(string name) => Clip(name);
    public static AudioClip Horn() => Clip(Random.value < 0.5f ? "CarHorn_A" : "CarHorn_B");
    public static AudioClip GlassCrack() => Any("GlassCrack", 3);
    public static AudioClip GlassShatter(bool big) => big ? Clip("GlassShatterBig") : Any("GlassShatter", 3);
    public static AudioClip GlassCubes() => Clip("GlassCubes");
    public static AudioClip GlassTinkle() => Any("GlassTinkle", 2);

    public static AudioClip Prop(LooseProp.Stuff stuff) => stuff switch
    {
        LooseProp.Stuff.Wood => Clip("PropHit_Wood_0"),
        LooseProp.Stuff.Plastic => Clip("PropHit_Plastic_0"),
        _ => Any("PropHit_Metal", 2),
    };
}
