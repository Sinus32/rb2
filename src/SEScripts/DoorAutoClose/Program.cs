using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace OreProcessingOptimizer.DoorAutoClose
{
    public class Program : MyGridProgram
    {
        public bool MY_GRID_ONLY = true;

        public readonly TimeSpan DELAY = new TimeSpan(0, 0, 5);

        private Dictionary<IMyDoor, DateTime> _dict = null;

        void Main(string argument)
        {
            var allDoors = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyDoor>(allDoors, DoorFilter);
            Echo(String.Format("Opened doors found: {0}", allDoors.Count));

            var newDict = new Dictionary<IMyDoor, DateTime>();
            var now = DateTime.Now;

            for (int i = 0; i < allDoors.Count; ++i)
            {
                var door = (IMyDoor)allDoors[i];
                DateTime tmp;
                if (_dict != null && _dict.TryGetValue(door, out tmp))
                {
                    var time = now - tmp;
                    if (time >= DELAY)
                    {
                        door.ApplyAction("Open_Off");
                        Echo(String.Format("Closing {0}...", door.CustomName));
                    }
                    else
                    {
                        newDict[door] = tmp;
                    }
                }
                else
                {
                    newDict[door] = now;
                }
            }

            _dict = newDict;
        }

        private bool DoorFilter(IMyTerminalBlock arg)
        {
            var door = (IMyDoor)arg;
            if (MY_GRID_ONLY && door.CubeGrid != Me.CubeGrid)
                return false;

            var def = door.BlockDefinition.ToString();
            if (!def.StartsWith("MyObjectBuilder_Door/"))
                return false;

            return door.Enabled && door.IsFunctional && door.Open && door.OpenRatio >= 0.99f;
        }
    }
}
