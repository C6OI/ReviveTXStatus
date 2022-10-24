namespace ReviveTXStatus;

public class Server {
    public ulong Id { get; set; }
    public ulong Channel { get; set; }
    public ulong LastEmbed { get; set; }
    public bool DeletePreviousEmbed { get; set; }
}
