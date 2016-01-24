using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItemInfoFinder
{
    public class ItemInfo : IEquatable<ItemInfo>
    {
        public ItemInfo(string typeId, string subtypeId, string mass, string volume)
        {
            TypeId = typeId;
            SubtypeId = subtypeId;
            Mass = mass;
            Volume = volume;
        }

        public string TypeId { get; set; }

        public string SubtypeId { get; set; }

        public string Mass { get; set; }

        public string Volume { get; set; }

        public bool IsSingleItem { get { return TypeId != "Ore" && TypeId != "Ingot"; } }

        public bool IsStackable { get { return TypeId != "PhysicalGunObject" && TypeId != "OxygenContainerObject" && TypeId != "GasContainerObject"; } }

        public bool Equals(ItemInfo other)
        {
            return TypeId == other.TypeId && SubtypeId == other.SubtypeId && Mass == other.Mass && Volume == other.Volume;
        }

        public override bool Equals(object obj)
        {
            return Equals((ItemInfo)obj);
        }

        public override int GetHashCode()
        {
            return TypeId.GetHashCode() + SubtypeId.GetHashCode() + Mass.GetHashCode() + Volume.GetHashCode();
        }
    }
}
