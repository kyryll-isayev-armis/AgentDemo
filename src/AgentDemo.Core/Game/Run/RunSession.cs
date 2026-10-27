using System.Globalization;
using System.Text.Json;

namespace AgentDemo.Web.Game.Run;

public enum RunPhase { Encounter, Shop, Cleared, Failed }

public enum RunActionKind { Place, Buy, Sell, Reroll, Leave }

public readonly record struct RunAction(RunActionKind Kind, int Arg = 0)
{
    public static RunAction Place(int cell) => new(RunActionKind.Place, cell);
    public static RunAction Buy(int offer) => new(RunActionKind.Buy, offer);
    public static RunAction Sell(int slot) => new(RunActionKind.Sell, slot);
    public static RunAction Reroll() => new(RunActionKind.Reroll);
    public static RunAction Leave() => new(RunActionKind.Leave);

    public string Encode() => Kind switch
    {
        RunActionKind.Place => $"P{Arg}",
        RunActionKind.Buy => $"B{Arg}",
        RunActionKind.Sell => $"S{Arg}",
        RunActionKind.Reroll => "R",
        _ => "L",
    };

    public static bool TryParse(string? text, out RunAction action)
    {
        action = default;
        if (string.IsNullOrEmpty(text)) return false;
        var arg = 0;
        if (text.Length > 1 && !int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out arg)) return false;
        var parsed = text[0] switch
        {
            'P' when text.Length > 1 => Place(arg),
            'B' when text.Length > 1 => Buy(arg),
            'S' when text.Length > 1 => Sell(arg),
            'R' when text.Length == 1 => Reroll(),
            'L' when text.Length == 1 => Leave(),
            _ => (RunAction?)null,
        };
        // Only the canonical encoding is accepted (no padding, signs or leading zeros).
        if (parsed is not { } result || result.Encode() != text) return false;
        action = result;
        return true;
    }
}

public sealed record ActionResult(bool Ok, string? Error = null);

/// <summary>Versioned save: the run is fully reproduced from the seed plus the accepted action log.</summary>
public sealed record RunSave(int Version, ulong Seed, List<string> Actions, int ContentVersion = 0)
{
    public const int CurrentVersion = 1;

    // Bump whenever modifiers, encounters, shop or scoring balance change: old action logs would replay differently.
    public const int CurrentContentVersion = 1;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static RunSave? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var save = JsonSerializer.Deserialize<RunSave>(json);
            return save is { Actions: not null } && save.Actions.Count <= RunSession.MaxSavedActions ? save : null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

public sealed class RunSession
{
    public const int MaxSlots = 4;
    public const int MaxSavedActions = 500;
    public const int StartingCred = 6;
    public const int BossBonusCred = 4;
    public const int RerollBaseCost = 2;

    readonly DeterministicRng _root;
    readonly List<Modifier> _mods = [];
    readonly List<string> _actions = [];
    readonly List<ShopOffer?> _offers = [];
    int _flushed;
    int _rerolls;

    public RunSession(ulong seed, IReadOnlyList<Encounter>? encounters = null)
    {
        Seed = seed;
        _root = new DeterministicRng(seed);
        Encounters = encounters ?? Acts.Act1;
        Cred = StartingCred;
        Log.Add(new GameEvent(GameEventKind.RunStarted, (int)(seed & 0x7FFFFFFF)));
        StartRound();
    }

    public ulong Seed { get; }
    public IReadOnlyList<Encounter> Encounters { get; }
    public int EncounterIndex { get; private set; }
    public RunPhase Phase { get; private set; } = RunPhase.Encounter;
    public int Cred { get; private set; }
    public LaneLevels Lanes { get; } = new();
    public IReadOnlyList<Modifier> Mods => _mods;
    public IReadOnlyList<ShopOffer?> Offers => _offers;
    public Round Round { get; private set; } = null!;
    public List<GameEvent> Log { get; } = [];
    public IReadOnlyList<string> Actions => _actions;
    public int RerollCost => RerollBaseCost + _rerolls;

    public RunSave Save() => new(RunSave.CurrentVersion, Seed, [.. _actions], RunSave.CurrentContentVersion);

