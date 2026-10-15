using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.Core;

namespace RetroHub.App.ViewModels;

/// <summary>Uma barra: rótulo, valor já formatado e proporção 0–1 em relação ao maior da série.</summary>
public sealed record StatBar(string Label, string ValueText, double Ratio, string Tooltip)
{
    /// <summary>Largura (barras horizontais) ou altura (colunas) em px; mínimo de 3 px para valores &gt; 0 continuarem visíveis.</summary>
    public double Length(double max) => Ratio <= 0 ? 0 : Math.Max(3, Ratio * max);
    public double BarWidth => Length(320);
    public double ColumnHeight => Length(120);
}

/// <summary>Estatísticas de jogo: totais, horas por sistema, jogos mais jogados e últimos 14 dias.</summary>
public partial class StatsViewModel(HubServices services) : ObservableObject, IActivatable
{
    public ObservableCollection<StatBar> BySystem { get; } = [];
    public ObservableCollection<StatBar> TopGames { get; } = [];
    public ObservableCollection<StatBar> LastDays { get; } = [];

    [ObservableProperty] private string _totalTime = "—";
    [ObservableProperty] private string _gamesPlayed = "0";
    [ObservableProperty] private string _sessions = "0";
    [ObservableProperty] private string _weekTime = "—";
    [ObservableProperty] private bool _hasData;

    public void OnActivated()
    {
        var games = services.Database.GetGames();
        var played = games.Where(g => g.PlaySeconds > 0).ToList();
        var sessions = services.Database.GetSessions(DateTimeOffset.UnixEpoch);

        HasData = played.Count > 0;
        TotalTime = Shell.FormatPlaytime(played.Sum(g => g.PlaySeconds));
        GamesPlayed = played.Count.ToString();
        Sessions = sessions.Count.ToString();
        var weekStart = DateTimeOffset.Now.Date.AddDays(-6);
        WeekTime = Shell.FormatPlaytime(sessions.Where(s => s.StartedAt.ToLocalTime() >= weekStart).Sum(s => s.Seconds));

        Fill(BySystem, played
            .GroupBy(g => g.SystemId)
            .Select(g => (Label: services.Manifests.GetSystem(g.Key)?.Name ?? g.Key, Seconds: g.Sum(x => x.PlaySeconds), Extra: $"{g.Count()} jogo(s)"))
            .OrderByDescending(x => x.Seconds));

        Fill(TopGames, played
            .OrderByDescending(g => g.PlaySeconds)
            .Take(8)
            .Select(g => (Label: g.Title, Seconds: g.PlaySeconds, Extra: services.Manifests.GetSystem(g.SystemId)?.ShortName ?? g.SystemId)));

        // Últimos 14 dias, inclusive os dias sem jogo (barra zerada).
        var culture = new CultureInfo("pt-BR");
        var today = DateTime.Now.Date;
        var perDay = sessions.GroupBy(s => s.StartedAt.ToLocalTime().Date).ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
        Fill(LastDays, Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).Select(day =>
            (Label: day == today ? "hoje" : day.ToString("ddd d", culture).Replace(".", ""),
             Seconds: perDay.GetValueOrDefault(day),
             Extra: day.ToString("dddd, dd/MM", culture))));
    }

    private static void Fill(ObservableCollection<StatBar> target, IEnumerable<(string Label, long Seconds, string Extra)> rows)
    {
        var list = rows.ToList();
        var max = list.Count == 0 ? 0 : list.Max(r => r.Seconds);
        target.Clear();
        foreach (var (label, seconds, extra) in list)
        {
            var text = seconds == 0 ? "—" : Shell.FormatPlaytime(seconds);
            target.Add(new StatBar(label, text, max == 0 ? 0 : (double)seconds / max, $"{label} · {text} · {extra}"));
        }
    }
}
