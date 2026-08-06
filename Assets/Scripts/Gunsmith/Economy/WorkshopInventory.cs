using System;
using System.Collections.Generic;
using Krofken.Ballistics;

namespace Gunsmith.Economy
{
    /// <summary>
    /// What the gunsmith has on the shelves.
    ///
    /// Bulk materials are tracked by MASS, not by count, because that is what the
    /// physics consumes: a longer bullet uses more lead, a heavier charge uses more
    /// powder, and the player feels both. Cases and primers are counted, because they
    /// are discrete objects.
    ///
    /// The scarcity here is what makes the test range meaningful. Every round fired
    /// into the gel block is a round that cannot be delivered, so a player cannot
    /// simply brute-force a design by testing a hundred variations.
    /// </summary>
    [Serializable]
    public sealed class WorkshopInventory
    {
        private readonly Dictionary<string, double> _bulkMass = new Dictionary<string, double>();
        private readonly Dictionary<string, int> _cases = new Dictionary<string, int>();

        /// <summary>Primers on hand. One is consumed per round assembled.</summary>
        public int Primers;

        /// <summary>Coin on hand.</summary>
        public int Funds;

        public event Action Changed;

        // ---- Bulk materials (kg) ------------------------------------------

        /// <summary>Mass of a material on hand, kg. Covers both metals and propellants.</summary>
        public double MassOf(string materialId)
            => materialId != null && _bulkMass.TryGetValue(materialId, out double kg) ? kg : 0.0;

        public void AddMass(string materialId, double kilograms)
        {
            if (string.IsNullOrEmpty(materialId) || kilograms <= 0.0) return;
            _bulkMass.TryGetValue(materialId, out double existing);
            _bulkMass[materialId] = existing + kilograms;
            Changed?.Invoke();
        }

        /// <summary>Removes mass if enough is present. Returns false and changes
        /// nothing otherwise.</summary>
        public bool TryConsumeMass(string materialId, double kilograms)
        {
            if (string.IsNullOrEmpty(materialId)) return false;
            if (kilograms <= 0.0) return true;

            if (!_bulkMass.TryGetValue(materialId, out double existing) || existing < kilograms)
                return false;

            _bulkMass[materialId] = existing - kilograms;
            Changed?.Invoke();
            return true;
        }

        public IEnumerable<KeyValuePair<string, double>> AllBulk => _bulkMass;

        // ---- Cases ---------------------------------------------------------

        public int CasesOf(string caseId)
            => caseId != null && _cases.TryGetValue(caseId, out int n) ? n : 0;

        public void AddCases(string caseId, int count)
        {
            if (string.IsNullOrEmpty(caseId) || count <= 0) return;
            _cases.TryGetValue(caseId, out int existing);
            _cases[caseId] = existing + count;
            Changed?.Invoke();
        }

        public bool TryConsumeCases(string caseId, int count)
        {
            if (count <= 0) return true;
            if (string.IsNullOrEmpty(caseId)) return false;

            if (!_cases.TryGetValue(caseId, out int existing) || existing < count) return false;

            _cases[caseId] = existing - count;
            Changed?.Invoke();
            return true;
        }

        public IEnumerable<KeyValuePair<string, int>> AllCases => _cases;

        // ---- Money ----------------------------------------------------------

        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (Funds < amount) return false;
            Funds -= amount;
            Changed?.Invoke();
            return true;
        }

        public void Earn(int amount)
        {
            if (amount <= 0) return;
            Funds += amount;
            Changed?.Invoke();
        }

