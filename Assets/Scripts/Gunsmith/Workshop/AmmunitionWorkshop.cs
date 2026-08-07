using System;
using System.Collections.Generic;
using Gunsmith.Economy;
using Krofken.Ballistics;

namespace Gunsmith.Workshop
{
    /// <summary>A design the player has saved, together with its baked results.</summary>
    public sealed class SavedDesign
    {
        public string Id;
        public string Name;
        public CartridgeDesign Design;
        public BakedCartridge Baked;

        /// <summary>Day the design was last edited. Shown in the notebook.</summary>
        public int LastEditedDay;

        public bool IsValid => Baked != null && Baked.IsValid;
    }

    /// <summary>The player's saved designs.</summary>
    public sealed class DesignLibrary
    {
        private readonly Dictionary<string, SavedDesign> _designs = new Dictionary<string, SavedDesign>();
        private int _nextId = 1;

        public event Action<SavedDesign> DesignChanged;

        public IEnumerable<SavedDesign> All => _designs.Values;

        public SavedDesign Get(string id)
            => id != null && _designs.TryGetValue(id, out var d) ? d : null;

        /// <summary>
        /// Saves a design, baking it against the workshop's reference barrel.
        ///
        /// THIS is where the expensive work happens -- the interior ballistics ODE,
        /// the mass integration, the drag curve. Once, on commit. Everything the
        /// player does afterwards with this design reads the baked results.
        /// </summary>
        public SavedDesign Save(string id, string name, in CartridgeDesign design, in Barrel barrel, int day)
        {
            if (string.IsNullOrEmpty(id)) id = $"design_{_nextId++}";

            var baked = CartridgeBaker.Bake(design, barrel);

            if (!_designs.TryGetValue(id, out var saved))
            {
                saved = new SavedDesign { Id = id };
                _designs[id] = saved;
            }

            saved.Name = string.IsNullOrEmpty(name) ? id : name;
            saved.Design = design;
            saved.Baked = baked;
            saved.LastEditedDay = day;

            DesignChanged?.Invoke(saved);
            return saved;
        }

        public bool Remove(string id) => id != null && _designs.Remove(id);

        /// <summary>
        /// Copies a saved design so one thing can be changed on the copy.
        ///
        /// THIS IS THE MOST IMPORTANT AFFORDANCE IN THE GAME and it is nearly free.
        /// If changing one variable costs one click and changing three costs nine, a
        /// player naturally runs controlled experiments and learns causality fast. If
        /// changing everything is equally easy they change everything at once and learn
        /// nothing. Keep duplicate cheaper than editing in place, always.
        ///
        /// The copy is NOT re-baked lazily — it bakes here, so the duplicate is
        /// immediately loadable and the player never hits a half-made design.
        /// </summary>
        /// <returns>The new design, or null if the source does not exist.</returns>
        public SavedDesign Duplicate(string sourceId, in Barrel barrel, int day)
        {
            var source = Get(sourceId);
            if (source == null) return null;

            // CartridgeDesign is a struct, so this is already a deep copy of every
            // dimension and material choice.
            return Save(NextIdFrom(source.Id), NextNameFrom(source.Name), source.Design, barrel, day);
        }

        /// <summary>An unused id derived from the original.</summary>
        private string NextIdFrom(string id)
        {
            for (int n = 2; n < 1000; n++)
            {
                string candidate = $"{id}_{n}";
                if (!_designs.ContainsKey(candidate)) return candidate;
            }

            return $"design_{_nextId++}";
        }

        /// <summary>
        /// "Brass Nose" becomes "Brass Nose Mk2"; "Brass Nose Mk2" becomes "Mk3".
        /// A gunsmith numbers their attempts, and the numbering is what makes a rack of
        /// recovered bullets legible later.
        /// </summary>
        private static string NextNameFrom(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Mk2";

            int mark = name.LastIndexOf("Mk", StringComparison.OrdinalIgnoreCase);
            if (mark >= 0 && int.TryParse(name.Substring(mark + 2).Trim(), out int number))
                return $"{name.Substring(0, mark).TrimEnd()} Mk{number + 1}".Trim();

            return $"{name} Mk2";
        }
    }

    /// <summary>Outcome of a crafting attempt.</summary>
    public struct CraftResult
    {
        public bool Success;
        public int RoundsProduced;
        public string Message;
        public BillOfMaterials Bill;
    }

    /// <summary>
    /// The loading bench: turns materials into finished rounds.
    ///
    /// Consumption is derived from the SIMULATED design, not from an authored recipe.
    /// The core mass the bill charges for is the mass the mass-properties solver
    /// integrated out of the player's geometry, so making a bullet longer really does
    /// use more lead, and there is no second set of numbers that can drift away from
    /// the first.
    /// </summary>
    public sealed class AmmunitionWorkshop
    {
        private readonly WorkshopInventory _inventory;
        private readonly Dictionary<string, int> _stock = new Dictionary<string, int>();

        public event Action StockChanged;

