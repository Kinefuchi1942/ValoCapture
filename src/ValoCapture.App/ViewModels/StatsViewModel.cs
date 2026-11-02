using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ValoCapture.Core.Game;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.ViewModels;

/// <summary>スタッツ画面（自分の成績・ラウンドの流れ・スコアボード・選んだラウンドの詳細）。</summary>
public sealed partial class StatsViewModel : ObservableObject
{
    public StatsViewModel(MatchStats stats, MatchResult? result)
    {
        var me = stats.Me;
        IsRoundBased = stats.Rounds.Count > 1;
        Tiles = me is null ? [] : BuildTiles(stats, me, IsRoundBased);

        var maxDamage = stats.Rounds.Select(r => r.Damage).DefaultIfEmpty(0).Max();
        Rounds = stats.Rounds.Select((r, i) => new RoundItemViewModel(r, i > 0 ? stats.Rounds[i - 1] : null, maxDamage)).ToList();
        _selectedRound = Rounds.FirstOrDefault();

        // ラウンド制は味方と相手に分ける。デスマッチ等（全員が別チーム）は自分とそれ以外にする
        var allies = stats.Players.Where(p => p.IsAlly || p.IsMe).OrderByDescending(p => p.Acs).ToList();
        var others = stats.Players.Except(allies).OrderByDescending(p => p.Acs).ToList();
        Allies = allies.Select(p => new ScoreRowViewModel(p, IsRoundBased)).ToList();
        Opponents = others.Select(p => new ScoreRowViewModel(p, IsRoundBased)).ToList();
        AllyTitle = result is { } r1 && IsRoundBased ? $"味方 · {r1.MyScore}" : "自分";
        OpponentTitle = result is { } r2 && IsRoundBased ? $"相手 · {r2.OpponentScore}" : "ほかのプレイヤー";
    }

    public bool IsRoundBased { get; }
    public IReadOnlyList<StatTile> Tiles { get; }
    public IReadOnlyList<RoundItemViewModel> Rounds { get; }
    public IReadOnlyList<ScoreRowViewModel> Allies { get; }
    public IReadOnlyList<ScoreRowViewModel> Opponents { get; }
    public string AllyTitle { get; }
    public string OpponentTitle { get; }

    /// <summary>右の列に詳細を出すラウンド。</summary>
    [ObservableProperty] private RoundItemViewModel? _selectedRound;

    /// <summary>選んでいるラウンドを前後に動かす（← / → キー）。</summary>
    public void MoveRound(int delta)
    {
        if (Rounds.Count == 0) return;
        var index = SelectedRound is null ? 0 : Rounds.ToList().IndexOf(SelectedRound) + delta;
        SelectedRound = Rounds[Math.Clamp(index, 0, Rounds.Count - 1)];
    }

    private static List<StatTile> BuildTiles(MatchStats stats, PlayerStats me, bool isRoundBased)
    {
        var allies = stats.Players.Where(p => p.IsAlly).OrderByDescending(p => p.Acs).ToList();
        var rank = allies.IndexOf(me) + 1;
        var tiles = new List<StatTile>
        {
            new("ACS", $"{me.Acs}", isRoundBased && rank > 0 ? $"チーム {rank} 位" : "", rank == 1 && isRoundBased),
            new("K / D / A", $"{me.Kills}/{me.Deaths}/{me.Assists}", $"K/D {(double)me.Kills / Math.Max(1, me.Deaths):0.00}", me.Kills > me.Deaths),
        };
        if (!isRoundBased) return tiles;

        var best = me.MultiKills.OrderByDescending(kv => kv.Key).ToList();
        tiles.AddRange(
        [
            new("ADR", $"{me.Adr}", "1 ラウンドあたり"),
            new("HS%", me.HeadshotPercent is { } hs ? $"{hs}%" : "—", $"頭 {me.Headshots} · 体 {me.Bodyshots} · 脚 {me.Legshots}"),
            new("KAST", $"{me.KastPercent}%", $"{me.KastRounds} / {me.Rounds} ラウンド"),
            new("先制キル / デス", $"{me.FirstKills} / {me.FirstDeaths}", "ファーストブラッド"),
            new("マルチキル",
                best.Count > 0 ? $"{best[0].Key}K×{best[0].Value}" : "—",
                string.Join(" · ", best.Skip(1).Select(kv => $"{kv.Key}K×{kv.Value}"))),
        ]);
        return tiles;
    }
}

/// <param name="IsGood">補足を勝ちの色にするか。</param>
public sealed record StatTile(string Label, string Value, string Sub, bool IsGood = false);

