using System.Collections.Generic;

namespace AppStein;

/// <summary>The Goblin's dialogue: greedy, bored, cruel-funny. Lines are picked deterministically from (category, a seed built from public match state),
/// so every peer shows the same sentence for the same event. {0} = a miner's name (or a number), {1} = second argument.
/// The Goblin never says who is ahead or behind while the round runs; only the weighing reveals piles.</summary>
public static class GoblinLines
{
    public static readonly Dictionary<string, string[]> All = new()
    {
        ["round_start"] = new[]
        {
            "Round {0}. Bring me weight, little investments. Bring me a lot of it.",
            "Round {0}! Somebody goes down the shaft tonight. Try to make it somebody else.",
            "Round {0}. I am bored and peckish. This is a dangerous combination for you.",
            "Welcome to round {0}. The lightest miner will be thrown away. Efficiency!",
            "Round {0}. Dig, haul, feed. The ones who feed least get fed to the dark.",
            "Round {0} begins. Do not worry about the weighing. Worry about being the lightest.",
            "Round {0}. Chin up, shoulders back, sack full. I judge by weight alone.",
        },
        ["feed_small"] = new[]
        {
            "That's it? I've had heavier dust bunnies.",
            "Adorable. A snack for an ant.",
            "Hm. A nibble. I'll bill you for the chewing.",
            "Pocket change. Delicious, pitiful pocket change.",
            "Was that tribute or did you sneeze?",
            "A morsel. Bring friends. Bring more.",
            "I felt that. Barely. Barely felt it.",
        },
        ["feed_large"] = new[]
        {
            "Now THAT has some heft! Do it again, but faster!",
            "Mmm, weighty. I can hear the Scale clearing its throat.",
            "Oh, you shouldn't have! No, really, you should have. More.",
            "Beautiful, beautiful rocks. I may need a moment. Not a long one.",
            "A proper mouthful! Somebody is trying to live.",
            "Yes! Yes! Feed the Scale!",
            "That's the stuff. I almost stopped being bored.",
        },
        ["feed_stein"] = new[]
        {
            "A stein! You fed me your own power? Bold. Foolish, but bold.",
            "Crunchy. I do love it when they give up their toys.",
            "Mmm. {0}. A little dusty, a little desperate. Delicious.",
            "You paid in power. I'll spend it wisely. On snacks.",
            "A whole stein, gone. I won't tell you whether it was enough.",
            "Oh, that one had a lovely glow. It's mine now. Thank you for the donation.",
            "A power stein, willingly fed? I feel valued. For once.",
        },
        ["warn60"] = new[]
        {
            "One minute. The lift leaves with or without your dignity.",
            "Sixty seconds. Anyone not in my Hall weighs nothing. Literally.",
            "One minute left! Run, little interns, run.",
            "A minute remains. Plenty of time to hurry. Not plenty of time to dawdle.",
            "Sixty seconds, and the lift does not wait. It has never waited.",
            "Time check: one minute. Feed me, then stand where I can see you.",
        },
        ["warn30"] = new[]
        {
            "Thirty seconds! Get to the Hall or be weighed as air.",
            "Half a minute. I'm already warming up the lift. And the shaft.",
            "Thirty seconds. Anyone still below is now classified as 'sunk cost'.",
            "Thirty! Everything you carry will be lost if you are not here. Sprint!",
            "Thirty seconds, folks. The stairs are not covered by the warranty.",
            "Half a minute. Run like your weight depends on it. It does.",
        },
        ["warn10"] = new[]
        {
            "TEN SECONDS! Do not make me file the paperwork!",
            "Ten! Drop everything you don't want to be weighed. Run!",
            "Ten seconds. Enjoy your last moments of employment.",
            "TEN! The lift is clearing its throat!",
            "Ten seconds! Sprint! Think of your pile!",
            "Ten seconds left. I'm not saying I'm rooting for the ones outside.",
        },
        ["weigh_start"] = new[]
        {
            "The lift has left. Everything outside the Hall is now someone else's problem. Line up for the Scale.",
            "Time! Face the Scale, all of you. Let us see what you are worth.",
            "The lift is gone, the doors are shut. Now we weigh. My favourite part.",
            "That's the whistle. Piles on the Scale, one by one. Don't fidget.",
            "Stand still, look pretty, and let the Scale do the talking.",
            "Round {0} is over. Everyone in the Hall: you are about to be measured. Everyone else: you weigh nothing.",
        },
        ["reveal_first"] = new[]
        {
            "Starting at the top: {0}. Weight {1}. Oh, show-off.",
            "First on the Scale: {0}, with {1}. Decent. Not safe, but decent.",
            "{0} puts {1} on the Scale. A solid start to the evening.",
            "Heaviest so far, and so far is the first: {0}, {1}.",
            "{0}: {1}. The bar is set. Let's see who limbo-dances under it.",
            "{0} weighs {1}. Nobody is safe yet. Not even them.",
        },
        ["reveal_mid"] = new[]
        {
            "{0}: {1}. Respectable. Or lucky.",
            "{0} brings {1}. I've seen worse. Today.",
            "The Scale says {1} for {0}. The Scale is rarely kind.",
            "{0}, {1}. Safe for now, which is a strange thing to be proud of.",
            "{1}. That's {0}'s weight. Don't clap, it's only a number.",
            "{0} weighs {1}. Next! My patience is lighter than your piles.",
            "{0}: {1}. It could be worse. It could be {0} next to someone heavier.",
        },
        ["reveal_last"] = new[]
        {
            "And last, the lightest: {0}, with {1}. Oh dear. Oh dear, oh dear, oh dear.",
            "{0}: {1}. That is the end of the Scale. And, I suspect, the end of {0}.",
            "Final pile: {0}, {1}. I'd say good luck, but it has already been used up.",
            "{0} weighs {1}. Anyone lighter? No? Then I know whom I'm having for dessert.",
            "Last one: {0}, {1}. The Scale is smiling. I am smiling. {0} is not.",
            "{0}: {1}. The lightest of the lot. The shaft sends its regards.",
        },
        ["reveal_zero"] = new[]
        {
            "{0}: zero. Nothing. A void with a name tag.",
            "{0} brought absolutely nothing. I'm almost impressed by the commitment.",
            "The Scale shows zero for {0}. It checked twice. So did I.",
            "{0} weighs nothing. Not 'little'. Nothing. Fascinating.",
            "Zero for {0}. Did they miss the lift or just miss the point?",
            "{0}, zero. A perfect score, in the wrong direction.",
        },
        ["verdict"] = new[]
        {
            "The lightest is {0}. Into the shaft you go. It's nothing personal. It's arithmetic.",
            "{0}, you are the lightest. That is a medical fact and also a death sentence.",
            "Verdict: {0}. Too light, too late.",
            "{0}. Lightest in the Hall. I'll miss you roughly as much as you weighed.",
            "The Scale has spoken, and it said '{0}'. I just translate.",
            "{0} is the lightest. Please remain calm, and then please remain falling.",
            "Weighed, measured, found wanting: {0}.",
        },
        ["verdict_tie"] = new[]
        {
            "A tie for lightest: {0}. Two for one! A bargain!",
            "{0}: the same pathetic weight. I'll take all of you. I'm not proud.",
            "Equally light: {0}. Equally doomed. How touching.",
            "It's a tie, so it's a pair: {0}. The shaft likes company.",
            "{0} weigh the same and share a fate. Teamwork!",
            "{0}, the lightest, together. Quite romantic, really.",
        },
        ["throw"] = new[]
        {
            "Up you go, {0}. Mind the drop. Actually, don't.",
            "Wheeee! Goodbye, {0}. Try to land on something soft. There is nothing soft.",
            "Off you go, {0}. Say hi to whatever lives down there.",
            "{0} takes the express route. Down, down, down.",
            "Fly, {0}! Fly! Oh, you don't. Pity.",
            "And down goes {0}. Tidy. Quiet. I'll hear them hit eventually.",
        },
        ["final_two"] = new[]
        {
            "Two left, and the heavier one is {0}. {0} wins. {1} goes down the shaft. The end.",
            "The final weighing: {0} is the heavier. {1} is the lighter. You can guess the rest.",
            "{0} wins by weight. {1} loses by exactly the same. Physics!",
            "Last two miners: {0} outweighs {1}. That is the whole game. Congratulations to one of them.",
            "{0} tips the Scale. {1} tips into the shaft. Perfect symmetry.",
            "It is over: {0} is heavier. {1} is a story I will tell at parties.",
        },
        ["winner"] = new[]
        {
            "{0} wins! The last miner standing. I'll be taking a modest fee of everything. Kidding. Mostly.",
            "Congratulations {0}. You are officially my favourite employee. Until the next shift.",
            "{0} is the champion. Please enjoy this complimentary shaft-free evening.",
            "A winner: {0}! Heavy where it counts. The rest are now very light company.",
            "{0} survives the Scale. The others have a great story, and a long way down.",
            "{0} takes it all. Well. Almost all. I'm sending an invoice.",
            "{0} is the last one in the Hall. I'm bored already. Let's do it again.",
        },
        ["death"] = new[]
        {
            "{0} has perished. Their sack is now a charity.",
            "{0} is no more. Thoughts and prayers, but mostly the loot.",
            "Oh dear, {0} died. Unpaid leave begins immediately.",
            "{0} has been permanently downsized. For this round.",
            "{0} has left the company, vertically.",
            "{0} is dead. Anyone seen their sack? Asking for a friend.",
        },
        ["downed"] = new[]
        {
            "{0} is down. Their pockets are open for business.",
            "{0} has fallen over. Thirty seconds to get up, or to be robbed.",
            "Oh look, {0} is lying down on the job.",
            "{0} is bleeding on my floor. Someone fetch a mop. Or a thief.",
            "{0} went down. Their stones are now just... available.",
            "Down goes {0}. The clock is ticking, and so are the vultures.",
        },
        ["all_dead"] = new[]
        {
            "Every miner is dead. That's a record low for retention.",
            "Nobody left in the cave? Ghastly. Weigh what's already fed.",
            "All dead. I'll weigh whatever got to me in time.",
            "Total wipeout. The cave won this round. I'm filing a complaint.",
            "Every miner is dead. The round is over. Lunch is on me. It's not.",
            "The cave is empty. The Scale does not care. Let's begin.",
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
