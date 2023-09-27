using ItemInfoFinder;
using Xunit;

namespace Tests
{
    public class WorkshopItemInfoTests
    {
        [Theory]
        [InlineData("(AR) Ceramic Armor", "(AR) Ceramic Armor")]
        [InlineData("(Discontinued)Maglock Surface Docking Clamps V2.0", "Maglock Surface Docking Clamps V2.0")]
        [InlineData("(DX11) Double Sided Upgrade Modules", "Double Sided Upgrade Modules")]
        [InlineData("(DX11) Nuke Launcher [WiP]", "Nuke Launcher")]
        [InlineData("(DX11) Small Missile Turret", "Small Missile Turret")]
        [InlineData("(DX11)Laser Turret", "Laser Turret")]
        [InlineData("(DX11)Mass Driver", "Mass Driver")]
        [InlineData("(DX11)Minotaur Cannon", "Minotaur Cannon")]
        [InlineData("[DEPRECATED] CSD Battlecannon", "CSD Battlecannon")]
        [InlineData("[Fixed] rearth's Advanced Combat Systems", "rearth's Advanced Combat Systems")]
        [InlineData("[New Version] Stargate Modpack (Economy Support!)", "Stargate Modpack (Economy Support!)")]
        [InlineData("[SEI] Weapon Pack DX11", "[SEI] Weapon Pack DX11")]
        [InlineData("[VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry", "[VisSE] [2020] Oxy Hydro Gasses & Reactors with Tritium Weaponry")]
        [InlineData("Advanced Designator Turret", "Advanced Designator Turret")]
        [InlineData("Arc Reactor Pack [DX-11 Ready]", "Arc Reactor Pack")]
        [InlineData("Azimuth Complete Mega Mod Pack~(DX-11 Ready)", "Azimuth Complete Mega Mod Pack~")]
        [InlineData("Azimuth Remastered", "Azimuth Remastered")]
        [InlineData("Battle Cannon and Turrets (DX11)", "Battle Cannon and Turrets")]
        [InlineData("Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update", "Better Stone v7.0.7 (SE 1.197) Pertam Off-Road Update")]
        [InlineData("Concrete Tool - placing voxels in survival", "Concrete Tool - placing voxels in survival")]
        [InlineData("Deuterium Fusion Reactors", "Deuterium Fusion Reactors")]
        [InlineData("EM Thruster", "EM Thruster")]
        [InlineData("Energy shields (new modified version)", "Energy shields")]
        [InlineData("GH-Industry (ModPack) (17 new blocks)", "GH-Industry (ModPack) (17 new blocks)")]
        [InlineData("Graphene Armor (Core) (Beta) - Updated", "Graphene Armor (Core) - Updated")]
        [InlineData("Gravel Gatling Turret MK 2", "Gravel Gatling Turret MK 2")]
        [InlineData("GSF Energy Weapons Pack <Link to V2 Beta Testing now Available> Currently Broken", "GSF Energy Weapons Pack Currently Broken")]
        [InlineData("High Tech Solar Arrays", "High Tech Solar Arrays")]
        [InlineData("HSR (WeaponCore) Strategic Update November 2020", "HSR (WeaponCore) Strategic Update November 2020")]
        [InlineData("Independent Survival", "Independent Survival")]
        [InlineData("Industrial Centrifuge (stable/dev)", "Industrial Centrifuge (stable/dev)")]
        [InlineData("Industrial Overhaul - 1.0", "Industrial Overhaul - 1.0")]
        [InlineData("ISM Mega Mod Pack [OUTDATED]", "ISM Mega Mod Pack")]
        [InlineData("MWI - Weapon Collection (DX11)", "MWI - Weapon Collection")]
        [InlineData("OKI Grand Weapons Bundle", "OKI Grand Weapons Bundle")]
        [InlineData("Revived Large Ship Railguns (With penetration and shield damage!)", "Revived Large Ship Railguns (With penetration and shield damage!)")]
        [InlineData("RG_RailGun", "RG_RailGun")]
        [InlineData("Ripptide's CW+EE (DX11). Reuploaded", "Ripptide's CW+EE . Reuploaded")]
        [InlineData("Small Ship Mega Mod Pack [100% DX-11 Ready]", "Small Ship Mega Mod Pack")]
        [InlineData("SpinalWeaponry", "SpinalWeaponry")]
        [InlineData("Star Trek - Weapons Tech [WIP]", "Star Trek - Weapons Tech")]
        [InlineData("Stone and Gravel to Metal Ingots (DX 11)", "Stone and Gravel to Metal Ingots")]
        [InlineData("Tiered Engine Super Pack", "Tiered Engine Super Pack")]
        [InlineData("Tiered Thorium Reactors and Refinery", "Tiered Thorium Reactors and Refinery")]
        [InlineData("WeaponCore - 1.6(22)", "WeaponCore - 1.6(22)")]
        [InlineData("White Dwarf - Directed Energy Platform [DX11]", "White Dwarf - Directed Energy Platform")]
        public void GetBaseTitleTest(string title, string expected)
        {
            var wii = new WorkshopItemInfo();
            var result = wii.GetBaseTitle(title);
            Assert.Equal(expected, result);
        }
    }
}
