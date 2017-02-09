using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game;
using VRageMath;

namespace SEScripts.ResourceExchanger2_1_168
{
    public class Program : MyGridProgram
    {
/// Resource Exchanger version 2.1.10 2017-02-09 for SE 1.172+
/// Made by Sinus32
/// http://steamcommunity.com/sharedfiles/filedetails/546221822

/** Configuration section starts here. ******************************************/

/// Optional name of a group of blocks that will be affected by the script.
/// By default all blocks connected to grid are processed.
/// You can use this variable to limit the script to affect only certain blocks.
public string MANAGED_BLOCKS_GROUP = null;

/// Limit affected blocks to only these that are connected to the same ship/station as the
/// the programmable block. Set to true if blocks on ships connected by connectors
/// or rotors should not be affected.
public bool MY_GRID_ONLY = false;

/// Set this variable to false to disable exchanging uranium between reactors.
public bool ENABLE_BALANCING_REACTORS = true;

/// Set this variable to false to disable exchanging ore
/// between refineries and arc furnaces.
public bool ENABLE_DISTRIBUTING_ORE_IN_REFINERIES = true;

/// Set this variable to false to disable exchanging ore between drills and
/// to disable processing lights that indicates how much free space left in drills.
public bool ENABLE_DISTRIBUTING_ORE_IN_DRILLS = true;

/// Set this variable to false to disable exchanging ammunition between turrets and launchers.
public bool ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS = true;

/// Set this variable to false to disable exchanging ice (and only ice - not bottles)
/// between oxygen generators.
public bool ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS = true;

/// Set this variable to false to disable exchanging items in blocks of custom groups.
public bool ENABLE_EXCHANGING_ITEMS_IN_GROUPS = true;

/// Group of wide LCD screens that will act as debugger output for this script.
/// You can name this screens as you wish, but pay attention that
/// they will be used in alphabetical order according to their names.
public const string DISPLAY_LCD_GROUP = "Resource exchanger output";

/// Name of a group of lights that will be used as indicators of space left in drills.
/// Both Interior Light and Spotlight are supported.
/// The lights will change colors to tell you how much free space left:
/// White - All drills are connected to each other and they are empty.
/// Yellow - Drills are full in a half.
/// Red - Drills are almost full (95%).
/// Purple - Less than WARNING_LEVEL_IN_KILOLITERS_LEFT m3 of free space left.
/// Cyan - Some drills are not connected to each other.
/// You can change this colors a few lines below
/// 
/// Set this variable to null to disable this feature
public string DRILLS_PAYLOAD_LIGHTS_GROUP = "Payload indicators";

/// Amount of free space left in drills when the lights turn into purple
/// Measured in cubic meters
/// Default is 5 with means the lights from DRILLS_PAYLOAD_LIGHTS_GROUP will turn
/// into purple when there will be only 5,000 liters of free space
/// left in drills (or less)
public int WARNING_LEVEL_IN_CUBIC_METERS_LEFT = 5;

/// Configuration of lights colors
/// Values are in RGB format
/// Minimum value of any color component is 0, and maximum is 255
public Color COLOR_WHEN_DRILLS_ARE_EMPTY = new Color(255, 255, 255);
public Color COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF = new Color(255, 255, 0);
public Color COLOR_WHEN_DRILLS_ARE_ALMOST_FULL = new Color(255, 0, 0);
public Color FIRST_WARNING_COLOR_WHEN_DRILLS_ARE_FULL = new Color(128, 0, 128);
public Color SECOND_WARNING_COLOR_WHEN_DRILLS_ARE_FULL = new Color(128, 0, 64);
public Color FIRST_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED = new Color(0, 128, 128);
public Color SECOND_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED = new Color(0, 64, 128);

/// Number of lines displayed on single LCD wide panel from DISPLAY_LCD_GROUP.
/// The default value of 17 is designated for panels with font size set to 1.0
public const int LINES_PER_DEBUG_SCREEN = 17;

/// Top priority item type to process in refineries and/or arc furnaces.
/// The script will move an item of this type to the first slot of a refinery or arc
/// furnace if it find that item in the refinery (or arc furnace) processing queue.
/// You can find definitions of other materials in line 1273 and below.
/// Set this variable to null to disable this feature
public readonly string TopRefineryPriority = IRON;

/// Lowest priority item type to process in refineries and/or arc furnaces.
/// The script will move an item of this type to the last slot of a refinery or arc
/// furnace if it find that item in the refinery (or arc furnace) processing queue.
/// You can find definitions of other materials in line 1273 and below.
/// Set this variable to null to disable this feature
public readonly string LowestRefineryPriority = STONE;

/// Regular expression used to recognize groups
public static readonly System.Text.RegularExpressions.Regex GROUP_TAG_PATTERN
    = new System.Text.RegularExpressions.Regex(@"\bGR\d{1,3}\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

/* Configuration section ends here. *********************************************/
// The rest of the code does magic.

public Program()
{
    BuildItemInfoDict();
    _blockTypeInfoDict = new Dictionary<string, BlockTypeInfo>();
}

public void Save()
{ }

private StringBuilder _output;
private List<IMyTextPanel> _debugScreen;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _reactorsInventories;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _oxygenGeneratorsInventories;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _refineriesInventories;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _drillsInventories;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _turretsInventories;
private List<VRage.Game.ModAPI.Ingame.IMyInventory> _cargoContainersInventories;
private Dictionary<string, HashSet<VRage.Game.ModAPI.Ingame.IMyInventory>> _groups;
private HashSet<VRage.Game.ModAPI.Ingame.IMyInventory> _allGroupedInventories;
private List<IMyLightingBlock> _drillsPayloadLights;
public Dictionary<ItemType, ItemInfo> _itemInfoDict;
public Dictionary<string, BlockTypeInfo> _blockTypeInfoDict;
private VRage.MyFixedPoint _drillsMaxVolume;
private VRage.MyFixedPoint _drillsCurrentVolume;
private bool _notConnectedDrillsFound;
private int _cycleNumber = 0;
private bool _isInitialized = false;

public void Main(string argument)
{
    bool hasScreen = CollectTerminals();

    if (!_isInitialized)
    {
        DoInitialization();
    }
    else
    {
        ProcessReactors();
        ProcessRefineries();
        ProcessDrills();
        ProcessDrillsLights();
        ProcessTurrets();
        ProcessOxygenGenerators();
        ProcessGroups();
    }

    PrintOnlineStatus();

    if (hasScreen)
    {
        WriteOutput();
    }
}

private void WriteOutput()
{
    _debugScreen.Sort(MyTextPanelNameComparer.Instance);
    string[] lines = _output.ToString().Split(new char[] { '\n' },
        StringSplitOptions.RemoveEmptyEntries);

    int totalScreens = lines.Length + LINES_PER_DEBUG_SCREEN - 1;
    totalScreens /= LINES_PER_DEBUG_SCREEN;

    for (int i = 0; i < _debugScreen.Count; ++i)
    {
        var screen = _debugScreen[i];
        var sb = new StringBuilder();
        int firstLine = i * LINES_PER_DEBUG_SCREEN;
        for (int j = 0; j < LINES_PER_DEBUG_SCREEN && firstLine + j < lines.Length; ++j)
            sb.AppendLine(lines[firstLine + j].Trim());
        screen.WritePublicText(sb.ToString());
        screen.ShowPublicTextOnScreen();
    }
}

private bool CollectTerminals()
{
    _output = new StringBuilder();
    _debugScreen = null;
    _reactorsInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _oxygenGeneratorsInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _refineriesInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _drillsInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _turretsInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _cargoContainersInventories = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _groups = new Dictionary<string, HashSet<VRage.Game.ModAPI.Ingame.IMyInventory>>();
    _allGroupedInventories = new HashSet<VRage.Game.ModAPI.Ingame.IMyInventory>();
    _drillsPayloadLights = new List<IMyLightingBlock>();
    _drillsMaxVolume = 0;
    _drillsCurrentVolume = 0;
    _notConnectedDrillsFound = false;

    var blocks = new List<IMyTerminalBlock>();

    if (String.IsNullOrEmpty(MANAGED_BLOCKS_GROUP))
    {
        GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectContainer((IMyCargoContainer)blocks[i]);

        GridTerminalSystem.GetBlocksOfType<IMyRefinery>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectRefinery((IMyRefinery)blocks[i]);

        GridTerminalSystem.GetBlocksOfType<IMyReactor>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectReactor((IMyReactor)blocks[i]);

        GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectDrill((IMyShipDrill)blocks[i]);

        GridTerminalSystem.GetBlocksOfType<IMyUserControllableGun>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectTurret((IMyUserControllableGun)blocks[i]);

        GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(blocks, MyTerminalBlockFilter);
        for (int i = 0; i < blocks.Count; ++i)
            CollectOxygenGenerator((IMyGasGenerator)blocks[i]);
    }
    else
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(MANAGED_BLOCKS_GROUP);
        if (group == null)
        {
            _output.Append("Error: a group ");
            _output.Append(MANAGED_BLOCKS_GROUP);
            _output.AppendLine(" has not been found");
        }
        else
        {
            group.GetBlocksOfType<IMyCargoContainer>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectContainer((IMyCargoContainer)blocks[i]);

            group.GetBlocksOfType<IMyRefinery>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectRefinery((IMyRefinery)blocks[i]);

            group.GetBlocksOfType<IMyReactor>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectReactor((IMyReactor)blocks[i]);

            group.GetBlocksOfType<IMyShipDrill>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectDrill((IMyShipDrill)blocks[i]);

            group.GetBlocksOfType<IMyUserControllableGun>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectTurret((IMyUserControllableGun)blocks[i]);

            group.GetBlocksOfType<IMyGasGenerator>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                CollectOxygenGenerator((IMyGasGenerator)blocks[i]);
        }
    }

    if (!String.IsNullOrEmpty(DISPLAY_LCD_GROUP))
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(DISPLAY_LCD_GROUP);
        if (group != null)
        {
            _debugScreen = new List<IMyTextPanel>();
            group.GetBlocksOfType<IMyTextPanel>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                _debugScreen.Add((IMyTextPanel)blocks[i]);
        }
    }

