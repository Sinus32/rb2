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

namespace SEScripts.ResourceExchanger2_5_0_187
{
    public class Program : MyGridProgram
    {
        /// Resource Exchanger version 2.5.0 2018-??-?? for SE 1.187+
        /// Made by Sinus32
        /// http://steamcommunity.com/sharedfiles/filedetails/546221822
        ///
        /// Attention! This script does not require any timer blocks and will run immediately.
        /// If you want to stop it just switch the programmable block off.
        ///
        /// Configuration can be changed in custom data of the programmable block

        /** Default configuration *****************************************************************/

        public bool MyGridOnly = false;
        public string ManagedBlocksGroup = "";
        public bool EnableReactors = true;
        public bool EnableRefineries = true;
        public bool EnableDrills = true;
        public bool EnableTurrets = true;
        public bool EnableOxygenGenerators = true;
        public bool EnableGroups = true;
        public string DrillsPayloadLightsGroup = "Payload indicators";
        public string TopRefineryPriority = "MyObjectBuilder_Ore/Iron";
        public string LowestRefineryPriority = "MyObjectBuilder_Ore/Stone";
        public string GroupTagPattern = @"\bGR\d{1,3}\b";
        public string DisplayLcdGroup = "Resource exchanger output";

        /** Implementation ************************************************************************/

        private const decimal SmallNumber = 0.000003M;
        private const string OreType = "MyObjectBuilder_Ore";
        private const string IngotType = "MyObjectBuilder_Ingot";
        private const string ComponentType = "MyObjectBuilder_Component";
        private const string AmmoType = "MyObjectBuilder_AmmoMagazine";
        private const string GunType = "MyObjectBuilder_PhysicalGunObject";
        private const string OxygenType = "MyObjectBuilder_OxygenContainerObject";
        private const string GasType = "MyObjectBuilder_GasContainerObject";
        private readonly Dictionary<MyDefinitionId, ulong[]> _blockToGroupIdMap;
        private readonly Dictionary<ulong[], string> _groupIdToNameMap;
        private readonly int[] _avgMovements;
        private int _cycleNumber = 0;

