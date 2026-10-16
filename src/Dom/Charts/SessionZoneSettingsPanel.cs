using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;
namespace BookDepthIQ.Dom.Charts;
public sealed class SessionZoneSettingsPanel : StackPanel
{
    readonly ChartWorkspaceSettings workspace;readonly ChartPaneSettings pane;
    readonly CheckBox master,showPane,labels,extend;readonly NumericUpDown opacity,previous;readonly ComboBox target;
    readonly List<(SessionDefinition Definition,CheckBox Enabled,TextBox Color,TextBox Start,TextBox End,TextBox Zone)> rows=new();
    readonly TextBlock error;
    public SessionZoneSettingsPanel(ChartWorkspaceSettings workspace,ChartPaneSettings pane,string coverage)
    {
        this.workspace=workspace;this.pane=pane;Spacing=8;var settings=workspace.Sessions.Copy();
        Children.Add(new Border {Height=1,Background=Pal.B(Pal.Line),Margin=new Thickness(0,8)});
        Children.Add(new TextBlock {Text="SESSION ZONES",FontSize=12,Foreground=Pal.B(Pal.Accent)});
        master=Check("Enable session zones",settings.Enabled,"SessionEnabled");showPane=Check("Show on this chart",pane.ShowSessionZones||!settings.Enabled,"SessionPane");
        foreach(var d in settings.Definitions)
        {
            var prefix="Session"+(d.Id=="ny"?"NewYork":char.ToUpperInvariant(d.Id[0])+d.Id[1..]);
            var group=new StackPanel {Spacing=6};var enabled=new CheckBox {Content=d.Name,IsChecked=d.Enabled,Name=prefix+"Enabled"};group.Children.Add(enabled);
            TextBox Field(string label,string name,string value)
            {group.Children.Add(new TextBlock {Text=label,FontSize=10,Foreground=Pal.B(Pal.Muted)});var box=new TextBox {Name=name,Text=value,FontFamily=Pal.Mono,FontSize=12};group.Children.Add(box);return box;}
            var color=Field("COLOR · #RRGGBB",prefix+"Color",d.Color);
            var swatch=new Border {Height=4,Background=Pal.B(Color.Parse(d.Color)),CornerRadius=new CornerRadius(2)};group.Children.Add(swatch);
            color.TextChanged+=(_,_)=> {if(LargeTradeProfile.ValidColor(color.Text)) swatch.Background=Pal.B(Color.Parse(color.Text!));};
            var start=Field("START · HH:mm",prefix+"Start",d.Start);var end=Field("END · HH:mm (earlier = overnight)",prefix+"End",d.End);
            var zone=Field("TIME ZONE · IANA or Windows ID",prefix+"Zone",d.TimeZoneId);
            Children.Add(new Expander {Header=d.Name,Content=group,HorizontalAlignment=HorizontalAlignment.Stretch});rows.Add((d,enabled,color,start,end,zone));
        }
        Hint("Fill opacity %");opacity=new NumericUpDown {Value=(decimal)(settings.Opacity*100),Minimum=0,Maximum=30,Name="SessionOpacity",FormatString="0"};Children.Add(opacity);
        labels=Check("High / low labels",settings.ShowLabels,"SessionLabels");extend=Check("Extend completed highs / lows",settings.ExtendLevels,"SessionExtend");
        Hint("Previous completed windows per session");previous=new NumericUpDown {Value=settings.Previous,Minimum=1,Maximum=8,Name="SessionPrevious",FormatString="0"};Children.Add(previous);
        target=new ComboBox {Name="SessionApplyTo",ItemsSource=new[]{"This chart","Both charts"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};Children.Add(target);
        Hint("Custom windows, not exchange calendars. Named zones follow DST. Day charts omit zones.");Hint(coverage);
        error=new TextBlock {FontSize=11,Foreground=Pal.B(Pal.Ask),TextWrapping=TextWrapping.Wrap};Children.Add(error);
    }
    void Hint(string text)=>Children.Add(new TextBlock {Text=text,FontSize=11,Foreground=Pal.B(Pal.Muted),TextWrapping=TextWrapping.Wrap});
    CheckBox Check(string text,bool value,string name) {var c=new CheckBox {Name=name,Content=text,IsChecked=value};Children.Add(c);return c;}
    public bool TryDraft(out SessionZoneSettings draft)
    {
        draft=new();var definitions=new List<SessionDefinition>();
        foreach(var row in rows)
        {
            var d=row.Definition with {Enabled=row.Enabled.IsChecked==true,Color=row.Color.Text??"",Start=row.Start.Text??"",End=row.End.Text??"",TimeZoneId=row.Zone.Text??""};
            if(!LargeTradeProfile.ValidColor(d.Color)) {error.Text=d.Name+": use #RRGGBB color.";return false;}
            if(!ChartSessionZones.TryWindow(d,new DateOnly(2026,1,15),out _,out _,out var reason)) {error.Text=d.Name+": "+reason;return false;}
            definitions.Add(d);
        }
        if(opacity.Value==null||previous.Value==null) {error.Text="Enter opacity and previous-window count.";return false;}
        draft=new() {Enabled=master.IsChecked==true,Opacity=(double)opacity.Value/100,Previous=(int)previous.Value,ShowLabels=labels.IsChecked==true,ExtendLevels=extend.IsChecked==true,Definitions=definitions.ToArray()};draft.Normalize();return true;
    }
    public void ApplyDraft(SessionZoneSettings draft)
    {
        workspace.Sessions=draft;pane.ShowSessionZones=showPane.IsChecked==true;
        if(target.SelectedIndex==1) workspace.Top.ShowSessionZones=workspace.Bottom.ShowSessionZones=pane.ShowSessionZones;
    }
}
