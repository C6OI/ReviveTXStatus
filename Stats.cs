namespace ReviveTXStatus; 

public class Stats {
    public ushort OnlineCount { get; set; }
    public byte MMBattles { get; set; }
    public byte CustomBattles { get; set; }

    public override string ToString() => $"Online: {OnlineCount}\n" +
                                         $"Matchmaking battles: {MMBattles}\n" +
                                         $"Custom battles: {CustomBattles}";
}
