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

namespace SEScriptsDotnet.ResourceExchanger2_5_4_197
{
    public class Program : MyGridProgram
    {
        /// Resource Exchanger version 2.5.4 2021-01-03 for SE 1.197.073
        /// Made by Sinus32
        /// http://steamcommunity.com/sharedfiles/filedetails/546221822
        ///
        /// Warning! This script does not require any timer blocks and will run immediately.
        /// If you want to stop it just switch the programmable block off.
        ///
        /// Configuration can be changed in custom data of the programmable block.

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
        private const string Consumable = "MyObjectBuilder_ConsumableItem";
        private const string Datapad = "MyObjectBuilder_Datapad";
        private const string Package = "MyObjectBuilder_Package";
        private const string Physical = "MyObjectBuilder_PhysicalObject";
        private readonly int[] _avgMovements;
        private readonly Dictionary<MyDefinitionId, List<MyItemType>> _blockMap;
        private BlockStore _blockStore = null;
        private int _cycleNumber = 0;
        private object _prevConfig;

        public Program()
        {
            Items = ItemDict.BuildItemInfoDict();
            _blockMap = new Dictionary<MyDefinitionId, List<MyItemType>>();
            _avgMovements = new int[0x08];

            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            ReadConfig();

            var stat = new Statistics();
            BlockStore bs = CollectTerminals(stat, (_cycleNumber & 0x0f) > 0);
            if (bs == null)
            {
                PrintOnlineStatus(bs, stat);
                return;
            }

            if (Runtime.CurrentInstructionCount < (Runtime.MaxInstructionCount >> 1))
            {
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
            }

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

        private BlockStore CollectTerminals(Statistics stat, bool refreshReferences)
        {
            BlockStore bs;
            if (refreshReferences && _blockStore != null)
            {
                bs = _blockStore;
                bs.RefreshReferences(stat);
            }
            else
            {
                bs = new BlockStore(this);
                stat.DiscoveryDone = true;
                var blocks = new List<IMyTerminalBlock>();

                if (String.IsNullOrEmpty(ManagedBlocksGroup))
                {
                    GridTerminalSystem.GetBlocksOfType(blocks, hasInventory);
                }
                else
                {
                    var group = GridTerminalSystem.GetBlockGroupWithName(ManagedBlocksGroup);
                    if (group == null)
                        stat.Output.Append("Error: a group ").Append(ManagedBlocksGroup).AppendLine(" has not been found");
                    else
                        group.GetBlocksOfType(blocks, hasInventory);
                }

                var top = Runtime.MaxInstructionCount;
                top -= top >> 2;
                foreach (var dt in blocks)
                {
                    if (bs.Collect(dt) && Runtime.CurrentInstructionCount > top)
                        return null;
                }

                if (!String.IsNullOrEmpty(DisplayLcdGroup))
                {
                    var group = GridTerminalSystem.GetBlockGroupWithName(DisplayLcdGroup);
                    if (group != null)
                        group.GetBlocksOfType<IMyTextPanel>(bs.DebugScreen, isFunctional);
                }

                if (!String.IsNullOrEmpty(DrillsPayloadLightsGroup))
                {
                    var group = GridTerminalSystem.GetBlockGroupWithName(DrillsPayloadLightsGroup);
                    if (group != null)
                        group.GetBlocksOfType<IMyLightingBlock>(bs.DrillsPayloadLights, isFunctional);
                }
                _blockStore = bs;
            }

            stat.Output.Append("Resource exchanger 2.5.2. Blocks managed:")
                .Append(" reactors: ").Append(countOrNA(bs.Reactors, EnableReactors))
                .Append(", refineries: ").Append(countOrNA(bs.Refineries, EnableRefineries)).AppendLine(",")
                .Append("oxygen gen.: ").Append(countOrNA(bs.OxygenGenerators, EnableOxygenGenerators))
                .Append(", drills: ").Append(countOrNA(bs.Drills, EnableDrills))
                .Append(", turrets: ").Append(countOrNA(bs.Turrets, EnableTurrets))
                .Append(", cargo cont.: ").Append(countOrNA(bs.CargoContainers, EnableGroups))
                .Append(", custom groups: ").Append(countOrNA(bs.Groups, EnableGroups)).AppendLine();
            return bs;
        }

        private string countOrNA(ICollection c, bool e) => e ? c.Count.ToString() : "n/a";

        private bool hasInventory(IMyTerminalBlock block) => block.IsFunctional && block.HasInventory && (AnyConstruct || block.IsSameConstructAs(Me));

        private bool isFunctional(IMyTerminalBlock block) => block.IsFunctional && (AnyConstruct || block.IsSameConstructAs(Me));

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
            if (bs == null)
            {
                sb.AppendLine("Initialization in progress...");
            }
            else
            {
                var blocksAffected = bs.Reactors.Count
                    + bs.Refineries.Count
                    + bs.Drills.Count
                    + bs.Turrets.Count
                    + bs.OxygenGenerators.Count
                    + bs.CargoContainers.Count;

                sb.Append("Grids connected: ").Append(bs.AllGrids.Count)
                    .Append(AnyConstruct ? " (AC" : " (SC").AppendLine(stat.DiscoveryDone ? "+)" : ")");
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

                _avgMovements[_cycleNumber & 0x07] = stat.MovementsDone;
                var samples = Math.Min(_cycleNumber + 1, 0x08);
                double avg = 0;
                for (int i = 0; i < samples; ++i)
                    avg += _avgMovements[i];
                avg /= samples;

                sb.Append("Avg. movements: ").Append(avg.ToString("F2")).Append(" (last ").Append(samples).AppendLine(" runs)");
            }

            float cpu = Runtime.CurrentInstructionCount * 100;
            cpu /= Runtime.MaxInstructionCount;
            sb.Append("Complexity limit usage: ").Append(cpu.ToString("F2")).AppendLine("%");

            sb.Append("Prev. run time: ").Append(Runtime.LastRunTimeMs.ToString("F3")).AppendLine(" ms");

            const string bar = "··|· ···· ···· ··|· ···· ···· ";
            var pos = _cycleNumber++ % bar.Length;
            sb.Append(bar.Substring(pos)).AppendLine(bar.Remove(pos));
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

        private Color step0() => new Color(255, 255, 255);

        private Color step1() => new Color(255, 255, 0);

        private Color step2() => new Color(255, 0, 0);

        private Color warn() => (_cycleNumber & 0x1) == 0 ? new Color(128, 0, 128) : new Color(128, 0, 64);

        private Color err() => (_cycleNumber & 0x1) == 0 ? new Color(0, 128, 128) : new Color(0, 64, 128);

        private void ProcessDrillsLights(List<BlockWrapper> drills, List<IMyLightingBlock> lights, Statistics stat)
        {
            VRage.MyFixedPoint warningLevelInCubicMetersLeft = 5;

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
            const string anyConstructComment = "\n Set to \"true\" to enable exchanging items with connected ships (AC mode)."
                + "\n Keep \"false\" if blocks connected by connectors should not be affected (SC mode).";
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

            Me.CustomData = ini.ToString();
            Echo("Configuration readed");
            _prevConfig = Me.CustomData;
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
                var screen = (IMyTextSurface)bs.DebugScreen[i];
                var sb = new StringBuilder();
                int firstLine = i * linesPerDebugScreen;
                for (int j = 0; j < linesPerDebugScreen && firstLine + j < lines.Length; ++j)
                    sb.AppendLine(lines[firstLine + j].Trim());
                screen.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                screen.WriteText(sb);
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

            public bool Collect(IMyTerminalBlock block)
            {
                return CollectContainer(block as IMyCargoContainer)
                    || CollectRefinery(block as IMyRefinery)
                    || CollectReactor(block as IMyReactor)
                    || CollectDrill(block as IMyShipDrill)
                    || CollectTurret(block as IMyUserControllableGun)
                    || CollectOxygenGenerator(block as IMyGasGenerator);
            }

            public void RefreshReferences(Statistics stat)
            {
                AllGrids.Clear();
                AllGroupedInventories.Clear();

                Refresh(Reactors);
                Refresh(Refineries);
                Refresh(Turrets);
                Refresh(Drills);
                Refresh(OxygenGenerators);
                Refresh(CargoContainers);
                Refresh(DrillsPayloadLights);
                Refresh(DebugScreen);

                foreach (var gr in Groups.Values)
                {
                    var list = gr.ToList();
                    Refresh(list);
                    gr.Clear();
                    foreach (var dt in list)
                    {
                        gr.Add(dt);
                        AllGroupedInventories.Add(dt);
                    }
                }
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

            private void Refresh(List<BlockWrapper> list)
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    var wrp = list[i];
                    var block = _program.GridTerminalSystem.GetBlockWithId(wrp.Block.EntityId);
                    if (block == null)
                    {
                        list.RemoveAt(i--);
                    }
                    else
                    {
                        AllGrids.Add(block.CubeGrid);
                        list[i] = wrp.CopyFor(block);
                    }
                }
            }

            private void Refresh<TBlock>(List<TBlock> list)
                where TBlock : class, IMyTerminalBlock
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    var old = list[i];
                    var block = _program.GridTerminalSystem.GetBlockWithId(old.EntityId) as TBlock;
                    if (block == null)
                    {
                        list.RemoveAt(i--);
                    }
                    else
                    {
                        AllGrids.Add(block.CubeGrid);
                        list[i] = block;
                    }
                }
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

