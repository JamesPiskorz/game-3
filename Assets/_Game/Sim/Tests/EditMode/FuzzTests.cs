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
                int verb = rng.NextInt(27);
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
                    case 16:
                        var activities = session.Activities();
                        if (activities.Count > 0) session.DoActivity(rng.Pick(activities).Id);
                        break;
                    case 17: session.StartChat(); break;
                    case 18: session.ChatReply(rng.NextInt(3)); break;
                    case 19: session.Haircut(rng.Pick(Salon.Cuts(world.Characters)).Id); break;
                    case 20:
                        var bangs = Salon.Bangs(world.Characters);
                        session.Bangs(rng.Chance(0.25f) ? null : rng.Pick(bangs).Id);
                        break;
                    case 21: session.Dye(rng.Pick(Salon.Colours(world.Characters))); break;
                    case 22:
                        var spare = session.Giveable();
                        if (spare.Count > 0) session.GiveClothes(rng.Pick(spare));
                        break;
                    case 23: session.AnswerConfession(rng.Chance(0.5f)); break;
                    case 24: session.Lipstick(rng.Chance(0.3f) ? (Game.Characters.Dna.HsvColor?)null : rng.Pick(Salon.Lipsticks(world.Characters))); break;
                    case 25: session.Blush(rng.Chance(0.6f)); break;
                    case 26:
                        // Now and then, someone here has quietly fallen for the player since yesterday, so confessions come up.
                        if (session.Selected != null && rng.Chance(0.15f))
                        {
                            Relationship smitten = world.RelationshipWith(session.Selected);
                            smitten.Met = true;
                            smitten.Familiarity = System.Math.Max(smitten.Familiarity, 65f);
                            smitten.Affection = System.Math.Max(smitten.Affection, 80f);
                            smitten.Attraction = System.Math.Max(smitten.Attraction, 85f);
                            smitten.Trust = System.Math.Max(smitten.Trust, 40f);
                            smitten.LastSeenDay = world.Now.Day - 1;
                        }
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

            // The outfit is always wearable, with the hair the player has now.
            var outfit = world.Player.Dna.Outfits.First(o => o.Context == world.Player.Outfit);
            var slots = outfit.Items.Select(i => world.Characters.Part(i.ItemId).Slot).ToList();
            Assert.That(slots, Is.Unique, where);
            Assert.That(slots.Contains(Slots.OnePiece) || (slots.Contains(Slots.Top) && slots.Contains(Slots.Bottom)), where);
            Assert.That(DnaValidator.Validate(world.Characters, world.Player.Dna), Is.Empty, where);

            // Couples are pairs, and nobody dating the player is also with someone else.
            for (int i = 0; i < world.Residents.Count; i++)
            {
                int partner = world.Social.PartnerOf(i);
                if (partner < 0) continue;
                Assert.That(world.Social.PartnerOf(partner), Is.EqualTo(i), where);
            }

            // Residents stay valid through makeovers and gifts (checked for whoever's selected, to keep it quick).
            if (session.Selected != null) Assert.That(DnaValidator.Validate(world.Characters, session.Selected.Dna), Is.Empty, where);
            if (session.Confession != null) Assert.That(session.Confession.With, Is.EqualTo(session.Selected), where);

            // Whoever's selected is actually here.
            if (session.Selected != null) Assert.That(session.Present(), Does.Contain(session.Selected), where);
            if (session.DateWith != null) Assert.That(session.Date, Is.Not.Null, where);
        }
    }
}