/// <summary>スコアボードの 1 行。他のプレイヤーは名前を出さずエージェントで表す。</summary>
public sealed class ScoreRowViewModel(PlayerStats p, bool isRoundBased)
{
    public string Agent { get; } = DisplayNames.Agent(p.AgentId) ?? "不明";
    public bool IsMe => p.IsMe;
    public bool IsAlly => p.IsAlly;
    public bool IsMvp => p.IsTeamMvp && isRoundBased;
    public int Acs => p.Acs;
    public int Kills => p.Kills;
    public int Deaths => p.Deaths;
    public int Assists => p.Assists;
    public int Diff => p.Kills - p.Deaths;
    public int DiffSign => Math.Sign(Diff);
    public string DiffText => Diff > 0 ? $"+{Diff}" : Diff < 0 ? $"−{-Diff}" : "0";
    public string AdrText => isRoundBased ? $"{p.Adr}" : "—";
    public string HeadshotText => isRoundBased && p.HeadshotPercent is { } hs ? $"{hs}%" : "—";
    public string KastText => isRoundBased ? $"{p.KastPercent}%" : "—";
    public string FirstKillsText => isRoundBased ? $"{p.FirstKills}" : "—";
}

public enum BuyKind { Pistol, Eco, Half, Full }

/// <summary>ラウンドの流れの 1 列と、選んだときの右の列の詳細。</summary>
public sealed class RoundItemViewModel
{
    /// <summary>この額以上の装備ならフルバイ、<see cref="HalfBuyValue"/> 以上ならハーフバイとする。</summary>
    private const int FullBuyValue = 3_700, HalfBuyValue = 2_000;

    /// <summary>ダメージがこれ以上のラウンドは棒を目立たせる。</summary>
    private const int HighDamage = 200;

    private const double DamageBarHeight = 26;

    public RoundItemViewModel(RoundStats r, RoundStats? previous, int maxDamage)
    {
        Round = r;
        // 攻守が入れ替わったラウンドの前を空ける。延長（24 ラウンド目以降）は毎ラウンド入れ替わるので空けない
        IsSideSwitch = previous is not null && r.Number < 24 && r.Attacking is not null && previous.Attacking is not null && r.Attacking != previous.Attacking;
        Buy = r.Number == 0 || IsSideSwitch ? BuyKind.Pistol
            : r.LoadoutValue >= FullBuyValue ? BuyKind.Full
            : r.LoadoutValue >= HalfBuyValue ? BuyKind.Half
            : BuyKind.Eco;
        DamageBar = maxDamage > 0 ? Math.Max(2, DamageBarHeight * r.Damage / maxDamage) : 2;

        var maxDealt = Math.Max(150, r.DamageDealt.Select(d => d.Damage).DefaultIfEmpty(0).Max());
        DamageRows = r.DamageDealt.Select(d => new DamageRowViewModel(d, maxDealt)).ToList();
        Events = r.Events.Select(e => new RoundEventItemViewModel(e)).ToList();
    }

    public RoundStats Round { get; }

    // ---- ラウンドの流れ ----
    public int Number => Round.Number + 1;
    public bool Won => Round.Won;
    public string Code => Round.ResultCode switch
    {
        "Elimination" => "E",
        "Detonate" => "B",
        "Defuse" => "D",
        "Surrendered" => "S",
        "" => "",
        _ => "T",
    };
    public bool IsSideSwitch { get; }
    public Thickness SideSwitchMargin => new(IsSideSwitch ? 8 : 0, 0, 0, 0);
    public IReadOnlyList<int> KillMarks => Enumerable.Range(0, Round.Kills).ToList();
    public bool Died => Round.Died;
    public double DamageBar { get; }
    public bool IsHighDamage => Round.Damage >= HighDamage;
    public BuyKind Buy { get; }
    public string BuyText => Buy switch
    {
        BuyKind.Pistol => "ピストル",
        BuyKind.Full => "フル",
        BuyKind.Half => "ハーフ",
        _ => "エコ",
    };
    public string ToolTipText => $"ラウンド {Number}：{(Won ? "勝ち" : "負け")} · {Round.Kills} キル · {Round.Damage} ダメージ · {BuyText}（{Round.LoadoutValue:N0}）";

