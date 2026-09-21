using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player has done, and what is left to do.
///
/// <b>Every achievement is derived, never stored.</b> Nothing here records that an
/// achievement was earned -- each one is a threshold read against <see cref="PlayerStats"/>
/// the moment somebody looks. That rules out the entire class of bug where a counter and a
/// flag disagree, and it means an achievement added later is retroactive for free: a player
/// with four hundred kills already has the hundred-kill one the first time the screen
/// opens, which is the only behaviour that is not insulting.
///
/// The cost is that nothing can be awarded for something not counted, so a new achievement
/// usually means a new counter first. That is the right way round: the counter is the fact
/// and the achievement is an opinion about it.
/// </summary>
public static class Achievements
{
    public enum Category
    {
        Combat,
        Progression,
        Challenge,
        Economy,
    }

    /// <summary>One achievement: what it is called, what it asks for, and where to look.</summary>
    public readonly struct Entry
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Detail;
        public readonly Category Group;
        public readonly int Target;
        readonly Func<int> _progress;

        public Entry(string id, string title, string detail, Category group, int target, Func<int> progress)
        {
            Id = id; Title = title; Detail = detail;
            Group = group; Target = target; _progress = progress;
        }

        /// <summary>How far along, never past the target.</summary>
        public int Progress => Mathf.Clamp(_progress?.Invoke() ?? 0, 0, Target);

        public bool Earned => (_progress?.Invoke() ?? 0) >= Target;

        /// <summary>0 to 1, for a bar.</summary>
        public float Fraction => Target <= 0 ? 0f : Progress / (float)Target;

        /// <summary>"340 / 500", or the title of the thing once it is done.</summary>
        public string Readout => Earned ? "COMPLETE" : $"{Progress} / {Target}";
    }

    /// <summary>
    /// The catalogue.
    ///
    /// Tiers climb by roughly 5x rather than evenly, so the first of a set lands inside a
    /// session or two and the last is a genuine target. Evenly spaced tiers give three
    /// achievements that all arrive at once and then nothing.
    /// </summary>
    static readonly Entry[] All =
    {
        // ---- combat ----
        new Entry("kills.100",   "Contact",        "Put down 100 hostiles.",            Category.Combat, 100,  () => PlayerProfile.TotalKills),
        new Entry("kills.1000",  "Attrition",      "Put down 1,000 hostiles.",          Category.Combat, 1000, () => PlayerProfile.TotalKills),
        new Entry("head.50",     "Precision",      "Land 50 headshots.",                Category.Combat, 50,   () => PlayerStats.Headshots),
        new Entry("head.500",    "Marksman",       "Land 500 headshots.",               Category.Combat, 500,  () => PlayerStats.Headshots),
        new Entry("bomb.100",    "Demolition",     "Kill 100 with explosives.",         Category.Combat, 100,  () => PlayerStats.BombKills),
        new Entry("combo.10",    "Chain",          "Reach a 10 kill chain.",            Category.Combat, 10,   () => PlayerStats.BestCombo),
        new Entry("combo.25",    "Unbroken",       "Reach a 25 kill chain.",            Category.Combat, 25,   () => PlayerStats.BestCombo),
        new Entry("boss.10",     "Headhunter",     "Bring down 10 bosses.",             Category.Combat, 10,   () => PlayerStats.BossesKilled),

        // ---- progression ----
        new Entry("clear.10",    "Deployed",       "Clear 10 levels.",                  Category.Progression, 10, () => PlayerStats.LevelsCleared),
        new Entry("clear.48",    "Full Tour",      "Clear all 48 levels.",              Category.Progression, 48, () => PlayerStats.LevelsCleared),
        new Entry("star.24",     "Decorated",      "Earn 24 stars.",                    Category.Progression, 24, () => PlayerStats.TotalStars),
        new Entry("star.144",    "Exemplary",      "Earn every star.",                  Category.Progression, 144, () => PlayerStats.TotalStars),
        new Entry("three.10",    "Thorough",       "Take 3 stars on 10 levels.",        Category.Progression, 10, () => PlayerStats.ThreeStarLevels),
        new Entry("arena.6",     "Cartographer",   "Finish all six arenas.",            Category.Progression, 6,  () => PlayerStats.ArenasFinished),

        // ---- challenge ----
        new Entry("flaw.1",      "Untouched",      "Clear a level without being hit.",  Category.Challenge, 1,  () => PlayerStats.FlawlessClears),
        new Entry("flaw.10",     "Ghost",          "Do it 10 times.",                   Category.Challenge, 10, () => PlayerStats.FlawlessClears),
        new Entry("fast.10",     "Ahead of Time",  "Clear 10 levels with half the clock left.", Category.Challenge, 10, () => PlayerStats.FastClears),

        // ---- economy ----
        new Entry("coin.10k",    "Salvage",        "Earn 10,000 coins.",                Category.Economy, 10000, () => PlayerStats.CoinsEarned),
        new Entry("coin.100k",   "Quartermaster",  "Earn 100,000 coins.",               Category.Economy, 100000, () => PlayerStats.CoinsEarned),
        new Entry("buy.10",      "Outfitted",      "Make 10 purchases.",                Category.Economy, 10, () => PlayerStats.ItemsBought),
    };

    /// <summary>Every achievement, in catalogue order.</summary>
    public static IReadOnlyList<Entry> Catalogue => All;

    /// <summary>Just the ones in this group.</summary>
    public static List<Entry> In(Category group)
    {
        var list = new List<Entry>();
        foreach (var e in All)
            if (e.Group == group) list.Add(e);
        return list;
    }

    public static int EarnedCount
    {
        get
        {
            int n = 0;
            foreach (var e in All) if (e.Earned) n++;
            return n;
        }
    }

    public static int Total => All.Length;

    /// <summary>"7 / 20".</summary>
    public static string Readout => $"{EarnedCount} / {Total}";

}
