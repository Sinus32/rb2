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

namespace OreProcessingOptimizer.BottlesFiller
{
    public class Program : MyGridProgram
    {
        private const string AmmoType = "MyObjectBuilder_AmmoMagazine";
        private const string ComponentType = "MyObjectBuilder_Component";
        private const string GasType = "MyObjectBuilder_GasContainerObject";
        private const string GunType = "MyObjectBuilder_PhysicalGunObject";
        private const string IngotType = "MyObjectBuilder_Ingot";
        private const string OreType = "MyObjectBuilder_Ore";
        private const string OxygenType = "MyObjectBuilder_OxygenContainerObject";
        private readonly MyDefinitionId _oxygenBottleType, _hydrogenBottleType;
        private int _cycleNumber = 0;

        public Program()
        {
            _oxygenBottleType = MyDefinitionId.Parse(OxygenType + "/OxygenBottle");
            _hydrogenBottleType = MyDefinitionId.Parse(GasType + "/HydrogenBottle");
            //Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            var bs = new BlockStore(this);
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, bs.Collect);

            MoveItems(bs.O2H2Generators, FilledBottles, bs.BottleContainers);
            MoveItems(bs.BottleContainers, EmptyBottles, bs.O2H2Generators);

            PrintOnlineStatus(bs);
        }

        public void Save()
        { }

        private string ContentName(IMyInventoryItem item)
        {
            var fullName = MyDefinitionId.FromContent(item.Content).ToString();
            if (fullName.StartsWith(GasType))
                return "HydrogenBottle";
            if (fullName.StartsWith(OxygenType))
                return "OxygenBottle";
            return fullName;
        }

        private bool EmptyBottles(IMyInventoryItem item)
        {
            var contentType = MyDefinitionId.FromContent(item.Content);
            var dd = item.Content as Sandbox.Common.ObjectBuilders.Definitions.MyObjectBuilder_GasContainerObject;
            if (contentType == _oxygenBottleType || contentType == _hydrogenBottleType)
                Echo("Bottle " + item.Scale + " a " + item.Amount);
            return (contentType == _oxygenBottleType || contentType == _hydrogenBottleType)
                && false /*item.GasLevel < 1.0f*/;
        }

        private bool FilledBottles(IMyInventoryItem item)
        {
            var contentType = MyDefinitionId.FromContent(item.Content);
            return (contentType == _oxygenBottleType || contentType == _hydrogenBottleType)
                && false /*item.GasLevel >= 1.0f*/;
        }

        private void MoveItems(List<InventoryWrapper> source, Func<IMyInventoryItem, bool> filterItems,
            List<InventoryWrapper> target)
        {
            if (source.Count == 0 || target.Count == 0)
                return;

            foreach (var src in source)
            {
                var items = src.Inventory.GetItems();
                for (int i = 0; i < items.Count; ++i)
                {
                    var item = items[i];
                    if (filterItems(item) && MoveItemTo(target, src, i, item))
                        break;
                }
            }
        }

        private bool MoveItemTo(List<InventoryWrapper> target, InventoryWrapper src, int i, IMyInventoryItem item)
        {
            foreach (var wrp in target)
            {
                if (wrp.Inventory.CanAddItemAmount(item, item.Amount))
                {
                    var log = String.Format("Move {0}\n  from {1} to {2}", ContentName(item), src.Block.CustomName, wrp.Block.CustomName);
                    Echo(log);
                    src.Inventory.TransferItemTo(wrp.Inventory, i, stackIfPossible: true);
                    return true;
                }
            }
            return false;
        }

        private void PrintOnlineStatus(BlockStore bs)
        {
            var sb = new StringBuilder(4096);

            var blocksAffected = bs.BottleContainers.Count
                + bs.O2H2Generators.Count;

            sb.AppendLine()
                .Append("Blocks affected: ")
                .Append(blocksAffected)
                .AppendLine();

            sb.Append("Bottle containers: ").AppendLine(bs.BottleContainers.Count.ToString());
            sb.Append("O2/H2 generators: ").AppendLine(bs.O2H2Generators.Count.ToString());

            float cpu = Runtime.CurrentInstructionCount * 100;
            cpu /= Runtime.MaxInstructionCount;
            sb.Append("Complexity limit usage: ").Append(cpu.ToString("F2")).AppendLine("%");

            sb.Append("Last run time: ").Append(Runtime.LastRunTimeMs.ToString("F1")).AppendLine(" ms");

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

        private class BlockStore
        {
            public readonly List<InventoryWrapper> BottleContainers;
            public readonly List<InventoryWrapper> O2H2Generators;
            private readonly Program _program;

            public BlockStore(Program program)
            {
                _program = program;
                BottleContainers = new List<InventoryWrapper>();
                O2H2Generators = new List<InventoryWrapper>();
            }

            public bool Collect(IMyTerminalBlock block)
            {
                var collected = CollectBottleContainer(block as IMyCargoContainer)
                    || CollectO2H2Generator(block as IMyGasGenerator);
                return false;
            }

            private bool CollectBottleContainer(IMyCargoContainer myCargoContainer)
            {
                if (myCargoContainer == null)
                    return false;

                // Container can be on any connected grid and have to have fraze "Bottles" in it's name
                if (myCargoContainer.IsFunctional && myCargoContainer.CustomName.Contains("Bottles"))
                {
                    var inv = InventoryWrapper.Create(myCargoContainer, myCargoContainer.GetInventory());
                    if (inv != null)
                        BottleContainers.Add(inv);
                }

                return true;
            }

            private bool CollectO2H2Generator(IMyGasGenerator myGasGenerator)
            {
                if (myGasGenerator == null)
                    return false;

                // Generator have to be on the same grid as PB and also have to be functional and enabled
                if (myGasGenerator.IsFunctional && myGasGenerator.Enabled && myGasGenerator.CubeGrid == _program.Me.CubeGrid)
                {
                    var inv = InventoryWrapper.Create(myGasGenerator, myGasGenerator.GetInventory());
                    if (inv != null)
                        O2H2Generators.Add(inv);
                }
                return true;
            }
        }

        private class InventoryWrapper
        {
            public IMyTerminalBlock Block;
            public IMyInventory Inventory;

            public static InventoryWrapper Create(IMyTerminalBlock block, IMyInventory inv)
            {
                if (inv != null && inv.MaxVolume > 0)
                {
                    var result = new InventoryWrapper();
                    result.Block = block;
                    result.Inventory = inv;
                    return result;
                }
                return null;
            }

            public bool TransferItemTo(InventoryWrapper dst, int sourceItemIndex, int? targetItemIndex = null,
                bool? stackIfPossible = null, VRage.MyFixedPoint? amount = null)
            {
                return Inventory.TransferItemTo(dst.Inventory, sourceItemIndex, targetItemIndex, stackIfPossible, amount);
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