    public static RunSession? Resume(RunSave? save)
    {
        if (save?.Actions is null || save.Version != RunSave.CurrentVersion
            || save.ContentVersion != RunSave.CurrentContentVersion || save.Actions.Count > MaxSavedActions) return null;
        var session = new RunSession(save.Seed);
        try
        {
            foreach (var text in save.Actions)
            {
                if (!RunAction.TryParse(text, out var action) || !session.Apply(action).Ok) return null;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
        return session;
    }

    public ActionResult Apply(RunAction action)
    {
        var error = Execute(action);
        if (error is not null) return new ActionResult(false, error);
        _actions.Add(action.Encode());
        return new ActionResult(true);
    }

    public string Snapshot() =>
        $"{Phase}|{EncounterIndex}|{Cred}|{Lanes}|{string.Join(',', _mods.Select(m => m.Id))}|" +
        $"{Round.Circuit.Snapshot()}|{Round.Score}|{Round.Cycle}|{Round.Outcome}|" +
        string.Join(',', _offers.Select(o => o is null ? "-" : $"{o.Kind}{o.Modifier}{o.Lane}{o.Cost}"));

    string? Execute(RunAction action)
    {
        switch (action.Kind)
        {
            case RunActionKind.Place:
                if (Phase != RunPhase.Encounter) return "Not in an encounter.";
                var error = Round.TryPlace(action.Arg);
                if (error is not null) return error;
                FlushRoundEvents();
                AfterPlace();
                return null;
            case RunActionKind.Buy:
                return Phase != RunPhase.Shop ? "Not in the shop." : Buy(action.Arg);
            case RunActionKind.Sell:
                return Phase != RunPhase.Shop ? "Not in the shop." : Sell(action.Arg);
            case RunActionKind.Reroll:
                return Phase != RunPhase.Shop ? "Not in the shop." : Reroll();
            case RunActionKind.Leave:
                if (Phase != RunPhase.Shop) return "Not in the shop.";
                EncounterIndex++;
                Phase = RunPhase.Encounter;
                StartRound();
                return null;
            default:
                return "Unknown action.";
        }
    }

    void AfterPlace()
    {
        switch (Round.Outcome)
        {
            case RoundOutcome.Cleared:
                Cred += Round.Reward + (Round.Encounter.IsBoss ? BossBonusCred : 0);
                if (EncounterIndex >= Encounters.Count - 1)
                {
                    Phase = RunPhase.Cleared;
                    Log.Add(new GameEvent(GameEventKind.RunCleared, Cred));
                }
                else
                {
                    Phase = RunPhase.Shop;
                    _rerolls = 0;
                    GenerateOffers();
                    Log.Add(new GameEvent(GameEventKind.ShopOpened, EncounterIndex));
                }
                break;
            case RoundOutcome.Failed:
                Phase = RunPhase.Failed;
                Log.Add(new GameEvent(GameEventKind.RunFailed, EncounterIndex));
                break;
        }
    }

    string? Buy(int index)
    {
        if (index < 0 || index >= _offers.Count || _offers[index] is not { } offer) return "No such offer.";
        if (Cred < offer.Cost) return "Not enough Cred.";
        if (offer.Kind == ShopOfferKind.Modifier)
        {
            if (_mods.Count >= MaxSlots) return "All slots are full; sell a modifier first.";
            _mods.Add(ModifierCatalog.Get(offer.Modifier));
            Log.Add(new GameEvent(GameEventKind.Purchased, (int)offer.Modifier, offer.Cost));
        }
        else
        {
            if (!Lanes.CanTune(offer.Lane)) return "Lane is already at max level.";
            Lanes.Tune(offer.Lane);
            Log.Add(new GameEvent(GameEventKind.Tuned, offer.Lane, Lanes[offer.Lane], offer.Cost));
        }
        Cred -= offer.Cost;
        _offers[index] = null;
        return null;
    }

    string? Sell(int slot)
    {
        if (slot < 0 || slot >= _mods.Count) return "No such slot.";
        var mod = _mods[slot];
        _mods.RemoveAt(slot);
        Cred += mod.SellValue;
        Log.Add(new GameEvent(GameEventKind.Sold, (int)mod.Id, mod.SellValue));
        return null;
    }

    string? Reroll()
    {
        var cost = RerollCost;
        if (Cred < cost) return "Not enough Cred.";
        Cred -= cost;
        _rerolls++;
        GenerateOffers();
        Log.Add(new GameEvent(GameEventKind.Rerolled, cost));
        return null;
    }

    void GenerateOffers()
    {
        var rng = _root.Fork($"shop:{EncounterIndex}:{_rerolls}");
        _offers.Clear();
        _offers.AddRange(ShopGenerator.Generate(rng, _mods.Select(m => m.Id).ToList(), Lanes, Cred));
    }

    void StartRound()
    {
        var rival = new SeededRival(_root.Fork($"rival:{EncounterIndex}"));
        Round = new Round(Encounters[EncounterIndex], [.. _mods], Lanes.Clone(), rival);
        _flushed = 0;
        Log.Add(new GameEvent(GameEventKind.EncounterStarted, EncounterIndex, Round.Quota, Round.Cycles));
    }

    void FlushRoundEvents()
    {
        for (; _flushed < Round.Events.Count; _flushed++) Log.Add(Round.Events[_flushed]);
    }
}
