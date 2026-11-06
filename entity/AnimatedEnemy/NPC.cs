public partial class NPC : AnimatedEnemy
{
    public bool IsTrading { get; private set; }
    public TradingStand ActiveTradingStand { get; private set; }
    public Player TradingPlayer { get; private set; }

    public bool BeginTrading(TradingStand stand, Player player)
    {
        if (IsTrading || stand == null || player == null)
            return false;

        ActiveTradingStand = stand;
        TradingPlayer = player;
        IsTrading = true;
        return true;
    }

    public void EndTrading(TradingStand stand, Player player)
    {
        if (!IsTrading || ActiveTradingStand != stand || TradingPlayer != player)
            return;

        ActiveTradingStand = null;
        TradingPlayer = null;
        IsTrading = false;
    }
}