        public Program()
        {
            _blockToGroupIdMap = new Dictionary<MyDefinitionId, ulong[]>(MyDefinitionId.Comparer);
            _groupIdToNameMap = new Dictionary<ulong[], string>(LongArrayComparer.Instance);
            _avgMovements = new int[0x10];

            BuildItemInfoDict();
            //Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Save()
        { }

        public void Main(string argument, UpdateType updateSource)
        {
            ReadConfig();

            var bs = new BlockStore(this);
            var stat = new Statistics();
            CollectTerminals(bs, stat);

            ProcessBlocks("Balancing reactors", EnableReactors, bs.Reactors, stat, exclude: bs.AllGroupedInventories);
            ProcessBlocks("Balancing refineries", EnableRefineries, bs.Refineries, stat, exclude: bs.AllGroupedInventories);
            var dcn = ProcessBlocks("Balancing drills", EnableDrills, bs.Drills, stat, invGroup: "drills", exclude: bs.AllGroupedInventories);
            stat.NotConnectedDrillsFound = dcn > 1;
            ProcessBlocks("Balancing turrets", EnableTurrets, bs.Turrets, stat, exclude: bs.AllGroupedInventories);
            ProcessBlocks("Balancing oxygen gen.", EnableOxygenGenerators, bs.OxygenGenerators, stat, invGroup: "oxygen generators",
                exclude: bs.AllGroupedInventories, filter: item => item.Content.TypeId.ToString() == OreType);

            if (EnableGroups)
            {
                foreach (var kv in bs.Groups)
                    ProcessBlocks("Balancing group " + kv.Key, true, kv.Value, stat, invGroup: kv.Key);
            }
            else
            {
                stat.Output.AppendLine("Balancing groups: disabled");
            }

            if (EnableRefineries)
            {
                MyDefinitionId tmp;
                MyDefinitionId? topPriority = null, lowestPriority = null;
                if (String.IsNullOrEmpty(TopRefineryPriority))
                {
                    if (MyDefinitionId.TryParse(TopRefineryPriority, out tmp))
                        topPriority = tmp;
                    else
                        Echo("Err: type is invalid: " + TopRefineryPriority);
                }
                if (String.IsNullOrEmpty(LowestRefineryPriority))
                {
                    if (MyDefinitionId.TryParse(LowestRefineryPriority, out tmp))
                        lowestPriority = tmp;
                    else
                        Echo("Err: type is invalid: " + LowestRefineryPriority);
                }
                EnforceItemPriority(bs.Refineries, stat, topPriority, lowestPriority);
            }

            if (dcn >= 1)
            {
                ProcessDrillsLights(bs.Drills, bs.DrillsPayloadLights, stat);
            }

            PrintOnlineStatus(bs, stat);
            WriteOutput(bs, stat);
        }

        public void ReadConfig()
        {
            const string myGridOnlyComment = "Limit affected blocks to only these that are connected to the same ship/station as the"
                + "\nthe programmable block. Set to true if blocks on ships connected by connectors"
                + "\nor rotors should not be affected.";
            const string managedGroupComment = "Optional name of a group of blocks that will be affected by the script."
                + "\nBy default all blocks connected to the grid are processed, but you can set this"
                + "\nto force the script to affect only certain blocks.";
            const string reactorsComment = "Enables exchanging uranium between reactors";
            const string refineriesComment = "Enables exchanging ore between refineries and arc furnaces";
            const string drillsComment = "Enables exchanging ore between drills and"
                + "\nprocessing lights that indicates how much free space left in drills";
            const string turretsComment = "Enables exchanging ammunition between turrets and launchers";
            const string oxygenGeneratorsComment = "Enables exchanging ice between oxygen generators";
            const string groupsComment = "Enables exchanging items in blocks of custom groups";
            const string drillsPayloadLightsGroupComment = "Name of a group of lights that will be used as indicators of space left in drills."
                + "\nBoth Interior Light and Spotlight are supported."
                + "\nThe lights will change colors to tell you how much free space left:"
                + "\nWhite - All drills are connected to each other and they are empty."
                + "\nYellow - Drills are full in a half."
                + "\nRed - Drills are almost full (95%)."
                + "\nPurple - Less than WARNING_LEVEL_IN_KILOLITERS_LEFT m3 of free space left."
                + "\nCyan - Some drills are not connected to each other.";
            const string topRefineryPriorityComment = "Top priority item type to process in refineries and/or arc furnaces."
                + "\nThe script will move an item of this type to the first slot of a refinery or arc"
                + "\nfurnace if it find that item in the refinery (or arc furnace) processing queue.";
            const string lowestRefineryPriorityComment = "Lowest priority item type to process in refineries and/or arc furnaces."
                + "\nThe script will move an item of this type to the last slot of a refinery or arc"
                + "\nfurnace if it find that item in the refinery (or arc furnace) processing queue.";
            const string groupTagPatternComment = "Regular expression used to recognize groups";
            const string displayLcdGroupComment = "Group of wide LCD screens that will act as debugger output for this script."
                + "\nYou can name this screens as you wish, but pay attention that"
                + "\nthey will be used in alphabetical order according to their names.";

            MyIniParseResult result;
            var ini = new MyIni();
            if (!ini.TryParse(Me.CustomData, out result))
            {
                Echo(String.Format("Err: invalid config in line {0}: {1}", result.LineNo, result.Error));
                return;
            }

            ReadConfigBoolean(ini, nameof(MyGridOnly), ref MyGridOnly, myGridOnlyComment);
            ReadConfigString(ini, nameof(ManagedBlocksGroup), ref ManagedBlocksGroup, managedGroupComment);
            ReadConfigBoolean(ini, nameof(EnableReactors), ref EnableReactors, reactorsComment);
            ReadConfigBoolean(ini, nameof(EnableRefineries), ref EnableRefineries, refineriesComment);
            ReadConfigBoolean(ini, nameof(EnableDrills), ref EnableDrills, drillsComment);
            ReadConfigBoolean(ini, nameof(EnableTurrets), ref EnableTurrets, turretsComment);
            ReadConfigBoolean(ini, nameof(EnableOxygenGenerators), ref EnableOxygenGenerators, oxygenGeneratorsComment);
            ReadConfigBoolean(ini, nameof(EnableGroups), ref EnableGroups, groupsComment);
            ReadConfigString(ini, nameof(DrillsPayloadLightsGroup), ref DrillsPayloadLightsGroup, drillsPayloadLightsGroupComment);
            ReadConfigString(ini, nameof(TopRefineryPriority), ref TopRefineryPriority, topRefineryPriorityComment);
            ReadConfigString(ini, nameof(LowestRefineryPriority), ref LowestRefineryPriority, lowestRefineryPriorityComment);
            ReadConfigString(ini, nameof(GroupTagPattern), ref GroupTagPattern, groupTagPatternComment);
            ReadConfigString(ini, nameof(DisplayLcdGroup), ref DisplayLcdGroup, displayLcdGroupComment);

            Me.CustomData = ini.ToString();
        }

        public void ReadConfigBoolean(MyIni ini, string name, ref bool value, string comment)
        {
            const string section = "ResourceExchanger";
            var key = new MyIniKey(section, name);
            MyIniValue val = ini.Get(key);
            bool tmp;
            if (val.TryGetBoolean(out tmp))
                value = tmp;
            else
                ini.Set(key, value);
            ini.SetComment(key, comment);
        }

        public void ReadConfigString(MyIni ini, string name, ref string value, string comment)
        {
            const string section = "ResourceExchanger";
            var key = new MyIniKey(section, name);
            MyIniValue val = ini.Get(key);
            string tmp;
            if (val.TryGetString(out tmp))
                value = tmp.Trim();
            else
                ini.Set(key, value);
            ini.SetComment(key, comment);
        }

        private BlockStore CollectTerminals(BlockStore bs, Statistics stat)
        {
            var blocks = new List<IMyTerminalBlock>();

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

            bool myTerminalBlockFilter(IMyTerminalBlock b) => b.IsFunctional && (!MyGridOnly || b.CubeGrid == Me.CubeGrid);
            string countOrNA(ICollection c, bool e) => e ? c.Count.ToString() : "n/a";
        }

        private int ProcessBlocks(string msg, bool enable, ICollection<InventoryWrapper> blocks, Statistics stat, string invGroup = null,
            HashSet<InventoryWrapper> exclude = null, Func<IMyInventoryItem, bool> filter = null)
        {
            stat.Output.Append(msg);
            if (enable)
            {
                if (blocks.Count >= 2)
                {
                    var conveyorNetworks = FindConveyorNetworks(blocks, exclude);
                    stat.NumberOfNetworks += conveyorNetworks.Count;
                    stat.Output.Append(": ").Append(conveyorNetworks.Count).AppendLine(" conveyor networks found");

                    if (invGroup != null)
                    {
                        foreach (var network in conveyorNetworks)
                            BalanceInventories(stat, network.Inventories, network.No, 0, invGroup, filter);
                    }
                    else
                    {
                        foreach (var network in conveyorNetworks)
                            foreach (var group in DivideByBlockType(network))
                                BalanceInventories(stat, group.Inventories, network.No, group.No, group.Name, filter);
                    }

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

        private List<ConveyorNetwork> FindConveyorNetworks(ICollection<InventoryWrapper> inventories, HashSet<InventoryWrapper> exclude)
        {
            var result = new List<ConveyorNetwork>();

            foreach (var wrp in inventories)
            {
                if (exclude != null && exclude.Contains(wrp))
                    continue;

                bool add = true;
                foreach (var network in result)
                {
                    if (network.Inventories[0].Inventory.IsConnectedTo(wrp.Inventory)
                        && wrp.Inventory.IsConnectedTo(network.Inventories[0].Inventory))
                    {
                        network.Inventories.Add(wrp);
                        add = false;
                        break;
                    }
                }

                if (add)
                {
                    var network = new ConveyorNetwork(result.Count + 1);
                    network.Inventories.Add(wrp);
                    result.Add(network);
                }
            }

            return result;
        }

        private List<InventoryGroup> DivideByBlockType(ConveyorNetwork network)
        {
            var groupMap = new Dictionary<string, InventoryGroup>();

            foreach (var inv in network.Inventories)
            {
                InventoryGroup group;
                if (!groupMap.TryGetValue(inv.GroupName, out group))
                {
                    group = new InventoryGroup(groupMap.Count + 1, inv.GroupName);
                    groupMap.Add(inv.GroupName, group);
                }
                group.Inventories.Add(inv);
            }

            var result = new List<InventoryGroup>(groupMap.Count);
            result.AddRange(groupMap.Values);
            return result;
        }

        private void BalanceInventories(Statistics stat, List<InventoryWrapper> group, int networkNumber,
            int groupNumber, string groupName, Func<IMyInventoryItem, bool> filter)
        {
            const int maxMovementsPerGroup = 2;

            if (group.Count < 2)
            {
                stat.Output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
                    .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
                    .AppendLine("\"")
                    .AppendLine("  because there is only one inventory.");
                return; // nothing to do
            }

            if (filter != null)
            {
                foreach (var wrp in group)
                    wrp.LoadVolume().FilterItems(stat, filter).CalculatePercent();
            }
            else
            {
                foreach (var wrp in group)
                    wrp.LoadVolume().CalculatePercent();
            }

            group.Sort(InventoryWrapperComparer.Instance);

            var last = group[group.Count - 1];

            if (last.CurrentVolume < SmallNumber)
            {
                stat.Output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
                    .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
                    .AppendLine("\"")
                    .AppendLine("  because of lack of items in it.");
                return; // nothing to do
            }

            stat.Output.Append("Balancing conveyor network ").Append(networkNumber + 1)
                .Append(" group ").Append(groupNumber + 1)
                .Append(" \"").Append(groupName).AppendLine("\"...");

            for (int i = 0; i < maxMovementsPerGroup && i < group.Count / 2; ++i)
            {
                var inv1 = group[i];
                var inv2 = group[group.Count - i - 1];

                decimal toMove;
                if (inv1.MaxVolume == inv2.MaxVolume)
                {
                    toMove = (inv2.CurrentVolume - inv1.CurrentVolume) / 2.0M;
                }
                else
                {
                    toMove = (inv2.CurrentVolume * inv1.MaxVolume
                        - inv1.CurrentVolume * inv2.MaxVolume)
                        / (inv1.MaxVolume + inv2.MaxVolume);
                }

                stat.Output.Append("Inv. 1 vol: ").Append(inv1.CurrentVolume.ToString("F6")).Append("; ");
                stat.Output.Append("Inv. 2 vol: ").Append(inv2.CurrentVolume.ToString("F6")).Append("; ");
                stat.Output.Append("To move: ").Append(toMove.ToString("F6")).AppendLine();

                if (toMove < 0.0M)
                    throw new InvalidOperationException("Something went wrong with calculations: volumeDiff is " + toMove);

                if (toMove < SmallNumber)
                    continue;

                MoveVolume(stat, inv2, inv1, (VRage.MyFixedPoint)toMove, filter);
            }
        }

        private VRage.MyFixedPoint MoveVolume(Statistics stat, InventoryWrapper from, InventoryWrapper to,
            VRage.MyFixedPoint volumeAmountToMove, Func<IMyInventoryItem, bool> filter)
        {
            if (volumeAmountToMove == 0)
                return volumeAmountToMove;

            if (volumeAmountToMove < 0)
                throw new ArgumentException("Invalid volume amount", "volumeAmount");

            stat.Output.Append("Move ").Append(volumeAmountToMove).Append(" l. from ")
                .Append(from.Block.CustomName).Append(" to ").AppendLine(to.Block.CustomName);
            List<IMyInventoryItem> itemsFrom = from.Inventory.GetItems();

            for (int i = itemsFrom.Count - 1; i >= 0; --i)
            {
                IMyInventoryItem item = itemsFrom[i];

                if (filter != null && !filter(item))
                    continue;

                var key = MyDefinitionId.FromContent(item.Content);
                var data = ItemInfo.Get(stat, key);
                if (data == null)
                    continue;

                decimal amountToMoveRaw = (decimal)volumeAmountToMove * 1000M / data.Volume;
                VRage.MyFixedPoint amountToMove;

                if (data.HasIntegralAmounts)
                    amountToMove = (VRage.MyFixedPoint)((int)(amountToMoveRaw + 0.1M));
                else
                    amountToMove = (VRage.MyFixedPoint)amountToMoveRaw;

                if (amountToMove == 0)
                    continue;

                List<IMyInventoryItem> itemsTo = to.Inventory.GetItems();
                int targetItemIndex = 0;
                while (targetItemIndex < itemsTo.Count)
                {
                    IMyInventoryItem item2 = itemsTo[targetItemIndex];
                    if (MyDefinitionId.FromContent(item2.Content).Equals(key))
                        break;
                    ++targetItemIndex;
                }

                decimal itemVolume;
                bool success;
                if (amountToMove <= item.Amount)
                {
                    itemVolume = (decimal)amountToMove * data.Volume / 1000M;
                    success = from.TransferItemTo(to, i, targetItemIndex, true, amountToMove);
                    stat.MovementsDone += 1;
                    stat.Output.Append("Move ").Append(amountToMove).Append(" -> ").AppendLine(success ? "success" : "failure");
                }
                else
                {
                    itemVolume = (decimal)item.Amount * data.Volume / 1000M;
                    success = from.TransferItemTo(to, i, targetItemIndex, true, item.Amount);
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

        private void ProcessDrillsLights(List<InventoryWrapper> drills, List<IMyLightingBlock> lights, Statistics stat)
        {
            VRage.MyFixedPoint warningLevelInCubicMetersLeft = 5;
            Color step0() => new Color(255, 255, 255);
            Color step1() => new Color(255, 255, 0);
            Color step2() => new Color(255, 0, 0);
            Color warn() => (_cycleNumber & 0x1) == 0 ? new Color(128, 0, 128) : new Color(128, 0, 64);
            Color err() => (_cycleNumber & 0x1) == 0 ? new Color(0, 128, 128) : new Color(0, 64, 128);

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
                    drillsMaxVolume += drill.Inventory.MaxVolume;
                    drillsCurrentVolume += drill.Inventory.CurrentVolume;
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

        private void EnforceItemPriority(List<InventoryWrapper> group, Statistics stat, MyDefinitionId? topPriority, MyDefinitionId? lowestPriority)
        {
            if (topPriority == null && lowestPriority == null)
                return;

            foreach (var inv in group)
            {
                var items = inv.Inventory.GetItems();
                if (items.Count < 2)
                    continue;

                if (topPriority.HasValue && !MyDefinitionId.FromContent(items[0].Content).Equals(topPriority.Value))
                {
                    for (int i = 1; i < items.Count; ++i)
                    {
                        var item = items[i];
                        if (MyDefinitionId.FromContent(item.Content).Equals(topPriority.Value))
                        {
                            stat.Output.Append("Moving ").Append(topPriority.Value.SubtypeName).Append(" from ")
                                .Append(i + 1).Append(" slot to first slot of ").AppendLine(inv.Block.CustomName);
                            inv.TransferItemTo(inv, i, 0, false, item.Amount);
                            stat.MovementsDone += 1;
                            break;
                        }
                    }
                }

                if (lowestPriority.HasValue && !MyDefinitionId.FromContent(items[items.Count - 1].Content).Equals(lowestPriority.Value))
                {
                    for (int i = items.Count - 2; i >= 0; --i)
                    {
                        var item = items[i];
                        if (MyDefinitionId.FromContent(item.Content).Equals(lowestPriority.Value))
                        {
                            stat.Output.Append("Moving ").Append(lowestPriority.Value.SubtypeName).Append(" from ")
                                .Append(i + 1).Append(" slot to last slot of ").AppendLine(inv.Block.CustomName);
                            inv.TransferItemTo(inv, i, items.Count, false, item.Amount);
                            stat.MovementsDone += 1;
                            break;
                        }
                    }
                }
            }
        }

        private void PrintOnlineStatus(BlockStore bs, Statistics stat)
        {
            var sb = new StringBuilder(4096);

            sb.Append("Grids connected: ").Append(bs.AllGrids.Count);
            if (MyGridOnly)
                sb.Append(" (MGO)");

            sb.AppendLine()
                .Append("Conveyor networks: ")
                .Append(stat.NumberOfNetworks);

            var blocksAffected = bs.Reactors.Count
                + bs.Refineries.Count
                + bs.Drills.Count
                + bs.Turrets.Count
                + bs.OxygenGenerators.Count
                + bs.CargoContainers.Count;

            sb.AppendLine()
                .Append("Blocks affected: ")
                .Append(blocksAffected)
                .AppendLine();

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

            _avgMovements[_cycleNumber & 0x0F] = stat.MovementsDone;
            var samples = Math.Min(_cycleNumber + 1, 0x10);
            double avg = 0;
            for (int i = 0; i < samples; ++i)
                avg += _avgMovements[i];
            avg /= samples;

            sb.Append("Avg. movements: ").Append(avg.ToString("F2")).Append(" (last ").Append(samples).AppendLine(" runs)");

            if (stat.MissingInfo.Count > 0)
                sb.Append("Err: missing volume information for ").AppendLine(String.Join(", ", stat.MissingInfo));

            float cpu = Runtime.CurrentInstructionCount * 100;
            cpu /= Runtime.MaxInstructionCount;
            sb.Append("Complexity limit usage: ").Append(cpu.ToString("F2")).AppendLine("%");

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
            sb.AppendLine(new String(tab));
            ++_cycleNumber;

            Echo(sb.ToString());
        }

        private void WriteOutput(BlockStore bs, Statistics stat)
        {
            const int linesPerDebugScreen = 17;

            if (bs.DebugScreen.Count == 0)
                return;

            bs.DebugScreen.Sort(MyTextPanelNameComparer.Instance);
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

        private void BuildItemInfoDict()
        {
            ItemInfo.Add(OreType, "Akimotoite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Autunite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Carbon", 1M, 0.37M, false, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(OreType, "Carnotite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Cattierite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Chlorargyrite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Cobalt", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Cohenite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Cooperite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Dense Iron", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Deuterium", 1.5M, 0.5M, false, true); // Deuterium Fusion Reactors
            ItemInfo.Add(OreType, "Dolomite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Electrum", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Galena", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Glaucodot", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Gold", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Hapkeite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Heazlewoodite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Helium", 1M, 5.6M, false, true); // (DX11)Mass Driver
            ItemInfo.Add(OreType, "Ice", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Icy Stone", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Iron", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Kamacite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Magnesium", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Naquadah", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(OreType, "Neutronium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(OreType, "Nickel", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Niggliite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Olivine", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Organic", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Petzite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Platinum", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Porphyry", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Pyrite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Quartz", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Silicon", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Silver", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Sinoite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Sperrylite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Stone", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Taenite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Thorium", 1M, 0.9M, false, true); // Tiered Thorium Reactors and Refinery (new)
            ItemInfo.Add(OreType, "Trinium", 1M, 0.37M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(OreType, "Tungsten", 1M, 0.47M, false, true); // (DX11)Mass Driver
            ItemInfo.Add(OreType, "Uraniaurite", 1M, 0.37M, false, true); // Better Stone v6.9.2
            ItemInfo.Add(OreType, "Uranium", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(OreType, "Wadsleyite", 1M, 0.37M, false, true); // Better Stone v6.9.2

            ItemInfo.Add(IngotType, "Carbon", 1M, 0.052M, false, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
            ItemInfo.Add(IngotType, "Cobalt", 1M, 0.112M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Gold", 1M, 0.052M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "HeavyH2OIngot", 2M, 1M, false, true); // Deuterium Fusion Reactors
            ItemInfo.Add(IngotType, "Iron", 1M, 0.127M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "LiquidHelium", 1M, 4.6M, false, true); // (DX11)Mass Driver
            ItemInfo.Add(IngotType, "Magmatite", 100M, 37M, false, true); // Stone and Gravel to Metal Ingots (DX 11)
            ItemInfo.Add(IngotType, "Magnesium", 1M, 0.575M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Naquadah", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(IngotType, "Neutronium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(IngotType, "Nickel", 1M, 0.112M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Platinum", 1M, 0.047M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Silicon", 1M, 0.429M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Silver", 1M, 0.095M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "Stone", 1M, 0.37M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "SuitFuel", 0.0003M, 0.052M, false, true); // Independent Survival
            ItemInfo.Add(IngotType, "SuitRTGPellet", 1.0M, 0.052M, false, true); // Independent Survival
            ItemInfo.Add(IngotType, "ThoriumIngot", 3M, 20M, false, true); // Tiered Thorium Reactors and Refinery (new)
            ItemInfo.Add(IngotType, "Trinium", 1M, 0.052M, false, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(IngotType, "Tungsten", 1M, 0.52M, false, true); // (DX11)Mass Driver
            ItemInfo.Add(IngotType, "Uranium", 1M, 0.052M, false, true); // Space Engineers
            ItemInfo.Add(IngotType, "v2HydrogenGas", 2.1656M, 0.43M, false, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
            ItemInfo.Add(IngotType, "v2OxygenGas", 4.664M, 0.9M, false, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2

            ItemInfo.Add(ComponentType, "AdvancedReactorBundle", 50M, 20M, true, true); // Tiered Thorium Reactors and Refinery (new)
            ItemInfo.Add(ComponentType, "AlloyPlate", 30M, 3M, true, true); // Industrial Centrifuge (stable/dev)
            ItemInfo.Add(ComponentType, "ampHD", 10M, 15.5M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
            ItemInfo.Add(ComponentType, "ArcFuel", 2M, 0.627M, true, true); // Arc Reactor Pack [DX-11 Ready]
            ItemInfo.Add(ComponentType, "ArcReactorcomponent", 312M, 100M, true, true); // Arc Reactor Pack [DX-11 Ready]
            ItemInfo.Add(ComponentType, "AzimuthSupercharger", 10M, 9M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(ComponentType, "BulletproofGlass", 15M, 8M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Canvas", 15M, 8M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Computer", 0.2M, 1M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "ConductorMagnets", 900M, 200M, true, true); // (DX11)Mass Driver
            ItemInfo.Add(ComponentType, "Construction", 8M, 2M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "DenseSteelPlate", 200M, 30M, true, true); // Arc Reactor Pack [DX-11 Ready]
            ItemInfo.Add(ComponentType, "Detector", 5M, 6M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Display", 8M, 6M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Drone", 200M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(ComponentType, "DT-MiniSolarCell", 0.08M, 0.2M, true, true); // }DT{ Modpack
            ItemInfo.Add(ComponentType, "Explosives", 2M, 2M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Girder", 6M, 2M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "GrapheneAerogelFilling", 0.160M, 2.9166M, true, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(ComponentType, "GrapheneNanotubes", 0.01M, 0.1944M, true, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(ComponentType, "GraphenePlate", 6.66M, 0.54M, true, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(ComponentType, "GraphenePowerCell", 25M, 45M, true, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(ComponentType, "GrapheneSolarCell", 4M, 12M, true, true); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(ComponentType, "GravityGenerator", 800M, 200M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "InteriorPlate", 3M, 5M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "LargeTube", 25M, 38M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Magna", 100M, 15M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
            ItemInfo.Add(ComponentType, "MagnetronComponent", 50M, 20M, true, true); // Deuterium Fusion Reactors
            ItemInfo.Add(ComponentType, "Magno", 10M, 5.5M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
            ItemInfo.Add(ComponentType, "Medical", 150M, 160M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "MetalGrid", 6M, 15M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Mg_FuelCell", 15M, 16M, true, true); // Ripptide's CW & EE Continued (DX11)
            ItemInfo.Add(ComponentType, "Motor", 24M, 8M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Naquadah", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(ComponentType, "Neutronium", 500M, 5M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(ComponentType, "PowerCell", 25M, 45M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "productioncontrolcomponent", 40M, 15M, true, true); // (DX11) Double Sided Upgrade Modules
            ItemInfo.Add(ComponentType, "RadioCommunication", 8M, 70M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Reactor", 25M, 8M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Scrap", 2M, 2M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(ComponentType, "SmallTube", 4M, 2M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "SolarCell", 8M, 20M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "SteelPlate", 20M, 3M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Superconductor", 15M, 8M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "Thrust", 40M, 10M, true, true); // Space Engineers
            ItemInfo.Add(ComponentType, "TractorHD", 1500M, 200M, true, true); // (DX11) Maglock Surface Docking Clamps V2.0
            ItemInfo.Add(ComponentType, "Trinium", 100M, 10M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(ComponentType, "Tritium", 3M, 3M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
            ItemInfo.Add(ComponentType, "TVSI_DiamondGlass", 40M, 8M, true, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
            ItemInfo.Add(ComponentType, "WaterTankComponent", 200M, 160M, true, true); // Industrial Centrifuge (stable/dev)
            ItemInfo.Add(ComponentType, "ZPM", 50M, 60M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)

            ItemInfo.Add(AmmoType, "250shell", 128M, 64M, true, true); // [DEPRECATED] CSD Battlecannon
            ItemInfo.Add(AmmoType, "300mmShell_AP", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "300mmShell_HE", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "88hekc", 16M, 16M, true, true); // [DEPRECATED] CSD Battlecannon
            ItemInfo.Add(AmmoType, "88shell", 16M, 16M, true, true); // [DEPRECATED] CSD Battlecannon
            ItemInfo.Add(AmmoType, "900mmShell_AP", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "900mmShell_HE", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "Aden30x113", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "AFmagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "AZ_Missile_AA", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "AZ_Missile200mm", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "BatteryCannonAmmo1", 50M, 50M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "BatteryCannonAmmo2", 200M, 200M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "BigBertha", 3600M, 2800M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "BlasterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "Bofors40mm", 36M, 28M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "ConcreteMix", 2M, 2M, true, true); // Concrete Tool - placing voxels in survival
            ItemInfo.Add(AmmoType, "Eikester_Missile120mm", 25M, 30M, true, true); // (DX11) Small Missile Turret
            ItemInfo.Add(AmmoType, "Eikester_Nuke", 1800M, 8836M, true, true); // (DX11) Nuke Launcher [WiP]
            ItemInfo.Add(AmmoType, "EmergencyBlasterMagazine", 0.45M, 0.2M, true, true); // Independent Survival
            ItemInfo.Add(AmmoType, "Flak130mm", 2M, 3M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "Flak200mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "Flak500mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "HDTCannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "HighDamageGatlingAmmo", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(AmmoType, "ISM_FusionAmmo", 35M, 10M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISM_GrendelAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISM_Hellfire", 45M, 60M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISM_LongbowAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISM_MinigunAmmo", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISMNeedles", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "ISMTracer", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 - BROKEN]
            ItemInfo.Add(AmmoType, "LargeKlingonCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "LargeShipShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "LargeShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "LaserAmmo", 0.001M, 0.01M, true, true); // (DX11)Laser Turret
            ItemInfo.Add(AmmoType, "LaserArrayFlakMagazine", 45M, 30M, true, true); // White Dwarf - Directed Energy Platform [DX11]
            ItemInfo.Add(AmmoType, "LaserArrayShellMagazine", 45M, 120M, true, true); // White Dwarf - Directed Energy Platform [DX11]
            ItemInfo.Add(AmmoType, "Liquid Naquadah", 0.25M, 0.1M, true, true); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(AmmoType, "LittleDavid", 360M, 280M, true, true); // Battle Cannon and Turrets (DX11)
            ItemInfo.Add(AmmoType, "MinotaurAmmo", 360M, 128M, true, true); // (DX11)Minotaur Cannon
            ItemInfo.Add(AmmoType, "Missile200mm", 45M, 60M, true, true); // Space Engineers
            ItemInfo.Add(AmmoType, "MK1CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "MK2CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "MK3CannonMagazineAP", 100M, 100M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "MK3CannonMagazineHE", 300M, 100M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "NATO_25x184mm", 35M, 16M, true, true); // Space Engineers
            ItemInfo.Add(AmmoType, "NATO_5p56x45mm", 0.45M, 0.2M, true, true); // Space Engineers
            ItemInfo.Add(AmmoType, "NiFeDUSlugMagazineLZM", 45M, 50M, true, true); // Large Ship Railguns [Deprecated, link inside]
            ItemInfo.Add(AmmoType, "Phaser2Charge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "Phaser2ChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "PhaserCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "PhaserChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "Plasma_Hydrogen", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "PlasmaCutterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
            ItemInfo.Add(AmmoType, "RB_NATO_125x920mm", 875M, 160M, true, true); // RB Weapon Collection [DX11]
            ItemInfo.Add(AmmoType, "RB_Rocket100mm", 11.25M, 15M, true, true); // RB Weapon Collection [DX11]
            ItemInfo.Add(AmmoType, "RB_Rocket400mm", 180M, 240M, true, true); // RB Weapon Collection [DX11]
            ItemInfo.Add(AmmoType, "RomulanCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "RomulanChargeLarge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "SmallKlingonCharge", 1M, 5M, true, true); // Star Trek Weapon Pack 2.0 (Working Sound)
            ItemInfo.Add(AmmoType, "SmallShotGunAmmo", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "SmallShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "SniperRoundHighSpeedLowDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(AmmoType, "SniperRoundHighSpeedLowDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(AmmoType, "SniperRoundLowSpeedHighDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(AmmoType, "SniperRoundLowSpeedHighDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
            ItemInfo.Add(AmmoType, "TankCannonAmmoSEM4", 35M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
            ItemInfo.Add(AmmoType, "TelionAF_PMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "TelionAMMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
            ItemInfo.Add(AmmoType, "TritiumMissile", 72M, 60M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
            ItemInfo.Add(AmmoType, "TritiumShot", 3M, 3M, true, true); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
            ItemInfo.Add(AmmoType, "TungstenBolt", 4812M, 250M, true, true); // (DX11)Mass Driver
            ItemInfo.Add(AmmoType, "Vulcan20x102", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)

            ItemInfo.Add(GunType, "AngleGrinder2Item", 3M, 20M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "AngleGrinder3Item", 3M, 20M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "AngleGrinder4Item", 3M, 20M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "AngleGrinderItem", 3M, 20M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "AutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "CubePlacerItem", 1M, 1M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "EmergencyBlasterItem", 3M, 14M, true, false); // Independent Survival
            ItemInfo.Add(GunType, "GoodAIRewardPunishmentTool", 0.1M, 1M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "HandDrill2Item", 22M, 25M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "HandDrill3Item", 22M, 25M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "HandDrill4Item", 22M, 25M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "HandDrillItem", 22M, 25M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "P90", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(GunType, "PhysicalConcreteTool", 5M, 15M, true, false); // Concrete Tool - placing voxels in survival
            ItemInfo.Add(GunType, "PreciseAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "RapidFireAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "Staff", 3M, 16M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)
            ItemInfo.Add(GunType, "TritiumAutomaticRifleItem", 6M, 21M, true, false); // [VisSE] [2018] Hydro Reactors & Ice to Oxy Hydro Gasses V2
            ItemInfo.Add(GunType, "UltimateAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "Welder2Item", 5M, 8M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "Welder3Item", 5M, 8M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "Welder4Item", 5M, 8M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "WelderItem", 5M, 8M, true, false); // Space Engineers
            ItemInfo.Add(GunType, "Zat", 3M, 12M, true, false); // [New Version] Stargate Modpack (Server admin block filtering)

            ItemInfo.Add(OxygenType, "GrapheneOxygenBottle", 20M, 100M, true, false); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(OxygenType, "OxygenBottle", 30M, 120M, true, false); // Space Engineers

            ItemInfo.Add(GasType, "GrapheneHydrogenBottle", 20M, 100M, true, false); // Graphene Armor [Core] [Beta]
            ItemInfo.Add(GasType, "HydrogenBottle", 30M, 120M, true, false); // Space Engineers
        }

        private string FindInvGroupName(MyDefinitionId def, IMyInventory inv)
        {
            const string MY_OBJECT_BUILDER = "MyObjectBuilder_";

            ulong[] groupId;
            if (_blockToGroupIdMap.TryGetValue(def, out groupId))
                return _groupIdToNameMap[groupId];

            groupId = new ulong[ItemInfo.ID_LENGTH];
            foreach (var kv in ItemInfo.ItemInfoDict)
            {
                if (inv.CanItemsBeAdded(-1, kv.Key))
                {
                    for (int i = 0; i < ItemInfo.ID_LENGTH; ++i)
                        groupId[i] |= kv.Value.Id[i];
                }
            }

            string result;
            if (_groupIdToNameMap.TryGetValue(groupId, out result))
            {
                _blockToGroupIdMap.Add(def, groupId);
                return result;
            }
            else
            {
                var fullType = def.ToString();
                result = fullType.StartsWith(MY_OBJECT_BUILDER)
                ? '$' + fullType.Substring(MY_OBJECT_BUILDER.Length)
                : fullType;

                _groupIdToNameMap.Add(groupId, result);
                _blockToGroupIdMap.Add(def, groupId);
                return result;
            }
        }

        private class BlockStore
        {
            public readonly Program _program;
            public readonly List<IMyTextPanel> DebugScreen;
            public readonly List<InventoryWrapper> Reactors;
            public readonly List<InventoryWrapper> OxygenGenerators;
            public readonly List<InventoryWrapper> Refineries;
            public readonly List<InventoryWrapper> Drills;
            public readonly List<InventoryWrapper> Turrets;
            public readonly List<InventoryWrapper> CargoContainers;
            public readonly Dictionary<string, HashSet<InventoryWrapper>> Groups;
            public readonly HashSet<InventoryWrapper> AllGroupedInventories;
            public readonly HashSet<IMyCubeGrid> AllGrids;
            public readonly List<IMyLightingBlock> DrillsPayloadLights;
            private System.Text.RegularExpressions.Regex _groupTagPattern;

            public BlockStore(Program program)
            {
                _program = program;
                DebugScreen = new List<IMyTextPanel>();
                Reactors = new List<InventoryWrapper>();
                OxygenGenerators = new List<InventoryWrapper>();
                Refineries = new List<InventoryWrapper>();
                Drills = new List<InventoryWrapper>();
                Turrets = new List<InventoryWrapper>();
                CargoContainers = new List<InventoryWrapper>();
                Groups = new Dictionary<string, HashSet<InventoryWrapper>>();
                AllGroupedInventories = new HashSet<InventoryWrapper>();
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

            private bool CollectContainer(IMyCargoContainer myCargoContainer)
            {
                if (myCargoContainer == null)
                    return false;

                if (!_program.EnableGroups)
                    return true;

                var inv = InventoryWrapper.Create(_program, myCargoContainer);
                if (inv != null)
                {
                    CargoContainers.Add(inv);
                    AllGrids.Add(myCargoContainer.CubeGrid);
                    AddToGroup(inv);
                }
                return true;
            }

            private bool CollectRefinery(IMyRefinery myRefinery)
            {
                if (myRefinery == null)
                    return false;

                if (!_program.EnableRefineries || !myRefinery.UseConveyorSystem)
                    return true;

                var inv = InventoryWrapper.Create(_program, myRefinery);
                if (inv != null)
                {
                    Refineries.Add(inv);
                    AllGrids.Add(myRefinery.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(inv);
                }
                return true;
            }

            private bool CollectReactor(IMyReactor myReactor)
            {
                if (myReactor == null)
                    return false;

                if (!_program.EnableReactors || !myReactor.UseConveyorSystem)
                    return true;

                var inv = InventoryWrapper.Create(_program, myReactor);
                if (inv != null)
                {
                    Reactors.Add(inv);
                    AllGrids.Add(myReactor.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(inv);
                }
                return true;
            }

            private bool CollectDrill(IMyShipDrill myDrill)
            {
                if (myDrill == null)
                    return false;

                if (!_program.EnableDrills || !myDrill.UseConveyorSystem)
                    return true;

                var inv = InventoryWrapper.Create(_program, myDrill);
                if (inv != null)
                {
                    Drills.Add(inv);
                    AllGrids.Add(myDrill.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(inv);
                }
                return true;
            }

            private bool CollectTurret(IMyUserControllableGun myTurret)
            {
                if (myTurret == null)
                    return false;

                if (!_program.EnableTurrets || myTurret is IMyLargeInteriorTurret)
                    return true;

                var inv = InventoryWrapper.Create(_program, myTurret);
                if (inv != null)
                {
                    Turrets.Add(inv);
                    AllGrids.Add(myTurret.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(inv);
                }
                return true;
            }

            private bool CollectOxygenGenerator(IMyGasGenerator myOxygenGenerator)
            {
                if (myOxygenGenerator == null)
                    return false;

                if (!_program.EnableOxygenGenerators)
                    return true;

                var inv = InventoryWrapper.Create(_program, myOxygenGenerator);
                if (inv != null)
                {
                    OxygenGenerators.Add(inv);
                    AllGrids.Add(myOxygenGenerator.CubeGrid);
                    if (_program.EnableGroups)
                        AddToGroup(inv);
                }
                return true;
            }

            private void AddToGroup(InventoryWrapper inv)
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
                    HashSet<InventoryWrapper> tmp;
                    if (!Groups.TryGetValue(dt.Value, out tmp))
                    {
                        tmp = new HashSet<InventoryWrapper>();
                        Groups.Add(dt.Value, tmp);
                    }
                    tmp.Add(inv);
                    AllGroupedInventories.Add(inv);
                }
            }
        }

        private class Statistics
        {
            public readonly StringBuilder Output;
            public readonly HashSet<string> MissingInfo;
            public int NumberOfNetworks;
            public int MovementsDone;
            public string DrillsPayloadStr;
            public bool NotConnectedDrillsFound;
            public bool DrillsVolumeWarning;

            public Statistics()
            {
                Output = new StringBuilder();
                MissingInfo = new HashSet<string>();
            }
        }

        private class ItemInfo
        {
            public const int ID_LENGTH = 4;
            public static readonly Dictionary<MyDefinitionId, ItemInfo> ItemInfoDict;

            static ItemInfo()
            {
                ItemInfoDict = new Dictionary<MyDefinitionId, ItemInfo>(MyDefinitionId.Comparer);
            }

            private ItemInfo(int itemInfoNo, decimal mass, decimal volume, bool hasIntegralAmounts, bool isStackable)
            {
                Id = new ulong[ID_LENGTH];
                Id[itemInfoNo >> 6] = 1ul << (itemInfoNo & 0x3F);
                Mass = mass;
                Volume = volume;
                HasIntegralAmounts = hasIntegralAmounts;
                IsStackable = isStackable;
            }
            
            public readonly ulong[] Id;
            public readonly decimal Mass;
            public readonly decimal Volume;
            public readonly bool HasIntegralAmounts;
            public readonly bool IsStackable;

            public static void Add(string mainType, string subtype,
                decimal mass, decimal volume, bool hasIntegralAmounts, bool isStackable)
            {
                MyDefinitionId key;
                if (!MyDefinitionId.TryParse(mainType, subtype, out key))
                    return;
                var value = new ItemInfo(ItemInfoDict.Count, mass, volume, hasIntegralAmounts, isStackable);
                try
                {
                    ItemInfoDict.Add(key, value);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException("Item info for " + mainType + "/" + subtype + " already added", ex);
                }
            }

            public static ItemInfo Get(Statistics stat, MyDefinitionId key)
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

        private class InventoryWrapperComparer : IComparer<InventoryWrapper>
        {
            public static readonly IComparer<InventoryWrapper> Instance = new InventoryWrapperComparer();

            public int Compare(InventoryWrapper x, InventoryWrapper y)
            {
                return Decimal.Compare(x.Percent, y.Percent);
            }
        }

        private class MyTextPanelNameComparer : IComparer<IMyTextPanel>
        {
            public static readonly IComparer<IMyTextPanel> Instance = new MyTextPanelNameComparer();

            public int Compare(IMyTextPanel x, IMyTextPanel y)
            {
                return String.Compare(x.CustomName, y.CustomName, true);
            }
        }

        private class LongArrayComparer : IEqualityComparer<ulong[]>
        {
            public static readonly IEqualityComparer<ulong[]> Instance = new LongArrayComparer();

            public bool Equals(ulong[] x, ulong[] y)
            {
                //return System.Linq.Enumerable.SequenceEqual<ulong>(x, y);
                if (x.Length != y.Length)
                    return false;
                for (int i = 0; i < x.Length; ++i)
                    if (x[i] != y[i])
                        return false;
                return true;
            }

            public int GetHashCode(ulong[] obj)
            {
                ulong result = 0ul;
                for (int i = 0; i < obj.Length; ++i)
                    result += obj[i];
                return (int)result + (int)(result >> 32);
            }
        }

        private class InventoryWrapper
        {
            public IMyTerminalBlock Block;
            public IMyInventory Inventory;
            public string GroupName;
            public decimal CurrentVolume;
            public decimal MaxVolume;
            public decimal Percent;

            public static InventoryWrapper Create(Program prog, IMyTerminalBlock block)
            {
                var inv = block.GetInventory(0);
                if (inv != null && inv.MaxVolume > 0)
                {
                    var result = new InventoryWrapper();
                    result.Block = block;
                    result.Inventory = inv;
                    result.GroupName = prog.FindInvGroupName(block.BlockDefinition, inv);

                    return result;
                }
                return null;
            }

            public InventoryWrapper LoadVolume()
            {
                CurrentVolume = (decimal)Inventory.CurrentVolume;
                MaxVolume = (decimal)Inventory.MaxVolume;
                return this;
            }

            public InventoryWrapper FilterItems(Statistics stat, Func<IMyInventoryItem, bool> filter)
            {
                decimal volumeBlocked = 0.0M;
                foreach (var item in Inventory.GetItems())
                {
                    if (filter(item))
                        continue;

                    var key = MyDefinitionId.FromContent(item.Content);
                    var data = ItemInfo.Get(stat, key);
                    if (data == null)
                        continue;

                    volumeBlocked += (decimal)item.Amount * data.Volume / 1000M;
                }

                if (volumeBlocked > 0.0M)
                {
                    CurrentVolume -= volumeBlocked;
                    MaxVolume -= volumeBlocked;
                    stat.Output.Append("volumeBlocked ").AppendLine(volumeBlocked.ToString("N6"));
                }
                return this;
            }

            public void CalculatePercent()
            {
                Percent = CurrentVolume / MaxVolume;
            }

            public bool TransferItemTo(InventoryWrapper dst, int sourceItemIndex, int? targetItemIndex = null,
                bool? stackIfPossible = null, VRage.MyFixedPoint? amount = null)
            {
                return Inventory.TransferItemTo(dst.Inventory, sourceItemIndex, targetItemIndex, stackIfPossible, amount);
            }
        }

        private class ConveyorNetwork
        {
            public int No;
            public List<InventoryWrapper> Inventories;

            public ConveyorNetwork(int no)
            {
                No = no;
                Inventories = new List<InventoryWrapper>();
            }
        }

        private class InventoryGroup
        {
            public int No;
            public string Name;
            public List<InventoryWrapper> Inventories;

            public InventoryGroup(int no, string name)
            {
                No = no;
                Name = name;
                Inventories = new List<InventoryWrapper>();
            }
        }
    }

    internal class ReferencedTypes
    {
        private static readonly Type[] ImplicitIngameNamespacesFromTypes = new Type[]
        {
            typeof(Object),
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