            public virtual BlockWrapper CopyFor(IMyTerminalBlock block)
            {
                return new BlockWrapper(block, this.AcceptedItems);
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

            public override BlockWrapper CopyFor(IMyTerminalBlock block)
            {
                return new BlockWrapperProd((IMyProductionBlock)block, this.AcceptedItems);
            }

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

                ret.Add(OreType, "[CM] Cattierite (Co)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Cohenite (Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Dense Iron (Fe+)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Glaucodot (Fe,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Heazlewoodite (Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Iron (Fe)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Kamacite (Fe,Ni,Co)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Pyrite (Fe,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[CM] Taenite (Fe,Ni)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[EI] Autunite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[EI] Carnotite (U)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[EI] Uraniaurite (U,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Chlorargyrite (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Cooperite (Ni,Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Electrum (Au,Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Galena (Ag)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Niggliite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Petzite (Ag,Au)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Porphyry (Au)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[PM] Sperrylite (Pt)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Akimotoite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Dolomite (Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Hapkeite (Fe,Si)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Olivine (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Quartz (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Sinoite (Si)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "[S] Wadsleyite (Si,Mg)", 1M, 0.37M, false, true); // Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update
                ret.Add(OreType, "Bauxite", 1M, 0.3M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Carbon", 1M, 0.37M, false, true); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(OreType, "Cobalt", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Copper", 1M, 0.2M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrudeOil", 1M, 0.7M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedBauxite", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedCobalt", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedCopper", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedGold", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedIron", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedLithium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedNickel", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedNiter", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedPlatinum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedSilicon", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedSilver", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedTantalum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedTitanium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "CrushedUranium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Deuterium", 1M, 1M, false, true); // Deuterium Fusion Reactors
                ret.Add(OreType, "Gold", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Helium", 1M, 5.6M, false, true); // (DX11)Mass Driver
                ret.Add(OreType, "Ice", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Iron", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "K_HSR_Nanites_Sludge", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(OreType, "Lithium", 1M, 0.4M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Magnesium", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Naquadah", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(OreType, "Neutronium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(OreType, "Nickel", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Niter", 1M, 0.3M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Oil Sand", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Organic", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Platinum", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "PurifiedBauxite", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedCobalt", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedCopper", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedGold", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedIron", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedLithium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedNickel", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedNiter", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedPlatinum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedSilicon", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedSilver", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedTantalum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedTitanium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "PurifiedUranium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
                ret.Add(OreType, "Silicon", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Silver", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Stone", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(OreType, "Tantalum", 1M, 0.4M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Thorium", 1M, 0.9M, false, true); // Tiered Thorium Reactors and Refinery
                ret.Add(OreType, "Titanium", 1M, 0.15M, false, true); // Industrial Overhaul - 1.0
                ret.Add(OreType, "Trinium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(OreType, "Tungsten", 1M, 0.47M, false, true); // (DX11)Mass Driver
                ret.Add(OreType, "Uranium", 1M, 0.37M, false, true); // Space Engineers

                ret.Add(IngotType, "Aluminum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Carbon", 1M, 0.025M, false, true); // Graphene Armor (Core) (Beta) - Updated
                duplicate: ret.Add(IngotType, "Carbon", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Cobalt", 1M, 0.112M, false, true); // Space Engineers
                ret.Add(IngotType, "Copper", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "DeuteriumContainer", 2M, 2M, false, true); // Deuterium Fusion Reactors
                ret.Add(IngotType, "FuelOil", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Gold", 1M, 0.052M, false, true); // Space Engineers
                ret.Add(IngotType, "HeavyWater", 3M, 0.052M, false, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(IngotType, "Iron", 1M, 0.127M, false, true); // Space Engineers
                ret.Add(IngotType, "K_HSR_Nanites_Cerebi", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_Chromium", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_EnergizedGel", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_Hexagol", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_IX", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_KEL", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "K_HSR_Nanites_Sludge", 0.001M, 0.001M, false, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(IngotType, "LiquidHelium", 1M, 4.6M, false, true); // (DX11)Mass Driver
                ret.Add(IngotType, "Lithium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Magmatite", 100M, 37M, false, true); // Stone and Gravel to Metal Ingots (DX 11)
                ret.Add(IngotType, "Magnesium", 1M, 0.575M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Naquadah", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(IngotType, "Neutronium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(IngotType, "Nickel", 1M, 0.112M, false, true); // Space Engineers
                ret.Add(IngotType, "Niter", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Plastic", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Platinum", 1M, 0.047M, false, true); // Space Engineers
                ret.Add(IngotType, "Polymer", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
                ret.Add(IngotType, "ShieldPoint", 0.00001M, 0.0001M, false, true); // Energy shields (new modified version)
                ret.Add(IngotType, "Silicon", 1M, 0.429M, false, true); // Space Engineers
                ret.Add(IngotType, "Silver", 1M, 0.095M, false, true); // Space Engineers
                ret.Add(IngotType, "Stone", 1M, 0.37M, false, true); // Space Engineers
                ret.Add(IngotType, "SuitFuel", 0.0003M, 0.052M, false, true); // Independent Survival
                ret.Add(IngotType, "SuitRTGPellet", 1.0M, 0.052M, false, true); // Independent Survival
                ret.Add(IngotType, "Sulfur", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Tantalum", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "ThoriumIngot", 3M, 20M, false, true); // Tiered Thorium Reactors and Refinery
                ret.Add(IngotType, "Titanium", 1M, 0.37M, false, true); // Industrial Overhaul - 1.0
                ret.Add(IngotType, "Trinium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(IngotType, "Tungsten", 1M, 0.52M, false, true); // (DX11)Mass Driver
                ret.Add(IngotType, "Uranium", 1M, 0.052M, false, true); // Space Engineers
                ret.Add(IngotType, "v2HydrogenGas", 2.1656M, 0.43M, false, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(IngotType, "v2OxygenGas", 4.664M, 0.9M, false, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry

                ret.Add(ComponentType, "AcidPowerCell", 45M, 10M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "AdvancedComputer", 0.5M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "AdvancedReactorBundle", 50M, 20M, true, true); // Tiered Thorium Reactors and Refinery
                ret.Add(ComponentType, "AdvancedThrustModule", 80M, 30M, true, true); // Tiered Engine Super Pack
                ret.Add(ComponentType, "AegisLicense", 0.2M, 1M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "Aggitator", 40M, 10M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(ComponentType, "AlloyPlate", 30M, 3M, true, true); // Industrial Centrifuge (stable/dev)
                ret.Add(ComponentType, "ampHD", 10M, 15.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "ArcFuel", 2M, 0.627M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "ArcReactorcomponent", 312M, 100M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "ArmoredPlate", 50M, 2M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "ArmorGlass", 15M, 8M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "AVTech_DualJunction_Cell", 8M, 12M, true, true); // High Tech Solar Arrays
                ret.Add(ComponentType, "AVTech_MultiJunction_Cell", 10M, 12M, true, true); // High Tech Solar Arrays
                ret.Add(ComponentType, "AVTech_Nanocrystalline_Cell", 6M, 12M, true, true); // High Tech Solar Arrays
                ret.Add(ComponentType, "AzimuthSupercharger", 10M, 9M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(ComponentType, "BulletproofGlass", 15M, 8M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Canvas", 15M, 8M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "CapacitorBank", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "Ceramic", 15M, 4M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "CeramicPlate", 30M, 10M, true, true); // (AR) Ceramic Armor
                ret.Add(ComponentType, "Computer", 0.2M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Concrete", 50M, 5M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "ConductorMagnets", 900M, 200M, true, true); // (DX11)Mass Driver
                ret.Add(ComponentType, "Construction", 8M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "CoolingHeatsink", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "CopperWire", 2M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "DenseSteelPlate", 200M, 30M, true, true); // Arc Reactor Pack [DX-11 Ready]
                ret.Add(ComponentType, "Detector", 5M, 6M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Display", 8M, 6M, true, true); // Space Engineers
                ret.Add(ComponentType, "Drone", 200M, 60M, true, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(ComponentType, "Electromagnet", 5M, 2M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Explosives", 2M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "Fabric", 1M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "FocusPrysm", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "Girder", 6M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "GoldWire", 3M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "GrapheneAerogelFilling", 0.160M, 2.9166M, true, true); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(ComponentType, "GrapheneNanotubes", 0.01M, 0.1944M, true, true); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(ComponentType, "GraphenePlate", 3.33M, 0.54M, true, true); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(ComponentType, "GraphenePowerCell", 15M, 30M, true, true); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(ComponentType, "GravityGenerator", 800M, 200M, true, true); // Space Engineers
                duplicate: ret.Add(ComponentType, "GravityGenerator", 800M, 1M, true, true); // EM Thruster
                ret.Add(ComponentType, "HeatingElement", 15M, 3M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "InteriorPlate", 3M, 5M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "K_HSR_AssemblerSystem", 15M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_GelConduit", 2M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_Globe", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_Globe_II", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_Globe_III", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_HexagolPlating", 1M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_HexagolPlating_II", 1M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_HyperConductiveCircuitry", 5M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_Mainframe", 5M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_Mainframe_K3", 15M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_PulseSystem", 10M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_RailComponents", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_RailComponents_II", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "K_HSR_RailComponents_III", 3M, 0.01M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(ComponentType, "largehydrogeninjector", 40M, 15M, true, true); // Tiered Engine Super Pack
                ret.Add(ComponentType, "LargeTube", 25M, 38M, true, true); // Space Engineers
                ret.Add(ComponentType, "LaserConstructionBoxL", 10M, 100M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "LaserConstructionBoxS", 5M, 50M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "LaserEmitter", 15M, 6M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Lightbulb", 5M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "Magna", 100M, 15M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Magnetron", 10M, 0.5M, true, true); // EM Thruster
                ret.Add(ComponentType, "Magnetron_Component", 50M, 20M, true, true); // Deuterium Fusion Reactors
                ret.Add(ComponentType, "MagnetronComponent", 50M, 20M, true, true); // Deuterium Fusion Reactors
                ret.Add(ComponentType, "Magno", 10M, 5.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Medical", 150M, 160M, true, true); // Space Engineers
                ret.Add(ComponentType, "MetalGrid", 6M, 15M, true, true); // Space Engineers
                ret.Add(ComponentType, "Mg_FuelCell", 15M, 16M, true, true); // Ripptide's CW+EE (DX11). Reuploaded
                ret.Add(ComponentType, "Motor", 24M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "Naquadah", 100M, 10M, true, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(ComponentType, "Neutronium", 500M, 5M, true, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(ComponentType, "PowerCell", 15M, 30M, true, true); // Industrial Overhaul - 1.0
                duplicate: ret.Add(ComponentType, "PowerCell", 25M, 40M, true, true); // Space Engineers
                ret.Add(ComponentType, "PowerCoupler", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "productioncontrolcomponent", 40M, 15M, true, true); // (DX11) Double Sided Upgrade Modules
                ret.Add(ComponentType, "PulseCannonConstructionBoxL", 10M, 100M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "PulseCannonConstructionBoxS", 5M, 50M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "PWMCircuit", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "QuantumComputer", 1M, 1M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "RadioCommunication", 8M, 70M, true, true); // Space Engineers
                ret.Add(ComponentType, "Reactor", 25M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "Rubber", 15M, 3M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "SafetyBypass", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "Shield", 5M, 25M, true, true); // Energy shields (new modified version)
                ret.Add(ComponentType, "ShieldFrequencyModule", 25M, 45M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "SmallTube", 4M, 2M, true, true); // Space Engineers
                ret.Add(ComponentType, "SolarCell", 6M, 12M, true, true); // Space Engineers
                ret.Add(ComponentType, "SteelPlate", 20M, 3M, true, true); // Space Engineers
                duplicate: ret.Add(ComponentType, "SteelPlate", 10M, 0.5M, true, true); // EM Thruster
                ret.Add(ComponentType, "Superconductor", 15M, 8M, true, true); // Space Engineers
                ret.Add(ComponentType, "TekMarLicense", 0.2M, 1M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(ComponentType, "Thrust", 40M, 10M, true, true); // Space Engineers
                ret.Add(ComponentType, "TitaniumPlate", 15M, 2M, true, true); // Industrial Overhaul - 1.0
                ret.Add(ComponentType, "TractorHD", 1500M, 200M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
                ret.Add(ComponentType, "Trinium", 100M, 10M, true, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(ComponentType, "Tritium", 3M, 3M, true, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(ComponentType, "WaterTankComponent", 200M, 160M, true, true); // Industrial Centrifuge (stable/dev)
                ret.Add(ComponentType, "ZoneChip", 0.250M, 0.2M, true, true); // Space Engineers
                ret.Add(ComponentType, "ZPM", 50M, 60M, true, true); // [New Version] Stargate Modpack (Economy Support!)

                ret.Add(AmmoType, "250shell", 128M, 64M, true, true); // [DEPRECATED] CSD Battlecannon
                ret.Add(AmmoType, "300mmShell_HE", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "88shell", 16M, 16M, true, true); // [DEPRECATED] CSD Battlecannon
                ret.Add(AmmoType, "900mmShell_HE", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "Aden30x113", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "AFmagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "AGM114_Angelfire", 35M, 16M, true, true); // Azimuth Remastered
                ret.Add(AmmoType, "AZ_Missile_AA", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "AZ_Missile200mm", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "BatteryCannonAmmo1", 50M, 50M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "BatteryCannonAmmo2", 200M, 200M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "BigBertha", 3600M, 2800M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "BlasterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Bofors40mm", 36M, 28M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "Class1LaserBeamCharge", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "ConcreteMix", 2M, 2M, true, true); // Concrete Tool - placing voxels in survival
                ret.Add(AmmoType, "crystalline_microcapacitor", 25M, 16M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "crystalline_nanocapacitor", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "discovery_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "Eikester_Missile120mm", 25M, 30M, true, true); // (DX11) Small Missile Turret
                ret.Add(AmmoType, "Eikester_Nuke", 1800M, 8836M, true, true); // (DX11) Nuke Launcher [WiP]
                ret.Add(AmmoType, "EmergencyBlasterMagazine", 0.45M, 0.2M, true, true); // Independent Survival
                ret.Add(AmmoType, "Energy", 1M, 1M, true, true); // WeaponCore - 1.6(20)
                ret.Add(AmmoType, "federationphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "Flak130mm", 2M, 3M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Flak200mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "Flak500mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "GH15mmAmmo", 45M, 16M, true, true); // GH-Industry (ModPack) (17 new blocks)
                ret.Add(AmmoType, "GH75mmAmmo", 35M, 16M, true, true); // GH-Industry (ModPack) (17 new blocks)
                ret.Add(AmmoType, "GravelMag", 0M, 0M, true, true); // Industrial Overhaul - 1.0
                ret.Add(AmmoType, "GravelMagBig", 0M, 0M, true, true); // Industrial Overhaul - 1.0
                ret.Add(AmmoType, "HDTCannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "heavy_photon_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "HighDamageGatlingAmmo", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "ISM_FusionAmmo", 35M, 10M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISM_GrendelAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISM_Hellfire", 45M, 60M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISM_LongbowAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISM_MinigunAmmo", 35M, 16M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISMNeedles", 35M, 16M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "ISMTracer", 35M, 16M, true, true); // ISM Mega Mod Pack [OUTDATED]
                ret.Add(AmmoType, "K_CS_DarkLance", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_DarkLance_Red", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Eye", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Reaper", 0M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Reaper_Green", 0M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_Spear", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_SG_SpearBlue", 1M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_WarpCascadeBeam", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_CS_WarpCascadeBeamII", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaser_adaptive", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaser_adaptive_Green", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserII_adaptive", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserII_adaptive_Green", 5M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HS_SpinalLaserIII", 15M, 0.25M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "K_HSR_Seeker", 1M, 0.1M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(AmmoType, "K_HSR_Slug", 1M, 0.1M, true, true); // HSR (WeaponCore) Strategic Update November 2020
                ret.Add(AmmoType, "K_SpinalLaser_Beam_True", 0M, 1M, true, true); // SpinalWeaponry
                ret.Add(AmmoType, "LargeShipShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "LargeShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "LaserAmmo", 0.001M, 0.01M, true, true); // (DX11)Laser Turret
                ret.Add(AmmoType, "LaserArrayFlakMagazine", 45M, 30M, true, true); // White Dwarf - Directed Energy Platform [DX11]
                ret.Add(AmmoType, "LaserArrayShellMagazine", 45M, 120M, true, true); // White Dwarf - Directed Energy Platform [DX11]
                ret.Add(AmmoType, "Liquid Naquadah", 0.25M, 0.1M, true, true); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(AmmoType, "LittleDavid", 360M, 280M, true, true); // Battle Cannon and Turrets (DX11)
                ret.Add(AmmoType, "MagazineCitadelBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineFighterDualLightBlaster", 1M, 20M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineLargeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineMediumBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineNovaTorpedoPowerCellRed", 1M, 20M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineSmallBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineSmallThorMissilePowerCellOrange", 1M, 20M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineSmallTorpedoPowerCellRed", 1M, 20M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineThorMissilePowerCellOrange", 1M, 20M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineTMLargeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineTMMedBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineTMSiegeBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MagazineTMSmallBlasterTurret", 35M, 16M, true, true); // GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken
                ret.Add(AmmoType, "MedBlaster", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "MinotaurAmmo", 360M, 128M, true, true); // (DX11)Minotaur Cannon
                ret.Add(AmmoType, "Missile200mm", 45M, 60M, true, true); // Space Engineers
                ret.Add(AmmoType, "MK1CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK2CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK3CannonMagazineAP", 100M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "MK3CannonMagazineHE", 300M, 100M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "NATO_25x137mm", 35M, 16M, true, true); // Azimuth Remastered
                ret.Add(AmmoType, "NATO_25x184mm", 35M, 16M, true, true); // Space Engineers
                ret.Add(AmmoType, "NATO_5p56x45mm", 0.45M, 0.2M, true, true); // Space Engineers
                ret.Add(AmmoType, "NiFeDUSlugMagazineLZM", 45M, 50M, true, true); // Revived Large Ship Railguns (With penetration and shield damage!)
                ret.Add(AmmoType, "NukeSiloMissile", 90M, 150M, true, true); // [Fixed] rearth's Advanced Combat Systems
                ret.Add(AmmoType, "OKI122mmAmmo", 45M, 32M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKI180mmHVXammo", 75M, 48M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKI230mmAmmo", 90M, 64M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKI23mmAmmo", 35M, 16M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKI50mmAmmo", 45M, 24M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKI75mmHVPammo", 75M, 48M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "OKIObserverMAG", 1M, 1M, true, true); // OKI Grand Weapons Bundle
                ret.Add(AmmoType, "PDLaserCharge", 0M, 0M, true, true); // Industrial Overhaul - 1.0
                ret.Add(AmmoType, "photon_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "Plasma_Hydrogen", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "PlasmaCutterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
                ret.Add(AmmoType, "PlasmaMissile", 30M, 50M, true, true); // [Fixed] rearth's Advanced Combat Systems
                ret.Add(AmmoType, "quantum_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "RB_NATO_125x920mm", 875M, 160M, true, true);
                ret.Add(AmmoType, "RB_Rocket100mm", 11.25M, 15M, true, true);
                ret.Add(AmmoType, "RB_Rocket400mm", 180M, 240M, true, true);
                ret.Add(AmmoType, "RG_RG_ammo", 45M, 60M, true, true); // RG_RailGun
                ret.Add(AmmoType, "romulanphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "small_discovery_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "small_federationphase", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "SmallShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "SmallShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "SniperRoundHighSpeedLowDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundHighSpeedLowDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundLowSpeedHighDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "SniperRoundLowSpeedHighDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
                ret.Add(AmmoType, "TankCannonAmmoSEM4", 35M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
                ret.Add(AmmoType, "TelionAF_PMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "TelionAMMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
                ret.Add(AmmoType, "tng_quantum_torpedo", 5M, 3.2M, true, true); // Star Trek - Weapons Tech [WIP]
                ret.Add(AmmoType, "tritiumlongmagazine", 5M, 10M, true, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(AmmoType, "TritiumMissile", 72M, 60M, true, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(AmmoType, "TritiumShot", 3M, 3M, true, true); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(AmmoType, "TungstenBolt", 4812M, 250M, true, true); // (DX11)Mass Driver
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
                ret.Add(GunType, "P90", 3M, 12M, true, false); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(GunType, "PhysicalConcreteTool", 5M, 15M, true, false); // Concrete Tool - placing voxels in survival
                ret.Add(GunType, "PreciseAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "RapidFireAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "RG_RG_Item", 5M, 24M, true, false); // RG_RailGun
                ret.Add(GunType, "Staff", 3M, 16M, true, false); // [New Version] Stargate Modpack (Economy Support!)
                ret.Add(GunType, "TritiumAutomaticRifleItem", 6M, 21M, true, false); // [VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry
                ret.Add(GunType, "UltimateAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
                ret.Add(GunType, "Welder2Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Welder3Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Welder4Item", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "WelderItem", 5M, 8M, true, false); // Space Engineers
                ret.Add(GunType, "Zat", 3M, 12M, true, false); // [New Version] Stargate Modpack (Economy Support!)

                ret.Add(OxygenType, "GrapheneOxygenBottle", 3.33M, 80M, true, false); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(OxygenType, "OxygenBottle", 30M, 120M, true, false); // Space Engineers

                ret.Add(GasType, "GrapheneHydrogenBottle", 3.33M, 80M, true, false); // Graphene Armor (Core) (Beta) - Updated
                ret.Add(GasType, "HydrogenBottle", 30M, 120M, true, false); // Space Engineers

                ret.Add(Consumable, "ClangCola", 1M, 1M, true, true); // Space Engineers
                ret.Add(Consumable, "CosmicCoffee", 1M, 1M, true, true); // Space Engineers
                ret.Add(Consumable, "Medkit", 10M, 12M, true, true); // Space Engineers
                ret.Add(Consumable, "Powerkit", 9M, 9M, true, true); // Space Engineers

                ret.Add(Datapad, "Datapad", 0.2M, 0.4M, true, false); // Space Engineers

                ret.Add(Package, "Package", 100M, 125M, true, false); // Space Engineers

                ret.Add(Physical, "SpaceCredit", 0.001M, 0.001M, true, true); // Space Engineers

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
            public readonly List<MyInventoryItem> _tmpItems;
            public readonly HashSet<string> MissingInfo;
            public readonly StringBuilder Output;
            public string DrillsPayloadStr;
            public bool NotConnectedDrillsFound, DrillsVolumeWarning, DiscoveryDone;
            public int NumberOfNetworks, MovementsDone;

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
