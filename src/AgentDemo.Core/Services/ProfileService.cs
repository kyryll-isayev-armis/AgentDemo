using System.Globalization;
using System.Text.Json;
using AgentDemo.Web.Game;
using AgentDemo.Web.Game.Run;

namespace AgentDemo.Web.Services;

public interface IProfileService
{
    // Reads the stored profile (defaults if missing/corrupt/old version) and caches it.
    Task<ProfileData> LoadAsync();

    // Validates and persists the whole profile.
    Task SaveAsync(ProfileData data);

    Task SaveSettingsAsync(UserSettings settings);

    // Prepends to history (cap 20), updates lifetime stats, persists.
    Task<ProfileData> RecordMatchAsync(MatchRecord record);

    Task ClearHistoryAsync();

    // Persists (or clears, when null) the in-progress OVERCLOCK run.
    Task SaveRunAsync(AgentDemo.Web.Game.Run.RunSave? save);

    // Persists (or clears, when null) the in-progress Classic/Decay match.
    Task SaveMatchAsync(MatchSnapshot? snapshot);
}

public sealed class ProfileService(ILocalStore store) : IProfileService
{
    public const string StorageKey = "gridzero.v1";
    public const string CorruptKey = "gridzero.v1.corrupt";

    // 2 added activeMatch and run contentVersion; version 1 data is migrated on load.
    public const int Version = 2;
    public const int MaxHistory = 20;
    private const int MaxScore = 99;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProfileData? _cache;

    public async Task<ProfileData> LoadAsync()
    {
        string? json = null;
        try
        {
            json = await store.GetAsync(StorageKey);
        }
        catch (Exception)
        {
        }

        var status = TryParse(json, out var data);
        if (status == ParseStatus.Unreadable)
        {
            // Never delete what we could not read: keep a copy for recovery.
            await SetGuardedAsync(CorruptKey, json!);
        }

        _cache = data;
        if (status == ParseStatus.Migrated || data.RunRetired)
        {
            var retired = data.RunRetired;
            await SaveAsync(data);
            _cache!.RunRetired = retired;
        }
        return _cache!;
    }

