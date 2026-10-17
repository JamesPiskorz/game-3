using System.Linq;
using Game.Characters.Content;
using Game.Characters.Generation;
using Game.Characters.Rng;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>
    /// A player mashing buttons for weeks of game time: every verb, in any order, with any arguments the UI could
    /// send. Nothing may throw, and the world must stay consistent after every step.
    /// </summary>
    public class FuzzTests
    {
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void ARandomPlayerNeverBreaksTheWorld(ulong seed)
        {
            SimWorld world = Fixtures.Town(seed * 7919, 20);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(seed);
            world.Player.Money = 300;
            var session = new TownSession(world);
            var rng = new Pcg32(seed, 99);
            string[] places = world.Catalog.Locations.Select(l => l.Id).Append(SimCatalog.Home).ToArray();

            for (int step = 0; step < 1000; step++)
            {
                int verb = rng.NextInt(16);
                string action = verb.ToString();
                switch (verb)
                {
                    case 0: session.Travel(rng.Pick(places)); break;
                    case 1:
                    case 2:
                        var present = session.Present();
                        if (present.Count > 0) session.Select(rng.Pick(present));
                        break;
                    case 3: session.Greet(); break;
                    case 4:
                    case 5: session.Talk(rng.Pick(world.Catalog.Topics).Id); break;
                    case 6: session.Compliment(); break;
                    case 7: session.Gift(rng.Pick(world.Catalog.Gifts).Id); break;
                    case 8: session.AskOnDate(rng.Pick(world.Catalog.Locations).Id); break;
                    case 9:
                        if (session.Date != null && !session.Date.Finished) session.DateChoose(rng.NextInt(session.Date.Options.Count));
                        else session.EndDate();
                        break;
                    case 10: session.Wait(rng.Range(15, 180)); break;
                    case 11: if (rng.Chance(0.3f)) session.Sleep(); break;
                    case 12: session.Work(); break;
                    case 13:
                        var stock = session.Shop();
                        if (stock.Count > 0) session.Buy(rng.Pick(stock));
                        break;
                    case 14:
                        var wardrobe = session.Changeable();
                        if (wardrobe.Count > 0) session.Wear(rng.Pick(wardrobe));
                        break;
                    case 15:
                        var contacts = session.Contacts();
                        if (contacts.Count > 0) session.Find(rng.Pick(contacts));
                        break;
                }
                Invariants(session, $"seed {seed}, step {step}, verb {action}");
            }

            string json = SimSave.Capture(world).ToJson();
            SimWorld loaded = SimSave.FromJson(json).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(SimSave.Capture(loaded).ToJson(), Is.EqualTo(json), "A save after weeks of play loads back identically.");
        }

        static void Invariants(TownSession session, string where)
        {
            SimWorld world = session.World;
            Assert.That(world.Player.Money, Is.GreaterThanOrEqualTo(0), where);
            foreach (Relationship rel in world.Relationships)
                foreach (float meter in new[] { rel.Familiarity, rel.Affection, rel.Attraction, rel.Trust })
                    Assert.That(meter, Is.InRange(0f, 100f), where);

            // The outfit is always wearable.
            var outfit = world.Player.Dna.Outfits.First(o => o.Context == world.Player.Outfit);
            var slots = outfit.Items.Select(i => world.Characters.Part(i.ItemId).Slot).ToList();
            Assert.That(slots, Is.Unique, where);
            Assert.That(slots.Contains(Slots.OnePiece) || (slots.Contains(Slots.Top) && slots.Contains(Slots.Bottom)), where);

            // Couples are pairs, and nobody dating the player is also with someone else.
            for (int i = 0; i < world.Residents.Count; i++)
            {
                int partner = world.Social.PartnerOf(i);
                if (partner < 0) continue;
                Assert.That(world.Social.PartnerOf(partner), Is.EqualTo(i), where);
            }

            // Whoever's selected is actually here.
            if (session.Selected != null) Assert.That(session.Present(), Does.Contain(session.Selected), where);
            if (session.DateWith != null) Assert.That(session.Date, Is.Not.Null, where);
        }
    }
}
