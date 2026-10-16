using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace BookDepthIQ.Dom.Charts;

/// <summary>Draft preferences; no changes escape until the parent dialog's Apply.</summary>
public sealed class LargeTradeSettingsPanel : StackPanel
{
    readonly ChartWorkspaceSettings workspace;readonly ChartPaneSettings pane;readonly string instrument;
    readonly CheckBox enabled;readonly NumericUpDown minimum,opacity,minSize,maxSize;
    readonly TextBox buy,sell,unknown;readonly ComboBox labels,applyTo;readonly TextBlock error;
    public LargeTradeSettingsPanel(ChartWorkspaceSettings workspace,ChartPaneSettings pane,string instrument,string coverage)
    {
        this.workspace=workspace;this.pane=pane;this.instrument=instrument;Spacing=8;
        var draft=workspace.TradeProfiles.TryGetValue(ChartWorkspaceSettings.TradeProfileKey(instrument),out var existing)&&existing!=null?existing with{}:new LargeTradeProfile();draft.Normalize();
        Children.Add(new Border {Height=1,Background=Pal.B(Pal.Line),Margin=new Thickness(0,8)});
        Children.Add(new TextBlock {Text="LARGE TRADES",FontSize=12,Foreground=Pal.B(Pal.Accent)});
        enabled=new CheckBox {Name="TradeEnabled",Content="Show executed-trade bubbles",IsChecked=pane.ShowLargeTrades};Children.Add(enabled);
        Hint($"Profile: {ChartWorkspaceSettings.TradeProfileKey(instrument)} · Individual prints");
        Hint("Profile values are shared; Apply to controls pane visibility.");
        minimum=Number("Minimum contracts","TradeMinimum",draft.MinContracts,1,1000000);
        buy=ColorField("Buy color","TradeBuyColor",draft.BuyColor);
        sell=ColorField("Sell color","TradeSellColor",draft.SellColor);
        unknown=ColorField("Unknown color","TradeUnknownColor",draft.UnknownColor);
        opacity=Number("Fill opacity %","TradeOpacity",draft.Opacity*100,0,100);
        minSize=Number("Minimum diameter (DIP)","TradeMinSize",draft.MinDiameter,6,80);
        maxSize=Number("Maximum diameter (DIP)","TradeMaxSize",draft.MaxDiameter,6,80);
        Hint("Quantity labels");labels=new ComboBox {Name="TradeLabels",ItemsSource=new[]{"Off","Large markers","All"},SelectedIndex=(int)draft.Labels,HorizontalAlignment=HorizontalAlignment.Stretch};Children.Add(labels);
        Hint("Apply to");applyTo=new ComboBox {Name="TradeApplyTo",ItemsSource=new[]{"This chart","Both charts"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};Children.Add(applyTo);
        Hint("Live capture only · no historical backfill in this build.");
        Hint("Intraday only; unproved first/session-gap intervals omitted.");Hint(coverage);
        error=new TextBlock {Foreground=Pal.B(Pal.Ask),TextWrapping=TextWrapping.Wrap,FontSize=11};Children.Add(error);
    }
    void Hint(string text)=>Children.Add(new TextBlock {Text=text,FontSize=11,Foreground=Pal.B(Pal.Muted),TextWrapping=TextWrapping.Wrap});
    NumericUpDown Number(string title,string name,double value,int min,int max)
    {Hint(title);var c=new NumericUpDown {Name=name,Value=(decimal)value,Minimum=min,Maximum=max,FormatString="0"};Children.Add(c);return c;}
    TextBox ColorField(string label,string name,string value)
    {
        Hint(label);var row=new Grid {ColumnDefinitions=new("28,*")};
        var swatch=new Border {Background=Pal.B(Color.Parse(value)),CornerRadius=new CornerRadius(4),Margin=new Thickness(0,4,6,4)};row.Children.Add(swatch);
        var box=new TextBox {Name=name,Text=value,Watermark="#RRGGBB",FontFamily=Pal.Mono};Grid.SetColumn(box,1);row.Children.Add(box);Children.Add(row);
        box.TextChanged+=(_,_)=> {if(LargeTradeProfile.ValidColor(box.Text)) swatch.Background=Pal.B(Color.Parse(box.Text!));};
        return box;
    }
    public bool TryApply()
    {
        if(!LargeTradeProfile.ValidColor(buy.Text)||!LargeTradeProfile.ValidColor(sell.Text)||!LargeTradeProfile.ValidColor(unknown.Text))
        {error.Text="Use #RRGGBB colors, for example #00C8F4.";return false;}
        if(minimum.Value==null||opacity.Value==null||minSize.Value==null||maxSize.Value==null||maxSize.Value<minSize.Value)
        {error.Text="Enter all numbers; maximum diameter must be ≥ minimum.";return false;}
        var profile=new LargeTradeProfile {MinContracts=(long)minimum.Value,BuyColor=buy.Text!,SellColor=sell.Text!,UnknownColor=unknown.Text!,Opacity=(double)opacity.Value/100,MinDiameter=(double)minSize.Value,MaxDiameter=(double)maxSize.Value,Labels=(TradeQuantityLabels)labels.SelectedIndex};profile.Normalize();
        workspace.TradeProfiles[ChartWorkspaceSettings.TradeProfileKey(instrument)]=profile;
        pane.ShowLargeTrades=enabled.IsChecked==true;
        if(applyTo.SelectedIndex==1) workspace.Top.ShowLargeTrades=workspace.Bottom.ShowLargeTrades=pane.ShowLargeTrades;
        return true;
    }
}