    public async Task SaveAsync(ProfileData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var clean = Sanitize(data);
        await _gate.WaitAsync();
        try
        {
            _cache = clean;
            await store.SetAsync(StorageKey, Serialize(clean));
        }
        catch (Exception)
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetGuardedAsync(string key, string value)
    {
        await _gate.WaitAsync();
        try
        {
            await store.SetAsync(key, value);
        }
        catch (Exception)
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSettingsAsync(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var data = _cache ?? await LoadAsync();
        data.Settings = settings;
        await SaveAsync(data);
    }

    public async Task<ProfileData> RecordMatchAsync(MatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var data = _cache ?? await LoadAsync();
        var clean = SanitizeRecord(record);
        data.History.Insert(0, clean);
        if (data.History.Count > MaxHistory) data.History.RemoveRange(MaxHistory, data.History.Count - MaxHistory);
        data.Stats.Add(clean);

        var date = DateOnly.FromDateTime(clean.CompletedAt.UtcDateTime);
        if (!clean.Assisted && clean.Settings.Opponent == Opponent.Ai
            && Challenges.IsCompleted(Challenges.ForDate(date), clean))
        {
            data.Challenge = data.Challenge.Complete(date);
        }

        foreach (var earned in Achievements.Earned(clean, data.Stats.Total.Total, data.Challenge.CurrentStreak(date)))
        {
            data.Achievements.TryAdd(earned, clean.CompletedAt);
        }

        await SaveAsync(data);
        return _cache!;
    }

    public async Task ClearHistoryAsync()
    {
        var data = _cache ?? await LoadAsync();
        data.History.Clear();
        await SaveAsync(data);
    }

    public async Task SaveRunAsync(RunSave? save)
    {
        var data = _cache ?? await LoadAsync();
        data.ActiveRun = save;
        await SaveAsync(data);
    }

    public async Task SaveMatchAsync(MatchSnapshot? snapshot)
    {
        var data = _cache ?? await LoadAsync();
        data.ActiveMatch = snapshot;
        await SaveAsync(data);
    }

    public const int MaxRunActions = RunSession.MaxSavedActions;

    // A run is kept only if its content version matches and its action log replays cleanly from the seed.
    internal static RunSave? SanitizeRun(RunSave? run)
    {
        if (run is null || run.Actions is null || run.Actions.Count > MaxRunActions) return null;
        var copy = new RunSave(run.Version, run.Seed, [.. run.Actions], run.ContentVersion);
        var session = RunSession.Resume(copy);
        return session is { Phase: RunPhase.Encounter or RunPhase.Shop } ? copy : null;
    }

    internal static MatchSnapshot? SanitizeMatch(MatchSnapshot? snapshot) =>
        Match.Restore(snapshot) is { IsMatchOver: false } match ? match.ToSnapshot() : null;

    internal static ProfileData Sanitize(ProfileData data)
    {
        var result = new ProfileData { Settings = (data.Settings ?? new UserSettings()).Normalized() };
        foreach (var r in (data.History ?? []).Where(r => r is not null).Take(MaxHistory))
        {
            result.History.Add(SanitizeRecord(r));
        }
        foreach (var d in Enum.GetValues<Difficulty>())
        {
            result.Stats.Set(d, data.Stats?[d] ?? new StatLine(0, 0, 0));
        }
        foreach (var (id, at) in data.Achievements ?? [])
        {
            if (Enum.IsDefined(id)) result.Achievements[id] = at;
        }
        var c = data.Challenge ?? new ChallengeProgress();
        result.Challenge = new ChallengeProgress(
            Math.Clamp(c.Streak, 0, ChallengeProgress.MaxCount),
            c.LastCompleted,
            Math.Clamp(c.TotalCompleted, 0, ChallengeProgress.MaxCount));
        result.ActiveRun = SanitizeRun(data.ActiveRun);
        result.ActiveMatch = SanitizeMatch(data.ActiveMatch);
        result.RunRetired = data.ActiveRun is not null && result.ActiveRun is null;
        return result;
    }

    private const int MaxRoundsStored = 15;

    private static MatchRecord SanitizeRecord(MatchRecord r)
    {
        var winner = Enum.IsDefined(r.Winner) ? r.Winner : Player.None;
        var settings = ProfileValidation.Normalize(r.Settings);
        var rounds = (r.Rounds ?? [])
            .Where(x => Replay.IsValid(settings.Rules, x))
            .Take(MaxRoundsStored)
            .Select(x => new RoundRecord(x.Starter, x.Moves.ToArray()))
            .ToArray();
        return new MatchRecord(
            settings,
            Math.Clamp(r.ScoreX, 0, MaxScore),
            Math.Clamp(r.ScoreO, 0, MaxScore),
            Math.Clamp(r.Draws, 0, MaxScore),
            Math.Clamp(r.RoundsPlayed, 0, MaxScore * 3),
            winner,
            r.CompletedAt)
        {
            Assisted = r.Assisted,
            Rounds = rounds
        };
    }

    internal static string Serialize(ProfileData data)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);

            var s = data.Settings;
            w.WriteStartObject("settings");
            w.WriteString("theme", s.Theme);
            w.WriteBoolean("sound", s.SoundEnabled);
            w.WriteBoolean("reducedEffects", s.ReducedEffects);
            w.WriteString("handle", s.Handle);
            w.WritePropertyName("lastMatch");
            WriteMatchSettings(w, s.LastMatch);
            w.WriteEndObject();

            w.WriteStartArray("history");
            foreach (var r in data.History)
            {
                w.WriteStartObject();
                w.WritePropertyName("settings");
                WriteMatchSettings(w, r.Settings);
                w.WriteNumber("scoreX", r.ScoreX);
                w.WriteNumber("scoreO", r.ScoreO);
                w.WriteNumber("draws", r.Draws);
                w.WriteNumber("rounds", r.RoundsPlayed);
                w.WriteString("winner", r.Winner.ToString());
                w.WriteString("completedAt", r.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
                w.WriteBoolean("assisted", r.Assisted);
                WriteRoundLog(w, r.Rounds);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("achievements");
            foreach (var (id, at) in data.Achievements)
            {
                w.WriteStartObject();
                w.WriteString("id", id.ToString());
                w.WriteString("at", at.ToString("O", CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("challenge");
            w.WriteNumber("streak", data.Challenge.Streak);
            w.WriteNumber("total", data.Challenge.TotalCompleted);
            if (data.Challenge.LastCompleted is { } last)
            {
                w.WriteString("last", last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            w.WriteEndObject();

            if (data.ActiveRun is { } run)
            {
                w.WriteStartObject("run");
                w.WriteNumber("version", run.Version);
                w.WriteNumber("contentVersion", run.ContentVersion);
                w.WriteNumber("seed", run.Seed);
                w.WriteStartArray("actions");
                foreach (var a in run.Actions) w.WriteStringValue(a);
                w.WriteEndArray();
                w.WriteEndObject();
            }

            if (data.ActiveMatch is { } am)
            {
                w.WriteStartObject("activeMatch");
                w.WritePropertyName("settings");
                WriteMatchSettings(w, am.Settings);
                w.WriteNumber("scoreX", am.ScoreX);
                w.WriteNumber("scoreO", am.ScoreO);
                w.WriteNumber("draws", am.Draws);
                w.WriteNumber("round", am.RoundNumber);
                w.WriteString("starter", am.Starter.ToString());
                w.WriteBoolean("assisted", am.Assisted);
                WriteRoundLog(w, am.Rounds);
                w.WriteStartArray("moves");
                foreach (var m in am.Moves) w.WriteNumberValue(m);
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteStartObject("stats");
            foreach (var d in Enum.GetValues<Difficulty>())
            {
                var l = data.Stats[d];
                w.WriteStartObject(d.ToString());
                w.WriteNumber("wins", l.Wins);
                w.WriteNumber("losses", l.Losses);
                w.WriteNumber("draws", l.Draws);
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRoundLog(Utf8JsonWriter w, IEnumerable<RoundRecord> rounds)
    {
        w.WriteStartArray("roundLog");
        foreach (var round in rounds)
        {
            w.WriteStartObject();
            w.WriteString("starter", round.Starter.ToString());
            w.WriteStartArray("moves");
            foreach (var m in round.Moves) w.WriteNumberValue(m);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteMatchSettings(Utf8JsonWriter w, MatchSettings m)
    {
        w.WriteStartObject();
        w.WriteNumber("bestOf", m.BestOf);
        w.WriteString("rules", m.Rules.ToString());
        w.WriteString("opponent", m.Opponent.ToString());
        w.WriteString("difficulty", m.Difficulty.ToString());
        w.WriteString("humanSide", m.HumanSide.ToString());
        w.WriteEndObject();
    }

    internal enum ParseStatus { Empty, Ok, Migrated, Unreadable }

    internal static ProfileData Parse(string? json)
    {
        TryParse(json, out var data);
        return data;
    }

    // Unreadable: not JSON, wrong shape, missing/invalid version, or written by a newer app.
    // Versions 1..Version load; older ones are reported as Migrated so the caller re-saves them upgraded.
    internal static ParseStatus TryParse(string? json, out ProfileData data)
    {
        data = new ProfileData();
        if (string.IsNullOrWhiteSpace(json)) return ParseStatus.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var v)
                || v.ValueKind != JsonValueKind.Number
                || !v.TryGetInt32(out var version)
                || version < 1 || version > Version)
            {
                return ParseStatus.Unreadable;
            }

            var parsed = ParseRoot(root);
            data = parsed;
            return version < Version ? ParseStatus.Migrated : ParseStatus.Ok;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            data = new ProfileData();
            return ParseStatus.Unreadable;
        }
    }

    private static ProfileData ParseRoot(JsonElement root)
    {
        {
            var data = new ProfileData();
            if (root.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                data.Settings = new UserSettings
                {
                    Theme = Str(s, "theme") ?? Themes.NeonNoir,
                    SoundEnabled = Bool(s, "sound"),
                    ReducedEffects = Bool(s, "reducedEffects"),
                    Handle = Str(s, "handle") ?? string.Empty,
                    LastMatch = s.TryGetProperty("lastMatch", out var lm) ? ReadMatchSettings(lm) : new MatchSettings()
                };
            }

            if (root.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in h.EnumerateArray())
                {
                    if (data.History.Count >= MaxHistory) break;
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!DateTimeOffset.TryParse(Str(item, "completedAt"), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var at)) continue;
                    if (!Enum.TryParse<Player>(Str(item, "winner"), out var winner) || !Enum.IsDefined(winner)) continue;
                    data.History.Add(new MatchRecord(
                        item.TryGetProperty("settings", out var ms) ? ReadMatchSettings(ms) : new MatchSettings(),
                        Int(item, "scoreX"), Int(item, "scoreO"), Int(item, "draws"), Int(item, "rounds"),
                        winner, at)
                    {
                        Assisted = Bool(item, "assisted"),
                        Rounds = ReadRounds(item)
                    });
                }
            }

            if (root.TryGetProperty("achievements", out var ach) && ach.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ach.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && Enum.TryParse<Achievement>(Str(item, "id"), out var id) && Enum.IsDefined(id)
                        && DateTimeOffset.TryParse(Str(item, "at"), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var unlockedAt))
                    {
                        data.Achievements[id] = unlockedAt;
                    }
                }
            }

            if (root.TryGetProperty("challenge", out var ch) && ch.ValueKind == JsonValueKind.Object)
            {
                DateOnly? last = DateOnly.TryParseExact(Str(ch, "last"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) ? parsed : null;
                data.Challenge = new ChallengeProgress(Int(ch, "streak"), last, Int(ch, "total"));
            }

            if (root.TryGetProperty("run", out var run))
            {
                RunSave? read = null;
                if (run.ValueKind == JsonValueKind.Object
                    && run.TryGetProperty("seed", out var seed) && seed.ValueKind == JsonValueKind.Number && seed.TryGetUInt64(out var seedValue)
                    && run.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array
                    && acts.GetArrayLength() <= MaxRunActions)
                {
                    var actions = new List<string>();
                    var valid = true;
                    foreach (var a in acts.EnumerateArray())
                    {
                        if (a.ValueKind == JsonValueKind.String && a.GetString() is { Length: > 0 and <= 12 } text) actions.Add(text);
                        else valid = false;
                    }
                    if (valid) read = new RunSave(Int(run, "version"), seedValue, actions, Int(run, "contentVersion"));
                }
                data.ActiveRun = read;
                data.RunRetired = read is null;
            }

            if (root.TryGetProperty("activeMatch", out var am) && am.ValueKind == JsonValueKind.Object)
            {
                data.ActiveMatch = ReadMatch(am);
            }

            var stats = new LifetimeStats();
            if (root.TryGetProperty("stats", out var st) && st.ValueKind == JsonValueKind.Object)
            {
                foreach (var d in Enum.GetValues<Difficulty>())
                {
                    if (st.TryGetProperty(d.ToString(), out var l) && l.ValueKind == JsonValueKind.Object)
                    {
                        stats.Set(d, new StatLine(Int(l, "wins"), Int(l, "losses"), Int(l, "draws")));
                    }
                }
            }
            data.Stats = stats;

            var clean = Sanitize(data);
            clean.RunRetired |= data.RunRetired;
            return clean;
        }
    }

    private static MatchSnapshot? ReadMatch(JsonElement am)
    {
        if (!am.TryGetProperty("moves", out var mv) || mv.ValueKind != JsonValueKind.Array
            || mv.GetArrayLength() > Replay.MaxMovesPerRound
            || !Enum.TryParse<Player>(Str(am, "starter"), out var starter)
            || !am.TryGetProperty("settings", out var ms) || ms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var moves = new List<int>();
        foreach (var m in mv.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out var cell)) return null;
            moves.Add(cell);
        }
        return new MatchSnapshot(ReadMatchSettings(ms), Int(am, "scoreX"), Int(am, "scoreO"), Int(am, "draws"),
            Int(am, "round"), starter, Bool(am, "assisted"), ReadRounds(am), moves);
    }

    private static RoundRecord[] ReadRounds(JsonElement item)
    {
        var rounds = new List<RoundRecord>();
        if (!item.TryGetProperty("roundLog", out var log) || log.ValueKind != JsonValueKind.Array) return [];

        foreach (var r in log.EnumerateArray())
        {
            if (rounds.Count >= MaxRoundsStored) break;
            if (r.ValueKind != JsonValueKind.Object
                || !Enum.TryParse<Player>(Str(r, "starter"), out var starter)
                || !r.TryGetProperty("moves", out var mv) || mv.ValueKind != JsonValueKind.Array
                || mv.GetArrayLength() > Replay.MaxMovesPerRound)
            {
                continue;
            }

            var moves = new List<int>();
            var ok = true;
            foreach (var m in mv.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var cell)) moves.Add(cell);
                else ok = false;
            }
            if (ok) rounds.Add(new RoundRecord(starter, moves));
        }
        return [.. rounds];
    }

    private static MatchSettings ReadMatchSettings(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return new MatchSettings();
        var d = new MatchSettings();
        return ProfileValidation.Normalize(new MatchSettings
        {
            BestOf = e.TryGetProperty("bestOf", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out var bo) ? bo : d.BestOf,
            Rules = Enum.TryParse<RuleSet>(Str(e, "rules"), out var r) ? r : d.Rules,
            Opponent = Enum.TryParse<Opponent>(Str(e, "opponent"), out var o) ? o : d.Opponent,
            Difficulty = Enum.TryParse<Difficulty>(Str(e, "difficulty"), out var df) ? df : d.Difficulty,
            HumanSide = Enum.TryParse<Player>(Str(e, "humanSide"), out var hs) ? hs : d.HumanSide
        });
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : 0;
}
