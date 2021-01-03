using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItemInfoFinder
{
    public class ItemInfo
    {
        public ItemInfo(long modId, MainType typeId, string subtypeId, string mass, string volume)
        {
            ModId = modId;
            TypeId = typeId;
            SubtypeId = subtypeId;
            Mass = mass;
            Volume = volume;
        }

        public long ModId { get; }

        public MainType TypeId { get; }

        public string SubtypeId { get; }

        public string Mass { get; }

        public string Volume { get; }

        public bool HasIntegralAmounts
        {
            get { return TypeId.HasIntegralAmounts; }
        }

        public bool IsStackable
        {
            get { return TypeId.IsStackable; }
        }

        public override string ToString()
        {
            return String.Format("{0}/{1} modId:", TypeId, SubtypeId, ModId);
        }
    }
}
