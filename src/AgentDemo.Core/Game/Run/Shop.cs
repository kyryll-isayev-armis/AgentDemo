namespace AgentDemo.Web.Game.Run;

public enum ShopOfferKind { Modifier, Tune }

public sealed record ShopOffer(ShopOfferKind Kind, ModifierId Modifier, int Lane, int Cost);

public static class ShopGenerator
{
    public const int OfferCount = 3;

    /// <summary>
    /// Three offers: [0] a modifier, [1] a modifier (pity: synergy-capable with what you own when possible),
    /// [2] a lane Tune (pity: if nothing is affordable, the cheapest item available).
    /// </summary>
    public static ShopOffer?[] Generate(DeterministicRng rng, IReadOnlyCollection<ModifierId> owned, LaneLevels lanes, int cred)
    {
        var modPool = ModifierCatalog.All.Where(m => !owned.Contains(m.Id)).ToList();
        var tunePool = Enumerable.Range(0, Circuit.Lines.Length).Where(lanes.CanTune).ToList();
        var offers = new ShopOffer?[OfferCount];

        offers[0] = DrawModifier(rng, modPool, offers);
        offers[1] = DrawModifier(rng, modPool, offers);
        offers[2] = tunePool.Count > 0 ? TuneOffer(lanes, rng.Pick(tunePool)) : DrawModifier(rng, modPool, offers);

        var ownedTags = owned.Aggregate(ModifierTags.None, (t, id) => t | ModifierCatalog.Get(id).Tags);
        if (ownedTags != ModifierTags.None && !offers.Any(o => IsSynergy(o, ownedTags)))
        {
            var synergy = modPool
                .Where(m => (m.Tags & ownedTags) != 0 && !offers.Any(o => o is { Kind: ShopOfferKind.Modifier } x && x.Modifier == m.Id))
                .ToList();
            if (synergy.Count > 0) offers[1] = ModifierOffer(Weighted(rng, synergy));
        }

        if (!offers.Any(o => o is not null && o.Cost <= cred))
        {
            var cheapest = CheapestCandidate(offers, modPool, tunePool, lanes, cred);
            if (cheapest is not null)
            {
                // Never overwrite the only synergy offer.
                var slot = IsSynergy(offers[2], ownedTags) && offers.Count(o => IsSynergy(o, ownedTags)) == 1 ? 0 : 2;
                offers[slot] = cheapest;
            }
        }

        return offers;
    }

    public static ShopOffer ModifierOffer(Modifier m) => new(ShopOfferKind.Modifier, m.Id, -1, m.Cost);
    public static ShopOffer TuneOffer(LaneLevels lanes, int lane) => new(ShopOfferKind.Tune, default, lane, lanes.TuneCost(lane));

    static bool IsSynergy(ShopOffer? offer, ModifierTags ownedTags) =>
        offer is { Kind: ShopOfferKind.Modifier } && (ModifierCatalog.Get(offer.Modifier).Tags & ownedTags) != 0;

    static ShopOffer? DrawModifier(DeterministicRng rng, List<Modifier> pool, ShopOffer?[] offers)
    {
        var available = pool.Where(m => !offers.Any(o => o is { Kind: ShopOfferKind.Modifier } x && x.Modifier == m.Id)).ToList();
        return available.Count == 0 ? null : ModifierOffer(Weighted(rng, available));
    }

    static Modifier Weighted(DeterministicRng rng, List<Modifier> pool)
    {
        var total = pool.Sum(m => m.Rarity == Rarity.Common ? 3 : 1);
        var roll = rng.NextInt(total);
        foreach (var m in pool)
        {
            roll -= m.Rarity == Rarity.Common ? 3 : 1;
            if (roll < 0) return m;
        }
        return pool[^1];
    }

    static ShopOffer? CheapestCandidate(ShopOffer?[] current, List<Modifier> modPool, List<int> tunePool, LaneLevels lanes, int cred)
    {
        var candidates = new List<ShopOffer>();
        candidates.AddRange(tunePool.Select(l => TuneOffer(lanes, l)));
        candidates.AddRange(modPool.Select(ModifierOffer));
        // Prefer something affordable; if nothing is (Cred < 3 or in debt), show the cheapest goal.
        return candidates
            .Where(c => !current.Contains(c))
            .OrderBy(c => c.Cost <= cred ? 0 : 1).ThenBy(c => c.Cost).ThenBy(c => c.Kind).ThenBy(c => c.Modifier).ThenBy(c => c.Lane)
            .FirstOrDefault();
    }
}
