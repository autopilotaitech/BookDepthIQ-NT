using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BookDepthIQ.Dom.Charts;
using Xunit;
public class LargeTradeSettingsUiTests
{
    [AvaloniaFact] public void Apply_and_cancel_remain_visible_without_scrolling_past_all_settings()
    {
        var cfg=new ChartWorkspaceSettings();var window=new ChartSettingsWindow(cfg.Top,()=>{},workspaceSettings:cfg,instrument:"NQ TEST");window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach(var button in window.GetVisualDescendants().OfType<Button>().Where(c=>c.Content is string s&&s is "APPLY" or "CANCEL"))
            {
                var point=button.TranslatePoint(default,window)!.Value;
                Assert.True(point.Y>=0&&point.Y+button.Bounds.Height<=window.Bounds.Height,$"{button.Content} outside settings viewport at {point.Y}");
            }
        }
        finally {window.Close();}
    }
    [AvaloniaFact] public void Cancel_and_invalid_apply_preserve_settings_and_both_chart_apply_does_not_reload()
    {
        var cfg=new ChartWorkspaceSettings();int changes=0;
        ChartSettingsWindow Open()=>new(cfg.Top,()=>changes++,workspaceSettings:cfg,instrument:"NQ TEST");
        var first=Open();first.Show();
        first.GetVisualDescendants().OfType<NumericUpDown>().Single(c=>c.Name=="TradeMinimum").Value=85;
        first.GetVisualDescendants().OfType<Button>().Single(c=>c.Content is string text&&text=="CANCEL").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(50,cfg.TradeProfile("NQ TEST").MinContracts);Assert.Equal(0,changes);
        var window=Open();window.Show();
        try
        {
            T Named<T>(string name) where T:Control=>window.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
            var apply=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Content is string text&&text=="APPLY");
            Named<NumericUpDown>("TradeMinimum").Value=85;Named<TextBox>("TradeBuyColor").Text="invalid";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(0,changes);Assert.Equal(50,cfg.TradeProfile("NQ TEST").MinContracts);
            Named<TextBox>("TradeBuyColor").Text="#22EEAA";Named<CheckBox>("TradeEnabled").IsChecked=true;
            Named<ComboBox>("TradeApplyTo").SelectedIndex=1;
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(1,changes);
            Assert.True(cfg.Top.ShowLargeTrades);Assert.True(cfg.Bottom.ShowLargeTrades);
            Assert.Equal(85,cfg.TradeProfile("NQ TEST").MinContracts);Assert.Equal("#22EEAA",cfg.TradeProfile("NQ TEST").BuyColor);
        }
        finally {window.Close();}
    }
}
