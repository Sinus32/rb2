using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace SEScripts.ResourceExchanger2_5_0_189
{
    public class Program : MyGridProgram
    {
        /// Resource Exchanger version 2.5.0 2019-03-05 for SE 1.189
        /// Made by Sinus32
        /// http://steamcommunity.com/sharedfiles/filedetails/546221822
        ///
        /// Warning! This script does not require any timer blocks and will run immediately.
        /// If you want to stop it just switch the programmable block off.
        ///
        /// Configuration can be changed in custom data of the programmable block

        /** Default configuration *****************************************************************/

        public bool AnyConstruct = false;
        public string DisplayLcdGroup = "Resource exchanger output";
        public string DrillsPayloadLightsGroup = "Payload indicators";
        public bool EnableDrills = true;
        public bool EnableGroups = true;
        public bool EnableOxygenGenerators = true;
        public bool EnableReactors = true;
        public bool EnableRefineries = true;
        public bool EnableTurrets = true;
        public string GroupTagPattern = @"\bGR\d{1,3}\b";
        public MyItemType? LowestRefineryPriority = new MyItemType(OreType, "Stone");
        public string ManagedBlocksGroup = "";
        public MyItemType? TopRefineryPriority = new MyItemType(OreType, "Iron");

        /** Implementation ************************************************************************/

        internal readonly ItemDict Items;
        private const string AmmoType = "MyObjectBuilder_AmmoMagazine";
        private const string ComponentType = "MyObjectBuilder_Component";
        private const string ConfigSection = "ResourceExchanger";
        private const string GasType = "MyObjectBuilder_GasContainerObject";
        private const string GunType = "MyObjectBuilder_PhysicalGunObject";
        private const string IngotType = "MyObjectBuilder_Ingot";
        private const string OreType = "MyObjectBuilder_Ore";
        private const string OxygenType = "MyObjectBuilder_OxygenContainerObject";
        private const decimal SmallNumber = 0.000005M;
        private readonly int[] _avgMovements;
        private readonly Dictionary<MyDefinitionId, List<MyItemType>> _blockMap;
        private int _cycleNumber = 0;
        private object _prevConfig;

        public Program()
        {
            Items = ItemDict.BuildItemInfoDict();
            _blockMap = new Dictionary<MyDefinitionId, List<MyItemType>>();
            _avgMovements = new int[0x10];

            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            ReadConfig();

            var bs = new BlockStore(this);
            var stat = new Statistics();
            CollectTerminals(bs, stat);

            ProcessBlocks("Balancing reactors", EnableReactors, bs.Reactors, stat, exclude: bs.AllGroupedInventories);
            ProcessBlocks("Balancing refineries", EnableRefineries, bs.Refineries, stat, exclude: bs.AllGroupedInventories);
            var dcn = ProcessBlocks("Balancing drills", EnableDrills, bs.Drills, stat, exclude: bs.AllGroupedInventories);
            stat.NotConnectedDrillsFound = dcn > 1;
            ProcessBlocks("Balancing turrets", EnableTurrets, bs.Turrets, stat, exclude: bs.AllGroupedInventories);
            ProcessBlocks("Balancing oxygen gen.", EnableOxygenGenerators, bs.OxygenGenerators, stat,
                exclude: bs.AllGroupedInventories, filter: item => item.Type.TypeId == OreType);

            if (EnableGroups)
            {
                foreach (var kv in bs.Groups)
                    ProcessBlocks("Balancing group " + kv.Key, true, kv.Value, stat);
            }
            else
            {
                stat.Output.AppendLine("Balancing groups: disabled");
            }

            if (EnableRefineries)
                EnforceItemPriority(bs.Refineries, stat, TopRefineryPriority, LowestRefineryPriority);

            if (dcn >= 1)
                ProcessDrillsLights(bs.Drills, bs.DrillsPayloadLights, stat);

            PrintOnlineStatus(bs, stat);
            WriteOutput(bs, stat);
        }

        public void Save()
        { }

        private void BalanceInventories(Statistics stat, List<BlockWrapper> group, int networkNumber,
            string groupName, Func<MyInventoryItem, bool> filter)
        {
            if (group.Count < 2)
            {
                stat.Output.Append("Cannot balance conveyor network ").Append(networkNumber)
                    .Append(" group \"").Append(groupName).AppendLine("\"")
                    .AppendLine("  because there is only one inventory.");
                return; // nothing to do
            }

            foreach (var wrp in group)
                wrp.LoadVolume(this, stat, filter);

            BlockWrapper min = group[0], max = group[0];
            for (int i = 1; i < group.Count; ++i)
            {
                var dt = group[i];
                if (min.Percent > dt.Percent)
                    min = dt;
                if (max.Percent < dt.Percent)
                    max = dt;
            }

            if (max.CurrentVolume < SmallNumber)
            {
                stat.Output.Append("Cannot balance conveyor network ").Append(networkNumber)
                    .Append(" group \"").Append(groupName)
                    .AppendLine("\"")
                    .AppendLine("  because of lack of items in it.");
                return; // nothing to do
            }

            stat.Output.Append("Balancing conveyor network ").Append(networkNumber)
                .Append(" group \"").Append(groupName).AppendLine("\"...");

            if (min == max)
            {
                stat.Output.AppendLine("  nothing to do");
                return;
            }

            decimal toMove;
            if (min.MaxVolume == max.MaxVolume)
            {
                toMove = (max.CurrentVolume - min.CurrentVolume) / 2.0M;
            }
            else
            {
                toMove = (max.CurrentVolume * min.MaxVolume - min.CurrentVolume * max.MaxVolume)
                    / (min.MaxVolume + max.MaxVolume);
            }

            stat.Output.Append("Inv. 1 vol: ").Append(min.CurrentVolume.ToString("F6")).Append("; ");
            stat.Output.Append("Inv. 2 vol: ").Append(max.CurrentVolume.ToString("F6")).Append("; ");
            stat.Output.Append("To move: ").Append(toMove.ToString("F6")).AppendLine();

            if (toMove < 0.0M)
                throw new InvalidOperationException("Something went wrong with calculations: volumeDiff is " + toMove);

            if (toMove < SmallNumber)
                return;

            MoveVolume(stat, max, min, (VRage.MyFixedPoint)toMove, filter);
        }

        private BlockStore CollectTerminals(BlockStore bs, Statistics stat)
        {
            var blocks = new List<IMyTerminalBlock>();
            Func<IMyTerminalBlock, bool> myTerminalBlockFilter = b => b.IsFunctional && (AnyConstruct || b.IsSameConstructAs(Me));
            Func<ICollection, bool, string> countOrNA = (c, e) => e ? c.Count.ToString() : "n/a";

            if (String.IsNullOrEmpty(ManagedBlocksGroup))
            {
                GridTerminalSystem.GetBlocksOfType(blocks, myTerminalBlockFilter);
            }
            else
            {
                var group = GridTerminalSystem.GetBlockGroupWithName(ManagedBlocksGroup);
                if (group == null)
                    stat.Output.Append("Error: a group ").Append(ManagedBlocksGroup).AppendLine(" has not been found");
                else
                    group.GetBlocksOfType(blocks, myTerminalBlockFilter);
            }

            foreach (var dt in blocks)
                bs.Collect(dt);

            if (!String.IsNullOrEmpty(DisplayLcdGroup))
            {
                var group = GridTerminalSystem.GetBlockGroupWithName(DisplayLcdGroup);
                if (group != null)
                    group.GetBlocksOfType<IMyTextPanel>(bs.DebugScreen, myTerminalBlockFilter);
            }

            if (!String.IsNullOrEmpty(DrillsPayloadLightsGroup))
            {
                var group = GridTerminalSystem.GetBlockGroupWithName(DrillsPayloadLightsGroup);
                if (group != null)
                    group.GetBlocksOfType<IMyLightingBlock>(bs.DrillsPayloadLights, myTerminalBlockFilter);
            }

            stat.Output.Append("Resource exchanger 2.5.0. Blocks managed:")
                .Append(" reactors: ").Append(countOrNA(bs.Reactors, EnableReactors))
                .Append(", refineries: ").Append(countOrNA(bs.Refineries, EnableRefineries)).AppendLine(",")
                .Append("oxygen gen.: ").Append(countOrNA(bs.OxygenGenerators, EnableOxygenGenerators))
                .Append(", drills: ").Append(countOrNA(bs.Drills, EnableDrills))
                .Append(", turrets: ").Append(countOrNA(bs.Turrets, EnableTurrets))
                .Append(", cargo cont.: ").Append(countOrNA(bs.CargoContainers, EnableGroups))
                .Append(", custom groups: ").Append(countOrNA(bs.Groups, EnableGroups)).AppendLine();

            return bs;
        }

        private List<InventoryGroup> DivideBlocks(ICollection<BlockWrapper> inventories, HashSet<BlockWrapper> exclude)
        {
            const string MY_OBJECT_BUILDER = "MyObjectBuilder_";

            var result = new List<InventoryGroup>();

            foreach (var wrp1 in inventories)
            {
                if (exclude != null && exclude.Contains(wrp1))
                    continue;

                bool add = true;
                for (int n = result.Count - 1; n >= 0; --n)
                {
                    var network = result[n];
                    var wrp2 = network.Blocks[0];
                    if (ReferenceEquals(wrp1.AcceptedItems, wrp2.AcceptedItems) && wrp1.GetInventory().IsConnectedTo(wrp2.GetInventory()))
                    {
                        network.Blocks.Add(wrp1);
                        add = false;
                        break;
                    }
                }

                if (add)
                {
                    var name = wrp1.Block.BlockDefinition.ToString();
                    if (name.StartsWith(MY_OBJECT_BUILDER))
                        name = name.Substring(MY_OBJECT_BUILDER.Length);

                    var network = new InventoryGroup(result.Count + 1, name);
                    network.Blocks.Add(wrp1);
                    result.Add(network);
                }
            }

            return result;
        }

        private void EnforceItemPriority(List<BlockWrapper> group, Statistics stat, MyItemType? topPriority, MyItemType? lowestPriority)
        {
            if (topPriority == null && lowestPriority == null)
                return;

            foreach (var wrp in group)
            {
                var inv = wrp.GetInventory();
                if (inv.ItemCount < 2)
                    continue;

                var items = stat.EmptyTempItemList();
                inv.GetItems(items, null);

                if (topPriority.HasValue && !items[0].Type.Equals(topPriority.Value))
                {
                    for (int i = 1; i < items.Count; ++i)
                    {
                        var item = items[i];
                        if (item.Type.Equals(topPriority.Value))
                        {
                            stat.Output.Append("Moving ").Append(topPriority.Value.SubtypeId).Append(" from ")
                                .Append(i + 1).Append(" slot to first slot of ").AppendLine(wrp.Block.CustomName);
                            inv.TransferItemTo(inv, i, 0, false, null);
                            stat.MovementsDone += 1;
                            break;
                        }
                    }
                }

                if (lowestPriority.HasValue && !items[items.Count - 1].Type.Equals(lowestPriority.Value))
                {
                    for (int i = items.Count - 2; i >= 0; --i)
                    {
                        var item = items[i];
                        if (item.Type.Equals(lowestPriority.Value))
                        {
                            stat.Output.Append("Moving ").Append(lowestPriority.Value.SubtypeId).Append(" from ")
                                .Append(i + 1).Append(" slot to last slot of ").AppendLine(wrp.Block.CustomName);
                            inv.TransferItemTo(inv, i, items.Count, false, null);
                            stat.MovementsDone += 1;
                            break;
                        }
                    }
                }
            }
        }

        private List<MyItemType> FindAcceptedItems(MyDefinitionId def, IMyInventory inv)
        {
            List<MyItemType> result;
            if (_blockMap.TryGetValue(def, out result))
                return result;

            result = new List<MyItemType>();
            //inv.GetAcceptedItems(result, t => true);
            foreach (var key in Items.ItemInfoDict.Keys)
            {
                if (inv.CanItemsBeAdded(-1, key))
                    result.Add(key);
            }

            foreach (var list in _blockMap.Values)
            {
                if (Enumerable.SequenceEqual(result, list))
                {
                    _blockMap[def] = list;
                    return list;
                }
            }

            _blockMap[def] = result;
            return result;
        }

        private VRage.MyFixedPoint MoveVolume(Statistics stat, BlockWrapper from, BlockWrapper to,
            VRage.MyFixedPoint volumeAmountToMove, Func<MyInventoryItem, bool> filter)
        {
            if (volumeAmountToMove == 0)
                return volumeAmountToMove;

            if (volumeAmountToMove < 0)
                throw new ArgumentException("Invalid volume amount", "volumeAmount");

            stat.Output.Append("Move ").Append(volumeAmountToMove).Append(" l. from ")
                .Append(from.Block.CustomName).Append(" to ").AppendLine(to.Block.CustomName);

            var itemsFrom = stat.EmptyTempItemList();
            var fromInv = from.GetInventory();
            var destInv = to.GetInventory();
            fromInv.GetItems(itemsFrom, filter);

            for (int i = itemsFrom.Count - 1; i >= 0; --i)
            {
                MyInventoryItem item = itemsFrom[i];
                var data = Items.Get(stat, item.Type);
                if (data == null)
                    continue;

                decimal amountToMoveRaw = (decimal)volumeAmountToMove * 1000M / data.Volume;
                VRage.MyFixedPoint amountToMove;

                if (data.HasIntegralAmounts)
                    amountToMove = (int)(amountToMoveRaw + 0.1M);
                else
                    amountToMove = (VRage.MyFixedPoint)amountToMoveRaw;

                if (amountToMove == 0)
                    continue;

                decimal itemVolume;
                bool success;
                if (amountToMove <= item.Amount)
                {
                    itemVolume = (decimal)amountToMove * data.Volume / 1000M;
                    success = fromInv.TransferItemTo(destInv, item, amountToMove);
                    stat.MovementsDone += 1;
                    stat.Output.Append("Move ").Append(amountToMove).Append(" -> ").AppendLine(success ? "success" : "failure");
                }
                else
                {
                    itemVolume = (decimal)item.Amount * data.Volume / 1000M;
                    success = fromInv.TransferItemTo(destInv, item, null);
                    stat.MovementsDone += 1;
                    stat.Output.Append("Move all ").Append(item.Amount).Append(" -> ").AppendLine(success ? "success" : "failure");
                }

                if (success)
                    volumeAmountToMove -= (VRage.MyFixedPoint)itemVolume;
                if (volumeAmountToMove < (VRage.MyFixedPoint)SmallNumber)
                    return volumeAmountToMove;
            }

            stat.Output.Append("Cannot move ").Append(volumeAmountToMove).AppendLine(" l.");
            return volumeAmountToMove;
        }

        private void PrintOnlineStatus(BlockStore bs, Statistics stat)
        {
            var sb = new StringBuilder(4096);

            var blocksAffected = bs.Reactors.Count
                + bs.Refineries.Count
                + bs.Drills.Count
                + bs.Turrets.Count
                + bs.OxygenGenerators.Count
                + bs.CargoContainers.Count;

            sb.Append("Grids connected: ").Append(bs.AllGrids.Count).AppendLine(AnyConstruct ? " (AC)" : " (SC)");
            sb.Append("Conveyor networks: ").Append(stat.NumberOfNetworks).AppendLine();
            sb.Append("Blocks affected: ").Append(blocksAffected).AppendLine();

            sb.Append("reactors: ");
            if (bs.Reactors.Count != 0)
                sb.Append(bs.Reactors.Count);
            else
                sb.Append(EnableReactors ? "0" : "OFF");

            sb.Append(", refineries: ");
            if (bs.Refineries.Count != 0)
                sb.Append(bs.Refineries.Count);
            else
                sb.Append(EnableRefineries ? "0" : "OFF");

            sb.AppendLine().Append("drills: ");
            if (bs.Drills.Count != 0)
                sb.Append(bs.Drills.Count);
            else
                sb.Append(EnableDrills ? "0" : "OFF");

            sb.Append(", turrets: ");
            if (bs.Turrets.Count != 0)
                sb.Append(bs.Turrets.Count);
            else
                sb.Append(EnableTurrets ? "0" : "OFF");

            sb.Append(", o. gen.: ");
            if (bs.OxygenGenerators.Count != 0)
                sb.Append(bs.OxygenGenerators.Count);
            else
                sb.Append(EnableOxygenGenerators ? "0" : "OFF");

            sb.AppendLine().Append("cargo cont.: ");
            if (bs.CargoContainers.Count != 0)
                sb.Append(bs.CargoContainers.Count);
            else
                sb.Append(EnableGroups ? "0" : "OFF");

            sb.Append(", groups: ");
            if (bs.Groups.Count != 0)
                sb.Append(bs.Groups.Count);
            else
                sb.Append(EnableGroups ? "0" : "OFF");

            sb.AppendLine();

            if (bs.Drills.Count != 0)
            {
                if (stat.NotConnectedDrillsFound)
                    sb.AppendLine("Warn: Some drills are not connected");

                sb.Append("Drills payload: ").Append(stat.DrillsPayloadStr ?? "N/A");
                if (stat.DrillsVolumeWarning)
                    sb.AppendLine((_cycleNumber & 0x01) == 0 ? "%  !" : "% ! !");
                else
                    sb.AppendLine("%");
            }

            if (stat.MissingInfo.Count > 0)
                sb.Append("Err: missing volume information for ").AppendLine(String.Join(", ", stat.MissingInfo));

            _avgMovements[_cycleNumber & 0x0F] = stat.MovementsDone;
            var samples = Math.Min(_cycleNumber + 1, 0x10);
            double avg = 0;
            for (int i = 0; i < samples; ++i)
                avg += _avgMovements[i];
            avg /= samples;

            sb.Append("Avg. movements: ").Append(avg.ToString("F2")).Append(" (last ").Append(samples).AppendLine(" runs)");

            float cpu = Runtime.CurrentInstructionCount * 100;
            cpu /= Runtime.MaxInstructionCount;
            sb.Append("Complexity limit usage: ").Append(cpu.ToString("F2")).AppendLine("%");

            sb.Append("Last run time: ").Append(Runtime.LastRunTimeMs.ToString("F3")).AppendLine(" ms");

            var tab = new char[42];
            for (int i = 0; i < 42; ++i)
            {
                char c = ' ';
                if (i % 21 == 1)
                    c = '|';
                else if (i % 7 < 4)
                    c = '·';
                tab[41 - (i + _cycleNumber) % 42] = c;
            }
            sb.AppendLine(new string(tab));
            ++_cycleNumber;

            Echo(sb.ToString());
        }

        private int ProcessBlocks(string msg, bool enable, ICollection<BlockWrapper> blocks, Statistics stat,
            HashSet<BlockWrapper> exclude = null, Func<MyInventoryItem, bool> filter = null)
        {
            stat.Output.Append(msg);
            if (enable)
            {
                if (blocks.Count >= 2)
                {
                    var conveyorNetworks = DivideBlocks(blocks, exclude);
                    stat.NumberOfNetworks += conveyorNetworks.Count;
                    stat.Output.Append(": ").Append(conveyorNetworks.Count).AppendLine(" conveyor networks found");

                    foreach (var network in conveyorNetworks)
                        BalanceInventories(stat, network.Blocks, network.No, network.Name, filter);

                    return conveyorNetworks.Count;
                }
                else
                {
                    stat.Output.AppendLine(": nothing to do");
                    return 0;
                }
            }
            else
            {
                stat.Output.AppendLine(": disabled");
                return -1;
            }
        }

        private void ProcessDrillsLights(List<BlockWrapper> drills, List<IMyLightingBlock> lights, Statistics stat)
        {
            VRage.MyFixedPoint warningLevelInCubicMetersLeft = 5;
            Func<Color> step0 = () => new Color(255, 255, 255);
            Func<Color> step1 = () => new Color(255, 255, 0);
            Func<Color> step2 = () => new Color(255, 0, 0);
            Func<Color> warn = () => (_cycleNumber & 0x1) == 0 ? new Color(128, 0, 128) : new Color(128, 0, 64);
            Func<Color> err = () => (_cycleNumber & 0x1) == 0 ? new Color(0, 128, 128) : new Color(0, 64, 128);

            if (lights.Count == 0)
            {
                stat.Output.AppendLine("Setting color of drills payload indicators. Not enough lights found. Nothing to do.");
                return;
            }

            stat.Output.AppendLine("Setting color of drills payload indicators.");

            Color color;
            if (stat.NotConnectedDrillsFound)
            {
                stat.Output.AppendLine("Not all drills are connected.");
                color = err();
            }
            else
            {
                var drillsMaxVolume = VRage.MyFixedPoint.Zero;
                var drillsCurrentVolume = VRage.MyFixedPoint.Zero;
                foreach (var drill in drills)
                {
                    var inv = drill.GetInventory();
                    drillsMaxVolume += inv.MaxVolume;
                    drillsCurrentVolume += inv.CurrentVolume;
                }

                if (drillsMaxVolume > 0)
                {
                    var p = (float)drillsCurrentVolume * 1000.0f;
                    p /= (float)drillsMaxVolume;

                    stat.DrillsPayloadStr = (p / 10.0f).ToString("F1");

                    stat.Output.Append("Drills space usage: ");
                    stat.Output.Append(stat.DrillsPayloadStr);
                    stat.Output.AppendLine("%");

                    stat.DrillsVolumeWarning = (drillsMaxVolume - drillsCurrentVolume) < warningLevelInCubicMetersLeft;
                    if (stat.DrillsVolumeWarning)
                    {
                        color = warn();
                    }
                    else
                    {
                        Color c1, c2;
                        float m1, m2;

                        if (p < 500.0f)
                        {
                            c1 = step0();
                            c2 = step1();
                            m2 = p / 500.0f;
                            m1 = 1.0f - m2;
                        }
                        else
                        {
                            c1 = step2();
                            c2 = step1();
                            m1 = (p - 500.0f) / 450.0f;
                            if (m1 > 1.0f)
                                m1 = 1.0f;
                            m2 = 1.0f - m1;
                        }

                        float r = c1.R * m1 + c2.R * m2;
                        float g = c1.G * m1 + c2.G * m2;
                        float b = c1.B * m1 + c2.B * m2;

                        if (r > 255.0f)
                            r = 255.0f;
                        else if (r < 0.0f)
                            r = 0.0f;

                        if (g > 255.0f)
                            g = 255.0f;
                        else if (g < 0.0f)
                            g = 0.0f;

                        if (b > 255.0f)
                            b = 255.0f;
                        else if (b < 0.0f)
                            b = 0.0f;

                        color = new Color((int)r, (int)g, (int)b);
                    }
                }
                else
                {
                    color = step0();
                }
            }

            stat.Output.Append("Drills payload indicators lights color: ");
            stat.Output.Append(color);
            stat.Output.AppendLine();

            foreach (IMyLightingBlock light in lights)
            {
                var currentColor = light.GetValue<Color>("Color");
                if (currentColor != color)
                    light.SetValue<Color>("Color", color);
            }

            stat.Output.Append("Color of ");
            stat.Output.Append(lights.Count);
            stat.Output.AppendLine(" drills payload indicators has been set.");
        }

        private void ReadConfig()
        {
            const string anyConstructComment = "\n Set to \"true\" to enable exchanging items with connected ships."
                + "\n Keep \"false\" if blocks connected by connectors should not be affected.";
            const string managedGroupComment = "\n Optional name of a group of blocks that will be affected"
                + "\n by the script. By default all blocks connected to the grid are processed,"
                + "\n but you can set this to force the script to affect only certain blocks.";
            const string reactorsComment = "\n Enables exchanging uranium between reactors";
            const string refineriesComment = "\n Enables exchanging ore between refineries and arc furnaces";
            const string drillsComment = "\n Enables exchanging ore between drills and"
                + "\n processing lights that indicates how much free space left in drills";
            const string turretsComment = "\n Enables exchanging ammunition between turrets and launchers";
            const string oxygenGeneratorsComment = "\n Enables exchanging ice between oxygen generators";
            const string groupsComment = "\n Enables exchanging items in blocks of custom groups";
            const string drillsPayloadLightsGroupComment = "\n Name of a group of lights that will be used as indicators of space"
                + "\n left in drills. Both Interior Light and Spotlight are supported."
                + "\n The lights will change colors to tell you how much free space left:"
                + "\n White - All drills are connected to each other and they are empty."
                + "\n Yellow - Drills are full in a half."
                + "\n Red - Drills are almost full (95%)."
                + "\n Purple - Less than 5 m³ of free space left."
                + "\n Cyan - Some drills are not connected to each other.";
            const string topRefineryPriorityComment = "\n Top priority item type to process in refineries"
                + "\n and/or arc furnaces. The script will move an item of this type to"
                + "\n the first slot of a refinery or arc furnace if it find that item"
                + "\n in the refinery (or arc furnace) processing queue.";
            const string lowestRefineryPriorityComment = "\n Lowest priority item type to process in refineries"
                + "\n and/or arc furnaces. The script will move an item of this type to"
                + "\n the last slot of a refinery or arc furnace if it find that item"
                + "\n in the refinery (or arc furnace) processing queue.";
            const string groupTagPatternComment = "\n Regular expression used to recognize groups";
            const string displayLcdGroupComment = "\n Group of wide LCD screens that will act as debugger output for"
                + "\n this script. You can name this screens as you wish, but pay attention"
                + "\n that they will be used in alphabetical order according to their names.";

            if (ReferenceEquals(Me.CustomData, _prevConfig))
                return;

            MyIniParseResult result;
            var ini = new MyIni();
            if (!ini.TryParse(Me.CustomData, out result))
            {
                Echo(String.Format("Err: invalid config in line {0}: {1}", result.LineNo, result.Error));
                return;
            }

            ReadConfigBoolean(ini, nameof(AnyConstruct), ref AnyConstruct, anyConstructComment);
            ReadConfigString(ini, nameof(ManagedBlocksGroup), ref ManagedBlocksGroup, managedGroupComment);
            ReadConfigBoolean(ini, nameof(EnableReactors), ref EnableReactors, reactorsComment);
            ReadConfigBoolean(ini, nameof(EnableRefineries), ref EnableRefineries, refineriesComment);
            ReadConfigBoolean(ini, nameof(EnableDrills), ref EnableDrills, drillsComment);
            ReadConfigBoolean(ini, nameof(EnableTurrets), ref EnableTurrets, turretsComment);
            ReadConfigBoolean(ini, nameof(EnableOxygenGenerators), ref EnableOxygenGenerators, oxygenGeneratorsComment);
            ReadConfigBoolean(ini, nameof(EnableGroups), ref EnableGroups, groupsComment);
            ReadConfigString(ini, nameof(DrillsPayloadLightsGroup), ref DrillsPayloadLightsGroup, drillsPayloadLightsGroupComment);
            ReadConfigItemType(ini, nameof(TopRefineryPriority), ref TopRefineryPriority, topRefineryPriorityComment);
            ReadConfigItemType(ini, nameof(LowestRefineryPriority), ref LowestRefineryPriority, lowestRefineryPriorityComment);
            ReadConfigString(ini, nameof(GroupTagPattern), ref GroupTagPattern, groupTagPatternComment);
            ReadConfigString(ini, nameof(DisplayLcdGroup), ref DisplayLcdGroup, displayLcdGroupComment);

            Echo("Configuration readed");
            _prevConfig = Me.CustomData = ini.ToString();
        }

        private void ReadConfigBoolean(MyIni ini, string name, ref bool value, string comment)
        {
            var key = new MyIniKey(ConfigSection, name);
            MyIniValue val = ini.Get(key);
            bool tmp;
            if (val.TryGetBoolean(out tmp))
                value = tmp;
            else
                ini.Set(key, value);
            ini.SetComment(key, comment);
        }

        private void ReadConfigItemType(MyIni ini, string name, ref MyItemType? value, string comment)
        {
            var key = new MyIniKey(ConfigSection, name);
            MyIniValue val = ini.Get(key);
            string tmp;
            if (val.TryGetString(out tmp))
                value = String.IsNullOrEmpty(tmp) ? null : MyItemType.Parse(tmp.Trim());
            else
                ini.Set(key, value.HasValue ? value.Value.ToString() : String.Empty);
            ini.SetComment(key, comment);
        }

        private void ReadConfigString(MyIni ini, string name, ref string value, string comment)
        {
            var key = new MyIniKey(ConfigSection, name);
            MyIniValue val = ini.Get(key);
            string tmp;
            if (val.TryGetString(out tmp))
                value = tmp.Trim();
            else
                ini.Set(key, value);
            ini.SetComment(key, comment);
        }

        private void WriteOutput(BlockStore bs, Statistics stat)
        {
            const int linesPerDebugScreen = 17;

            if (bs.DebugScreen.Count == 0)
                return;

            bs.DebugScreen.Sort(new MyTextPanelNameComparer());
            string[] lines = stat.Output.ToString().Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int totalScreens = lines.Length + linesPerDebugScreen - 1;
            totalScreens /= linesPerDebugScreen;

            for (int i = 0; i < bs.DebugScreen.Count; ++i)
            {
                var screen = bs.DebugScreen[i];
                var sb = new StringBuilder();
                int firstLine = i * linesPerDebugScreen;
                for (int j = 0; j < linesPerDebugScreen && firstLine + j < lines.Length; ++j)
                    sb.AppendLine(lines[firstLine + j].Trim());
                screen.WritePublicText(sb.ToString());
                screen.ShowPublicTextOnScreen();
            }
        }

        internal class BlockStore
        {
            public readonly Program _program;
            public readonly HashSet<IMyCubeGrid> AllGrids;
            public readonly HashSet<BlockWrapper> AllGroupedInventories;
            public readonly List<BlockWrapper> CargoContainers;
            public readonly List<IMyTextPanel> DebugScreen;
            public readonly List<BlockWrapper> Drills;
            public readonly List<IMyLightingBlock> DrillsPayloadLights;
            public readonly Dictionary<string, HashSet<BlockWrapper>> Groups;
            public readonly List<BlockWrapper> OxygenGenerators;
            public readonly List<BlockWrapper> Reactors;
            public readonly List<BlockWrapper> Refineries;
            public readonly List<BlockWrapper> Turrets;
            private System.Text.RegularExpressions.Regex _groupTagPattern;

            public BlockStore(Program program)
            {
                _program = program;
                DebugScreen = new List<IMyTextPanel>();
                Reactors = new List<BlockWrapper>();
                OxygenGenerators = new List<BlockWrapper>();
                Refineries = new List<BlockWrapper>();
                Drills = new List<BlockWrapper>();
                Turrets = new List<BlockWrapper>();
                CargoContainers = new List<BlockWrapper>();
                Groups = new Dictionary<string, HashSet<BlockWrapper>>();
                AllGroupedInventories = new HashSet<BlockWrapper>();
                AllGrids = new HashSet<IMyCubeGrid>();
                DrillsPayloadLights = new List<IMyLightingBlock>();
            }

            public void Collect(IMyTerminalBlock block)
            {
                var collected = CollectContainer(block as IMyCargoContainer)
                    || CollectRefinery(block as IMyRefinery)
                    || CollectReactor(block as IMyReactor)
                    || CollectDrill(block as IMyShipDrill)
                    || CollectTurret(block as IMyUserControllableGun)
                    || CollectOxygenGenerator(block as IMyGasGenerator);
            }

            private void AddToGroup(BlockWrapper inv)
            {
                const string MatchNothingExpr = @"a^";

                if (_groupTagPattern == null)
                {
                    var expr = String.IsNullOrEmpty(_program.GroupTagPattern) ? MatchNothingExpr : _program.GroupTagPattern;
                    _groupTagPattern = new System.Text.RegularExpressions.Regex(expr,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                foreach (System.Text.RegularExpressions.Match dt in _groupTagPattern.Matches(inv.Block.CustomName))
                {
                    HashSet<BlockWrapper> tmp;
                    if (!Groups.TryGetValue(dt.Value, out tmp))
                    {
                        tmp = new HashSet<BlockWrapper>();
                        Groups.Add(dt.Value, tmp);
                    }
                    tmp.Add(inv);
                    AllGroupedInventories.Add(inv);
                }
            }

            private bool CollectContainer(IMyCargoContainer myCargoContainer)
            {
                if (myCargoContainer == null)
                    return false;

                if (!_program.EnableGroups)
                    return true;

                var wrp = InvWrp(myCargoContainer, myCargoContainer.GetInventory());
                if (wrp != null)
                {
                    CargoContainers.Add(wrp);
                    AllGrids.Add(myCargoContainer.CubeGrid);
                    AddToGroup(wrp);
                }
                return true;
            }

            private bool CollectDrill(IMyShipDrill myDrill)
            {
                if (myDrill == null)
                    return false;

                if (!_program.EnableDrills || !myDrill.UseConveyorSystem)
                    return true;

                var wrp = InvWrp(myDrill, myDrill.GetInventory());
                if (wrp != null)
                {
                    Drills.Add(wrp);
                    AllGrids.Add(myDrill.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(wrp);
                }
                return true;
            }

            private bool CollectOxygenGenerator(IMyGasGenerator myOxygenGenerator)
            {
                if (myOxygenGenerator == null)
                    return false;

                if (!_program.EnableOxygenGenerators)
                    return true;

                var wrp = InvWrp(myOxygenGenerator, myOxygenGenerator.GetInventory());
                if (wrp != null)
                {
                    OxygenGenerators.Add(wrp);
                    AllGrids.Add(myOxygenGenerator.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(wrp);
                }
                return true;
            }

            private bool CollectReactor(IMyReactor myReactor)
            {
                if (myReactor == null)
                    return false;

                if (!_program.EnableReactors || !myReactor.UseConveyorSystem)
                    return true;

                var wrp = InvWrp(myReactor, myReactor.GetInventory());
                if (wrp != null)
                {
                    Reactors.Add(wrp);
                    AllGrids.Add(myReactor.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(wrp);
                }
                return true;
            }

            private bool CollectRefinery(IMyRefinery myRefinery)
            {
                if (myRefinery == null)
                    return false;

                if (!_program.EnableRefineries || !myRefinery.UseConveyorSystem)
                    return true;

                var wrp = InvWrp(myRefinery, myRefinery.InputInventory);
                if (wrp != null)
                {
                    Refineries.Add(wrp);
                    AllGrids.Add(myRefinery.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(wrp);
                }
                return true;
            }

            private bool CollectTurret(IMyUserControllableGun myTurret)
            {
                if (myTurret == null)
                    return false;

                if (!_program.EnableTurrets || myTurret is IMyLargeInteriorTurret)
                    return true;

                var wrp = InvWrp(myTurret, myTurret.GetInventory());
                if (wrp != null)
                {
                    Turrets.Add(wrp);
                    AllGrids.Add(myTurret.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(wrp);
                }
                return true;
            }

            private BlockWrapper InvWrp(IMyTerminalBlock block, IMyInventory inv)
            {
                if (inv != null && inv.MaxVolume > 0)
                {
                    var accepted = _program.FindAcceptedItems(block.BlockDefinition, inv);
                    if (accepted.Count > 0)
                        return new BlockWrapper(block, accepted);
                }
                return null;
            }

            private BlockWrapper InvWrp(IMyProductionBlock block, IMyInventory inv)
            {
                if (inv != null && inv.MaxVolume > 0)
                {
                    var accepted = _program.FindAcceptedItems(block.BlockDefinition, inv);
                    if (accepted.Count > 0)
                        return new BlockWrapperProd(block, accepted);
                }
                return null;
            }
        }

        internal class BlockWrapper
        {
            public readonly List<MyItemType> AcceptedItems;
            public readonly IMyTerminalBlock Block;
            public decimal CurrentVolume;
            public decimal MaxVolume;
            public decimal Percent;

            public BlockWrapper(IMyTerminalBlock block, List<MyItemType> acceptedItems)
            {
                Block = block;
                AcceptedItems = acceptedItems;
            }

            public virtual IMyInventory GetInventory()
            {
                return Block.GetInventory();
            }

            public void LoadVolume(Program prog, Statistics stat, Func<MyInventoryItem, bool> filter)
            {
                var inv = GetInventory();
                CurrentVolume = (decimal)inv.CurrentVolume;
                MaxVolume = (decimal)inv.MaxVolume;

                if (filter != null)
                {
                    decimal volumeBlocked = 0.0M;
                    inv.GetItems(null, item =>
                    {
                        if (!filter(item))
                        {
                            var data = prog.Items.Get(stat, item.Type);
                            if (data != null)
                                volumeBlocked += (decimal)item.Amount * data.Volume / 1000M;
                        }
                        return false;
                    });

                    if (volumeBlocked > 0.0M)
                    {
                        CurrentVolume -= volumeBlocked;
                        MaxVolume -= volumeBlocked;
                        stat.Output.Append("volumeBlocked ").AppendLine(volumeBlocked.ToString("N6"));
                    }
                }

                Percent = CurrentVolume / MaxVolume;
            }

            public bool MoveItem(int sourceItemIndex, int targetItemIndex)
            {
                var inv = GetInventory();
                return inv.TransferItemTo(inv, sourceItemIndex, targetItemIndex, false, null);
            }
        }

        internal class BlockWrapperProd : BlockWrapper
        {
            public BlockWrapperProd(IMyProductionBlock block, List<MyItemType> acceptedItems)
                : base(block, acceptedItems)
            { }

            public override IMyInventory GetInventory()
            {
                return ((IMyProductionBlock)Block).InputInventory;
            }
        }

        internal class InventoryGroup
        {
            public List<BlockWrapper> Blocks;
            public string Name;
            public int No;

            public InventoryGroup(int no, string name)
            {
                No = no;
                Name = name;
                Blocks = new List<BlockWrapper>();
            }
        }

        internal class ItemDict
        {
            public readonly Dictionary<MyItemType, ItemInfo> ItemInfoDict;

            public ItemDict()
            {
                ItemInfoDict = new Dictionary<MyItemType, ItemInfo>();
            }

            public static ItemDict BuildItemInfoDict()
            {
                var ret = new ItemDict();

                ret.Add(OreType, "[CM] Cattierite (Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Cohenite (Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Dense Iron (Fe+)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Glaucodot (Fe,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Heazlewoodite (Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Iron (Fe)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Kamacite (Fe,Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Pyrite (Fe,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[CM] Taenite (Fe,Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[EI] Autunite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[EI] Carnotite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[EI] Uraniaurite (U,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Chlorargyrite (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Cooperite (Ni,Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Electrum (Au,Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Galena (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Niggliite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Petzite (Ag,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Porphyry (Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[PM] Sperrylite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Akimotoite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Dolomite (Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Hapkeite (Fe,Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Icy Stone", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Olivine (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Quartz (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Sinoite (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "[S] Wadsleyite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Akimotoite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Akimotoite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Autunite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Autunite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Carbon", 1M, 0.37M, false, true); // Graphene Armor [Core] [Beta]
                ret.Add(OreType, "Carnotite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Carnotite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cattierite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cattierite (Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Chlorargyrite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Chlorargyrite (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cobalt", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Cohenite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cohenite (Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cooperite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Cooperite (Ni,Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Dense Iron", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Deuterium", 1.5M, 0.5M, false, true); // Deuterium Fusion Reactors
                ret.Add(OreType, "Dolomite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Dolomite (Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Electrum", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Electrum (Au,Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Galena", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Galena (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Glaucodot", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Glaucodot (Fe,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Gold", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Hapkeite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Hapkeite (Fe,Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Heazlewoodite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Heazlewoodite (Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Helium", 1M, 5.6M, false, true); // (DX11)Mass Driver
                ret.Add(OreType, "Ice", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Icy Stone", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Iron", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Kamacite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Kamacite (Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Magnesium", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Naquadah", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(OreType, "Neutronium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(OreType, "Nickel", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Niggliite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Niggliite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Olivine", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Olivine (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Organic", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Petzite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Petzite (Au,Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Platinum", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Porphyry", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Porphyry (Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Pyrite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Pyrite (Fe,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Quartz", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Quartz (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
                ret.Add(OreType, "Silicon", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Silver", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Sinoite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Sinoite (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Sperrylite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Sperrylite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Stone", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Taenite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Taenite (Fe,Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Thorium", 1M, 0.9M, false, true); // Tiered Thorium Reactors and Refinery (new)
                ret.Add(OreType, "Trinium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(OreType, "Tungsten", 1M, 0.47M, false, true); // (DX11)Mass Driver
                ret.Add(OreType, "Uraniaurite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Uraniaurite (U,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Uranium", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Wadsleyite", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Wadsleyite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Акимотит (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Аутунит (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Вадселит (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Галенит (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Глаукодот (Fe,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Доломит (Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Камасит (Fe,Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Карнотит (U)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Катьерит (Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Кварц (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Когенит (Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Куперит (Ni,Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Ледяной камень", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Нигглиит (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Оливин (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Петцит (Ag,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Пирит (Fe,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Плотное железо (Fe+)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Порфир (Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Синоит (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Сперрилит (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Таенит (Fe,Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Ураниурит (U,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Хапкеит (Fe,Si)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Хизлевудит (Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Хлораргирит (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1
                ret.Add(OreType, "Электрум (Au,Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.1

                ret.Add(IngotType, "Carbon", 1M, 0.052M, false, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
                ret.Add(IngotType, "Cobalt", 1M, 0.112M, false, true); // Space Engineers
                ret.Add(IngotType, "Gold", 1M, 0.052M, false, true); // Space Engineers
                ret.Add(IngotType, "HeavyH2OIngot", 2M, 1M, false, true); // Deuterium Fusion Reactors
                ret.Add(IngotType, "HeavyWater", 5M, 0.052M, false, true); // GSF Energy Weapons Pack
                ret.Add(IngotType, "Iron", 1M, 0.127M, false, true); // Space Engineers
                ret.Add(IngotType, "K_HSR_Nanites_Gel", 0.001M, 0.001M, false, true); // HSR
                ret.Add(IngotType, "LiquidHelium", 1M, 4.6M, false, true); // (DX11)Mass Driver
                ret.Add(IngotType, "Magmatite", 100M, 37M, false, true); // Stone and Gravel to Metal Ingots (DX 11)
                ret.Add(IngotType, "Magnesium", 1M, 0.575M, false, true); // Space Engineers
                ret.Add(IngotType, "Naquadah", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(IngotType, "Neutronium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(IngotType, "Nickel", 1M, 0.112M, false, true); // Space Engineers
                ret.Add(IngotType, "Platinum", 1M, 0.047M, false, true); // Space Engineers
                ret.Add(IngotType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
                ret.Add(IngotType, "ShieldPoint", 0.00001M, 0.0001M, false, true); // Energy shields (new modified version)
                ret.Add(IngotType, "Silicon", 1M, 0.429M, false, true); // Space Engineers
                ret.Add(IngotType, "Silver", 1M, 0.095M, false, true); // Space Engineers
                ret.Add(IngotType, "Stone", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(IngotType, "SuitFuel", 0.0003M, 0.052M, false, true); // Independent Survival
                ret.Add(IngotType, "SuitRTGPellet", 1.0M, 0.052M, false, true); // Independent Survival
                ret.Add(IngotType, "Thorium", 2M, 0.5M, false, true); // Thorium Reactor Kit
                ret.Add(IngotType, "ThoriumIngot", 3M, 20M, false, true); // Tiered Thorium Reactors and Refinery (new)
                ret.Add(IngotType, "Trinium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(IngotType, "Tungsten", 1M, 0.52M, false, true); // (DX11)Mass Driver
                ret.Add(IngotType, "Uranium", 1M, 0.052M, false, true); // Space Engineers
                ret.Add(IngotType, "v2HydrogenGas", 2.1656M, 0.43M, false, true); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2
                ret.Add(IngotType, "v2OxygenGas", 4.664M, 0.9M, false, true); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2

                ret.Add(ComponentType, "AdvancedReactorBundle", 50M, 20M, true, true); // Tiered Thorium Reactors and Refinery (new)
                ret.Add(ComponentType, "AegisLicense", 0.2M, 1M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Aggitator", 40M, 10M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(ComponentType, "AlloyPlate", 30M, 3M, true, true); // Industrial Centrifuge (stable/dev)
                ret.Add(ComponentType, "ampHD", 10M, 15.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "ArcFuel", 2M, 0.627M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "ArcReactorcomponent", 312M, 100M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "AzimuthSupercharger", 10M, 9M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(ComponentType, "BulletproofGlass", 15M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "Canvas", 15M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "CapacitorBank", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Computer", 0.2M, 1M, true, true); // Space Engineers
                ret.Add(ComponentType, "ConductorMagnets", 900M, 200M, true, true); // (DX11)Mass Driver
                ret.Add(ComponentType, "Construction", 8M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "CoolingHeatsink", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "DenseSteelPlate", 200M, 30M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "Detector", 5M, 6M, true, true); // Space Engineers
                ret.Add(ComponentType, "Display", 8M, 6M, true, true); // Space Engineers
                ret.Add(ComponentType, "Drone", 200M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(ComponentType, "DT-MiniSolarCell", 0.08M, 0.2M, true, true); // }DT{ Modpack
                ret.Add(ComponentType, "Explosives", 2M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "FocusPrysm", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Girder", 6M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "GrapheneAerogelFilling", 0.160M, 2.9166M, true, true); // Graphene Armor [Core] [Beta]
                ret.Add(ComponentType, "GrapheneNanotubes", 0.01M, 0.1944M, true, true); // Graphene Armor [Core] [Beta]
                ret.Add(ComponentType, "GraphenePlate", 6.66M, 0.54M, true, true); // Graphene Armor [Core] [Beta]
                ret.Add(ComponentType, "GraphenePowerCell", 25M, 45M, true, true); // Graphene Armor [Core] [Beta]
                ret.Add(ComponentType, "GrapheneSolarCell", 4M, 12M, true, true); // Graphene Armor [Core] [Beta]
                ret.Add(ComponentType, "GravityGenerator", 800M, 200M, true, true); // Space Engineers
                ret.Add(ComponentType, "InteriorPlate", 3M, 5M, true, true); // Space Engineers
                ret.Add(ComponentType, "K_HSR_ElectroParts", 3M, 0.01M, true, true); // HSR
                ret.Add(ComponentType, "K_HSR_Globe", 3M, 0.01M, true, true); // HSR
                ret.Add(ComponentType, "K_HSR_Globe_Uncharged", 3M, 0.01M, true, true); // HSR
                ret.Add(ComponentType, "K_HSR_Mainframe", 15M, 0.01M, true, true); // HSR
                ret.Add(ComponentType, "K_HSR_Nanites", 0.01M, 0.001M, true, true); // HSR
                ret.Add(ComponentType, "K_HSR_RailComponents", 3M, 0.01M, true, true); // HSR
                ret.Add(ComponentType, "LargeTube", 25M, 38M, true, true); // Space Engineers
                ret.Add(ComponentType, "LaserConstructionBoxL", 10M, 100M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "LaserConstructionBoxS", 5M, 50M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Magna", 100M, 15M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Magnetron", 10M, 0.5M, true, true); // EM Thruster
                ret.Add(ComponentType, "MagnetronComponent", 50M, 20M, true, true); // Deuterium Fusion Reactors
                ret.Add(ComponentType, "Magno", 10M, 5.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Medical", 150M, 160M, true, true); // Space Engineers
                ret.Add(ComponentType, "MetalGrid", 6M, 15M, true, true); // Space Engineers
                ret.Add(ComponentType, "Mg_FuelCell", 15M, 16M, true, true); // Ripptide's CW+EE (DX11). Reuploaded
                ret.Add(ComponentType, "Motor", 24M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "Naquadah", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(ComponentType, "Neutronium", 500M, 5M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(ComponentType, "PowerCell", 25M, 40M, true, true); // Space Engineers
                ret.Add(ComponentType, "PowerCoupler", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "productioncontrolcomponent", 40M, 15M, true, true); // (DX11) Double Sided Upgrade Modules
                ret.Add(ComponentType, "PulseCannonConstructionBoxL", 10M, 100M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "PulseCannonConstructionBoxS", 5M, 50M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "PWMCircuit", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "RadioCommunication", 8M, 70M, true, true); // Space Engineers
                ret.Add(ComponentType, "Reactor", 25M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "SafetyBypass", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Scrap", 2M, 2M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(ComponentType, "Shield", 5M, 25M, true, true); // Energy shields (new modified version)
                ret.Add(ComponentType, "ShieldFrequencyModule", 25M, 45M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "SmallTube", 4M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "SolarCell", 6M, 12M, true, true); // Space Engineers
                ret.Add(ComponentType, "SteelPlate", 20M, 3M, true, true); // Space Engineers
                ret.Add(ComponentType, "Superconductor", 15M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "TekMarLicense", 0.2M, 1M, true, true); // GSF Energy Weapons Pack
                ret.Add(ComponentType, "Thrust", 40M, 10M, true, true); // Space Engineers
                ret.Add(ComponentType, "TractorHD", 1500M, 200M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Trinium", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(ComponentType, "Tritium", 3M, 3M, true, true); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2
                ret.Add(ComponentType, "TVSI_DiamondGlass", 40M, 8M, true, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
                ret.Add(ComponentType, "WaterTankComponent", 200M, 160M, true, true); // Industrial Centrifuge (stable/dev)
                ret.Add(ComponentType, "ZPM", 50M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)

                ret.Add(AmmoType, "250shell", 128M, 64M, true, true); // [DEPRECATED] CSD Battlecannon
                ret.Add(AmmoType, "300mmShell_HE", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "88shell", 16M, 16M, true, true); // [DEPRECATED] CSD Battlecannon
                ret.Add(AmmoType, "900mmShell_HE", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "Aden30x113", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "AFmagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "ARPhaserPulseAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "AZ_Missile_AA", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "AZ_Missile200mm", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "BatteryCannonAmmo1", 50M, 50M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "BatteryCannonAmmo2", 200M, 200M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "BigBertha", 3600M, 2800M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "BlasterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Bofors40mm", 36M, 28M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "Class10PhotonTorp", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Class1LaserBeamCharge", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "ConcreteMix", 2M, 2M, true, true); // Concrete Tool - placing voxels in survival
                ret.Add(AmmoType, "crystalline_microcapacitor", 25M, 16M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "crystalline_nanocapacitor", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "D7DisruptorBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "discovery_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "DisruptorBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "DisruptorPulseAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Eikester_Missile120mm", 25M, 30M, true, true); // (DX11) Small Missile Turret
                ret.Add(AmmoType, "Eikester_Nuke", 1800M, 8836M, true, true); // (DX11) Nuke Launcher [WiP]
                ret.Add(AmmoType, "EmergencyBlasterMagazine", 0.45M, 0.2M, true, true); // Independent Survival
                ret.Add(AmmoType, "EnormousPhaserBeamAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "EnormousPhaserBeamAmmo_LR", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Eq_GenericEnergyMag", 35M, 16M, true, true); // HSR
                ret.Add(AmmoType, "federationphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "Flak130mm", 2M, 3M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Flak200mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Flak500mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "GuidedMissileTargeterAmmoMagazine", 100M, 100M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HDTCannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "heavy_photon_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "HeavyDisruptorPulseAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HeavyPhaserBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HeavyPhaserBeamAmmo_LR", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HeavyPhaserPulseAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HeavySWDisruptorBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "HighDamageGatlingAmmo", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "ISM_FusionAmmo", 35M, 10M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISM_GrendelAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISM_Hellfire", 45M, 60M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISM_LongbowAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISM_MinigunAmmo", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISMNeedles", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "ISMTracer", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - Bringing it back]
                ret.Add(AmmoType, "K_CS_DarkLance", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_DarkLance_Red", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Eye", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Reaper", 0M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Reaper_Green", 0M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Spear", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_SpearBlue", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_WarpCascadeBeam", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_WarpCascadeBeamII", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_23x23_Merciless", 200M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_2x34_RailgunPrimary", 200M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_3x3_Pulsar", 20M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_3x5_Bombard", 10M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Bleaksky_Ballistic", 25M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Castigation_Ballistic", 150M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Condemnation", 100M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Maldiction_Ballistic", 100M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Phantom", 40M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_SkyShatter", 30M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_7x7_Terminous", 30M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_9x9_Calamity", 150M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_9x9_K3_King", 30M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HS_SpinalLaser_adaptive", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaser_adaptive_Green", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserII_adaptive", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserII_adaptive_Green", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserIII", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HSR_GateKeeper", 100M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HSR_MassDriver_I", 300M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HSR_SG_1xT", 1M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HSR_SG_3x", 3M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HSR_SG_ElectroBlade", 3M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_HSR_SG_Zeus", 15M, 0.1M, true, true); // HSR
                ret.Add(AmmoType, "K_SpinalLaser_Beam_True", 0M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "LargePhotonTorp", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "LargeShipShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "LargeShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "LargeTrikobaltCharge", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "LaserAmmo", 0.001M, 0.01M, true, true); // (DX11)Laser Turret
                ret.Add(AmmoType, "LaserArrayFlakMagazine", 45M, 30M, true, true); // White Dwarf - Directed Energy Platform [DX11]
                ret.Add(AmmoType, "LaserArrayShellMagazine", 45M, 120M, true, true); // White Dwarf - Directed Energy Platform [DX11]
                ret.Add(AmmoType, "Liquid Naquadah", 0.25M, 0.1M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(AmmoType, "LittleDavid", 360M, 280M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "MagazineCitadelBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineFighterDualLightBlaster", 1M, 20M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineLargeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineMediumBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineNovaTorpedoPowerCellRed", 1M, 20M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineSmallBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineSmallThorMissilePowerCellOrange", 1M, 20M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineSmallTorpedoPowerCellRed", 1M, 20M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineThorMissilePowerCellOrange", 1M, 20M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineTMLargeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineTMMedBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineTMSiegeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MagazineTMSmallBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack
                ret.Add(AmmoType, "MedBlaster", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "MinotaurAmmo", 360M, 128M, true, true); // (DX11)Minotaur Cannon
                ret.Add(AmmoType, "Missile200mm", 45M, 60M, true, true); // Space Engineers
                ret.Add(AmmoType, "Mk14PhaserBeamAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Mk15PhaserBeamAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "MK1CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK2CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK3CannonMagazineAP", 100M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK3CannonMagazineHE", 300M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "Mk6PhaserBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "NATO_25x184mm", 35M, 16M, true, true); // Space Engineers
                ret.Add(AmmoType, "NATO_5p56x45mm", 0.45M, 0.2M, true, true); // Space Engineers
                ret.Add(AmmoType, "NiFeDUSlugMagazineLZM", 45M, 50M, true, true); // Revived Large Ship Railguns (With penetration damage!)
                ret.Add(AmmoType, "NukeSiloMissile", 90M, 150M, true, true); // rearth's Advanced Combat Systems
                ret.Add(AmmoType, "OKI122mmAmmo", 150M, 120M, true, true); // OKI Grand Weapons Bundle (DX11)
                ret.Add(AmmoType, "OKI230mmAmmo", 800M, 800M, true, true); // OKI Grand Weapons Bundle (DX11)
                ret.Add(AmmoType, "OKI23mmAmmo", 100M, 50M, true, true); // OKI Grand Weapons Bundle (DX11)
                ret.Add(AmmoType, "OKI50mmAmmo", 200M, 60M, true, true); // OKI Grand Weapons Bundle (DX11)
                ret.Add(AmmoType, "OKIObserverMAG", 1M, 1M, true, true); // OKI Grand Weapons Bundle (DX11)
                ret.Add(AmmoType, "OSPhaserAmmo", 30.0M, 15.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "OSPhotonTorp", 30M, 60M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaseCannonAmmo", 12.0M, 3.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserBeamAmmo_LR", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserLanceAmmo", 984.0M, 850.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserLanceAmmo_LR", 984.0M, 850.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserLanceTurretAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PhaserPulseAmmo", 250.0M, 100.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "photon_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "Plasma_Hydrogen", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "PlasmaBeamAmmo", 0.1M, 0.5M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PlasmaCutterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "PlasmaMissile", 30M, 50M, true, true); // rearth's Advanced Combat Systems
                ret.Add(AmmoType, "PolaronBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "PolaronPulseAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "QuantenTorpedoLarge", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "quantum_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "RB_NATO_125x920mm", 875M, 160M, true, true); // RB Weapon Collection [DX11]
                ret.Add(AmmoType, "RB_Rocket100mm", 11.25M, 15M, true, true); // RB Weapon Collection [DX11]
                ret.Add(AmmoType, "RB_Rocket400mm", 180M, 240M, true, true); // RB Weapon Collection [DX11]
                ret.Add(AmmoType, "RG_RG_ammo", 45M, 60M, true, true); // RG_RailGun
                ret.Add(AmmoType, "romulanphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "RomulanTorp", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "small_discovery_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "small_federationphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "SmallDisruptorBeamAmmo", 12.0M, 3.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SmallPhaserBeamAmmo", 12.0M, 3.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SmallPhotonTorp", 12M, 8M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SmallPlasmaBeamAmmo", 0.1M, 0.5M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SmallPolaronBeamAmmo", 12.0M, 3.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SmallShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "SmallShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "SniperRoundHighSpeedLowDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundHighSpeedLowDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundLowSpeedHighDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundLowSpeedHighDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SpartialTorp", 45M, 50M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "SWDisruptorBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "TankCannonAmmoSEM4", 35M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "TelionAF_PMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "TelionAMMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "TMPPhaserAmmo", 30.0M, 15.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "tng_quantum_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "TOS", 35M, 16M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "TOSPhaserBeamAmmo", 25.0M, 10.0M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "TritiumMissile", 72M, 60M, true, true); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2
                ret.Add(AmmoType, "TritiumShot", 3M, 3M, true, true); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2
                ret.Add(AmmoType, "TungstenBolt", 4812M, 250M, true, true); // (DX11)Mass Driver
                ret.Add(AmmoType, "Type10", 35M, 16M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Type12", 35M, 16M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Type8", 35M, 16M, true, true); // Star Trek Weapons Pack
                ret.Add(AmmoType, "Vulcan20x102", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)

                ret.Add(GunType, "AngleGrinder2Item", 3M, 20M, true, false); // Space Engineers
                ret.Add(GunType, "AngleGrinder3Item", 3M, 20M, true, false); // Space Engineers
                ret.Add(GunType, "AngleGrinder4Item", 3M, 20M, true, false); // Space Engineers
                ret.Add(GunType, "AngleGrinderItem", 3M, 20M, true, false); // Space Engineers
                ret.Add(GunType, "AutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "CubePlacerItem", 1M, 1M, true, false); // Space Engineers
                ret.Add(GunType, "EmergencyBlasterItem", 3M, 14M, true, false); // Independent Survival
                ret.Add(GunType, "GoodAIRewardPunishmentTool", 0.1M, 1M, true, false); // Space Engineers
                ret.Add(GunType, "HandDrill2Item", 22M, 25M, true, false); // Space Engineers
                ret.Add(GunType, "HandDrill3Item", 22M, 25M, true, false); // Space Engineers
                ret.Add(GunType, "HandDrill4Item", 22M, 25M, true, false); // Space Engineers
                ret.Add(GunType, "HandDrillItem", 22M, 25M, true, false); // Space Engineers
                ret.Add(GunType, "P90", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(GunType, "PhysicalConcreteTool", 5M, 15M, true, false); // Concrete Tool - placing voxels in survival
                ret.Add(GunType, "PreciseAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "RapidFireAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "RG_RG_Item", 5M, 24M, true, false); // RG_RailGun
                ret.Add(GunType, "Staff", 3M, 16M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
                ret.Add(GunType, "TritiumAutomaticRifleItem", 6M, 21M, true, false); // [VisSE] [2019] Hydro Reactors & Ice to Oxy Hydro Gasses V2
                ret.Add(GunType, "UltimateAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "Welder2Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Welder3Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Welder4Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "WelderItem", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Zat", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)

                ret.Add(OxygenType, "GrapheneOxygenBottle", 20M, 100M, true, false); // Graphene Armor [Core] [Beta]
                ret.Add(OxygenType, "OxygenBottle", 30M, 120M, true, false); // Space Engineers

                ret.Add(GasType, "GrapheneHydrogenBottle", 20M, 100M, true, false); // Graphene Armor [Core] [Beta]
                ret.Add(GasType, "HydrogenBottle", 30M, 120M, true, false); // Space Engineers

                return ret;
            }

            public void Add(string mainType, string subtype, decimal mass, decimal volume, bool hasIntegralAmounts, bool isStackable)
            {
                var key = new MyItemType(mainType, subtype);
                var value = new ItemInfo(mass, volume, hasIntegralAmounts, isStackable);
                try
                {
                    ItemInfoDict.Add(key, value);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException("Item info for " + mainType + "/" + subtype + " already added", ex);
                }
            }

            public ItemInfo Get(Statistics stat, MyItemType key)
            {
                ItemInfo data;
                if (ItemInfoDict.TryGetValue(key, out data))
                    return data;

                stat.Output.Append("Volume to amount ratio for ");
                stat.Output.Append(key);
                stat.Output.AppendLine(" is not known.");
                stat.MissingInfo.Add(key.ToString());
                return null;
            }
        }

        internal class ItemInfo
        {
            public readonly bool HasIntegralAmounts;
            public readonly bool IsStackable;
            public readonly decimal Mass;
            public readonly decimal Volume;

            public ItemInfo(decimal mass, decimal volume, bool hasIntegralAmounts, bool isStackable)
            {
                Mass = mass;
                Volume = volume;
                HasIntegralAmounts = hasIntegralAmounts;
                IsStackable = isStackable;
            }
        }

        internal class MyTextPanelNameComparer : IComparer<IMyTextPanel>
        {
            public int Compare(IMyTextPanel x, IMyTextPanel y)
            {
                return String.Compare(x.CustomName, y.CustomName, true);
            }
        }

        internal class Statistics
        {
            public readonly HashSet<string> MissingInfo;
            public readonly StringBuilder Output;
            public readonly List<MyInventoryItem> _tmpItems;
            public string DrillsPayloadStr;
            public bool DrillsVolumeWarning;
            public int MovementsDone;
            public bool NotConnectedDrillsFound;
            public int NumberOfNetworks;

            public Statistics()
            {
                Output = new StringBuilder();
                MissingInfo = new HashSet<string>();
                _tmpItems = new List<MyInventoryItem>();
            }

            public List<MyInventoryItem> EmptyTempItemList()
            {
                _tmpItems.Clear();
                return _tmpItems;
            }
        }
    }

    internal class ReferencedTypes
    {
        private static readonly Type[] ImplicitIngameNamespacesFromTypes = new Type[]
        {
            typeof(object),
            typeof(IEnumerable),
            typeof(IEnumerable<>),
            typeof(Enumerable),
            typeof(StringBuilder),
            typeof(MyModelComponent),
            typeof(IMyGridTerminalSystem),
            typeof(ITerminalAction),
            typeof(IMyAirVent),
            typeof(ListReader<>),
            typeof(Game),
            typeof(IMyComponentAggregate),
            typeof(IMyCubeBlock),
            typeof(MyIni),
            typeof(MyObjectBuilder_FactionDefinition),
            typeof(Vector2),
        };
    }
}
