using System;
using System.Collections.Generic;
using Krofken.Ballistics;

namespace Gunsmith.Orders
{
    /// <summary>
    /// The orders available in the vertical slice.
    ///
    /// DESIGN INTENT: no two of these can be satisfied by the same round, and the
    /// conflicts are physical rather than arbitrary. Penetration and energy transfer
    /// are opposites -- a projectile that stopped inside gave up everything it had,
    /// one that exited kept some. Hardness defeats armour but refuses to expand.
    /// Brittleness dumps energy instantly but cannot reach anything deep.
    ///
    /// So a player who finds "the best bullet" and tries to sell it to everyone will
    /// fail four out of five briefs, and the physics will tell them exactly why.
    ///
    /// The briefs are written the way people talk. Nobody says "I require sub-30 cm
    /// penetration with no perforation" -- they say they work in crowds. Translating
    /// that into <see cref="OrderRequirement"/>s is the player's job; these are the
    /// answers, used only to judge the delivery.
    /// </summary>
    public static class OrderCatalogue
    {
        private static TargetLayer[] Gel(double metres) =>
            new[] { TargetLayer.Of(TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin), metres) };

        private static TargetLayer[] PlateThenGel(double plateMetres, double gelMetres) =>
            new[]
            {
                TargetLayer.Of(TargetMediumLibrary.Get(TargetMediumLibrary.MildSteelPlate), plateMetres),
                TargetLayer.Of(TargetMediumLibrary.Get(TargetMediumLibrary.Gelatin), gelMetres)
            };

        /// <summary>Every order the slice can offer.</summary>
        public static List<Order> All()
        {
            return new List<Order>
            {
                Hunter(),
                Bodyguard(),
                Watchman(),
                Ratcatcher(),
                Sailor()
            };
        }

        /// <summary>
        /// DEEP PENETRATION. Wants everything the bodyguard does not.
        /// A heavy, non-expanding, high-sectional-density projectile.
        /// </summary>
        public static Order Hunter() => new Order
        {
            Id = "hunter_boar",
            CustomerName = "Ilse Vanterpool",
            CustomerRole = "Hunter",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Quantity = 12,
            Payment = 90,
            EvaluationRange = 40.0,
            EvaluationTarget = Gel(0.80),
            Brief =
                "There's a boar working the barley on the east side. Big one, old. " +
                "I'll get maybe forty paces before it hears me, and I'll be taking it " +
                "through the shoulder — there's a lot of bone and gristle in front of " +
                "the heart on an animal that size. I need something that keeps going " +
                "after it's through all that. I don't care what it looks like coming out " +
                "the other side. I care that it gets there.",
            Requirements =
            {
                OrderRequirement.AtLeast(MeasuredQuantity.PenetrationDepth, 0.40,
                        "has to reach the heart through the shoulder", critical: true)
                    .WithConsequence(
                        "The boar ran. Ilse tracked it two days and found it dead in a ditch, " +
                        "wasted. She was not angry, which was worse."),

                OrderRequirement.AtLeast(MeasuredQuantity.ImpactEnergy, 280.0,
                    "still hitting hard at forty paces"),

                OrderRequirement.AtLeast(MeasuredQuantity.StabilityFactor, 1.4,
                        "flying true at that distance")
                    .WithConsequence("She said the holes in the target were sideways.")
            }
        };

        /// <summary>
        /// NO OVER-PENETRATION. The exact opposite of the hunter's brief.
        /// Wants an expanding projectile that stops inside a torso.
        /// </summary>
        public static Order Bodyguard() => new Order
        {
            Id = "bodyguard_crowd",
            CustomerName = "Adrien Kass",
            CustomerRole = "House guard",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Quantity = 20,
            Payment = 140,
            EvaluationRange = 7.0,
            // Thirty centimetres: the depth of the person being shot at. Anything
            // that comes out the far side of this block came out of the far side of
            // a human being.
            EvaluationTarget = Gel(0.30),
            Brief =
                "I walk the merchant's daughter through the market four days a week. " +
                "If it ever happens it happens at arm's length, in a crowd, with people " +
                "standing behind whoever I'm shooting at. Understand me: whatever I put " +
                "into a man has to stay in him. All of it. I'd rather it did nothing at " +
                "all than come out the back and find someone else.",
            Requirements =
            {
                OrderRequirement.MustBe(MeasuredQuantity.Perforated, false,
                        "must not pass through", critical: true)
                    .WithConsequence(
                        "There was an incident at the market. The man Adrien shot went down. " +
                        "So did a cooper's apprentice standing eight feet behind him, who had " +
                        "nothing to do with any of it. Adrien has not come back."),

                OrderRequirement.EnergyWithin(0.30, 300.0,
                    "still has to put him down"),

                OrderRequirement.MustBe(MeasuredQuantity.Fragmented, false,
                        "shouldn't come apart in him")
                    .WithConsequence("The surgeon spent an hour picking pieces out. It was not clean work."),

                OrderRequirement.AtLeast(MeasuredQuantity.ExpansionRatio, 1.30,
                    "needs to open up and stay put")
            }
        };

