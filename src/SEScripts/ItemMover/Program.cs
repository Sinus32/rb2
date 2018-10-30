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

namespace OreProcessingOptimizer.ItemMover
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
private readonly int[] _avgMovements;
private int _cycleNumber = 0;

public Program()
{
    _avgMovements = new int[0x10];
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Main(string argument, UpdateType updateSource)
{
    var bs = new BlockStore(this);
    var stat = new Statistics();
    CollectTerminals(bs, stat);

    var ingotContainers = bs.CargoContainers.Where(q => q.Block.CustomName.StartsWith("Cargo Ingots")).ToList();
    var componentsContainers = bs.CargoContainers.Where(q => q.Block.CustomName.StartsWith("Cargo Components")).ToList();

    MoveItems(stat, bs.RefineriesOutput, FilterIngots, ingotContainers);
    MoveItems(stat, bs.AssemblersInput, FilterIngots, ingotContainers);
    MoveItems(stat, bs.AssemblersOutput, FilterComponents, componentsContainers);

    MoveItems(stat, componentsContainers, FilterIngots, ingotContainers);
    MoveItems(stat, ingotContainers, FilterComponents, componentsContainers);

    var rf = bs.RefineriesInput.Where(q => q.Block.CustomName.StartsWith("Refinery")).ToList();
    var af = bs.RefineriesInput.Where(q => q.Block.CustomName.StartsWith("Arc furnace")).ToList();
    var sc = bs.RefineriesInput.Where(q => q.Block.CustomName.StartsWith("Stone Crusher")).ToList();

    MoveItems(stat, rf, FilterStone, sc);
    MoveItems(stat, rf, FilterIronNickelCobalt, af);

    PrintOnlineStatus(bs, stat);
}

public void Save()
{ }

private BlockStore CollectTerminals(BlockStore bs, Statistics stat)
{
    var blocks = new List<IMyTerminalBlock>();
    Func<IMyTerminalBlock, bool> myTerminalBlockFilter = b => b.IsFunctional && b.CubeGrid == Me.CubeGrid;

    GridTerminalSystem.GetBlocksOfType(blocks, myTerminalBlockFilter);

    foreach (var dt in blocks)
        bs.Collect(dt);

    return bs;
}

private string ContentName(IMyInventoryItem item)
{
    var fullName = MyDefinitionId.FromContent(item.Content).ToString();
    if (fullName.StartsWith(ComponentType))
        return "Comp" + fullName.Substring(ComponentType.Length);
    if (fullName.StartsWith(IngotType))
        return "Ingot" + fullName.Substring(IngotType.Length);
    if (fullName.StartsWith(OreType))
        return "Ore" + fullName.Substring(OreType.Length);
    return fullName;
}

private bool FilterComponents(IMyInventoryItem item)
{
    if (item.Amount < 1)
        return false;
    var def = MyDefinitionId.FromContent(item.Content).ToString();
    return def.StartsWith(ComponentType);
}

private bool FilterIngots(IMyInventoryItem item)
{
    if (item.Amount < 5)
        return false;
    var def = MyDefinitionId.FromContent(item.Content).ToString();
    return def.StartsWith(IngotType);
}

private bool FilterIronNickelCobalt(IMyInventoryItem item)
{
    if (item.Amount < 5)
        return false;
    var def = MyDefinitionId.FromContent(item.Content).ToString();
    return def.Equals(OreType + "/Iron") || def.Equals(OreType + "/Nickel") || def.Equals(OreType + "/Cobalt");
}

private bool FilterStone(IMyInventoryItem item)
{
    if (item.Amount < 5)
        return false;
    var def = MyDefinitionId.FromContent(item.Content).ToString();
    return def.Equals(OreType + "/Stone");
}

private void MoveItems(Statistics stat, List<InventoryWrapper> source, Func<IMyInventoryItem, bool> filterItems,
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
            if (filterItems(item) && MoveItemTo(stat, target, src, i, item))
                break;
        }
    }
}

private bool MoveItemTo(Statistics stat, List<InventoryWrapper> target, InventoryWrapper src, int i, IMyInventoryItem item)
{
    foreach (var wrp in target)
    {
        if (wrp.Inventory.CanAddItemAmount(item, item.Amount))
        {
            var log = String.Format("Move {0}\n  from {1} to {2}", ContentName(item), src.Block.CustomName, wrp.Block.CustomName);
            Echo(log);
            src.Inventory.TransferItemTo(wrp.Inventory, i, stackIfPossible: true);
            stat.MovementsDone += 1;
            return true;
        }
    }
    return false;
}

private void PrintOnlineStatus(BlockStore bs, Statistics stat)
{
    var sb = new StringBuilder(4096);

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
    public readonly Program _program;
    public readonly List<InventoryWrapper> AssemblersInput;
    public readonly List<InventoryWrapper> AssemblersOutput;
    public readonly List<InventoryWrapper> CargoContainers;
    public readonly List<InventoryWrapper> RefineriesInput;
    public readonly List<InventoryWrapper> RefineriesOutput;

    public BlockStore(Program program)
    {
        _program = program;
        AssemblersInput = new List<InventoryWrapper>();
        AssemblersOutput = new List<InventoryWrapper>();
        RefineriesInput = new List<InventoryWrapper>();
        RefineriesOutput = new List<InventoryWrapper>();
        CargoContainers = new List<InventoryWrapper>();
    }

    public void Collect(IMyTerminalBlock block)
    {
        var collected = CollectContainer(block as IMyCargoContainer)
            || CollectRefinery(block as IMyRefinery)
            || CollectAssembler(block as IMyAssembler);
    }

    private bool CollectAssembler(IMyAssembler myAssembler)
    {
        if (myAssembler == null)
            return false;

        if (!myAssembler.UseConveyorSystem)
            return true;

        if (!myAssembler.IsProducing)
        {
            var inv0 = InventoryWrapper.Create(_program, myAssembler, myAssembler.InputInventory);
            if (inv0 != null)
                AssemblersInput.Add(inv0);
        }

        if (myAssembler.Mode == MyAssemblerMode.Assembly)
        {
            var inv1 = InventoryWrapper.Create(_program, myAssembler, myAssembler.OutputInventory);
            if (inv1 != null)
                AssemblersOutput.Add(inv1);
        }

        return true;
    }

    private bool CollectContainer(IMyCargoContainer myCargoContainer)
    {
        if (myCargoContainer == null)
            return false;

        var inv = InventoryWrapper.Create(_program, myCargoContainer, myCargoContainer.GetInventory());
        if (inv != null)
            CargoContainers.Add(inv);

        return true;
    }

    private bool CollectRefinery(IMyRefinery myRefinery)
    {
        if (myRefinery == null)
            return false;

        if (!myRefinery.UseConveyorSystem)
            return true;

        var inv0 = InventoryWrapper.Create(_program, myRefinery, myRefinery.InputInventory);
        if (inv0 != null)
            RefineriesInput.Add(inv0);

        var inv1 = InventoryWrapper.Create(_program, myRefinery, myRefinery.OutputInventory);
        if (inv1 != null)
            RefineriesOutput.Add(inv1);

        return true;
    }
}

private class InventoryWrapper
{
    public IMyTerminalBlock Block;
    public IMyInventory Inventory;

    public static InventoryWrapper Create(Program prog, IMyTerminalBlock block, IMyInventory inv)
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

private class Statistics
{
    public readonly HashSet<string> MissingInfo;
    public int MovementsDone;

    public Statistics()
    {
        MissingInfo = new HashSet<string>();
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
