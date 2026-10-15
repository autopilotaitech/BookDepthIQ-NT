namespace BookDepthIQ.Dom.Charts;

public enum ChartTradeKind {Entry,Target,Stop,Position,Limit}
public sealed record ChartTradeLine(string Id,double Price,int Quantity,bool Buy,ChartTradeKind Kind,string Label);

/// <summary>Read-only projection of the selected DOM trading snapshot. Never submits/manages an order.</summary>
public static class ChartOrderOverlay
{
    public static IReadOnlyList<ChartTradeLine> Build(DomState state,string account,string instrument)
    {
        if(state.Account!=account || state.Instrument!=instrument || !state.HasTradingSnapshot || !state.AccountConnectionReady) return Array.Empty<ChartTradeLine>();
        var result=new List<ChartTradeLine>();
        if(state.PosQty!=0 && double.IsFinite(state.PosAvg)) result.Add(new("position",state.PosAvg,Math.Abs(state.PosQty),state.PosQty>0,ChartTradeKind.Position,"AVG "+(state.PosQty>0?"LONG":"SHORT")));
        foreach(var order in state.OrdersOnScreen)
        {
            if(order.Left<=0 || order.State is not ("Working" or "Accepted" or "Submitted" or "TriggerPending" or "PartFilled" or "ChangePending" or "ChangeSubmitted" or "CancelPending") || order.Type is not ("Limit" or "StopMarket" or "StopLimit" or "MIT")) continue;
            var kind=order.IsTarget?ChartTradeKind.Target:order.Name.StartsWith("BDIQ SL",StringComparison.Ordinal)?ChartTradeKind.Stop:ChartTradeKind.Entry;
            var side=order.IsBuy?"BUY":"SELL";
            var label=kind==ChartTradeKind.Target?"TP "+side:kind==ChartTradeKind.Stop?"SL "+side:side+" "+(order.Type=="Limit"?"LIMIT":order.Type=="MIT"?"MIT":"STOP");
            var price=order.Type=="MIT"?order.Stop:order.Price;
            if(double.IsFinite(price)) result.Add(new(order.Id,price,order.Left,order.IsBuy,kind,label));
            if(order.Type=="StopLimit" && order.Limit!=order.Stop && double.IsFinite(order.Limit))
                result.Add(new(order.Id+"-limit",order.Limit,order.Left,order.IsBuy,ChartTradeKind.Limit,side+" STOP-LIMIT"));
        }
        return result;
    }
}