    // ---- 詳細 ----
    public string Title => $"ラウンド {Number}";
    public string SideText
    {
        get
        {
            var side = Round.Attacking switch { true => "攻め · ", false => "守り · ", _ => "" };
            var (before, after) = Won
                ? ($"{Round.MyScore - 1}–{Round.OpponentScore}", $"{Round.MyScore}–{Round.OpponentScore}")
                : ($"{Round.MyScore}–{Round.OpponentScore - 1}", $"{Round.MyScore}–{Round.OpponentScore}");
            return $"{side}{before} → {after}";
        }
    }
    public string ResultTitle => Won ? "勝ち" : "負け";
    public string ResultDetail
    {
        get
        {
            var parts = new List<string>
            {
                Round.ResultCode switch
                {
                    "Elimination" => "全滅",
                    "Detonate" => "スパイク爆破",
                    "Defuse" => "スパイク解除",
                    "Surrendered" => "降参",
                    "" => "",
                    _ => "時間切れ",
                },
            };
            if (Round.PlantSite is { } site && Round.Events.FirstOrDefault(e => e.Type == RoundEventType.Plant) is { } plant)
                parts.Add($"{site} サイトに {TimeSpan.FromMilliseconds(plant.RoundTimeMs):m\\:ss} で設置");
            if (Ceremony(Round.Ceremony) is { } ceremony) parts.Add(ceremony);
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }
    }
    public string KillDeathText => $"{Round.Kills} / {(Died ? 1 : 0)}";
    public int Damage => Round.Damage;
    public string LoadoutText => string.Join(" · ", new[] { DisplayNames.Weapon(Round.Weapon), DisplayNames.Armor(Round.Armor) }.OfType<string>()) is { Length: > 0 } s ? s : "—";
    public string MoneyText => $"{Round.Spent:N0} / {Round.Remaining:N0}";
    public IReadOnlyList<DamageRowViewModel> DamageRows { get; }
    public bool HasDamage => DamageRows.Count > 0;
    public IReadOnlyList<RoundEventItemViewModel> Events { get; }

    /// <summary>ラウンドの戦闘開始の動画上の位置。録画範囲外なら null。</summary>
    public TimeSpan? StartVideoTime => Round.StartVideoMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
    public bool CanPlay => StartVideoTime is not null;

    private static string? Ceremony(string ceremony) => ceremony switch
    {
        "CeremonyAce" => "エース",
        "CeremonyTeamAce" => "チームエース",
        "CeremonyClutch" => "クラッチ",
        "CeremonyFlawless" => "フローレス",
        "CeremonyThrifty" => "スリフティ",
        _ => null,
    };
}

/// <summary>相手 1 人に与えたダメージ（頭・体・脚の当たった数の割合で棒を塗り分ける）。</summary>
public sealed class DamageRowViewModel
{
    public DamageRowViewModel(DamageDealt d, int scale)
    {
        Agent = DisplayNames.Agent(d.AgentId) ?? "不明";
        Damage = d.Damage;
        var shots = Math.Max(1, d.Headshots + d.Bodyshots + d.Legshots);
        var filled = Math.Min(1.0, (double)d.Damage / scale);
        HeadWidth = Star(filled * d.Headshots / shots);
        BodyWidth = Star(filled * d.Bodyshots / shots);
        LegWidth = Star(filled * d.Legshots / shots);
        RestWidth = Star(1 - filled);
        ToolTipText = $"頭 {d.Headshots} · 体 {d.Bodyshots} · 脚 {d.Legshots}";
    }

    public string Agent { get; }
    public int Damage { get; }
    public GridLength HeadWidth { get; }
    public GridLength BodyWidth { get; }
    public GridLength LegWidth { get; }
    public GridLength RestWidth { get; }
    public string ToolTipText { get; }

    private static GridLength Star(double value) => new(Math.Max(0, value), GridUnitType.Star);
}

/// <summary>ラウンドの詳細のイベント（自分のキル・デスとスパイク）。クリックでその場面を再生する。</summary>
public sealed class RoundEventItemViewModel(RoundEvent e)
{
    public RoundEventType Type => e.Type;
    public TimeSpan? VideoTime => e.VideoTimeMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
    public string TimeText => VideoTime is { } t ? t.ToString(@"mm\:ss") : "--:--";

    public string Label => e.Type switch
    {
        RoundEventType.Kill => $"キル → {DisplayNames.Agent(e.AgentId) ?? "不明"}",
        RoundEventType.Death => e.AgentId is null ? "デス" : $"デス ← {DisplayNames.Agent(e.AgentId) ?? "不明"}",
        RoundEventType.Plant => "スパイク設置",
        RoundEventType.Defuse => "スパイク解除",
        _ => "スパイク爆破",
    };

    /// <summary>武器（キル・デス）か、設置・解除した人のエージェント。</summary>
    public string Detail => e.Type switch
    {
        RoundEventType.Kill or RoundEventType.Death => DisplayNames.Weapon(e.DamageItem) ?? "",
        RoundEventType.Plant or RoundEventType.Defuse => DisplayNames.Agent(e.AgentId) ?? "",
        _ => "",
    };
}
