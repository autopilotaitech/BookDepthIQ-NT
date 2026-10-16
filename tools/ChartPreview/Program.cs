using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom;
using BookDepthIQ.Dom.Charts;

namespace BookDepthIQ.ChartPreview;

/// <summary>Standalone synthetic UI fixture. Does not construct MainWindow, DomState, bridges or feeds.</summary>
internal static class Program
{
    internal static string[] Options=Array.Empty<string>();
    [STAThread] public static void Main(string[] args)
    {Options=args;AppBuilder.Configure<PreviewApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);}
    internal static string? Option(string name) {var i=Array.IndexOf(Options,name);return i>=0&&i+1<Options.Length?Options[i+1]:null;}
}

public sealed class PreviewApplication : Application
{
    public override void Initialize() {RequestedThemeVariant=ThemeVariant.Dark;Styles.Add(new FluentTheme());}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow=new PreviewWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class PreviewWindow : Window
{
    readonly ChartWorkspaceSettings preferences=new() {Enabled=true,LinkCrosshair=true};
    readonly ChartPane[] panes;
    readonly ChartBar[] sessionSource=SessionFixture();
    public PreviewWindow()
    {
        Title="BookDepthIQ chart preview — SYNTHETIC DATA, NO TRADING";
        Width=int.TryParse(Program.Option("--width"),out var width)?Math.Clamp(width,320,1800):800;
        Height=int.TryParse(Program.Option("--height"),out var height)?Math.Clamp(height,300,1800):1000;
        Background=Pal.B(Pal.Bg);FontFamily=Flight.Sans;
        preferences.Top.Volume=true;preferences.Top.Sma=true;preferences.Bottom.Ema=true;
        if(Program.Options.Contains("--sessions"))
        {
            preferences.Top.Frame=15;preferences.Bottom.Frame=30;preferences.Top.ShowSessionZones=preferences.Bottom.ShowSessionZones=true;
            preferences.Sessions.Enabled=!Program.Options.Contains("--sessions-off");preferences.Sessions.Previous=1;
            preferences.Sessions.Definitions=new[]{
                new SessionDefinition("tokyo","TOKYO",true,"#9A85EF","00:00","06:00","UTC"),
                new SessionDefinition("london","LONDON",true,"#00BBD6","08:00","14:00","UTC"),
                new SessionDefinition("ny","NEW YORK",true,"#579AFF","14:00","21:00","UTC")};
        }
        panes=new[]{new ChartPane(preferences.Top,Refresh,()=>{},()=>{},preferences,Refresh),new ChartPane(preferences.Bottom,Refresh,()=>{},()=>{},preferences,Refresh)};
        panes[0].Chart.CursorChanged+=cursor=> {if(preferences.LinkCrosshair) panes[1].Chart.SetLinkedCursor(cursor);};
        panes[1].Chart.CursorChanged+=cursor=> {if(preferences.LinkCrosshair) panes[0].Chart.SetLinkedCursor(cursor);};
        var root=new DockPanel();var banner=new TextBlock {Text="BOOKDEPTHIQ · CHART QA / SYNTHETIC DATA · NO CONNECTIONS OR ORDERS",FontFamily=Flight.Sans,FontSize=10,Foreground=Pal.B(Pal.Accent),Margin=new Thickness(10,7)};
        DockPanel.SetDock(banner,Dock.Top);root.Children.Add(banner);
        var grid=new Grid {RowDefinitions=new("*,5,*")};grid.Children.Add(panes[0]);Grid.SetRow(panes[1],2);grid.Children.Add(panes[1]);
        var splitter=new Avalonia.Controls.GridSplitter {ResizeDirection=Avalonia.Controls.GridResizeDirection.Rows,Background=Pal.B(Pal.Line)};Grid.SetRow(splitter,1);grid.Children.Add(splitter);root.Children.Add(grid);Content=root;
        Refresh();
        if(Program.Option("--snapshot") is {} output)
        {
            ShowActivated=false;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.Manual;Position=new PixelPoint(-12000,40);
            Opened+=(_,_)=> {
                Position=new PixelPoint(-12000,40);
                DispatcherTimer.RunOnce(()=>Capture(output),TimeSpan.FromSeconds(1));
            };
        }
    }
    void Refresh()
    {
        for(int i=0;i<panes.Length;i++)
        {
            var settings=i==0?preferences.Top:preferences.Bottom;
            var requested=int.TryParse(Program.Option("--bars"),out var count)?Math.Clamp(count,250,10000):240;
            var bars=Program.Options.Contains("--sessions")?Aggregate(sessionSource,settings.Frame).TakeLast(requested).ToArray():Fixture(settings.Frame,requested);
            panes[i].Update("NQ · PREVIEW",new(bars,.25,"LIVE",bars[^1].Close+3,1));
            if(Program.Options.Contains("--sessions"))
            {
                panes[i].Chart.SessionStyle=preferences.Sessions.Copy();
                panes[i].Chart.SessionZones=ChartSessionZones.Build(sessionSource,preferences.Sessions.Definitions,1,new(2026,10,15,20,0,0,TimeSpan.Zero),true);
            }
            var last=bars[^1].Close;
            if(Program.Options.Contains("--bubbles"))
            {
                settings.ShowLargeTrades=true;
                panes[i].Chart.TradeProfile=new() {MinDiameter=32,MaxDiameter=54,Labels=TradeQuantityLabels.All};
                var indexes=new[]{bars.Length-100,bars.Length-80,bars.Length-58,bars.Length-35,bars.Length-14};
                var quantities=new long[]{85,120,65,210,95};
                var prints=indexes.Select((index,j)=>new ChartPrint(j+1,"NQ · PREVIEW",bars[index].Close,quantities[j],j is 1 or 4?'s':'b',bars[index].Time.AddMinutes(-settings.Frame*.5).ToUnixTimeMilliseconds())).ToArray();
                panes[i].Chart.CapturedTrades=new(prints,new[]{new ChartTradeCoverage(bars[indexes[0]].Time.ToUnixTimeMilliseconds(),null)},0,1);
            }
            if(Program.Options.Contains("--trade-load"))
            {
                var prints=Enumerable.Range(0,50000).Select(j=> {
                    var index=1+j%(bars.Length-1);return new ChartPrint(j+1,"NQ · PREVIEW",bars[index].Close,50+j%500,j%3==0?'s':j%3==1?'b':'?',bars[index].Time.AddMinutes(-settings.Frame*.5).ToUnixTimeMilliseconds());
                }).OrderBy(p=>p.TimeMs).ThenBy(p=>p.Id).ToArray();
                panes[i].Chart.CapturedTrades=new(prints,Array.Empty<ChartTradeCoverage>(),0,1);
            }
            panes[i].Chart.TradeLines=new[]{new ChartTradeLine("target",(last+45)*.25,2,false,ChartTradeKind.Target,"TP SELL"),new ChartTradeLine("stop",(last-50)*.25,2,false,ChartTradeKind.Stop,"SL SELL"),new ChartTradeLine("position",(last-8)*.25,2,true,ChartTradeKind.Position,"AVG LONG")};
            if(Program.Options.Contains("--center")) panes[i].Chart.Recenter();
            if(Program.Options.Contains("--all")) panes[i].Chart.Viewport.Zoom(100,1,bars.Length);
        }
    }
    public static ChartBar[] Fixture(int minutes,int count=240)
    {
        var random=new Random(37+minutes);var result=new List<ChartBar>();long previous=120300;
        var end=new DateTimeOffset(2026,10,15,20,0,0,TimeSpan.Zero);var start=end.AddMinutes(-(count-1)*(double)minutes);
        for(int i=0;i<count;i++)
        {
            var open=previous;var trend=i%100<55?5:-5;var close=open+random.Next(-14,15)+trend;
            var time=start.AddMinutes(i*(double)minutes);result.Add(new(i,time,DateOnly.FromDateTime(time.Date),open,Math.Max(open,close)+random.Next(3,20),Math.Min(open,close)-random.Next(3,20),close,random.Next(30,550),1,i==count-1));previous=close;
        }
        return result.ToArray();
    }
    static ChartBar[] SessionFixture()
    {
        var result=new List<ChartBar>();var end=new DateTimeOffset(2026,10,15,20,0,0,TimeSpan.Zero);long previous=120500;
        var count=Program.Options.Contains("--all")?75000:8641;
        for(int i=0;i<count;i++)
        {
            var time=end.AddSeconds((i-count+1)*30);var close=120500+(long)(Math.Sin(i/220d)*140+Math.Sin(i/61d)*65);
            result.Add(new(i,time,DateOnly.FromDateTime(time.UtcDateTime),previous,Math.Max(previous,close)+4,Math.Min(previous,close)-4,close,30+i%40,1,i==count-1));previous=close;
        }
        return result.ToArray();
    }
    static ChartBar[] Aggregate(ChartBar[] source,int minutes)=>source.GroupBy(b=>(b.Time.ToUnixTimeMilliseconds()+minutes*60000L-1)/(minutes*60000L))
        .Select((group,i)=>new ChartBar(i,DateTimeOffset.FromUnixTimeMilliseconds(group.Key*minutes*60000L),group.Last().TradingDate,group.First().Open,group.Max(b=>b.High),group.Min(b=>b.Low),group.Last().Close,group.Sum(b=>b.Volume),1,group.Last().Forming)).ToArray();
    void Capture(string output)
    {
        if(Program.Options.Contains("--cursor")) panes[0].Chart.SetCursor(new Point(panes[0].Chart.Geometry.PlotWidth*.65,panes[0].Chart.Geometry.PlotHeight*.42));
        var scale=double.TryParse(Program.Option("--scale"),NumberStyles.Float,CultureInfo.InvariantCulture,out var factor)?Math.Clamp(factor,1,3):1;
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)(Bounds.Width*scale),(int)(Bounds.Height*scale)),new Vector(96*scale,96*scale));
        bitmap.Render(this);bitmap.Save(output);
        var timings=new List<double>();long allocation=0;
        if(Program.Options.Contains("--benchmark"))
        {
            ChartTradeStore? liveStore=null;long clock=0;
            if(Program.Options.Contains("--stream")&&panes[0].Chart.CapturedTrades.Prints.Count>0)
            {
                liveStore=new();liveStore.Reset("NQ · PREVIEW");liveStore.SetCapture(true,0);
                clock=panes[0].Chart.CapturedTrades.Prints.Max(p=>p.TimeMs);
                foreach(var p in panes[0].Chart.CapturedTrades.Prints) liveStore.Add(p.Instrument,p.Tick,p.Quantity,p.Side,p.TimeMs,clock);
            }
            bitmap.Render(this);var before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<60;i++)
            {
                var watch=Stopwatch.StartNew();
                if(liveStore!=null)
                {
                    clock+=10;for(int j=0;j<20;j++) liveStore.Add("NQ · PREVIEW",120700+i%8,50+j*10,j%2==0?'b':'s',clock,clock);
                    var data=liveStore.Snapshot(clock);foreach(var pane in panes) pane.Chart.CapturedTrades=data;
                }
                bitmap.Render(this);timings.Add(watch.Elapsed.TotalMilliseconds);
            }
            allocation=GC.GetAllocatedBytesForCurrentThread()-before;timings.Sort();
        }
        Console.WriteLine(JsonSerializer.Serialize(new {snapshot=Path.GetFullPath(output),width=Bounds.Width,height=Bounds.Height,scale,synthetic=true,streamUpdates=Program.Options.Contains("--stream"),networkConnections=0,renderP50Ms=timings.Count>0?timings[30]:0,renderP95Ms=timings.Count>0?timings[56]:0,managedBytesFor60Renders=allocation}));
        Close();
    }
}
