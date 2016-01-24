using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace OreProcessingOptimizerMassRenamer
{
    public class Program : MyGridProgram
    {
        void Main(string argument)
        {
            System.Text.RegularExpressions.Regex namePrefixes = new System.Text.RegularExpressions.Regex("^(ma[lł]y|du[zż]y|piec|rafineria|[sś]redni|stacja|wewn[eę]trzna|airtight) ?(kontener|reaktor|[lł]ukowy|kontener|monta[zż]owa|lampa|hangar door)?\\s?\\d?\\d?\\d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Dictionary<string, int> dict = new Dictionary<string, int>();
            var allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(allBlocks);

            for (int i = allBlocks.Count - 1; i >= 0; --i)
            {
                var block = allBlocks[i];
                System.Text.RegularExpressions.Match match = namePrefixes.Match(block.CustomName);

                if (!match.Success)
                    continue;

                string baseName;
                if (match.Groups.Count == 2)
                {
                    baseName = match.Groups[1].Value;
                }
                else if (match.Groups.Count == 3)
                {
                    baseName = match.Groups[1].Value + " " + match.Groups[2].Value;
                }
                else
                {
                    continue;
                }

                int num;
                if (dict.TryGetValue(baseName, out num))
                    num += 1;
                else
                    num = 1;
                dict[baseName] = num;

                block.SetCustomName(baseName + " " + num.ToString("000"));
            }
        }
    }
}