        /// <summary>Raises <see cref="Changed"/>. For callers that mutate through
        /// several operations and want a single notification.</summary>
        public void NotifyChanged() => Changed?.Invoke();
    }

    /// <summary>One line of a bill of materials.</summary>
    public struct MaterialLine
    {
        public string MaterialId;
        public string DisplayName;

        /// <summary>Mass required, kg. Zero for counted items.</summary>
        public double Mass;

        /// <summary>Count required. Zero for bulk items.</summary>
        public int Count;

        /// <summary>True if this line is counted rather than weighed.</summary>
        public bool IsCounted => Count > 0;

        /// <summary>Mass on hand, or count on hand, at the time the bill was built.</summary>
        public double Available;

        /// <summary>True if the workshop has enough.</summary>
        public bool IsSatisfied;
    }

    /// <summary>Everything needed to assemble a batch of a given design.</summary>
    public sealed class BillOfMaterials
    {
        public readonly List<MaterialLine> Lines = new List<MaterialLine>();
        public int Rounds;
        public int EstimatedCost;

        /// <summary>True if every line can be satisfied from stock.</summary>
        public bool CanBuild
        {
            get
            {
                for (int i = 0; i < Lines.Count; i++)
                    if (!Lines[i].IsSatisfied) return false;
                return true;
            }
        }

        /// <summary>The first thing that is missing, for the UI to complain about.</summary>
        public string FirstShortage
        {
            get
            {
                for (int i = 0; i < Lines.Count; i++)
                    if (!Lines[i].IsSatisfied) return Lines[i].DisplayName;
                return null;
            }
        }
    }

    /// <summary>
    /// The merchant. In the vertical slice everything is in stock and arrives
    /// instantly, so buying is a single click and the interesting constraint is
    /// money rather than logistics.
    ///
    /// Prices are per kilogram for bulk goods. They are set so a plain jacketed lead
    /// round costs a couple of coin against an order paying over a hundred -- enough
    /// that a dozen test shots is a real decision, not enough that experimenting is
    /// ruinous. Exotic materials are priced to hurt: a tungsten carbide core costs
    /// more than the rest of the round put together, which is the point.
    /// </summary>
    public static class Merchant
    {
        private static readonly Dictionary<string, int> PricePerKilogram = BuildPrices();

        /// <summary>Coin per case.</summary>
        public const int CasePrice = 1;

        /// <summary>Coin per primer.</summary>
        public const int PrimerPrice = 1;

        /// <summary>Smallest quantity of a bulk material that can be bought, kg.
        /// You cannot buy a tenth of a gram of lead.</summary>
        public const double BulkIncrement = 0.010;

        public static int PriceOf(string materialId)
            => materialId != null && PricePerKilogram.TryGetValue(materialId, out int price) ? price : 200;

        /// <summary>Cost of a mass of a bulk material, rounded up to the coin.</summary>
        public static int CostOfMass(string materialId, double kilograms)
            => (int)Math.Ceiling(PriceOf(materialId) * kilograms);

        /// <summary>Buys bulk material into the inventory. Returns false if the
        /// player cannot afford it, and changes nothing.</summary>
        public static bool Buy(WorkshopInventory inventory, string materialId, double kilograms)
        {
            if (inventory == null || string.IsNullOrEmpty(materialId) || kilograms <= 0.0) return false;

            int cost = CostOfMass(materialId, kilograms);
            if (!inventory.TrySpend(cost)) return false;

            inventory.AddMass(materialId, kilograms);
            return true;
        }

        public static bool BuyCases(WorkshopInventory inventory, string caseId, int count)
        {
            if (inventory == null || count <= 0) return false;
            if (!inventory.TrySpend(CasePrice * count)) return false;

            inventory.AddCases(caseId, count);
            return true;
        }

        public static bool BuyPrimers(WorkshopInventory inventory, int count)
        {
            if (inventory == null || count <= 0) return false;
            if (!inventory.TrySpend(PrimerPrice * count)) return false;

            inventory.Primers += count;
            inventory.NotifyChanged();
            return true;
        }

        private static Dictionary<string, int> BuildPrices()
        {
            return new Dictionary<string, int>
            {
                // ---- Structural metals, coin per kg -------------------------
                { MaterialLibrary.Lead, 120 },
                { MaterialLibrary.HardenedLead, 180 },
                { MaterialLibrary.Copper, 680 },
                { MaterialLibrary.GildingMetal, 750 },
                { MaterialLibrary.CartridgeBrass, 600 },
                { MaterialLibrary.MildSteel, 90 },
                { MaterialLibrary.HardenedSteel, 820 },
                { MaterialLibrary.Aluminium, 450 },
                { MaterialLibrary.Zinc, 220 },
                { MaterialLibrary.Polymer, 70 },
                { MaterialLibrary.SinteredIron, 300 },

                // Dense and scarce. A core of this costs more than everything else
                // in the round combined, which is exactly the trade an armour
                // piercing brief forces the player to weigh.
                { MaterialLibrary.TungstenCarbide, 6000 },
                { MaterialLibrary.TungstenHeavyAlloy, 4800 },
                { MaterialLibrary.Bismuth, 1600 },

                // ---- Reactive fillers ---------------------------------------
                { MaterialLibrary.Thermite, 3200 },
                { MaterialLibrary.PhosphorusCompound, 12000 },

                // ---- Propellants ---------------------------------------------
                { PropellantLibrary.SingleBase, 1400 },
                { PropellantLibrary.DoubleBase, 2100 },
                { PropellantLibrary.TripleBase, 3400 },
                { PropellantLibrary.BlackPowder, 260 }
            };
        }
    }
}
