using UnityEngine;

/// <summary>
/// What a townsperson can be doing at a spot (TownHouseholds, CivilianLife; Docs/DESERT_PEOPLE.md). The number is the
/// civilians' animator's "Activity" parameter, and is saved in every built town: append, never reorder.
/// </summary>
public enum LifeActivity
{
    None = 0,
    SitChair = 1,     // at a table: talking, resting
    SitSofa = 2,      // watching the television
    SitFloor = 3,     // on a majlis mattress
    Sleep = 4,        // lying on a bed
    Cook = 5,         // at the burners
    Wash = 6,         // at the sink
    Pray = 7,         // on the floor of a quiet room
    Phone = 8,        // standing, on the phone
    Talk = 9,         // standing, talking (two at the same spot pair up)
    Browse = 10,      // in a shop, at the shelves
    Pay = 11,         // in a shop, at the counter
    Keeper = 12,      // behind the counter, minding the shop
    Clean = 13,       // sweeping, tidying
    Cower = 14,       // when the shooting starts
    Window = 15,      // looking out
    Eat = 16,         // at a table, eating
    SitBed = 17,      // on the edge of a bed
}

public static class LifeActivities
{
    /// <summary>The clip the builder looks for, Assets/Animations/Mixamo/Life/Life_&lt;name&gt;.fbx.</summary>
    public static string ClipName(LifeActivity a) => "Life_" + a;

    /// <summary>Done sitting or lying on something: the agent stops at the approach point and the body is moved onto the pose.</summary>
    public static bool Seated(LifeActivity a) =>
        a == LifeActivity.SitChair || a == LifeActivity.SitSofa || a == LifeActivity.SitFloor || a == LifeActivity.Sleep ||
        a == LifeActivity.Eat || a == LifeActivity.SitBed;

    /// <summary>How long it lasts, seconds (a range). Keeper never ends on its own.</summary>
    public static Vector2 Duration(LifeActivity a) => a switch
    {
        LifeActivity.Sleep => new Vector2(60f, 150f),
        LifeActivity.SitSofa or LifeActivity.SitFloor => new Vector2(30f, 90f),
        LifeActivity.SitChair or LifeActivity.Eat => new Vector2(25f, 70f),
        LifeActivity.Cook => new Vector2(20f, 50f),
        LifeActivity.Pray => new Vector2(30f, 60f),
        LifeActivity.Browse => new Vector2(8f, 20f),
        LifeActivity.Pay => new Vector2(5f, 9f),
        LifeActivity.Keeper => new Vector2(9999f, 9999f),
        _ => new Vector2(12f, 35f),
    };

    /// <summary>How often a person at home picks it, against the others the house has.</summary>
    public static float HomeWeight(LifeActivity a) => a switch
    {
        LifeActivity.SitSofa => 3f,
        LifeActivity.SitFloor => 3f,
        LifeActivity.SitChair => 1.5f,
        LifeActivity.Eat => 1.5f,
        LifeActivity.Cook => 2f,
        LifeActivity.Wash => 1f,
        LifeActivity.SitBed => 0.8f,
        LifeActivity.Sleep => 0.5f,
        LifeActivity.Pray => 0.8f,
        LifeActivity.Phone => 1f,
        LifeActivity.Clean => 0.8f,
        LifeActivity.Talk => 1f,
        LifeActivity.Window => 0.8f,
        _ => 0f,
    };
}
