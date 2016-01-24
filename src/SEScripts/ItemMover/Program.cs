using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace OreProcessingOptimizer.ItemMover
{
    public class Program : MyGridProgram
    {
        private StringBuilder _output;
        private List<IMyTextPanel> _debugScreen;
        private List<IMyInventory> _source;
        private List<IMyInventory> _target;

        void Main(string argument)
        { }
    }
}