        /// <summary>
        /// ARMOUR PIERCING. Needs a hard core that will not deform -- which rules out
        /// every material that would satisfy the bodyguard.
        /// </summary>
        public static Order Watchman() => new Order
        {
            Id = "watch_plate",
            CustomerName = "Sergeant Bruhn",
            CustomerRole = "City watch",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Quantity = 16,
            Payment = 170,
            EvaluationRange = 25.0,
            EvaluationTarget = PlateThenGel(0.003, 0.40),
            Brief =
                "The crew working the north road have got hold of boiler plate. They've " +
                "cut it into squares and sewn it into their coats — front and back, over " +
                "the chest. We put four rounds into one of them last week and every one " +
                "of them spread out flat and fell in the mud. I need something that goes " +
                "through the plate and still has business on the other side.",
            Requirements =
            {
                OrderRequirement.AtLeast(MeasuredQuantity.PenetrationDepth, 0.08,
                        "through the plate and well past it", critical: true)
                    .WithConsequence(
                        "It flattened on the plate like all the others. Two of the watch " +
                        "were hurt badly enough that they will not be walking the north road again."),

                OrderRequirement.AtMost(MeasuredQuantity.ExpansionRatio, 1.10,
                    "mustn't spread out on the steel"),

                OrderRequirement.AtLeast(MeasuredQuantity.ImpactVelocity, 260.0,
                    "hitting hard at twenty-five paces")
            }
        };

        /// <summary>
        /// INCENDIARY. Needs a reactive payload that will actually initiate on a soft
        /// target, which rules out the compounds that need a hard impact.
        /// </summary>
        public static Order Ratcatcher() => new Order
        {
            Id = "granary_burn",
            CustomerName = "Maren Toft",
            CustomerRole = "Granary keeper",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Quantity = 8,
            Payment = 120,
            EvaluationRange = 12.0,
            EvaluationTarget = Gel(0.50),
            Brief =
                "Something has got into the old grain store and I will not be going in " +
                "after it. It nests up in the rafters where I cannot reach and it does not " +
                "die when you hit it — I have hit it. What I need is something that starts " +
                "a fire inside the thing. Kill it or drive it out, I do not much mind which, " +
                "but there has to be heat in it and the heat has to happen in the animal, " +
                "not on the floor underneath.",
            Requirements =
            {
                OrderRequirement.AtLeast(MeasuredQuantity.ReactiveEnergyReleased, 400.0,
                        "has to set light to it from inside", critical: true)
                    .WithConsequence(
                        "Nothing caught. Maren says the rounds went in and out and the thing " +
                        "is still up there, and now it is angry."),

                OrderRequirement.AtLeast(MeasuredQuantity.ImpactEnergy, 200.0,
                    "still carrying at twelve paces"),

                OrderRequirement.AtMost(MeasuredQuantity.PenetrationDepth, 0.45,
                        "mustn't punch clean through into the grain")
                    .WithConsequence("A quarter of the harvest smouldered. That cost her more than the rounds did.")
            }
        };

        /// <summary>
        /// FRANGIBLE. Must come apart on impact -- which means a brittle core, the
        /// one property that guarantees it can never satisfy the hunter or the watch.
        /// </summary>
        public static Order Sailor() => new Order
        {
            Id = "hold_frangible",
            CustomerName = "Bosun Alcide",
            CustomerRole = "Ship's bosun",
            CaseId = CartridgeCaseLibrary.NineMillimetre,
            Quantity = 24,
            Payment = 155,
            EvaluationRange = 5.0,
            EvaluationTarget = Gel(0.60),
            Brief =
                "Below decks it is all oak ribs and iron knees and there is nowhere for a " +
                "round to go but back at you. I have watched a man shoot at a thief in the " +
                "hold and put the ball through his own thigh on the second bounce. I want " +
                "something that stops being a bullet the moment it touches anything. Break " +
                "it up, spend it, I do not care how ugly it is — just do not let it come back.",
            Requirements =
            {
                OrderRequirement.MustBe(MeasuredQuantity.Fragmented, true,
                        "must come apart on contact", critical: true)
                    .WithConsequence(
                        "It stayed whole, glanced off a knee-iron and went through the " +
                        "cook's shoulder. Alcide paid the surgeon himself and has not spoken to you since."),

                OrderRequirement.AtMost(MeasuredQuantity.FragmentationDepth, 0.05,
                    "has to break up straight away, not halfway through"),

                OrderRequirement.AtMost(MeasuredQuantity.PenetrationDepth, 0.20,
                    "nothing left to carry on"),

                OrderRequirement.MustBe(MeasuredQuantity.Perforated, false,
                    "and nothing coming out the far side")
            }
        };

        /// <summary>
        /// Picks the orders that turn up on a given day.
        ///
        /// Seeded so a day is reproducible: reloading a save must not reroll the
        /// board. The randomness is in WHO shows up, never in whether a round works.
        /// </summary>
        public static List<Order> ForDay(int day, int count = 3, int seed = 0)
        {
            var pool = All();
            var random = new Random(unchecked(seed * 73856093 ^ day * 19349663));

            // Fisher-Yates over the pool, then take the first `count`.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            if (count > pool.Count) count = pool.Count;
            return pool.GetRange(0, count);
        }
    }
}
