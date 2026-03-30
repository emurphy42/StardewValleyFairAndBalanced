using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;
using xTile.Dimensions;

namespace StardewValleyFairAndBalanced
{
    internal class ObjectPatches
    {
        // initialized by ModEntry.cs
        public static ModEntry ModInstance;
        public static ModConfig ModConfig;

        // map uniform random numbers (0 to 1000) to an approximate normal distribution (standard deviation 10, constrained to +/-30)
        // this is symmetric, so we just hardcode half the weights and mirror them for the other half

        private static readonly List<int> WeightedProbabilities = new() {
            1, 2, 3, 4, 5, 6, 8, 11, 14, 18, 23, 29, 36, 45, 55, 67, 81, 97, 115, 136, 159, 184, 212, 242, 274, 309, 345, 382, 421, 460
        };

        private static int GetFlatRandom(int min, int max)
        {
            return (int)Math.Round(Utility.getRandomDouble(min, max), 0);
        }

        private static int GetWeightedRandomFromFlatRandom(int r)
        {
            for (var i = 0; i < WeightedProbabilities.Count; ++i)
            {
                if (r < WeightedProbabilities[i])
                {
                    return WeightedProbabilities.Count - i;
                }
            }
            return 0;
        }

        private static int GetWeightedRandom()
        {
            var r = GetFlatRandom(0, 1000);
            return (r <= 500)
                ? GetWeightedRandomFromFlatRandom(r)
                : -GetWeightedRandomFromFlatRandom(1000 - r);
        }

        // Base game's rules for farmer's overall result, based on grange score:
        //   * DQ  = purple shorts (internally sets score to -666, lol)
        //   * 1st = 90 or more (you beat Pierre, max possible is 125)
        //   * 2nd = 75-89 (you beat Marnie)
        //   * 3rd = 60-74 (you beat Willy)
        //   * 4th = 59 or less

        // To avoid messing with fractions here, we interpret this as Pierre scoring 90, Marnie 75, Willy 60, but the farmer winning ties.

        // This mod randomizes NPC scores, roughly based on a normal distribution with standard deviation 10.
        // However, the cutoffs of 90 / 75 / 60 are hardcoded within both judgeGrange() and the much more complex checkAction(),
        // so instead of changing those, we also alter the farmer's score as needed to preserve both their place and the relative order.
        // Note that NPC scores don't need to correspond to these cutoffs.

        private static readonly List<int> baseGameScoreRanges = new() { 90, 75, 60 };

        private static readonly Dictionary<string, int> DefaultNPCAverageScores = new()
        {
            { "Pierre", baseGameScoreRanges[0] },
            { "Marnie", baseGameScoreRanges[1] },
            { "Willy", baseGameScoreRanges[2] }
        };

        private static Dictionary<string, int> NPCScores = new();

        const int grangeScore_Min = 5; // 14 base, -9 empty display
        const int grangeScore_Max = 125; // 14 base, +9 full display, +30 for variety, +8 per item
        const int grangeScore_PurpleShorts = -666;

        private static int grangeScoreBeforeAdjustment = -1;
        private static int grangeScoreAfterAdjustment = -1; // needed because checkAction() resets it to -100
        private static bool needToShowLeaderboard = false;

        private static int GetFarmerBaseGamePlace(int grangeScore)
        {
            for (var i = 1; i < 4; ++i)
            {
                if (grangeScore >= baseGameScoreRanges[i - 1])
                {
                    return i;
                }
            }
            return 4;
        }

        private static int GetFarmerModdedPlace(int grangeScore, Dictionary<string, int> NPCScores)
        {
            var farmerPlace = 1;
            foreach (var NPCName in NPCScores.Keys)
            {
                if (NPCScores[NPCName] > grangeScore)
                {
                    ++farmerPlace;
                }
            }
            return farmerPlace;
        }