    if (!String.IsNullOrEmpty(DRILLS_PAYLOAD_LIGHTS_GROUP))
    {
        var group = GridTerminalSystem.GetBlockGroupWithName(DRILLS_PAYLOAD_LIGHTS_GROUP);
        if (group != null)
        {
            group.GetBlocksOfType<IMyLightingBlock>(blocks, MyTerminalBlockFilter);
            for (int i = 0; i < blocks.Count; ++i)
                _drillsPayloadLights.Add((IMyLightingBlock)blocks[i]);
        }
    }

    _output.Append("Resource exchanger. Blocks managed:");
    _output.Append(" reactors: ");
    _output.Append(_reactorsInventories.Count);
    _output.Append(", refineries: ");
    _output.Append(_refineriesInventories.Count);
    _output.AppendLine(",");
    _output.Append("oxygen gen.: ");
    _output.Append(_oxygenGeneratorsInventories.Count);
    _output.Append(", drills: ");
    _output.Append(_drillsInventories.Count);
    _output.Append(", turrets: ");
    _output.Append(_turretsInventories.Count);
    _output.Append(", cargo cont.: ");
    _output.Append(_cargoContainersInventories.Count);
    _output.Append(", custom groups: ");
    _output.Append(_groups.Count);
    _output.AppendLine();

