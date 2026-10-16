using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BookDepthIQ.Dom.Charts;
using Xunit;
public class SessionZoneSettingsTests
{
    [Fact] public void Json_normalization_and_deep_drafts_preserve_session_identity()
    {
        var cfg=new ChartWorkspaceSettings();Assert.False(cfg.Sessions.Enabled);Assert.False(cfg.Top.ShowSessionZones);
        var draft=cfg.Sessions.Copy();draft.Definitions[1]=draft.Definitions[1] with {Color="#123456",Start="07:00"};
        Assert.Equal("#00BBD6",cfg.Sessions.Definitions[1].Color);
        draft.Opacity=double.NaN;draft.Previous=900;draft.Normalize();Assert.Equal(.08,draft.Opacity);Assert.Equal(8,draft.Previous);
        cfg.Sessions=draft;
        var saved=System.Text.Json.JsonSerializer.Deserialize<ChartWorkspaceSettings>(System.Text.Json.JsonSerializer.Serialize(cfg))!;saved.Normalize();
        Assert.Equal("#123456",saved.Sessions.Definitions[1].Color);Assert.Equal("07:00",saved.Sessions.Definitions[1].Start);
    }
    [AvaloniaFact] public void Invalid_session_apply_is_atomic_with_other_fields_and_both_panes_can_be_enabled()
    {
        var cfg=new ChartWorkspaceSettings();int changed=0;var window=new ChartSettingsWindow(cfg.Top,()=>changed++,workspaceSettings:cfg,instrument:"NQ TEST");window.Show();
        window.GetVisualDescendants().OfType<Expander>().Single(c=>c.Header is string s&&s=="LONDON").IsExpanded=true;Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            T Named<T>(string name) where T:Control=>window.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
            var apply=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is string t&&t=="APPLY");
            Named<TextBox>("SessionLondonZone").Text="no-such-zone";Named<NumericUpDown>("TradeMinimum").Value=777;
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(0,changed);Assert.Equal(50,cfg.TradeProfile("NQ TEST").MinContracts);
            Named<TextBox>("SessionLondonZone").Text="Europe/London";
            Named<CheckBox>("SessionEnabled").IsChecked=true;Named<ComboBox>("SessionApplyTo").SelectedIndex=1;
            Named<TextBox>("SessionLondonColor").Text="#22AABB";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1,changed);Assert.True(cfg.Sessions.Enabled);Assert.True(cfg.Top.ShowSessionZones);Assert.True(cfg.Bottom.ShowSessionZones);
            Assert.Equal("#22AABB",cfg.Sessions.Definitions[1].Color);
        }
        finally {window.Close();}
    }
    [AvaloniaFact] public void Cancel_leaves_custom_session_hours_and_enable_flags_unchanged()
    {
        var cfg=new ChartWorkspaceSettings();var window=new ChartSettingsWindow(cfg.Top,()=>{},workspaceSettings:cfg);window.Show();
        window.GetVisualDescendants().OfType<Expander>().Single(c=>c.Header is string s&&s=="LONDON").IsExpanded=true;Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="SessionLondonStart").Text="07:00";
        window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content is string t&&t=="CANCEL").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("08:00",cfg.Sessions.Definitions[1].Start);Assert.False(cfg.Sessions.Enabled);
    }
}
