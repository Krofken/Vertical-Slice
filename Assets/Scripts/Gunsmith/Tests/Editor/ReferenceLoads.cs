using Krofken.Ballistics;

namespace Gunsmith.Tests
{
    /// <summary>
    /// Worked solutions to the five briefs, used only by the tests.
    ///
    /// These are kept OUT of the game assembly on purpose. Working these out is the
    /// game; shipping them as presets would be shipping the answers. They exist here
    /// so the tests can assert the central design claim -- that each brief needs its
    /// own round, and that a round built for one brief fails another.
    /// </summary>
    internal static class ReferenceLoads
    {
        private static CartridgeDesign Base(string name) => new CartridgeDesign
        {
            Name = name,
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Projectile = ProjectileGeometry.Default9mmFmj,
            Materials = ProjectileMaterials.JacketedLead,
            PropellantId = PropellantLibrary.SingleBase,
            GrainShape = GrainShape.Sphere,
            WebThickness = 3.5e-5,
            DeterrentCoating = 0.3,
            ChargeMass = Units.GrainsToKilograms(5.5),
            SeatingDepth = 0.0030
        };

        /// <summary>
        /// DEEP PENETRATION. Heavy, closed nose, high sectional density, warm charge.
        /// Everything that makes it good here makes it dangerous in a crowd.
        /// </summary>
        internal static CartridgeDesign Penetrator()
        {
            var d = Base("Heavy FMJ");
            d.Projectile.BearingSurfaceLength = 0.0090;   // longer, so heavier
            d.Projectile.JacketThickness = 0.0006;        // thicker jacket resists upset
            d.ChargeMass = Units.GrainsToKilograms(5.2);
            return d;
        }

        /// <summary>
        /// EXPANDING. Soft lead behind a wide open cavity, thin jacket. Mushrooms,
        /// doubles its frontal area, and stops inside a torso.
        /// </summary>
        internal static CartridgeDesign HollowPoint()
        {
            var d = Base("Soft Point");
            d.Projectile.MeplatDiameter = 0.0038;
            d.Projectile.CavityDepth = 0.0055;
            d.Projectile.CavityMouthDiameter = 0.0038;
            d.Projectile.JacketThickness = 0.00030;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.Copper
            };
            d.ChargeMass = Units.GrainsToKilograms(5.3);
            return d;
        }

        /// <summary>
        /// ARMOUR PIERCING. A hardened steel core yields at 1.5 GPa, two orders of
        /// magnitude above anything soft tissue can apply, so it simply refuses to
        /// deform. Lighter than lead, so it trades depth in flesh for hardness.
        /// </summary>
        internal static CartridgeDesign ArmourPiercing()
        {
            var d = Base("Steel Core");
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.HardenedSteel,
                JacketMaterialId = MaterialLibrary.GildingMetal
            };
            d.ChargeMass = Units.GrainsToKilograms(5.6);
            return d;
        }

        /// <summary>
        /// INCENDIARY. Phosphorus initiates at 15 MPa, which soft tissue supplies
        /// easily -- thermite needs 200 MPa and would sail straight through inert.
        /// </summary>
        internal static CartridgeDesign Incendiary()
        {
            var d = Base("Firestarter");
            d.Projectile.MeplatDiameter = 0.0032;
            d.Projectile.CavityDepth = 0.0065;
            d.Projectile.CavityMouthDiameter = 0.0032;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.Lead,
                JacketMaterialId = MaterialLibrary.GildingMetal,
                CavityFillMaterialId = MaterialLibrary.PhosphorusCompound
            };
            return d;
        }

        /// <summary>
        /// FRANGIBLE. A sintered core tears at 1% strain, so the moment impact drives
        /// it past yield it comes apart instead of flowing.
        /// </summary>
        internal static CartridgeDesign Frangible()
        {
            var d = Base("Breaker");
            d.Projectile.MeplatDiameter = 0.0040;
            d.Projectile.CavityDepth = 0.0050;
            d.Projectile.CavityMouthDiameter = 0.0040;
            d.Materials = new ProjectileMaterials
            {
                CoreMaterialId = MaterialLibrary.SinteredIron,
                JacketMaterialId = MaterialLibrary.Copper
            };
            d.ChargeMass = Units.GrainsToKilograms(5.4);
            return d;
        }
    }
}
