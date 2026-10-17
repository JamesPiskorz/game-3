using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class BoutiqueTests
    {
        static TownSession AtTheMall(int money = 500)
        {
            SimWorld world = Fixtures.Town(0xB0B, 12);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(77);
            world.Player.Location = Boutique.Location;
            world.Player.Money = money;
            world.Now = SimTime.At(2, 12);
            return new TownSession(world);
        }

        static string SlotOf(TownSession session, ItemDna item) => session.World.Characters.Part(item.ItemId).Slot;

        static OutfitDna Wearing(TownSession session) => session.World.Player.Dna.Outfits.First(o => o.Context == session.World.Player.Outfit);

        [Test]
        public void StockIsClothingThatChangesDaily()
        {
            TownSession session = AtTheMall();
            var today = Boutique.Stock(session.World, 2);
            Assert.That(today.Count, Is.EqualTo(Boutique.StockSize));
            Assert.That(today.Select(s => s.Item.ItemId), Is.Unique);
            Assert.That(today.All(s => s.Price > 0 && !s.Part.Slot.StartsWith("hair")));
            Assert.That(Boutique.Stock(session.World, 2).Select(s => s.Item.ItemId), Is.EqualTo(today.Select(s => s.Item.ItemId)), "Same day, same stock.");
            Assert.That(Enumerable.Range(3, 5).Any(day => !Boutique.Stock(session.World, day).Select(s => s.Item.ItemId).SequenceEqual(today.Select(s => s.Item.ItemId))));
        }

        [Test]
        public void TheShopIsOnlyAtTheMallWhileItsOpen()
        {
            TownSession session = AtTheMall();
            Assert.That(session.Shop(), Is.Not.Empty);
            session.World.Now = SimTime.At(2, 22);
            Assert.That(session.Shop(), Is.Empty);
            session.World.Now = SimTime.At(2, 12);
            session.World.Player.Location = "park";
            Assert.That(session.Shop(), Is.Empty);
        }

        [Test]
        public void YouStartOwningWhatYouWear()
        {
            TownSession session = AtTheMall();
            foreach (ItemDna item in session.World.Player.Dna.Outfits.SelectMany(o => o.Items))
                Assert.That(session.Owns(item));
        }

        [Test]
        public void BuyingCostsMoneyAndPutsItOn()
        {
            TownSession session = AtTheMall();
            ShopItem item = session.Shop().First(s => !session.Owns(s.Item));
            int before = session.World.Player.Money;
            int look = session.World.Player.LookVersion;

            session.Buy(item);

            Assert.That(session.World.Player.Money, Is.EqualTo(before - item.Price));
            Assert.That(session.Owns(item.Item));
            Assert.That(Wearing(session).Items.Any(i => i.ItemId == item.Item.ItemId), session.Notice);
            Assert.That(session.World.Player.LookVersion, Is.GreaterThan(look));
        }

        [Test]
        public void YouCantBuyWhatYouCantAfford()
        {
            TownSession session = AtTheMall(money: 5);
            ShopItem item = session.Shop().First();
            session.Buy(item);
            Assert.That(session.World.Player.Money, Is.EqualTo(5));
            Assert.That(session.Owns(item.Item), Is.False);
            Assert.That(session.Notice, Does.Contain("coins"));
        }

        [Test]
        public void OutfitsStayWearable()
        {
            // Swap through every piece in the shop over a week: the outfit always has a one-piece, or a top and a bottom.
            TownSession session = AtTheMall(money: 10000);
            for (int day = 0; day < 7; day++)
            {
                session.World.Now = SimTime.At(day, 12);
                foreach (ShopItem item in session.Shop())
                {
                    session.Buy(item);
                    var slots = Wearing(session).Items.Select(i => SlotOf(session, i)).ToList();
                    Assert.That(slots, Is.Unique, "One item per slot.");
                    Assert.That(slots.Contains(Slots.OnePiece) || (slots.Contains(Slots.Top) && slots.Contains(Slots.Bottom)), string.Join(", ", slots));
                    if (slots.Contains(Slots.OnePiece)) Assert.That(slots, Has.None.EqualTo(Slots.Top).And.None.EqualTo(Slots.Bottom));
                }
            }
        }

        [Test]
        public void TheWardrobeSwapsBack()
        {
            TownSession session = AtTheMall();
            ItemDna original = Wearing(session).Items.First(i => SlotOf(session, i) == Slots.Shoes);
            ShopItem shoes = Enumerable.Range(0, 30).SelectMany(day => Boutique.Stock(session.World, day))
                .FirstOrDefault(s => s.Part.Slot == Slots.Shoes && s.Item.ItemId != original.ItemId);
            Assume.That(shoes, Is.Not.Null);
            session.World.Player.Wardrobe.Add(shoes.Item);

            Assert.That(session.Wear(shoes.Item));
            Assert.That(session.Changeable().Any(i => i.ItemId == original.ItemId));
            Assert.That(session.Wear(original));
            Assert.That(Wearing(session).Items.Any(i => i.ItemId == original.ItemId));
        }

        [Test]
        public void WorkPaysByTheHour()
        {
            TownSession session = AtTheMall(money: 0);
            Assert.That(session.CanWork);
            session.Work();
            Assert.That(session.World.Player.Money, Is.EqualTo(TownSession.ShiftHours * TownSession.PayPerHour));
            Assert.That(session.World.Now.Hour, Is.EqualTo(12 + TownSession.ShiftHours));

            session.World.Player.Location = "park";
            Assert.That(session.CanWork, Is.False, "Nobody's hiring in the park.");
        }

        /// <summary>The point of the shop: dressing in someone's style makes a better first impression.</summary>
        [Test]
        public void ShoppingForTheirStyleImpressesThem()
        {
            TownSession session = AtTheMall(money: 100000);
            SimWorld world = session.World;
            Resident gothic = Fixtures.Town(0xB0B, 40).Residents.First(r => r.Profile.Archetype == "gothic");
            float before = FirstImpression.Score(gothic.Profile, world.Player.Dna, world.Player.Outfit, world.Characters);

            for (int day = 0; day < 20; day++)
                foreach (ShopItem item in Boutique.Stock(world, day).Where(s => s.Part.Tags.Contains("gothic")))
                    if (!session.Owns(item.Item))
                    {
                        world.Player.Wardrobe.Add(item.Item);
                        session.Wear(item.Item);
                    }

            float after = FirstImpression.Score(gothic.Profile, world.Player.Dna, world.Player.Outfit, world.Characters);
            Assert.That(after, Is.GreaterThan(before), $"before {before:F2}, after {after:F2}");
        }

        [Test]
        public void MoneyAndWardrobeAreSaved()
        {
            TownSession session = AtTheMall();
            session.Buy(session.Shop().First(s => !session.Owns(s.Item)));
            string json = SimSave.Capture(session.World).ToJson();
            SimWorld loaded = SimSave.FromJson(json).Restore(Fixtures.Sim(), Fixtures.Characters());

            Assert.That(loaded.Player.Money, Is.EqualTo(session.World.Player.Money));
            Assert.That(loaded.Player.Wardrobe.Count, Is.EqualTo(session.World.Player.Wardrobe.Count));
            Assert.That(SimSave.Capture(loaded).ToJson(), Is.EqualTo(json));
        }
        [Test]
        public void ChangingIntoTrousersTakesTheTightsOff()
        {
            TownSession session = AtTheMall();
            SimWorld world = session.World;
            ItemDna Item(string id) => new ItemDna { ItemId = id, RegionColors = Enumerable.Repeat(new HsvColor(0.6f, 0.5f, 0.5f), world.Characters.Part(id).Regions).ToList() };
            OutfitDna outfit = Wearing(session);
            outfit.Items = new[] { "cloth.top.tshirt_01", "cloth.bottom.skirt_01", "cloth.legwear.tights_01", "cloth.shoes.sneakers_01" }.Select(Item).ToList();
            ItemDna pants = Item("cloth.bottom.pants_01");
            world.Player.Wardrobe.AddRange(outfit.Items.Select(i => Item(i.ItemId)));
            world.Player.Wardrobe.Add(Item(pants.ItemId));

            Assert.That(session.Wear(pants), session.Notice);
            Assert.That(Wearing(session).Items.Select(i => i.ItemId), Does.Contain("cloth.bottom.pants_01").And.Not.Contain("cloth.legwear.tights_01"));
            Assert.That(session.Notice, Does.Contain("tights come off"));
        }
    }
}
