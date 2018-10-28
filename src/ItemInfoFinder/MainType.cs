using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItemInfoFinder
{
    public class MainType
    {
        public MainType(string name, int order, string alias, bool hasIntegralAmounts, bool isStackable)
        {
            Order = order;
            Name = name;
            Alias = alias;
            HasIntegralAmounts = hasIntegralAmounts;
            IsStackable = isStackable;
        }

        public string Alias { get; }

        public bool HasIntegralAmounts { get; }

        public bool IsStackable { get; }

        public string Name { get; }

        public int Order { get; }
    }
}
