using Avalonia;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed partial class CandleChartControl
{
    void DrawTradeLines(DrawingContext context,double w,double h,Func<double,double> yAt)
    {
        if(!double.IsFinite(snapshot.TickSize)||snapshot.TickSize<=0) return;
        Color ColorFor(ChartTradeLine line)=>line.Kind switch {ChartTradeKind.Stop=>Pal.Neg,ChartTradeKind.Target=>Pal.Pos,ChartTradeKind.Position=>Pal.Accent,_=>line.Buy?Pal.Bid:Pal.Ask};
        var groups=TradeLines.Where(l=>double.IsFinite(l.Price)&&l.Quantity>0).GroupBy(l=>(l.Price,l.Kind,l.Buy,l.Label))
            .Select(g=>new {Line=g.First(),Quantity=g.Sum(l=>(long)l.Quantity),Count=g.Count(),Y=yAt(g.Key.Price/snapshot.TickSize)})
            .OrderBy(g=>g.Y).ToArray();
        foreach(var group in groups)
            if(group.Y>=0&&group.Y<=h) context.DrawLine(new Pen(Pal.B(ColorFor(group.Line)),1,DashStyle.Dash),new Point(0,group.Y),new Point(w,group.Y));
        int capacity=Math.Max(1,(int)(h/19)-1);var labels=groups.Take(capacity).ToArray();var positions=new double[labels.Length];
        for(int i=0;i<labels.Length;i++) positions[i]=Math.Max(Math.Clamp(labels[i].Y,8,Math.Max(8,h-9)),i==0?8:positions[i-1]+19);
        for(int i=positions.Length-1;i>=0;i--) positions[i]=Math.Min(positions[i],i==positions.Length-1?h-9:positions[i+1]-19);
        for(int i=0;i<labels.Length;i++)
        {
            var group=labels[i];var color=ColorFor(group.Line);var arrow=group.Y<0?"↑ ":group.Y>h?"↓ ":"";
            var text=$"{arrow}{group.Line.Label} ×{group.Quantity} @ {ChartAxis.Format(group.Line.Price,snapshot.TickSize)}"+(group.Count>1?$" ({group.Count})":"");
            var ft=Formatted(text,color,10,false,Math.Max(1,w-12));
            var x=Math.Max(4,w-ft.Width-8);var y=Math.Clamp(positions[i]-8,0,Math.Max(0,h-17));
            context.FillRectangle(Pal.B(Pal.Panel),new Rect(x-3,y,Math.Min(w-x+3,ft.Width+8),17));
            context.DrawRectangle(null,new Pen(Pal.B(color),.7),new Rect(x-3,y,Math.Min(w-x+3,ft.Width+8),17));context.DrawText(ft,new Point(x,y+2));
        }
        if(groups.Length>labels.Length) Text(context,$"+{groups.Length-labels.Length} order levels",8,h-15,Pal.Muted);
    }
}