    return _debugScreen != null && _debugScreen.Count > 0;
}

private bool MyTerminalBlockFilter(IMyTerminalBlock myTerminalBlock)
{
    return myTerminalBlock.IsFunctional && (MY_GRID_ONLY || myTerminalBlock.CubeGrid == Me.CubeGrid);
}

private void CollectContainer(IMyCargoContainer myCargoContainer)
{
    var inv = myCargoContainer.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _cargoContainersInventories.Add(inv);
    AddToGroup(myCargoContainer, inv);
}

private void CollectRefinery(IMyRefinery myRefinery)
{
    if (!myRefinery.UseConveyorSystem)
        return;

    var inv = myRefinery.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _refineriesInventories.Add(inv);
    AddToGroup(myRefinery, inv);
}

private void CollectReactor(IMyReactor myReactor)
{
    if (!myReactor.UseConveyorSystem)
        return;

    var inv = myReactor.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _reactorsInventories.Add(inv);
    AddToGroup(myReactor, inv);
}

private void CollectDrill(IMyShipDrill myDrill)
{
    if (!myDrill.UseConveyorSystem)
        return;

    var inv = myDrill.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _drillsInventories.Add(inv);
    AddToGroup(myDrill, inv);

    _drillsMaxVolume += inv.MaxVolume;
    _drillsCurrentVolume += inv.CurrentVolume;
}

private void CollectTurret(IMyUserControllableGun myTurret)
{
    if (myTurret is SpaceEngineers.Game.ModAPI.Ingame.IMyLargeInteriorTurret)
        return;

    var inv = myTurret.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _turretsInventories.Add(inv);
    AddToGroup(myTurret, inv);
}

private void CollectOxygenGenerator(IMyGasGenerator myOxygenGenerator)
{
    var inv = myOxygenGenerator.GetInventory(0);
    if (inv == null || inv.MaxVolume == 0)
        return;

    _oxygenGeneratorsInventories.Add(inv);
    AddToGroup(myOxygenGenerator, inv);
}

private void AddToGroup(IMyTerminalBlock myTerminalBlock, VRage.Game.ModAPI.Ingame.IMyInventory inv)
{
    var groupNames = GROUP_TAG_PATTERN.Matches(myTerminalBlock.CustomName);
    var it = groupNames.GetEnumerator();
    while (it.MoveNext())
    {
        var dt = (System.Text.RegularExpressions.Match)it.Current;
        HashSet<VRage.Game.ModAPI.Ingame.IMyInventory> tmp;
        if (!_groups.TryGetValue(dt.Value, out tmp))
        {
            tmp = new HashSet<VRage.Game.ModAPI.Ingame.IMyInventory>();
            _groups.Add(dt.Value, tmp);
        }
        tmp.Add(inv);
        _allGroupedInventories.Add(inv);
    }
}

private List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> FindConveyorNetworks(List<VRage.Game.ModAPI.Ingame.IMyInventory> inventories, bool excludeGroups)
{
    var result = new List<List<VRage.Game.ModAPI.Ingame.IMyInventory>>();

    for (int i = 0; i < inventories.Count; ++i)
    {
        var inv = inventories[i];
        if (excludeGroups && _allGroupedInventories.Contains(inv))
            continue;

        bool add = true;
        for (int j = 0; j < result.Count; ++j)
        {
            var network = result[j];
            if (network[0].IsConnectedTo(inv) && inv.IsConnectedTo(network[0]))
            {
                network.Add(inv);
                add = false;
                break;
            }
        }

        if (add)
        {
            var network = new List<VRage.Game.ModAPI.Ingame.IMyInventory>();
            network.Add(inv);
            result.Add(network);
        }
    }

    return result;
}

private List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> DivideByBlockType(List<VRage.Game.ModAPI.Ingame.IMyInventory> list,
    out List<string> groupNames)
{
    groupNames = new List<string>();
    var result = new List<List<VRage.Game.ModAPI.Ingame.IMyInventory>>(list.Count);
    var groupMap = new Dictionary<long[], int>(_longArrayComparer);

    for (int i = 0; i < list.Count; ++i)
    {
        string groupName;
        long[] acceptedItems = DetermineGroup(list[i], out groupName);

        int groupId;
        if (groupMap.TryGetValue(acceptedItems, out groupId))
        {
            result[groupId].Add(list[i]);
        }
        else
        {
            groupId = result.Count;
            groupMap.Add(acceptedItems, groupId);
            var l = new List<VRage.Game.ModAPI.Ingame.IMyInventory>(list.Count);
            l.Add(list[i]);
            result.Add(l);
            groupNames.Add(groupName);
        }
    }
    return result;
}

public const string MY_OBJECT_BUILDER = "MyObjectBuilder_";
public static readonly System.Text.RegularExpressions.Regex COMMON_WORDS
    = new System.Text.RegularExpressions.Regex("(Full|Large|Heavy|Medium|Light|Small|Tiny|Block|Turbo|NoAi|Stack)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
public const decimal SMALL_NUMBER_THAT_DOES_NOT_MATTER = 0.000003M;
public static LongArrayComparer _longArrayComparer = new LongArrayComparer();

private long[] DetermineGroup(VRage.Game.ModAPI.Ingame.IMyInventory myInventory, out string groupName)
{
    var owner = (VRage.Game.ModAPI.Ingame.IMyCubeBlock)myInventory.Owner;
    var fullType = owner.BlockDefinition.ToString();
    BlockTypeInfo blockTypeInfo;
    if (!_blockTypeInfoDict.TryGetValue(fullType, out blockTypeInfo))
    {
        blockTypeInfo = CreateBlockTypeInfo(fullType, myInventory);
        _blockTypeInfoDict.Add(fullType, blockTypeInfo);
    }

    groupName = blockTypeInfo.GroupName;
    return blockTypeInfo.AcceptedItems;
}

private void DoInitialization()
{
    int num = 0;
    bool isDone = DoInitialization(ref num, _reactorsInventories);
    isDone &= DoInitialization(ref num, _oxygenGeneratorsInventories);
    isDone &= DoInitialization(ref num, _refineriesInventories);
    isDone &= DoInitialization(ref num, _drillsInventories);
    isDone &= DoInitialization(ref num, _turretsInventories);
    isDone &= DoInitialization(ref num, _cargoContainersInventories);
    isDone &= DoInitialization(ref num, _allGroupedInventories);

    if (isDone)
    {
        Echo("Initialization completed.");
        Echo("Ready to work.");
        _output.AppendLine("Initialization completed.");
        _output.AppendLine("Ready to work.");
        _isInitialized = true;
    }
    else
    {
        Echo("Initialization in progress...");
        Echo("Please, run the block again.");
        _output.AppendLine("Initialization will be continued...");
    }
}

private bool DoInitialization(ref int num, IEnumerable<VRage.Game.ModAPI.Ingame.IMyInventory> _inventories)
{
    var it = _inventories.GetEnumerator();

    while (it.MoveNext())
    {
        var myInventory = it.Current;
        var owner = (VRage.Game.ModAPI.Ingame.IMyCubeBlock)myInventory.Owner;
        var fullType = owner.BlockDefinition.ToString();
        BlockTypeInfo blockTypeInfo;
        if (!_blockTypeInfoDict.TryGetValue(fullType, out blockTypeInfo))
        {
            if (num >= 48)
                return false;

            blockTypeInfo = CreateBlockTypeInfo(fullType, myInventory);
            _blockTypeInfoDict.Add(fullType, blockTypeInfo);
            _output.Append(fullType);
            _output.AppendLine(" initialized");
            num += 1;
        }
    }

    return true;
}

private BlockTypeInfo CreateBlockTypeInfo(string fullType, VRage.Game.ModAPI.Ingame.IMyInventory myInventory)
{
    var result = new BlockTypeInfo();

    if (fullType.StartsWith(MY_OBJECT_BUILDER))
        result.GroupName = '$' + fullType.Substring(MY_OBJECT_BUILDER.Length);
    else
        result.GroupName = fullType;

    result.GroupName = COMMON_WORDS.Replace(result.GroupName, "").Trim(' ', '-', '_');
    result.BlockDefinition = fullType;
    result.AcceptedItems = new long[1 + _itemInfoDict.Count / 32];

    //var dbef = new VRage.ObjectBuilders.SerializableDefinitionId(null, "fdgfd");

    //var it = _itemInfoDict.GetEnumerator();
    //while (it.MoveNext())
    //{
    //    var def = new VRage.ObjectBuilders.SerializableDefinitionId(VRage.ObjectBuilders.MyObjectBuilderType.Parse(it.Current.Key.MainType), it.Current.Key.Subtype);
    //    if (myInventory.CanItemsBeAdded(-1, def))
    //    {
    //        for (int i = 0; i < it.Current.Value.Id.Length; ++i)
    //            result.AcceptedItems[i] += it.Current.Value.Id[i];
    //    }
    //}
    result.AcceptedItems[0] = _blockTypeInfoDict.Count + 1;

    return result;
}

private void ProcessReactors()
{
    if (!ENABLE_BALANCING_REACTORS)
    {
        _output.AppendLine("Balancing reactors is disabled.");
        Echo("Balancing reactors: OFF");
        return;
    }

    Echo("Balancing reactors: ON");

    if (_reactorsInventories.Count < 2)
    {
        _output.AppendLine("Balancing reactors. Not enough reactors found. Nothing to do.");
        return;
    }

    List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(_reactorsInventories, true);

    _output.Append("Balancing reactors. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    for (int i = 0; i < conveyorNetworks.Count; ++i)
    {
        List<string> groupNames;
        List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> groups = DivideByBlockType(conveyorNetworks[i], out groupNames);

        for (int j = 0; j < groups.Count; ++j)
        {
            BalanceInventories(groups[j], i, j, groupNames[j]);
        }
    }
}

private void ProcessRefineries()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_REFINERIES)
    {
        _output.AppendLine("Balancing refineries is disabled.");
        Echo("Balancing refineries: OFF");
        return;
    }

    Echo("Balancing refineries: ON");

    if (_refineriesInventories.Count < 2)
    {
        _output.AppendLine("Balancing refineries. Not enough refineries found. Nothing to do.");
        return;
    }

    List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(_refineriesInventories, true);

    _output.Append("Balancing refineries. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    for (int i = 0; i < conveyorNetworks.Count; ++i)
    {
        List<string> groupNames;
        List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> groups = DivideByBlockType(conveyorNetworks[i], out groupNames);

        for (int j = 0; j < groups.Count; ++j)
        {
            BalanceInventories(groups[j], i, j, groupNames[j]);
            EnforceItemPriority(groups[j], i, j, OreType, TopRefineryPriority,
                OreType, LowestRefineryPriority);
        }
    }
}

private void ProcessDrills()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_DRILLS)
    {
        _output.AppendLine("Balancing drills is disabled.");
        Echo("Balancing drills: OFF");
        return;
    }

    Echo("Balancing drills: ON");

    if (_drillsInventories.Count < 2)
    {
        _output.AppendLine("Balancing drills. Not enough drills found. Nothing to do.");
        return;
    }

    List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(_drillsInventories, true);
    _notConnectedDrillsFound = conveyorNetworks.Count > 1;

