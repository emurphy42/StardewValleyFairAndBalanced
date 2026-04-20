using HarmonyLib;
using StardewModdingAPI;

namespace StardewValleyFairAndBalanced
{
    public class ModEntry : Mod
    {
        public ModConfig Config = new();

        public override void Entry(IModHelper helper)
        {
            Config = Helper.ReadConfig<ModConfig>();

            ObjectPatches.ModInstance = this;
            ObjectPatches.ModConfig = this.Config;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            // farmer's score is calculated by judgeGrange(), but we can't patch it because it's private
            harmony.Patch(
                original: AccessTools.Method(typeof(StardewValley.Event), nameof(StardewValley.Event.interpretGrangeResults)),
                prefix: new HarmonyMethod(typeof(ObjectPatches), nameof(ObjectPatches.Event_interpretGrangeResults_Prefix)),
                postfix: new HarmonyMethod(typeof(ObjectPatches), nameof(ObjectPatches.Event_interpretGrangeResults_Postfix))
            );

            // show full leaderboard along with Lewis's dialogue
            harmony.Patch(
                original: AccessTools.Method(typeof(StardewValley.Event), nameof(StardewValley.Event.checkAction)),
                postfix: new HarmonyMethod(typeof(ObjectPatches), nameof(ObjectPatches.Event_checkAction_Postfix))
            );
        }
    }
}