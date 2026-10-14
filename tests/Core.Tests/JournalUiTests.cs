using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(JournalTestApplication))]

public class JournalTestApplication : Application
{
    public override void Initialize() { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<JournalTestApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class JournalUiTests
{
    [AvaloniaFact]
    public void Calendar_and_analytics_render_and_select_simulated_trades()
    {
        var state = new DomState(new Settings(), Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json"));
        var now = DomState.NowMs();
        for (var i=0; i<12; i++) state.Journal.Add(new JournalTrade { Account="Sim101", Inst="MNQ 12-26", Long=i%2==0,
            Qty=2, Open=now-(12-i)*60000, Close=now-(11-i)*60000, Entry=30000+i, Exit=30004+i,
            Pnl=i%3==0 ? -44.18 : 75.82, FeesUsd=4.18, Setup="pullback" });
        var window = new JournalWindow(state);
        try
        {
            window.Show();
            using var calendar = window.CaptureRenderedFrame();
            Assert.NotNull(calendar);
            calendar!.Save(Path.Combine(Path.GetTempPath(), "bdiq-journal-calendar-preview.png"));
            var selector = window.GetVisualDescendants().OfType<Seg>().First(s=>s.SelectedItem=="CALENDAR");
            selector.SelectedIndex=1;
            using var analytics = window.CaptureRenderedFrame();
            Assert.NotNull(analytics);
            analytics!.Save(Path.Combine(Path.GetTempPath(), "bdiq-journal-analytics-preview.png"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="NET P&L");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="12");
            var range=window.GetVisualDescendants().OfType<Seg>().First(s=>s.SelectedItem=="30D");
            range.SelectedIndex=6;
            window.CaptureRenderedFrame()?.Dispose();
            var date=window.GetVisualDescendants().OfType<DatePicker>().First();
            date.SelectedDate=date.SelectedDate!.Value.AddDays(-1);
            window.CaptureRenderedFrame()?.Dispose();
            range.SelectedIndex=2;
            var env=window.GetVisualDescendants().OfType<Seg>().First(s=>s.SelectedItem=="SIM");
            env.SelectedIndex=1;
            window.CaptureRenderedFrame()?.Dispose();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text!=null && t.Text.StartsWith("No completed trades"));
        }
        finally { window.Close(); }
    }
}
