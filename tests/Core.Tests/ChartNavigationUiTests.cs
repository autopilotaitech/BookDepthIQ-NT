using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using SkiaSharp;
using Xunit;

public class ChartNavigationUiTests
{
    [AvaloniaFact] public void Right_click_opens_chart_menu_without_pan_or_trading_action()
    {
        var pane=new ChartPane(new(),()=>{},()=>{},()=>{});pane.Update("NQ TEST",new(Bars(),.25,"LIVE",120025,1));
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=pane};
        try
        {
            window.Show();var before=pane.Chart.Viewport.Start;
            window.MouseDown(new Point(200,150),MouseButton.Right);window.MouseUp(new Point(200,150),MouseButton.Right);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();Assert.True(pane.ContextMenu!.IsOpen);Assert.Equal(before,pane.Chart.Viewport.Start);
        }
        finally {pane.ContextMenu?.Close();window.Close();}
    }
    [AvaloniaFact] public void Linked_daily_crosshair_uses_native_trading_date_not_next_midnight()
    {
        var start=new DateTimeOffset(2026,10,14,0,0,0,TimeSpan.Zero);
        var daily=Enumerable.Range(0,3).Select(i=>new ChartBar(i,start.AddDays(i),new DateOnly(2026,10,14+i),120000,120040,119960,120010,1,1,false)).ToArray();
        var chart=new CandleChartControl(new() {Frame=1440});chart.Update(new(daily,.25,"LIVE",120025,1));Render(chart);
        chart.SetLinkedCursor(new ChartCursorLink(new DateTimeOffset(2026,10,15,20,0,0,TimeSpan.Zero),120020,new DateOnly(2026,10,15)));
        Assert.Equal(1,chart.CursorReadout!.Index);Assert.Equal(new DateOnly(2026,10,15),chart.CursorReadout.Bar.TradingDate);
    }
    [AvaloniaFact] public void Minimum_width_chart_header_keeps_contract_and_controls_readable()
    {
        var pane=new ChartPane(new(),()=>{},()=>{},()=>{});pane.Update("NQ 12-26",new(Bars(),.25,"LIVE",120025,1));
        pane.Measure(new Size(320,420));pane.Arrange(new Rect(0,0,320,420));
        var symbol=pane.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="NQ 12-26");Assert.True(symbol.Bounds.Width>=65,$"Contract only has {symbol.Bounds.Width} DIPs");
        foreach(var button in pane.GetVisualDescendants().OfType<Button>())
        {var p=button.TranslatePoint(default,pane)!.Value;Assert.True(p.X>=0&&p.X+button.Bounds.Width<=321);}
    }
    [AvaloniaTheory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Last_price_tag_has_readable_text_at_high_dpi(double scale)
    {
        var chart=new CandleChartControl(new());chart.Update(new(Bars(),.25,"LIVE",120025,1));Render(chart);
        using var target=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(600*scale),(int)(420*scale)),new Vector(96*scale,96*scale));target.Render(chart);
        using var bytes=new MemoryStream();target.Save(bytes);using var image=SKBitmap.Decode(bytes.ToArray());
        if(Environment.GetEnvironmentVariable("BDIQ_CHART_DPI_DIR") is {} folder) File.WriteAllBytes(Path.Combine(folder,$"price-dpi-{scale}.png"),bytes.ToArray());
        var g=chart.Geometry;var top=Math.Clamp(g.Y(120025)-11,1,g.PlotHeight-23);int dark=0;
        for(int y=(int)((top+3)*scale);y<(top+19)*scale;y++) for(int x=(int)((g.PlotWidth+6)*scale);x<(600-6)*scale;x++)
        {var p=image.GetPixel(x,y);if(p.Red<70&&p.Green<70&&p.Blue<70) dark++;}
        Assert.True(dark>8,$"Price text missing at {scale}x: {dark} dark pixels");
    }
    [AvaloniaFact] public void Real_wheel_and_drag_events_zoom_time_scale_price_and_release_capture()
    {
        var chart=new CandleChartControl(new());chart.Update(new(Bars(),.25,"LIVE",120025,1));
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=chart};
        try
        {
            window.Show();Render(chart);var g=chart.Geometry;var anchor=chart.Viewport.Start+200/g.PlotWidth*chart.Viewport.Count;
            window.MouseWheel(new Point(200,150),new Vector(0,1),RawInputModifiers.None);
            Assert.True(chart.Viewport.Count<120);Assert.False(chart.Viewport.Follow);
            Assert.Equal(anchor,chart.Viewport.Start+200/chart.Geometry.PlotWidth*chart.Viewport.Count,6);
            var count=chart.Viewport.Count;var span=chart.Geometry.High-chart.Geometry.Low;
            window.MouseWheel(new Point(chart.Geometry.PlotWidth+40,100),new Vector(0,1),RawInputModifiers.None);
            Assert.False(chart.Viewport.Auto);Assert.Equal(count,chart.Viewport.Count);Assert.True(chart.Geometry.High-chart.Geometry.Low<span);
            var oldStart=chart.Viewport.Start;
            window.MouseDown(new Point(200,200),MouseButton.Left);window.MouseMove(new Point(240,200),RawInputModifiers.LeftMouseButton);
            Assert.True(chart.Viewport.Start<oldStart);window.MouseUp(new Point(240,200),MouseButton.Left);
            oldStart=chart.Viewport.Start;window.MouseMove(new Point(270,200));Assert.Equal(oldStart,chart.Viewport.Start);
        }
        finally {window.Close();}
    }
    [AvaloniaFact] public void Chart_navigation_keys_are_local_but_existing_emergency_shortcuts_can_bubble()
    {
        var chart=new CandleChartControl(new());chart.Update(new(Bars(),.25,"LIVE",120025,1));
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=chart};var bubbled=new List<Key>();
        window.KeyDown+=(_,e)=>bubbled.Add(e.Key);
        try
        {
            window.Show();chart.Focus();chart.Viewport.Zoom(.5,.5,180);window.KeyPress(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");window.KeyRelease(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
            Assert.True(chart.Viewport.CenterOnLast);Assert.Equal(60,chart.Viewport.Count);Assert.DoesNotContain(Key.Space,bubbled);
            window.KeyPress(Key.K,RawInputModifiers.Control,PhysicalKey.K,"k");window.KeyRelease(Key.K,RawInputModifiers.Control,PhysicalKey.K,"k");
            window.KeyPress(Key.F,RawInputModifiers.None,PhysicalKey.F,"f");window.KeyRelease(Key.F,RawInputModifiers.None,PhysicalKey.F,"f");
            window.KeyPress(Key.Escape,RawInputModifiers.None,PhysicalKey.Escape,"");window.KeyRelease(Key.Escape,RawInputModifiers.None,PhysicalKey.Escape,"");
            Assert.Contains(Key.K,bubbled);Assert.Contains(Key.F,bubbled);Assert.Contains(Key.Escape,bubbled);
        }
        finally {window.Close();}
    }
    [AvaloniaFact] public void Crosshair_link_maps_to_containing_native_candle_without_moving_other_viewport()
    {
        var source=new CandleChartControl(new());var target=new CandleChartControl(new() {Frame=15});
        source.Update(new(Bars(),.25,"LIVE",120025,1));target.Update(new(Bars().Where((_,i)=>i%15==0).ToArray(),.25,"LIVE",120025,1));
        Render(source);Render(target);source.CursorChanged+=target.SetLinkedCursor;
        source.SetCursor(new Point(220,150));var one=source.CursorReadout!;var two=target.CursorReadout!;
        Assert.True(two.Time>=one.Time);Assert.Equal(one.PriceTicks,two.PriceTicks);Assert.True(target.Viewport.Follow);Assert.Equal(120,target.Viewport.Count);
        source.SetCursor(null);Assert.Null(target.CursorReadout);
    }
    [AvaloniaTheory] [InlineData(320,220)] [InlineData(600,420)] [InlineData(1000,700)]
    public void Tiny_empty_flat_negative_and_offscreen_prices_render_without_invalid_geometry(int width,int height)
    {
        var chart=new CandleChartControl(new());chart.Measure(new Size(width,height));chart.Arrange(new Rect(0,0,width,height));
        using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width,height));bmp.Render(chart);
        var bars=Bars().Select(b=>b with {Open=-500,High=-500,Low=-500,Close=-500}).ToArray();
        chart.Update(new(bars,.01,"LIVE",-500,1));chart.SetCursor(new Point(100,80));bmp.Render(chart);
        chart.Viewport.SetPriceRange(100,120);bmp.Render(chart);Assert.True(double.IsFinite(chart.Geometry.Y(-500)));
    }
    [AvaloniaFact] public void Center_header_action_and_explicit_history_reload_have_separate_effects()
    {
        int reloaded=0;var pane=new ChartPane(new(),()=>{},()=>{},()=>{},new(),()=>reloaded++);
        pane.Update("NQ TEST",new(Bars(),.25,"LIVE",120025,1));pane.Chart.Viewport.Zoom(.5,.5,180);
        var center=pane.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="⌖");
        center.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(pane.Chart.Viewport.CenterOnLast);Assert.Equal(60,pane.Chart.Viewport.Count);Assert.Equal(0,reloaded);
        var reload=pane.ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(m=>m.Header?.ToString()=="Reload history");
        reload.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Assert.Equal(1,reloaded);
    }
    [AvaloniaFact] public void Visual_settings_apply_without_mutating_before_apply_or_forcing_history_reload()
    {
        var pref=new ChartPaneSettings();var workspace=new ChartWorkspaceSettings();int changed=0;
        var dialog=new ChartSettingsWindow(pref,()=>changed++,null,workspace);
        try
        {
            dialog.Show();
            var checks=dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
            checks.Single(c=>c.Content?.ToString()=="Last-price line & tag").IsChecked=false;
            checks.Single(c=>c.Content?.ToString()=="Crosshair").IsChecked=false;
            checks.Single(c=>c.Content?.ToString()=="Link crosshair between charts").IsChecked=true;
            Assert.True(pref.ShowCrosshair);Assert.True(pref.ShowLastPrice);Assert.False(workspace.LinkCrosshair);
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="APPLY").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(pref.ShowCrosshair);Assert.False(pref.ShowLastPrice);Assert.True(workspace.LinkCrosshair);Assert.Equal(1,changed);
        }
        finally {dialog.Close();}
    }
    static ChartBar[] Bars()=>Enumerable.Range(0,180).Select(i=>new ChartBar(i,DateTimeOffset.UnixEpoch.AddMinutes(i),new(2026,10,15),120000,120040,119960,120010,10,1,false)).ToArray();
    static byte[] Render(CandleChartControl chart)
    {
        chart.Measure(new Size(600,420));chart.Arrange(new Rect(0,0,600,420));
        using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600,420));bmp.Render(chart);using var bytes=new MemoryStream();bmp.Save(bytes);return bytes.ToArray();
    }
    [AvaloniaFact] public void Price_guide_and_axis_tag_use_same_latest_tick_not_old_candle_close()
    {
        var bars=Bars();var chart=new CandleChartControl(new());chart.Update(new(bars,.25,"LIVE",120025,1));
        using var bitmap=SKBitmap.Decode(Render(chart));var range=chart.Viewport.PriceRange(bars.Skip(68).ToArray());
        var geometry=new ChartGeometry(600,420,chart.Viewport.Start,chart.Viewport.Count,range.Low,range.High);
        var pixel=bitmap.GetPixel((int)geometry.PlotWidth-15,(int)Math.Round(geometry.Y(120025)));
        Assert.True(pixel.Blue>100,$"Expected cyan latest-price guide, got {pixel}");
    }
    [AvaloniaFact] public void Pointer_crosshair_snaps_to_candle_and_ticks_and_clears_on_axes()
    {
        var chart=new CandleChartControl(new());chart.Update(new(Bars(),.25,"LIVE",120025,1));
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=chart};
        try
        {
            window.Show();Render(chart);window.MouseMove(new Point(220,150));
            var readout=Assert.IsType<ChartCursorReadout>(chart.CursorReadout);
            Assert.InRange(readout.Index,0,179);Assert.Equal(readout.Time,Bars()[readout.Index].Time);
            Assert.Equal(chart.Geometry.X(readout.Index),readout.X,6);
            window.MouseMove(new Point(chart.Geometry.PlotWidth+20,100));Assert.Null(chart.CursorReadout);
        }
        finally {window.Close();}
    }
    [AvaloniaFact] public void Recenter_preserves_spacing_and_keeps_current_tick_centered()
    {
        var chart=new CandleChartControl(new());chart.Update(new(Bars(),.25,"LIVE",120025,1));Render(chart);
        chart.Viewport.Zoom(.5,.5,180);chart.Viewport.SetPriceRange(100,200);chart.Recenter();Render(chart);
        Assert.Equal(60,chart.Viewport.Count);Assert.True(chart.Viewport.Follow);Assert.True(chart.Viewport.CenterOnLast);
        Assert.Equal(chart.Geometry.PlotHeight/2,chart.Geometry.Y(120025),6);
    }
    [AvaloniaFact] public void Cursor_and_last_price_can_be_hidden_without_touching_viewport()
    {
        var preferences=new ChartPaneSettings();var chart=new CandleChartControl(preferences);chart.Update(new(Bars(),.25,"LIVE",120025,1));
        Render(chart);chart.SetCursor(new Point(220,150));Assert.NotNull(chart.CursorReadout);
        var withCursor=Render(chart);preferences.ShowCrosshair=false;Assert.Null(chart.CursorReadout);var withoutCursor=Render(chart);Assert.False(withCursor.SequenceEqual(withoutCursor));
        preferences.ShowLastPrice=false;var withoutPrice=Render(chart);Assert.False(withoutCursor.SequenceEqual(withoutPrice));Assert.True(chart.Viewport.Follow);
    }
}