    _output.Append("Balancing drills. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    for (int i = 0; i < conveyorNetworks.Count; ++i)
    {
        BalanceInventories(conveyorNetworks[i], i, 0, "drills");
    }
}

private void ProcessDrillsLights()
{
    if (!ENABLE_DISTRIBUTING_ORE_IN_DRILLS)
    {
        _output.AppendLine("Setting color of drills payload indicators is disabled.");
        return;
    }

    if (_drillsPayloadLights.Count == 0)
    {
        _output.AppendLine("Setting color of drills payload indicators. Not enough lights found. Nothing to do.");
        return;
    }

    _output.AppendLine("Setting color of drills payload indicators.");

    Color color;

    if (_notConnectedDrillsFound)
    {
        _output.AppendLine("Not all drills are connected.");

        if (_cycleNumber % 2 == 0)
            color = FIRST_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED;
        else
            color = SECOND_ERROR_COLOR_WHEN_DRILLS_ARE_NOT_CONNECTED;
        Echo("Some drills are not connected");
    }
    else
    {
        float p;
        if (_drillsMaxVolume > 0)
        {
            p = (float)_drillsCurrentVolume * 1000.0f;
            p /= (float)_drillsMaxVolume;

            _output.Append("Drills space usage: ");
            var percentString = (p / 10.0f).ToString("F1");
            _output.Append(percentString);
            _output.AppendLine("%");
            string echo = String.Concat("Drils payload: ", percentString, "%");

            if ((_drillsMaxVolume - _drillsCurrentVolume) < WARNING_LEVEL_IN_CUBIC_METERS_LEFT)
            {
                if (_cycleNumber % 2 == 0)
                {
                    color = FIRST_WARNING_COLOR_WHEN_DRILLS_ARE_FULL;
                    echo += " ! !";
                }
                else
                {
                    color = SECOND_WARNING_COLOR_WHEN_DRILLS_ARE_FULL;
                    echo += "  ! !";
                }
            }
            else
            {
                Color c1, c2;
                float m1, m2;

                if (p < 500.0f)
                {
                    c1 = COLOR_WHEN_DRILLS_ARE_EMPTY;
                    c2 = COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF;
                    m2 = p / 500.0f;
                    m1 = 1.0f - m2;
                }
                else
                {
                    c1 = COLOR_WHEN_DRILLS_ARE_ALMOST_FULL;
                    c2 = COLOR_WHEN_DRILLS_ARE_FULL_IN_A_HALF;
                    m1 = (p - 500.0f) / 450.0f;
                    if (m1 > 1.0f)
                        m1 = 1.0f;
                    m2 = 1.0f - m1;
                }

                float r = c1.R * m1 + c2.R * m2;
                float g = c1.G * m1 + c2.G * m2;
                float b = c1.B * m1 + c2.B * m2;

                if (r > 255.0f) r = 255.0f;
                else if (r < 0.0f) r = 0.0f;

                if (g > 255.0f) g = 255.0f;
                else if (g < 0.0f) g = 0.0f;

                if (b > 255.0f) b = 255.0f;
                else if (b < 0.0f) b = 0.0f;

                color = new Color((int)r, (int)g, (int)b);
            }

            Echo(echo);
        }
        else
        {
            color = COLOR_WHEN_DRILLS_ARE_EMPTY;
        }
    }

    _output.Append("Drills payload indicators lights color: ");
    _output.Append(color);
    _output.AppendLine();

    for (int i = 0; i < _drillsPayloadLights.Count; ++i)
    {
        IMyLightingBlock light = _drillsPayloadLights[i];
        Color currentColor = light.GetValue<Color>("Color");
        if (currentColor != color)
        {
            light.SetValue<Color>("Color", color);
        }
    }

    _output.Append("Color of ");
    _output.Append(_drillsPayloadLights.Count);
    _output.AppendLine(" drills payload indicators has been set.");
}

private void ProcessTurrets()
{
    if (!ENABLE_EXCHANGING_AMMUNITION_IN_TURRETS)
    {
        _output.AppendLine("Exchanging ammunition in turrets is disabled.");
        Echo("Exchanging ammunition: OFF");
        return;
    }

    Echo("Exchanging ammunition: ON");

    if (_turretsInventories.Count < 2)
    {
        _output.AppendLine("Balancing turrets. Not enough turrets found. Nothing to do.");
        return;
    }

    List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(_turretsInventories, true);

    _output.Append("Balancing turrets. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    for (int i = 0; i < conveyorNetworks.Count; ++i)
    {
        List<string> groupNames;
        List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> groups = DivideByBlockType(conveyorNetworks[i], out groupNames);

        for (int j = 0; j < groups.Count; ++j)
        {
            BalanceInventories(groups[j], i, j, groupNames[j]);
        }
    }
}

private void ProcessOxygenGenerators()
{
    if (!ENABLE_EXCHANGING_ICE_IN_OXYGEN_GENERATORS)
    {
        _output.AppendLine("Exchanging ice in oxygen generators is disabled.");
        Echo("Exchanging ice: OFF");
        return;
    }

    Echo("Exchanging ice: ON");

    if (_oxygenGeneratorsInventories.Count < 2)
    {
        _output.AppendLine("Balancing ice in oxygen generators. Not enough generators found. Nothing to do.");
        return;
    }

    List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(_oxygenGeneratorsInventories, true);

    _output.Append("Balancing oxygen generators. Conveyor networks found: ");
    _output.Append(conveyorNetworks.Count);
    _output.AppendLine();

    for (int i = 0; i < conveyorNetworks.Count; ++i)
    {
        BalanceInventories(conveyorNetworks[i], i, 0, "oxygen generators", OreType, ICE);
    }
}

private void ProcessGroups()
{
    if (!ENABLE_EXCHANGING_ITEMS_IN_GROUPS)
    {
        _output.AppendLine("Exchanging items in groups is disabled.");
        Echo("Exchanging items in groups: OFF");
        return;
    }

    Echo("Exchanging items in groups: ON");

    if (_groups.Count < 1)
    {
        _output.AppendLine("Exchanging items in groups. No groups found. Nothing to do.");
        return;
    }

    var it = _groups.GetEnumerator();
    while (it.MoveNext())
    {
        var blocks = new List<VRage.Game.ModAPI.Ingame.IMyInventory>(it.Current.Value);
        List<List<VRage.Game.ModAPI.Ingame.IMyInventory>> conveyorNetworks = FindConveyorNetworks(blocks, false);

        _output.Append("Balancing custom group '");
        _output.Append(it.Current.Key);
        _output.Append("'. Conveyor networks found: ");
        _output.Append(conveyorNetworks.Count);
        _output.AppendLine();

        for (int i = 0; i < conveyorNetworks.Count; ++i)
        {
            BalanceInventories(conveyorNetworks[i], i, 0, it.Current.Key);
        }
    }
}

private void PrintOnlineStatus()
{
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
    Echo(new String(tab));
    ++_cycleNumber;
}

private void EnforceItemPriority(List<VRage.Game.ModAPI.Ingame.IMyInventory> group,
    int networkNumber, int groupNumber,
    string topPriorityType, string topPriority,
    string lowestPriorityType, string lowestPriority)
{
    if (topPriority == null && lowestPriority == null)
        return;

    for (int i = 0; i < group.Count; ++i)
    {
        var inv = group[i];
        var items = inv.GetItems();
        if (items.Count < 2)
            continue;

        if (topPriority != null)
        {
            var firstItemType = items[0].Content;
            if (firstItemType.TypeId.ToString() != topPriorityType
                || firstItemType.SubtypeName != topPriority)
            {
                int topPriorityItemIndex = 1;
                VRage.Game.ModAPI.Ingame.IMyInventoryItem item = null;
                while (topPriorityItemIndex < items.Count)
                {
                    item = items[topPriorityItemIndex];
                    if (item.Content.TypeId.ToString() == topPriorityType
                        && item.Content.SubtypeName == topPriority)
                        break;
                    ++topPriorityItemIndex;
                }

                if (topPriorityItemIndex < items.Count)
                {
                    _output.Append("Moving ");
                    _output.Append(topPriority);
                    _output.Append(" from ");
                    _output.Append(topPriorityItemIndex + 1);
                    _output.Append(" slot to first slot of ");
                    _output.Append(((IMyTerminalBlock)inv.Owner).CustomName);
                    _output.AppendLine();
                    inv.TransferItemTo(inv, topPriorityItemIndex, 0, false, item.Amount);
                }
            }
        }

        if (lowestPriority != null)
        {
            var lastItemType = items[items.Count - 1].Content;
            if (lastItemType.TypeId.ToString() != lowestPriorityType
                || lastItemType.SubtypeName != lowestPriority)
            {
                int lowestPriorityItemIndex = items.Count - 2;
                VRage.Game.ModAPI.Ingame.IMyInventoryItem item = null;
                while (lowestPriorityItemIndex >= 0)
                {
                    item = items[lowestPriorityItemIndex];
                    if (item.Content.TypeId.ToString() == lowestPriorityType
                        && item.Content.SubtypeName == lowestPriority)
                        break;
                    --lowestPriorityItemIndex;
                }

                if (lowestPriorityItemIndex >= 0)
                {
                    _output.Append("Moving ");
                    _output.Append(lowestPriority);
                    _output.Append(" from ");
                    _output.Append(lowestPriorityItemIndex + 1);
                    _output.Append(" slot to last slot of ");
                    _output.Append(((IMyTerminalBlock)inv.Owner).CustomName);
                    _output.AppendLine();
                    inv.TransferItemTo(inv, lowestPriorityItemIndex, items.Count, false, item.Amount);
                }
            }
        }
    }
}

private void BalanceInventories(List<VRage.Game.ModAPI.Ingame.IMyInventory> group, int networkNumber, int groupNumber,
    string groupName, string filterType = null, string filterSubtype = null)
{
    if (group.Count < 2)
    {
        _output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
            .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
            .AppendLine("\"")
            .AppendLine("  because there is only one inventory.");
        return; // nothing to do
    }

    Dictionary<VRage.Game.ModAPI.Ingame.IMyInventory, VolumeInfo> inventories = GetVolumeInfo(group, filterType, filterSubtype);
    var last = inventories[group[inventories.Count - 1]];

    if (last.CurrentVolume < SMALL_NUMBER_THAT_DOES_NOT_MATTER)
    {
        _output.Append("Cannot balance conveyor network ").Append(networkNumber + 1)
            .Append(" group ").Append(groupNumber + 1).Append(" \"").Append(groupName)
            .AppendLine("\"")
            .AppendLine("  because of lack of items in it.");
        return; // nothing to do
    }

    _output.Append("Balancing conveyor network ").Append(networkNumber + 1)
        .Append(" group ").Append(groupNumber + 1)
        .Append(" \"").Append(groupName).AppendLine("\"...");

    for (int i = 0; i < group.Count / 2; ++i)
    {
        var inv1Info = (VolumeInfo)inventories[group[i]];
        var inv2Info = (VolumeInfo)inventories[group[group.Count - i - 1]];

        decimal toMove;
        if (inv1Info.MaxVolume == inv2Info.MaxVolume)
        {
            toMove = (inv2Info.CurrentVolume - inv1Info.CurrentVolume) / 2.0M;
        }
        else
        {
            toMove = (inv2Info.CurrentVolume * inv1Info.MaxVolume
                - inv1Info.CurrentVolume * inv2Info.MaxVolume)
                / (inv1Info.MaxVolume + inv2Info.MaxVolume);
        }

        _output.Append("Inv. 1 vol: ").Append(inv1Info.CurrentVolume.ToString("F6")).Append("; ");
        _output.Append("Inv. 2 vol: ").Append(inv2Info.CurrentVolume.ToString("F6")).Append("; ");
        _output.Append("To move: ").Append(toMove.ToString("F6")).AppendLine();

        if (toMove < 0.0M)
            throw new InvalidOperationException("Something went wrong with calculations:"
                + " volumeDiff is " + toMove);

        if (toMove < SMALL_NUMBER_THAT_DOES_NOT_MATTER)
            continue;

        MoveVolume(inv2Info.MyInventory, inv1Info.MyInventory, (VRage.MyFixedPoint)toMove,
            filterType, filterSubtype);
    }
}

private Dictionary<VRage.Game.ModAPI.Ingame.IMyInventory, VolumeInfo> GetVolumeInfo(List<VRage.Game.ModAPI.Ingame.IMyInventory> group,
    string filterType, string filterSubtype)
{
    var result = new Dictionary<VRage.Game.ModAPI.Ingame.IMyInventory, VolumeInfo>(group.Count);

    if (filterType != null || filterSubtype != null)
    {
        for (int i = 0; i < group.Count; ++i)
        {
            var dt = group[i];
            var item = new VolumeInfo(dt, _itemInfoDict, filterType, filterSubtype, _output);
            result[dt] = item;
        }
    }
    else
    {
        for (int i = 0; i < group.Count; ++i)
        {
            var dt = group[i];
            var item = new VolumeInfo(dt);
            result[dt] = item;
        }
    }

    var comparer = new MyInventoryComparer(result);

    group.Sort(comparer);

    return result;
}

private VRage.MyFixedPoint MoveVolume(VRage.Game.ModAPI.Ingame.IMyInventory from, VRage.Game.ModAPI.Ingame.IMyInventory to,
    VRage.MyFixedPoint volumeAmountToMove,
    string filterType, string filterSubtype)
{
    if (volumeAmountToMove == 0)
        return volumeAmountToMove;

    if (volumeAmountToMove < 0)
        throw new ArgumentException("Invalid volume amount", "volumeAmount");

    _output.Append("Move ");
    _output.Append(volumeAmountToMove);
    _output.Append(" l. from ");
    _output.Append(((IMyTerminalBlock)from.Owner).CustomName);
    _output.Append(" to ");
    _output.AppendLine(((IMyTerminalBlock)to.Owner).CustomName);
    List<VRage.Game.ModAPI.Ingame.IMyInventoryItem> itemsFrom = from.GetItems();
    string groupName;
    long[] accepted = DetermineGroup(to, out groupName);

    for (int i = itemsFrom.Count - 1; i >= 0; --i)
    {
        VRage.Game.ModAPI.Ingame.IMyInventoryItem item = itemsFrom[i];

        if (filterType != null && item.Content.TypeId.ToString() != filterType)
        {
            continue;
        }
        if (filterSubtype != null && item.Content.SubtypeName != filterSubtype)
            continue;

        ItemInfo data;
        var key = new ItemType(item.Content.TypeId.ToString(), item.Content.SubtypeName);
        if (!_itemInfoDict.TryGetValue(key, out data))
        {
            _output.Append("Volume to amount ratio for ");
            _output.Append(item.Content.TypeId);
            _output.Append("/");
            _output.Append(item.Content.SubtypeName);
            _output.AppendLine(" is not known.");
            continue;
        }

        //if (!IsAcceptedItem(data.Id, accepted))
        //    continue;

        decimal amountToMoveRaw = (decimal)volumeAmountToMove * 1000M / data.Volume;
        VRage.MyFixedPoint amountToMove;

        if (data.IsSingleItem)
            amountToMove = (VRage.MyFixedPoint)((int)(amountToMoveRaw + 0.1M));
        else
            amountToMove = (VRage.MyFixedPoint)amountToMoveRaw;

        if (amountToMove == 0)
            continue;

        List<VRage.Game.ModAPI.Ingame.IMyInventoryItem> itemsTo = to.GetItems();
        int targetItemIndex = 0;
        while (targetItemIndex < itemsTo.Count)
        {
            VRage.Game.ModAPI.Ingame.IMyInventoryItem item2 = itemsTo[targetItemIndex];
            if (item2.Content.TypeId == item.Content.TypeId
                && item2.Content.SubtypeName == item.Content.SubtypeName)
                break;
            ++targetItemIndex;
        }

        decimal itemVolume;
        bool success;
        if (amountToMove <= item.Amount)
        {
            itemVolume = (decimal)amountToMove * data.Volume / 1000M;
            success = from.TransferItemTo(to, i, targetItemIndex, true, amountToMove);
            _output.Append("Move ");
            _output.Append(amountToMove);
            _output.Append(" -> ");
            _output.AppendLine(success ? "success" : "failure");
        }
        else
        {
            itemVolume = (decimal)item.Amount * data.Volume / 1000M;
            success = from.TransferItemTo(to, i, targetItemIndex, true, item.Amount);
            _output.Append("Move all ");
            _output.Append(item.Amount);
            _output.Append(" -> ");
            _output.AppendLine(success ? "success" : "failure");
        }
        if (success)
            volumeAmountToMove -= (VRage.MyFixedPoint)itemVolume;
        if (volumeAmountToMove < (VRage.MyFixedPoint)SMALL_NUMBER_THAT_DOES_NOT_MATTER)
            return volumeAmountToMove;
    }

    _output.Append("Cannot move ");
    _output.Append(volumeAmountToMove);
    _output.AppendLine(" l.");

    return volumeAmountToMove;
}

private bool IsAcceptedItem(long[] itemId, long[] acceptedIds)
{
    for (int i = 0; i < itemId.Length && i < acceptedIds.Length; ++i)
    {
        if ((itemId[i] & acceptedIds[i]) != 0)
            return true;
    }
    return false;
}

private const string OreType = "MyObjectBuilder_Ore";
private const string IngotType = "MyObjectBuilder_Ingot";
private const string ComponentType = "MyObjectBuilder_Component";
private const string AmmoMagazineType = "MyObjectBuilder_AmmoMagazine";
private const string PhysicalGunObjectType = "MyObjectBuilder_PhysicalGunObject";
private const string OxygenContainerObjectType = "MyObjectBuilder_OxygenContainerObject";
private const string GasContainerObjectType = "MyObjectBuilder_GasContainerObject";
private const string ModelComponentType = "MyObjectBuilder_ModelComponent";
private const string TreeObjectType = "MyObjectBuilder_TreeObject";

private const string COBALT = "Cobalt";
private const string GOLD = "Gold";
private const string ICE = "Ice";
private const string IRON = "Iron";
private const string MAGNESIUM = "Magnesium";
private const string NICKEL = "Nickel";
private const string ORGANIC = "Organic";
private const string PLATINUM = "Platinum";
private const string SCRAP = "Scrap";
private const string SILICON = "Silicon";
private const string SILVER = "Silver";
private const string STONE = "Stone";
private const string URANIUM = "Uranium";

public void BuildItemInfoDict()
{
    _itemInfoDict = new Dictionary<ItemType, ItemInfo>();

    AddItemInfo(AmmoMagazineType, "250shell", 128M, 64M, true, true); // CSD Battlecannon
    AddItemInfo(AmmoMagazineType, "300mmShell_AP", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "300mmShell_HE", 35M, 25M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "88hekc", 16M, 16M, true, true); // CSD Battlecannon
    AddItemInfo(AmmoMagazineType, "88shell", 16M, 16M, true, true); // CSD Battlecannon
    AddItemInfo(AmmoMagazineType, "900mmShell_AP", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "900mmShell_HE", 210M, 75M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "Aden30x113", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "AFmagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "AZ_Missile_AA", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    AddItemInfo(AmmoMagazineType, "AZ_Missile200mm", 45M, 60M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    AddItemInfo(AmmoMagazineType, "BatteryCannonAmmo1", 50M, 50M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "BatteryCannonAmmo2", 200M, 200M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "BigBertha", 3600M, 2800M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "BlasterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "Bofors40mm", 36M, 28M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "codecatAmmo40x368mm", 70M, 32M, true, true); // [codecat]Weaponry [DX11] [outdated]
    AddItemInfo(AmmoMagazineType, "codecatMissilePinocchio", 50M, 60M, true, true); // [codecat]Weaponry [DX11] [outdated]
    AddItemInfo(AmmoMagazineType, "codecatPunisherAmmo25x184mm", 70M, 32M, true, true); // [codecat]Weaponry [DX11] [outdated]
    AddItemInfo(AmmoMagazineType, "ConcreteMix", 2M, 2M, true, true); // Concrete Tool - placing voxels in survival
    AddItemInfo(AmmoMagazineType, "Eikester_Missile120mm", 25M, 30M, true, true); // (DX11) Small Missile Turret
    AddItemInfo(AmmoMagazineType, "Eikester_Nuke", 1800M, 8836M, true, true); // (DX11) Nuke Launcher [WiP]
    AddItemInfo(AmmoMagazineType, "Flak130mm", 2M, 3M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "Flak200mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "Flak500mm", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "HDTCannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "HighDamageGatlingAmmo", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(AmmoMagazineType, "ISM_FusionAmmo", 35M, 10M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISM_GrendelAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISM_Hellfire", 45M, 60M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISM_LongbowAmmo", 35M, 2M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISM_MinigunAmmo", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISMNeedles", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "ISMTracer", 35M, 16M, true, true); // ISM Mega Mod Pack [DX11 -WIP]
    AddItemInfo(AmmoMagazineType, "LargeKlingonCharge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "LargeShipShotGunAmmo", 50M, 16M, true, true); // Azimuth Industries Mega Mod Pack [OLD]
    AddItemInfo(AmmoMagazineType, "LargeShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Industries Mega Mod Pack [OLD]
    AddItemInfo(AmmoMagazineType, "LaserAmmo", 0.001M, 0.01M, true, true); // (DX11)Laser Turret
    AddItemInfo(AmmoMagazineType, "LaserArrayFlakMagazine", 45M, 30M, true, true); // White Dwarf - Directed Energy Platform [DX11]
    AddItemInfo(AmmoMagazineType, "LaserArrayShellMagazine", 45M, 120M, true, true); // White Dwarf - Directed Energy Platform [DX11]
    AddItemInfo(AmmoMagazineType, "Liquid Naquadah", 0.25M, 0.1M, true, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(AmmoMagazineType, "LittleDavid", 360M, 280M, true, true); // Battle Cannon and Turrets (DX11)
    AddItemInfo(AmmoMagazineType, "MinotaurAmmo", 360M, 128M, true, true); // (DX11)Minotaur Cannon
    AddItemInfo(AmmoMagazineType, "Missile200mm", 45M, 60M, true, true); // Space Engineers
    AddItemInfo(AmmoMagazineType, "MK1CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "MK2CannonAmmo", 150M, 100M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "MK3CannonMagazineAP", 100M, 100M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "MK3CannonMagazineHE", 300M, 100M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "NATO_25x184mm", 35M, 16M, true, true); // Space Engineers
    AddItemInfo(AmmoMagazineType, "NATO_5p56x45mm", 0.45M, 0.2M, true, true); // Space Engineers
    AddItemInfo(AmmoMagazineType, "NiFeDUSlugMagazineLZM", 45M, 50M, true, true); // Large Ship Railguns
    AddItemInfo(AmmoMagazineType, "OKI122mmAmmo", 180M, 60M, true, true); // OKI Weapons Collection (DX11)
    AddItemInfo(AmmoMagazineType, "OKI230mmAmmo", 450M, 240M, true, true); // OKI Weapons Collection (DX11)
    AddItemInfo(AmmoMagazineType, "OKI23mmAmmo", 50M, 20M, true, true); // OKI Weapons Collection (DX11)
    AddItemInfo(AmmoMagazineType, "OKI50mmAmmo", 70M, 20M, true, true); // OKI Weapons Collection (DX11)
    AddItemInfo(AmmoMagazineType, "Phaser2Charge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "Phaser2ChargeLarge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "PhaserCharge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "PhaserChargeLarge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "Plasma_Hydrogen", 4M, 6M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "PlasmaCutterCell", 1M, 1M, true, true); // [SEI] Weapon Pack DX11
    AddItemInfo(AmmoMagazineType, "RB_NATO_125x920mm", 875M, 160M, true, true); // RB Weapon Collection [DX11]
    AddItemInfo(AmmoMagazineType, "RB_Rocket100mm", 11.25M, 15M, true, true); // RB Weapon Collection [DX11]
    AddItemInfo(AmmoMagazineType, "RB_Rocket400mm", 180M, 240M, true, true); // RB Weapon Collection [DX11]
    AddItemInfo(AmmoMagazineType, "RomulanCharge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "RomulanChargeLarge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "SmallKlingonCharge", 35M, 16M, true, true); // Star Trek - Fixed Phaser Pack (Obsolete!)
    AddItemInfo(AmmoMagazineType, "SmallShotGunAmmo", 50M, 16M, true, true); // Azimuth Industries Mega Mod Pack [OLD]
    AddItemInfo(AmmoMagazineType, "SmallShotGunAmmoTracer", 50M, 16M, true, true); // Azimuth Industries Mega Mod Pack [OLD]
    AddItemInfo(AmmoMagazineType, "SniperRoundHighSpeedLowDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(AmmoMagazineType, "SniperRoundHighSpeedLowDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(AmmoMagazineType, "SniperRoundLowSpeedHighDamage", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(AmmoMagazineType, "SniperRoundLowSpeedHighDamageSmallShip", 35M, 16M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(AmmoMagazineType, "TankCannonAmmoSEM4", 35M, 16M, true, true); // Azimuth Complete Mega Mod Pack~(DX-11 Ready)
    AddItemInfo(AmmoMagazineType, "TelionAF_PMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "TelionAMMagazine", 35M, 16M, true, true); // MWI - Weapon Collection (DX11)
    AddItemInfo(AmmoMagazineType, "TritiumMissile", 72M, 60M, true, true); // [VisSE] [DX11] [2017] Hydro Reactors & Ice to Oxy Hydro Gasses MK2
    AddItemInfo(AmmoMagazineType, "TritiumShot", 3M, 3M, true, true); // [VisSE] [DX11] [V1] Hydro Reactors & Ice to Oxy Hydro Gasses
    AddItemInfo(AmmoMagazineType, "TungstenBolt", 4812M, 250M, true, true); // (DX11)Mass Driver
    AddItemInfo(AmmoMagazineType, "Vulcan20x102", 35M, 16M, true, true); // Battle Cannon and Turrets (DX11)

    AddItemInfo(ComponentType, "AdvancedReactorBundle", 50M, 20M, true, true); // Tiered Thorium Reactors and Refinery (new)
    AddItemInfo(ComponentType, "AlloyPlate", 30M, 3M, true, true); // Industrial Centrifuge (stable/dev)
    AddItemInfo(ComponentType, "ampHD", 10M, 15.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
    AddItemInfo(ComponentType, "ArcFuel", 2M, 0.627M, true, true); // Arc Reactor Pack [DX-11 Ready]
    AddItemInfo(ComponentType, "ArcReactorcomponent", 312M, 100M, true, true); // Arc Reactor Pack [DX-11 Ready]
    AddItemInfo(ComponentType, "AzimuthSupercharger", 10M, 9M, true, true); // Azimuth Industries Mega Mod Pack [OLD]
    AddItemInfo(ComponentType, "BulletproofGlass", 15M, 8M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Computer", 0.2M, 1M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "ConductorMagnets", 900M, 200M, true, true); // (DX11)Mass Driver
    AddItemInfo(ComponentType, "Construction", 8M, 2M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "DenseSteelPlate", 200M, 30M, true, true); // Arc Reactor Pack [DX-11 Ready]
    AddItemInfo(ComponentType, "Detector", 5M, 6M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Display", 8M, 6M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Drone", 200M, 60M, true, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(ComponentType, "DT-MiniSolarCell", 0.08M, 0.2M, true, true); // }DT{ Modpack
    AddItemInfo(ComponentType, "Explosives", 2M, 2M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Girder", 6M, 2M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "GraphenePlate", 0.1M, 3M, true, true); // Graphene Armor [Version 0.9 Beta]
    AddItemInfo(ComponentType, "GravityGenerator", 800M, 200M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "InteriorPlate", 3M, 5M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "LargeTube", 25M, 38M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Magna", 100M, 15M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
    AddItemInfo(ComponentType, "Magno", 10M, 5.5M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
    AddItemInfo(ComponentType, "Medical", 150M, 160M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "MetalGrid", 6M, 15M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Mg_FuelCell", 15M, 16M, true, true); // Ripptide's CW & EE Continued
    AddItemInfo(ComponentType, "Motor", 24M, 8M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Naquadah", 100M, 10M, true, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(ComponentType, "Neutronium", 500M, 5M, true, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(ComponentType, "PowerCell", 25M, 45M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "productioncontrolcomponent", 40M, 15M, true, true); // (DX11) Double Sided Upgrade Modules
    AddItemInfo(ComponentType, "RadioCommunication", 8M, 70M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Reactor", 25M, 8M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Scrap", 2M, 2M, true, true); // Small Ship Mega Mod Pack [100% DX-11 Ready]
    AddItemInfo(ComponentType, "SmallTube", 4M, 2M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "SolarCell", 8M, 20M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "SteelPlate", 20M, 3M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Superconductor", 15M, 8M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "Thrust", 40M, 10M, true, true); // Space Engineers
    AddItemInfo(ComponentType, "TractorHD", 1500M, 200M, true, true); // (Discontinued)Maglock Surface Docking Clamps V2.0
    AddItemInfo(ComponentType, "Trinium", 100M, 10M, true, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(ComponentType, "Tritium", 3M, 3M, true, true); // [VisSE] [DX11] [V1] Hydro Reactors & Ice to Oxy Hydro Gasses
    AddItemInfo(ComponentType, "TVSI_DiamondGlass", 40M, 8M, true, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
    AddItemInfo(ComponentType, "WaterTankComponent", 200M, 160M, true, true); // Industrial Centrifuge (stable/dev)
    AddItemInfo(ComponentType, "ZPM", 50M, 60M, true, true); // [Old Version] Stargate (working teleport)

    AddItemInfo(GasContainerObjectType, "HydrogenBottle", 30M, 120M, true, false); // Space Engineers

    AddItemInfo(IngotType, "Carbon", 1M, 0.052M, false, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
    AddItemInfo(IngotType, "Cobalt", 1M, 0.112M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Gold", 1M, 0.052M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Iron", 1M, 0.127M, false, true); // Space Engineers
    AddItemInfo(IngotType, "LiquidHelium", 1M, 4.6M, false, true); // (DX11)Mass Driver
    AddItemInfo(IngotType, "Magmatite", 100M, 37M, false, true); // Stone and Gravel to Metal Ingots (DX 11)
    AddItemInfo(IngotType, "Magnesium", 1M, 0.575M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Naquadah", 1M, 0.052M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(IngotType, "Neutronium", 1M, 0.052M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(IngotType, "Nickel", 1M, 0.112M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Platinum", 1M, 0.047M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Silicon", 1M, 0.429M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Silver", 1M, 0.095M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Stone", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(IngotType, "Thorium", 30M, 5M, false, true); // Tiered Thorium Reactors and Refinery (new)
    AddItemInfo(IngotType, "Trinium", 1M, 0.052M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(IngotType, "Tungsten", 1M, 0.52M, false, true); // (DX11)Mass Driver
    AddItemInfo(IngotType, "Uranium", 1M, 0.052M, false, true); // Space Engineers
    AddItemInfo(IngotType, "v2HydrogenGas", 2.1656M, 0.43M, false, true); // [VisSE] [DX11] [2017] Hydro Reactors & Ice to Oxy Hydro Gasses MK2
    AddItemInfo(IngotType, "v2OxygenGas", 4.664M, 0.9M, false, true); // [VisSE] [DX11] [2017] Hydro Reactors & Ice to Oxy Hydro Gasses MK2

    AddItemInfo(ModelComponentType, "AstronautBackpack", 5M, 60M, true, true); // Space Engineers

    AddItemInfo(OreType, "Carbon", 1M, 0.37M, false, true); // TVSI-Tech Diamond Bonded Glass (Survival) [DX11]
    AddItemInfo(OreType, "Cobalt", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Gold", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Helium", 1M, 5.6M, false, true); // (DX11)Mass Driver
    AddItemInfo(OreType, "HydrogenGas", 0.1665M, 0.45M, false, true); // [VisSE] [DX11] [V1] Hydro Reactors & Ice to Oxy Hydro Gasses
    AddItemInfo(OreType, "Ice", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Iron", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Magnesium", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Naquadah", 1M, 0.37M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(OreType, "Neutronium", 1M, 0.37M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(OreType, "Nickel", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Organic", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "OxygenGas", 2.664M, 0.9M, false, true); // [VisSE] [DX11] [V1] Hydro Reactors & Ice to Oxy Hydro Gasses
    AddItemInfo(OreType, "Platinum", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Scrap", 1M, 0.254M, false, true); // Space Engineers
    AddItemInfo(OreType, "Silicon", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Silver", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Stone", 1M, 0.37M, false, true); // Space Engineers
    AddItemInfo(OreType, "Trinium", 1M, 0.37M, false, true); // [Old Version] Stargate (working teleport)
    AddItemInfo(OreType, "Tungsten", 1M, 0.47M, false, true); // (DX11)Mass Driver
    AddItemInfo(OreType, "Uranium", 1M, 0.37M, false, true); // Space Engineers

    AddItemInfo(OxygenContainerObjectType, "OxygenBottle", 30M, 120M, true, false); // Space Engineers

    AddItemInfo(PhysicalGunObjectType, "AngleGrinder2Item", 3M, 20M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "AngleGrinder3Item", 3M, 20M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "AngleGrinder4Item", 3M, 20M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "AngleGrinderItem", 3M, 20M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "AutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "CubePlacerItem", 1M, 1M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "GoodAIRewardPunishmentTool", 0.1M, 1M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "HandDrill2Item", 22M, 25M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "HandDrill3Item", 22M, 25M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "HandDrill4Item", 22M, 25M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "HandDrillItem", 22M, 25M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "P90", 3M, 12M, true, false); // [Old Version] Stargate (working teleport)
    AddItemInfo(PhysicalGunObjectType, "PhysicalConcreteTool", 5M, 15M, true, false); // Concrete Tool - placing voxels in survival
    AddItemInfo(PhysicalGunObjectType, "PreciseAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "RapidFireAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "Staff", 3M, 16M, true, false); // [Old Version] Stargate (working teleport)
    AddItemInfo(PhysicalGunObjectType, "TritiumAutomaticRifleItem", 3M, 14M, true, false); // [VisSE] [DX11] [V1] Hydro Reactors & Ice to Oxy Hydro Gasses
    AddItemInfo(PhysicalGunObjectType, "UltimateAutomaticRifleItem", 3M, 14M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "Welder2Item", 5M, 8M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "Welder3Item", 5M, 8M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "Welder4Item", 5M, 8M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "WelderItem", 5M, 8M, true, false); // Space Engineers
    AddItemInfo(PhysicalGunObjectType, "Zat", 3M, 12M, true, false); // [Old Version] Stargate (working teleport)

    AddItemInfo(TreeObjectType, "DeadBushMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "DesertBushMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "DesertTree", 1500M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "DesertTreeDead", 1500M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "DesertTreeDeadMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "DesertTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "LeafBushMedium_var1", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "LeafBushMedium_var2", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "LeafTree", 1500M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "LeafTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "PineBushMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "PineTree", 1500M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "PineTreeMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "PineTreeSnow", 1500M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "PineTreeSnowMedium", 1300M, 8000M, true, true); // Space Engineers
    AddItemInfo(TreeObjectType, "SnowPineBushMedium", 1300M, 8000M, true, true); // Space Engineers
}

private void AddItemInfo(string mainType, string subtype,
    decimal mass, decimal volume, bool isSingleItem, bool isStackable)
{
    var key = new ItemType(mainType, subtype);
    var value = new ItemInfo();
    value.ItemType = key;
    value.Id = new long[1 + _itemInfoDict.Count / 32];
    value.Id[_itemInfoDict.Count / 32] = 1u << (_itemInfoDict.Count % 32);
    value.Mass = mass;
    value.Volume = volume;
    value.IsSingleItem = isSingleItem;
    value.IsStackable = isStackable;
    _itemInfoDict.Add(key, value);
}

public class ItemInfo
{
    public ItemType ItemType;
    public long[] Id;
    public decimal Mass;
    public decimal Volume;
    public bool IsSingleItem;
    public bool IsStackable;
}

public class BlockTypeInfo
{
    public string BlockDefinition;
    public string GroupName;
    public long[] AcceptedItems;
}

public class LongArrayComparer : IEqualityComparer<long[]>
{
    public bool Equals(long[] x, long[] y)
    {
        return System.Linq.Enumerable.SequenceEqual<long>(x, y);
    }

    public int GetHashCode(long[] obj)
    {
        return (int)System.Linq.Enumerable.Sum(obj);
    }
}

public class ItemType : IEquatable<ItemType>
{
    public ItemType(string mainType, string subtype)
    {
        MainType = mainType;
        Subtype = subtype;
    }

    public string MainType;

    public string Subtype;

    public bool Equals(ItemType other)
    {
        if (other == null)
            return false;
        return MainType == other.MainType && Subtype == other.Subtype;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as ItemType);
    }

    public override int GetHashCode()
    {
        return MainType.GetHashCode() * 11 + Subtype.GetHashCode();
    }
}

public class VolumeInfo
{
    public VolumeInfo(VRage.Game.ModAPI.Ingame.IMyInventory myInventory)
    {
        MyInventory = myInventory;
        CurrentVolume = (decimal)myInventory.CurrentVolume;
        MaxVolume = (decimal)myInventory.MaxVolume;

        Percent = CurrentVolume / MaxVolume;
    }

    public VolumeInfo(VRage.Game.ModAPI.Ingame.IMyInventory myInventory,
        Dictionary<ItemType, ItemInfo> itemInfoDict,
        string filterType, string filterSubtype, StringBuilder output)
    {
        MyInventory = myInventory;
        CurrentVolume = (decimal)myInventory.CurrentVolume;
        MaxVolume = (decimal)myInventory.MaxVolume;

        ReduceVolumeByItemsForCalculations(itemInfoDict, filterType, filterSubtype, output);

        Percent = CurrentVolume / MaxVolume;
    }

    public VRage.Game.ModAPI.Ingame.IMyInventory MyInventory;

    public decimal CurrentVolume;

    public decimal MaxVolume;

    public decimal Percent;

    private void ReduceVolumeByItemsForCalculations(
        Dictionary<ItemType, ItemInfo> itemInfoDict,
        string filterType,
        string filterSubtype, StringBuilder output)
    {
        List<VRage.Game.ModAPI.Ingame.IMyInventoryItem> items = MyInventory.GetItems();
        for (int i = 0; i < items.Count; ++i)
        {
            VRage.Game.ModAPI.Ingame.IMyInventoryItem item = items[i];

            if ((filterType == null || item.Content.TypeId.ToString() == filterType)
                && (filterSubtype == null || item.Content.SubtypeName == filterSubtype))
                continue;


            ItemInfo data;
            var key = new ItemType(item.Content.TypeId.ToString(), item.Content.SubtypeName);
            if (!itemInfoDict.TryGetValue(key, out data))
            {
                output.Append("Volume to amount ratio for ");
                output.Append(item.Content.TypeId);
                output.Append("/");
                output.Append(item.Content.SubtypeName);
                output.AppendLine(" is not known.");
                continue;
            }

            decimal volumeBlocked = (decimal)item.Amount * data.Volume / 1000M;
            CurrentVolume -= volumeBlocked;
            MaxVolume -= volumeBlocked;
            output.Append("volumeBlocked ");
            output.AppendLine(volumeBlocked.ToString("N6"));
        }
    }
}

private class MyInventoryComparer : IComparer<VRage.Game.ModAPI.Ingame.IMyInventory>
{
    private Dictionary<VRage.Game.ModAPI.Ingame.IMyInventory, VolumeInfo> _volumeInfo;

    public MyInventoryComparer(Dictionary<VRage.Game.ModAPI.Ingame.IMyInventory, VolumeInfo> volumeInfo)
    {
        _volumeInfo = volumeInfo;
    }

    public int Compare(VRage.Game.ModAPI.Ingame.IMyInventory x, VRage.Game.ModAPI.Ingame.IMyInventory y)
    {
        var a = _volumeInfo[x];
        var b = _volumeInfo[y];
        return Decimal.Compare(a.Percent, b.Percent);
    }
}

private class MyTextPanelNameComparer : IComparer<IMyTextPanel>
{
    public static IComparer<IMyTextPanel> Instance = new MyTextPanelNameComparer();

    public int Compare(IMyTextPanel x, IMyTextPanel y)
    {
        return String.Compare(x.CustomName, y.CustomName, true);
    }
}
    }
}