        public AmmunitionWorkshop(WorkshopInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <summary>Finished rounds of a design on hand.</summary>
        public int RoundsOf(string designId)
            => designId != null && _stock.TryGetValue(designId, out int n) ? n : 0;

        public IEnumerable<KeyValuePair<string, int>> AllStock => _stock;

        /// <summary>
        /// Works out what a batch would cost in materials, and whether the workshop
        /// can currently build it. Pure query -- consumes nothing.
        /// </summary>
        public BillOfMaterials BuildBill(SavedDesign design, int rounds)
        {
            var bill = new BillOfMaterials { Rounds = rounds };
            if (design?.Baked == null || rounds <= 0) return bill;

            var mass = design.Baked.Mass;
            var materials = design.Design.Materials;

            AddBulk(bill, materials.CoreMaterialId, mass.CoreMass * rounds);

            if (design.Design.Projectile.JacketThickness > 0.0)
                AddBulk(bill, materials.JacketMaterialId, mass.JacketMass * rounds);

            if (mass.PayloadMass > 0.0)
                AddBulk(bill, materials.CavityFillMaterialId, mass.PayloadMass * rounds);

            AddBulk(bill, design.Design.PropellantId, design.Design.ChargeMass * rounds);

            AddCounted(bill, design.Design.CaseId, DisplayNameOfCase(design.Design.CaseId), rounds,
                _inventory.CasesOf(design.Design.CaseId));

            AddCounted(bill, "primer", "Primers", rounds, _inventory.Primers);

            return bill;
        }

        /// <summary>
        /// Assembles a batch, consuming materials.
        ///
        /// Checks the whole bill BEFORE consuming anything. A partial consumption that
        /// then fails would quietly destroy the player's stock, which is exactly the
        /// kind of bug that is unforgivable in a game about scarce resources.
        /// </summary>
        public CraftResult Craft(SavedDesign design, int rounds)
        {
            var bill = BuildBill(design, rounds);

            if (design?.Baked == null)
                return new CraftResult { Message = "No design selected.", Bill = bill };

            // Only refuse what will not GO TOGETHER. A bullet that will not chamber or a
            // charge that will not fit the case are visible in your hands, and the bench
            // is right to stop you.
            //
            // A load that will burst the case or wreck the gun assembles perfectly well,
            // and the bench says nothing about it. Warning the player here would hand
            // them the answer and remove the reason to walk out to the range, which is
            // the entire game. They find out from the fired case.
            if (!design.Baked.CanAssemble)
                return new CraftResult
                {
                    Message = design.Baked.FirstAssemblyFault ?? "These parts do not go together.",
                    Bill = bill
                };

            if (rounds <= 0)
                return new CraftResult { Message = "Nothing to make.", Bill = bill };

            if (!bill.CanBuild)
                return new CraftResult
                {
                    Message = $"Not enough {bill.FirstShortage}.",
                    Bill = bill
                };

            // Everything checked; now commit.
            for (int i = 0; i < bill.Lines.Count; i++)
            {
                var line = bill.Lines[i];

                if (line.IsCounted)
                {
                    if (line.MaterialId == "primer") _inventory.Primers -= line.Count;
                    else _inventory.TryConsumeCases(line.MaterialId, line.Count);
                }
                else
                {
                    _inventory.TryConsumeMass(line.MaterialId, line.Mass);
                }
            }

            _inventory.NotifyChanged();

            _stock.TryGetValue(design.Id, out int existing);
            _stock[design.Id] = existing + rounds;
            StockChanged?.Invoke();

            return new CraftResult
            {
                Success = true,
                RoundsProduced = rounds,
                Bill = bill,
                Message = $"Loaded {rounds} rounds of {design.Name}."
            };
        }

        /// <summary>
        /// Takes rounds out of stock -- for a test shot or for a delivery.
        /// Returns false if there are not enough, which is the constraint that makes
        /// testing a real decision.
        /// </summary>
        public bool TryConsumeRounds(string designId, int count)
        {
            if (count <= 0) return true;
            if (designId == null) return false;

            if (!_stock.TryGetValue(designId, out int existing) || existing < count) return false;

            _stock[designId] = existing - count;
            StockChanged?.Invoke();
            return true;
        }

        /// <summary>Pulls apart finished rounds. Recovers the metals, which survive;
        /// the powder and primer do not.</summary>
        public bool TryDisassemble(SavedDesign design, int rounds)
        {
            if (design?.Baked == null) return false;
            if (!TryConsumeRounds(design.Id, rounds)) return false;

            var mass = design.Baked.Mass;
            var materials = design.Design.Materials;

            // Metals come back, at a loss for what is deformed in pulling them.
            const double recovery = 0.85;

            _inventory.AddMass(materials.CoreMaterialId, mass.CoreMass * rounds * recovery);

            if (design.Design.Projectile.JacketThickness > 0.0)
                _inventory.AddMass(materials.JacketMaterialId, mass.JacketMass * rounds * recovery);

            _inventory.AddCases(design.Design.CaseId, rounds);
            _inventory.NotifyChanged();
            return true;
        }

        private void AddBulk(BillOfMaterials bill, string materialId, double kilograms)
        {
            if (string.IsNullOrEmpty(materialId) || kilograms <= 0.0) return;

            double available = _inventory.MassOf(materialId);

            bill.Lines.Add(new MaterialLine
            {
                MaterialId = materialId,
                DisplayName = DisplayNameOfMaterial(materialId),
                Mass = kilograms,
                Available = available,
                IsSatisfied = available >= kilograms
            });

            bill.EstimatedCost += Merchant.CostOfMass(materialId, kilograms);
        }

        private static void AddCounted(BillOfMaterials bill, string id, string name, int count, int available)
        {
            bill.Lines.Add(new MaterialLine
            {
                MaterialId = id,
                DisplayName = name,
                Count = count,
                Available = available,
                IsSatisfied = available >= count
            });

            bill.EstimatedCost += id == "primer" ? Merchant.PrimerPrice * count : Merchant.CasePrice * count;
        }

        private static string DisplayNameOfMaterial(string id)
        {
            if (MaterialLibrary.TryGet(id, out var material)) return material.DisplayName;
            if (PropellantLibrary.TryGet(id, out var propellant)) return propellant.DisplayName;
            return id;
        }

        private static string DisplayNameOfCase(string id)
            => CartridgeCaseLibrary.TryGet(id, out var c) ? $"{c.DisplayName} cases" : $"{id} cases";
    }
}
