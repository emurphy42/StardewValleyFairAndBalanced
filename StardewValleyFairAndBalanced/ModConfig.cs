using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StardewValleyFairAndBalanced
{
    public sealed class ModConfig
    {
        // always includes Pierre, Marnie, Willy (default averages 90, 75, 60 respectively)
        // this config option may add new values and/or replace default values
        public Dictionary<string, int> NPCAverageScores = new()
        {
            { "Clint", 60 }
        };
    }
}