        private static List<int> GetFarmerBaseGameScoreRange(int place)
        {
            var minScore = (place <= baseGameScoreRanges.Count)
                ? baseGameScoreRanges[place - 1]
                : int.MinValue;
            var maxScore = (place >= 2)
                ? baseGameScoreRanges[place - 2] - 1
                : int.MaxValue;
            return new List<int>() { minScore, maxScore };
        }

        private static List<int> GetFarmerModdedScoreRange(int grangeScore, Dictionary<string, int> NPCScores)
        {
            var minScore = int.MinValue;
            var maxScore = int.MaxValue;
            foreach (var NPCName in NPCScores.Keys)
            {
                if (NPCScores[NPCName] < grangeScore)
                {
                    minScore = Math.Max(minScore, NPCScores[NPCName] + 1);
                }
                if (NPCScores[NPCName] > grangeScore)
                {
                    maxScore = Math.Min(maxScore, NPCScores[NPCName] - 1);
                }
            }
            return new List<int>() { minScore, maxScore };
        }

        private static bool NPCIsTiedWithAnotherNPC(string NPCName)
        {
            var NPCScore = NPCScores[NPCName];
            foreach (var OtherNPCName in NPCScores.Keys)
            {
                if (OtherNPCName != NPCName && NPCScores[OtherNPCName] == NPCScore)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool Event_interpretGrangeResults_Prefix(Event __instance)
        {
            grangeScoreBeforeAdjustment = __instance.grangeScore;
            ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Original farmer score = {__instance.grangeScore}", LogLevel.Debug);

            // build list of NPC participants and their average scores
            var NPCAverageScores = new Dictionary<string, int>();
            foreach (var NPCName in DefaultNPCAverageScores.Keys)
            {
                NPCAverageScores[NPCName] = DefaultNPCAverageScores[NPCName];
            }
            foreach (var NPCName in ModConfig.NPCAverageScores.Keys)
            {
                NPCAverageScores[NPCName] = ModConfig.NPCAverageScores[NPCName];
            }
            foreach (var NPCName in NPCAverageScores.Keys)
            {
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {NPCName} average score = {NPCAverageScores[NPCName]}", LogLevel.Debug);
            }

            // build list of NPC participants and their randomized scores, but avoid having any of them tie with the farmer or each other
            NPCScores = new Dictionary<string, int>();
            foreach (var NPCName in NPCAverageScores.Keys)
            {
                NPCScores[NPCName] = NPCAverageScores[NPCName] + GetWeightedRandom();
                if (NPCScores[NPCName] == __instance.grangeScore)
                {
                    --NPCScores[NPCName];
                }
                while (NPCIsTiedWithAnotherNPC(NPCName))
                {
                    --NPCScores[NPCName];
                }
            }
            foreach (var NPCName in NPCScores.Keys)
            {
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {NPCName} randomized score = {NPCScores[NPCName]}", LogLevel.Debug);
            }

            // do we need to adjust farmer's score to preserve their place?
            if (__instance.grangeScore != grangeScore_PurpleShorts)
            {
                int farmerBaseGamePlace = GetFarmerBaseGamePlace(__instance.grangeScore);
                int farmerModdedPlace = GetFarmerModdedPlace(__instance.grangeScore, NPCScores);
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Farmer place = {farmerBaseGamePlace} (base game) / {farmerModdedPlace} (modded)", LogLevel.Debug);
                if (farmerModdedPlace > 4)
                {
                    useFallbackMethod(__instance, farmerBaseGamePlace);
                } else {
                    if (farmerModdedPlace != farmerBaseGamePlace)
                    {
                        var farmerBaseGameScoreRange = GetFarmerBaseGameScoreRange(farmerModdedPlace);
                        ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Farmer score needs to be {farmerBaseGameScoreRange[0]} to {farmerBaseGameScoreRange[1]} (base game)", LogLevel.Debug);
                        var farmerModdedScoreRange = GetFarmerModdedScoreRange(__instance.grangeScore, NPCScores);
                        ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Farmer score needs to be {farmerModdedScoreRange[0]} to {farmerModdedScoreRange[1]} (modded)", LogLevel.Debug);
                        var minScore = Math.Max(farmerBaseGameScoreRange[0], farmerModdedScoreRange[0]);
                        minScore = Math.Max(minScore, grangeScore_Min);
                        var maxScore = Math.Min(farmerBaseGameScoreRange[1], farmerModdedScoreRange[1]);
                        maxScore = Math.Min(maxScore, grangeScore_Max);
                        ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Farmer score needs to be {minScore} to {maxScore} (overall)", LogLevel.Debug);
                        if (minScore <= maxScore)
                        {
                            __instance.grangeScore = GetFlatRandom(minScore, maxScore);
                            ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Adjusted farmer score to {__instance.grangeScore}", LogLevel.Debug);
                        }
                        else
                        {
                            useFallbackMethod(__instance, farmerBaseGamePlace);
                        }
                    }
                }
            }

            // allow base game function to run normally
            return true;
        }

        private static void useFallbackMethod(Event __instance, int farmerBaseGamePlace)
        {
            ModInstance.Monitor.Log("[Stardew Valley Fair and Balanced] Falling back to simpler method", LogLevel.Debug);

            var UnassignedNPCScores = new List<int>();

            // generate random scores for NPCs ahead of the farmer (ties between NPCs are allowed here)
            for (var i = 1; i < farmerBaseGamePlace; ++i)
            {
                UnassignedNPCScores.Add(GetFlatRandom(__instance.grangeScore + 1, grangeScore_Max));
            }

            // generate random scores for NPCs behind the farmer (ties between NPCs are allowed here)
            var NumberNPCsBehindFarmer = NPCScores.Count - UnassignedNPCScores.Count;
            for (var i = 1; i <= NumberNPCsBehindFarmer; ++i)
            {
                UnassignedNPCScores.Add(GetFlatRandom(grangeScore_Min, __instance.grangeScore - 1));
            }

            // distribute these scores to NPCs, starting with base game competitors
            UnassignedNPCScores.Sort();
            UnassignedNPCScores.Reverse();
            for (var i = 0; i < UnassignedNPCScores.Count; ++i)
            {
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Generated random score {UnassignedNPCScores[i]}", LogLevel.Debug);
            }
            NPCScores["Pierre"] = UnassignedNPCScores[0];
            NPCScores["Marnie"] = UnassignedNPCScores[1];
            NPCScores["Willy"] = UnassignedNPCScores[2];
            var RemainingNPCNames = NPCScores.Keys.ToList<string>();
            RemainingNPCNames.Remove("Pierre");
            RemainingNPCNames.Remove("Marnie");
            RemainingNPCNames.Remove("Willy");
            RemainingNPCNames.Sort((a, b) => GetFlatRandom(-1, 1));
            for (var i = 3; i < UnassignedNPCScores.Count; ++i)
            {
                NPCScores[RemainingNPCNames[i - 3]] = UnassignedNPCScores[i];
            }
            foreach (var NPCName in NPCScores.Keys)
            {
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {NPCName} randomized score = {NPCScores[NPCName]}", LogLevel.Debug);
            }
        }

        public static void Event_interpretGrangeResults_Postfix(Event __instance)
        {
            grangeScoreAfterAdjustment = __instance.grangeScore;
            needToShowLeaderboard = true;

            // check for all NPCs winning, not just Pierre
            var MaxNPCScore = NPCScores.Values.Max();
            ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Max NPC score = {MaxNPCScore}", LogLevel.Debug);
            if (MaxNPCScore < __instance.grangeScore)
            {
                return;
            }
            foreach (var actor in __instance.actors)
            {
                ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Found NPC {actor.Name}", LogLevel.Trace);
                if (NPCScores.ContainsKey(actor.Name))
                {
                    if (NPCScores[actor.Name] == MaxNPCScore)
                    {
                        ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {actor.Name} won, checking for dialogue adjustment", LogLevel.Debug);
                        Dialogue dialogue = actor.TryGetDialogue("Fair_Judged_NPCWon");
                        if (dialogue != null)
                        {
                            actor.setNewDialogue(dialogue, add: true);
                            ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {actor.Name} dialogue adjusted", LogLevel.Debug);
                        }
                    }
                    if (actor.Name == "Pierre" && NPCScores[actor.Name] < MaxNPCScore)
                    {
                        ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] {actor.Name} lost, adjusting dialogue", LogLevel.Debug);
                        Dialogue dialogue = actor.TryGetDialogue("Fair_Judged_PlayerWon");
                        if (dialogue != null)
                        {
                            actor.setNewDialogue(dialogue, add: true);
                        }
                    }
                }
            }
        }

        const string LineBreak = "\r\n";
        const string ReasonsDelimiter = "/";

        private static string GetExtraPointsReason()
        {
            var reasons = ModInstance.Helper.Translation.Get("GrangeDisplayScores_ExtraPointsReasons").ToString().Split(ReasonsDelimiter);
            var reasonIndex = GetFlatRandom(0, reasons.Length - 1);
            return reasons[reasonIndex];
        }

        private static string GetPointsDeductedReason()
        {
            var reasons = ModInstance.Helper.Translation.Get("GrangeDisplayScores_PointsDeductedReasons").ToString().Split(ReasonsDelimiter);
            var reasonIndex = GetFlatRandom(0, reasons.Length - 1);
            return reasons[reasonIndex];
        }

        public static void Event_checkAction_Postfix(Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who, Event __instance)
        {
            if (!needToShowLeaderboard)
            {
                return;
            }

            var NPCNames = NPCScores.Keys.ToList<string>();
            NPCNames.Sort((a, b) => (NPCScores[a] < NPCScores[b] ? 1 : -1)); // highest to lowest
            var FarmerHasBeenIncluded = false;
            var GrangeDisplayScoresPrefix = ModInstance.Helper.Translation.Get("GrangeDisplayScores_Prefix");
            var leaderboard = $"{GrangeDisplayScoresPrefix}{LineBreak}";
            foreach (var NPCName in NPCNames)
            {
                if (!FarmerHasBeenIncluded && (NPCScores[NPCName] < grangeScoreAfterAdjustment))
                {
                    leaderboard += $"{LineBreak}{who.Name} - {grangeScoreAfterAdjustment}";
                    FarmerHasBeenIncluded = true;
                }
                leaderboard += $"{LineBreak}{NPCName} - {NPCScores[NPCName]}";
            }
            if (!FarmerHasBeenIncluded) // they may be in last place
            {
                leaderboard += $"{LineBreak}{who.Name} - {grangeScoreAfterAdjustment}";
            }

            if (grangeScoreAfterAdjustment > grangeScoreBeforeAdjustment)
            {
                var ExtraPointsDescription = ModInstance.Helper.Translation.Get("GrangeDisplayScores_ExtraPointsDescription", new { reason = GetExtraPointsReason() });
                leaderboard += $" {LineBreak}{ExtraPointsDescription}";
            };
            if (grangeScoreAfterAdjustment < grangeScoreBeforeAdjustment)
            {
                var PointsDeductedDescription = ModInstance.Helper.Translation.Get("GrangeDisplayScores_PointsDeductedDescription", new { reason = GetPointsDeductedReason() });
                leaderboard += $" {LineBreak}{PointsDeductedDescription}";
            }

            var leaderboardForLog = leaderboard.Replace(LineBreak, " / ");
            ModInstance.Monitor.Log($"[Stardew Valley Fair and Balanced] Leaderboard = {leaderboardForLog}", LogLevel.Debug);
            Game1.showGlobalMessage(leaderboard);

            needToShowLeaderboard = false;
        }
    }
}
