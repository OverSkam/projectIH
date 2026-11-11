using System.Collections.Generic;

namespace AppStein;

/// <summary>The Goblin's dialogue. Lines are picked deterministically from (category, a seed built from public match state),
/// so every peer shows the same sentence for the same event. {0} = a miner's name or a number, {1} = second argument.</summary>
public static class GoblinLines
{
    public static readonly Dictionary<string, string[]> All = new()
    {
        ["round_start"] = new[]
        {
            "Round {0}. Dig, my little investments, dig.",
            "Round {0}! Time is money, and you have neither.",
            "Round {0}. Remember: the cave giveth, and I taketh my cut.",
            "Welcome to round {0}. Please sign the waiver you didn't read.",
            "Round {0}. My accountants have already priced in your deaths.",
            "Round {0}. Synergy! Leverage! Now go get me rocks.",
            "Round {0} begins. Smile. The collapse insurance is purely decorative.",
        },
        ["deposit_small"] = new[]
        {
            "That's it? I've seen bigger returns from a piggy bank.",
            "Adorable. A start-up contribution.",
            "Hm. I'll file that under 'tips'.",
            "Pocket change. Delicious pocket change.",
            "A modest deposit. Your ambition is noted, and mocked.",
            "Ooh, a whole handful. Don't spend it all in one place. You can't. I own the place.",
        },
        ["deposit_large"] = new[]
        {
            "Now THAT is a portfolio! Shareholders weep with joy!",
            "Magnificent! Do it again, but faster!",
            "Oh, you shouldn't have! No, really, you should have. More.",
            "Beautiful, beautiful rocks. I may need a moment.",
            "A fortune! I'll put it somewhere safe. Mine.",
            "Yes! Yes! Feed the ledger!",
        },
        ["warn60"] = new[]
        {
            "One minute left. Wrap up your business. And mine.",
            "Sixty seconds. The mine's expiry date approaches.",
            "One minute! Run, little interns, run.",
            "Sixty seconds until the structural collapse. Purely scheduled, I assure you.",
            "A minute remains. Plenty of time to make me richer.",
            "Time check: sixty seconds. The ceiling sends its regards.",
        },
        ["warn30"] = new[]
        {
            "Thirty seconds! Get back to camp or get buried, I'm flexible.",
            "Half a minute. I'm already warming the bulldozer.",
            "Thirty seconds. Consider this your final quarterly warning.",
            "Thirty! Anyone still down there is now classified as 'sunk cost'.",
            "Thirty seconds, folks. The stairs are not covered by the warranty.",
            "Half a minute. Run like your bonus depends on it. It does.",
        },
        ["warn10"] = new[]
        {
            "TEN SECONDS! Do not make me file the paperwork!",
            "Ten! Drop everything. Except the ore. Bring the ore.",
            "Ten seconds. Please enjoy your last moments of employment.",
            "TEN! The ceiling is about to hit its quarterly targets.",
            "Ten seconds! Sprint! Think of the quota!",
            "Ten seconds left. I'm not saying I'm rooting for the rockfall.",
        },
        ["collapse"] = new[]
        {
            "The mine is closed. Permanently. For some of you.",
            "Collapse! Everyone outside the camp has been... restructured.",
            "And down it all comes! Anyone left below is now a foundation.",
            "Time's up! The cave has downsized its workforce.",
            "Boom! Layoffs complete.",
            "The mine has collapsed. Don't worry, it was in the fine print.",
        },
        ["quota_met"] = new[]
        {
            "Quota met! I'm almost proud. Almost.",
            "The numbers are in, and I love them. Don't get used to it.",
            "Quota reached. I will be taking credit for this.",
            "Excellent! Everyone keeps their money. This time.",
            "You hit the quota. I'll raise it next round, out of love.",
            "Targets met. Morale: unchanged, because I don't measure it.",
        },
        ["quota_missed"] = new[]
        {
            "QUOTA MISSED! Everyone's wallet just got 20% lighter. You're welcome.",
            "Pathetic! The group missed its quota, so I'm taking twenty percent. For efficiency.",
            "You failed me collectively. Twenty percent of everything, please.",
            "No quota, no mercy. Twenty percent, each. Think of it as tuition.",
            "A shortfall! My heart bleeds. Your wallets bleed more. Twenty percent.",
            "Quota missed. I'm not angry, I'm just docking your pay by a fifth.",
        },
        ["poorest"] = new[]
        {
            "{0}! Poorest miner of the round! Be ashamed. Be cursed.",
            "{0}, you delivered the least. A little curse will motivate you. Free of charge.",
            "Today's participation trophy goes to {0}: a curse. Enjoy the 60% health.",
            "{0}. Dead weight. I'm taking that as a personal insult and a health penalty.",
            "I'd like to publicly thank {0} for setting the bar so low. Cursed.",
            "{0} brought me the least. The curse is non-negotiable.",
            "Everybody look at {0}. Cursed. Poorest of the lot.",
        },
        ["survivor"] = new[]
        {
            "{0} survived alone! A bonus of {1}, because I like a winner. And a witness.",
            "Last miner standing: {0}. Here's {1}. Try not to look so smug.",
            "{0} outlived everyone. {1} bonus. Murder is a legitimate career path.",
            "Only {0} walks out. {1} for the survivor. The rest are in the foundations.",
            "A lone survivor! {0} earns {1}. I'm legally required to say congratulations.",
            "{0}: {1} for being the last one breathing. Heartwarming.",
        },
        ["winner"] = new[]
        {
            "{0} wins! The richest miner! I'll be taking a modest fee of everything. Kidding. Mostly.",
            "Congratulations {0}. You are officially my favourite employee. Until Monday.",
            "{0} is the champion. Please enjoy this gold-plated participation certificate.",
            "A winner: {0}! Money truly can't buy happiness, but you look cheerful.",
            "{0} has the most money. The rest of you have a great story and a bad pension.",
            "{0} takes it all. Well. Almost all. I'm sending an invoice.",
        },
        ["death"] = new[]
        {
            "{0} has perished. Their sack is now a charity.",
            "{0} is no more. Thoughts and prayers, but mostly the loot.",
            "Oh dear, {0} died. Unpaid leave begins immediately.",
            "{0} has been permanently downsized.",
            "{0} has left the company, vertically.",
            "{0} is dead. Anyone seen their sack? Asking for a friend.",
        },
        ["all_dead"] = new[]
        {
            "Every miner is dead. That's a record low for retention.",
            "Nobody left? Ghastly. Consider this round concluded.",
            "All dead. I'll need to hire more. Cheaper ones.",
            "Total wipeout. The Goblin is not amused. The Goblin is slightly amused.",
            "The cave won this round. I'm filing a complaint.",
            "Every miner is dead. Lunch is on me. It's not.",
        },
    };

    public static int Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    public static string Pick(string category, string seed, string a0 = "", string a1 = "")
    {
        if (!All.TryGetValue(category, out var list) || list.Length == 0) return "";
        string s = list[Hash(category + "|" + seed) % list.Length];
        return s.Replace("{0}", a0).Replace("{1}", a1);
    }
}
