using System.Text.Json.Serialization;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed class ChartPaneSettings
{
    public bool Visible {get;set;}=true;
    public int Frame {get;set;}=3;
    public bool Pax {get;set;}
    public bool Volume {get;set;}
    public bool ShowOrders {get;set;}=true;
    public bool ShowLastPrice {get;set;}=true;
    public bool ShowCrosshair {get;set;}=true;
    public bool SnapCrosshair {get;set;}=true;
    public bool Sma {get;set;}
    public bool Ema {get;set;}
    public int Period {get;set;}=20;
    public int PaxDays {get;set;}=8;
    public Dictionary<int,int> History {get;set;}=new();
    [JsonIgnore] public bool PaxActive=>Pax && ChartFrame.SupportsPax(Frame);
    public int Lookback(int frame)=>History.TryGetValue(frame,out var count)?Math.Clamp(count,250,10000):ChartFrame.DefaultBars(frame);
    public void SetLookback(int frame,int count)=>History[frame]=Math.Clamp(count,250,10000);
    public void Normalize() {if(!ChartFrame.Choices.Contains(Frame)) Frame=3;History??=new();Period=Math.Clamp(Period,1,500);PaxDays=Math.Clamp(PaxDays,1,8);}
}
public sealed class ChartWorkspaceSettings
{
    public bool Enabled {get;set;}
    public double WidthRatio {get;set;}=.32;
    public double HeightRatio {get;set;}=.5;
    public bool LinkCrosshair {get;set;}
    public ChartPaneSettings Top {get;set;}=new();
    public ChartPaneSettings Bottom {get;set;}=new() {Frame=15};
    public void Normalize()
    {
        WidthRatio=double.IsFinite(WidthRatio)?Math.Clamp(WidthRatio,.1,.8):.32;
        HeightRatio=double.IsFinite(HeightRatio)?Math.Clamp(HeightRatio,.2,.8):.5;
        Top??=new();Bottom??=new() {Frame=15};Top.Normalize();Bottom.Normalize();
    }
}
