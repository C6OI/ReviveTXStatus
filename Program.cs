using System.Reflection;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ReviveTXStatus;

public static class Program {
    static Timer _timer = null!;
    
    public static async Task Main() => await BotStart();

    static async Task BotStart() {
        Config config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync("./Stuff/config.json")) ?? new Config();

        DiscordClient client = new(new DiscordConfiguration {
            Intents = DiscordIntents.All,
            Token = config.Token,
            TokenType = TokenType.Bot
        });
        
        client.GuildDownloadCompleted += OnGuildsDownloaded;

        CommandsNextExtension commandsNextExtension = client.UseCommandsNext(new CommandsNextConfiguration {
            StringPrefixes = new[] { "!" }
        });

        commandsNextExtension.RegisterCommands(Assembly.GetExecutingAssembly());
        commandsNextExtension.CommandErrored += OnCommandError;

        await client.ConnectAsync(new DiscordActivity("ReviveTX Statistics", ActivityType.Watching));
        await Task.Delay(-1);
    }

    static Task OnGuildsDownloaded(DiscordClient s, GuildDownloadCompletedEventArgs e) {
        JsonSerializerSettings serializerSettings = new() {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        _timer = new Timer(async _ => {
            List<Server>? serversInfo
                = JsonConvert.DeserializeObject<List<Server>>(await File.ReadAllTextAsync("./Stuff/servers.json"));
            
            if (serversInfo == null) return;
                
            foreach (Server server in serversInfo) {
                try {
                    DiscordGuild guild = await s.GetGuildAsync(server.Id);
                    DiscordChannel? channel = guild.GetChannel(server.Channel);

                    if (server.DeletePreviousEmbed) {
                        try {
                            DiscordMessage currentEmbed = await channel.GetMessageAsync(server.LastEmbed);
                            await currentEmbed.DeleteAsync();
                        } catch (Exception ex) {
                            await Console.Error.WriteLineAsync(ex.ToString());
                        }
                    }

                    DiscordMessage lastEmbed = await channel.SendMessageAsync(await ReviveTX.GetEmbed());
                    server.LastEmbed = lastEmbed.Id;
                } catch (Exception ex) {
                    await Console.Error.WriteLineAsync(ex.ToString());
                }
            }
            
            await File.WriteAllTextAsync("./Stuff/servers.json", JsonConvert.SerializeObject(serversInfo, serializerSettings));
        }, null, TimeSpan.Zero, TimeSpan.FromMinutes(10));

        return Task.CompletedTask;
    }

    static async Task OnCommandError(CommandsNextExtension s, CommandErrorEventArgs e) {
        if (e.Context.Member == null) return;

        if (e.Exception is ChecksFailedException checksFailed) {
            CooldownAttribute? cooldownReached = checksFailed.FailedChecks
                                                             .OfType<CooldownAttribute>()
                                                             .FirstOrDefault();

            if (cooldownReached != null) {
                TimeSpan cooldown = cooldownReached.GetRemainingCooldown(e.Context);
                await e.Context.RespondAsync($"You need to wait **{cooldown:mm\\:ss}** to use this command again.");
            }
        }
    }
}

public class Commands : BaseCommandModule {
    [Command("channel")]
    [Description("Sets the channel to which the bot will send server statistics")]
    [Cooldown(1, 60, CooldownBucketType.Guild)]
    [RequireGuild]
    public async Task SetStatusChannel(CommandContext ctx, DiscordChannel channel) {
        if (!ctx.Member?.Permissions.HasPermission(Permissions.ManageChannels) ?? false) {
            await ctx.RespondAsync("You must have **Manage Channels** permission to do this!");
            return;
        }

        JsonSerializerSettings serializerSettings = new() {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        
        List<Server>? serversInfo
            = JsonConvert.DeserializeObject<List<Server>>(await File.ReadAllTextAsync("./Stuff/servers.json"));

        serversInfo ??= new List<Server>();

        Server? server = serversInfo.FirstOrDefault(s => s.Id == ctx.Guild.Id);

        if (server == null) {
            server = new Server { Id = ctx.Guild.Id };
            serversInfo.Add(server);
        }

        if (server.DeletePreviousEmbed) {
            try {
                DiscordChannel ch = ctx.Guild.GetChannel(server.Channel);
                DiscordMessage me = await ch.GetMessageAsync(server.LastEmbed);
                await me.DeleteAsync();
            } catch (Exception ex) {
                await Console.Error.WriteLineAsync(ex.ToString());
            }
        }

        try {
            DiscordMessage lastEmbed = await channel.SendMessageAsync(await ReviveTX.GetEmbed());
            server.Channel = channel.Id;
            server.LastEmbed = lastEmbed.Id;

            await File.WriteAllTextAsync("./Stuff/servers.json", JsonConvert.SerializeObject(serversInfo, serializerSettings));
        } catch (UnauthorizedException) {
            await ctx.RespondAsync($"I can't write to the {channel.Mention}!");
        }
    }

    [Command("deletePrevious")]
    [Aliases("delete", "deletePrev", "del", "deleteEmbed")]
    [Description("If true - deletes the previous embed with stats when a new embed is sent")]
    [Cooldown(1, 30, CooldownBucketType.Guild)]
    [RequireGuild]
    public async Task ChangeDeletePreviousEmbed(CommandContext ctx) {
        if (!ctx.Member?.Permissions.HasPermission(Permissions.ManageMessages) ?? false) {
            await ctx.RespondAsync("You must have **Manage Messages** permission to do this!");
            return;
        }

        List<Server>? servers
            = JsonConvert.DeserializeObject<List<Server>>(await File.ReadAllTextAsync("./Stuff/servers.json"));
        
        if (servers == null) return;

        Server? server = servers.FirstOrDefault(s => s.Id == ctx.Guild.Id);

        if (server == null) {
            await ctx.RespondAsync("You don't have a channel for sending stats on this server!");
            return;
        }
        
        JsonSerializerSettings serializerSettings = new() {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        server.DeletePreviousEmbed = !server.DeletePreviousEmbed;
        
        await File.WriteAllTextAsync("./Stuff/servers.json", JsonConvert.SerializeObject(servers, serializerSettings));
        await ctx.RespondAsync($"This option is now set to {server.DeletePreviousEmbed}!");
    }

    [Command("stats")]
    [Aliases("statistic", "statistics", "stat")]
    [Description("Sends current server statistics")]
    [Cooldown(1, 15, CooldownBucketType.Channel)]
    public async Task GetCurrentStats(CommandContext ctx) {
        DiscordMessage response = await ctx.RespondAsync(await ReviveTX.GetEmbed());
        await Task.Delay(TimeSpan.FromSeconds(15));
        await response.DeleteAsync();
        await ctx.Message.DeleteAsync();
    }
}

public static class ReviveTX {
    public static async Task<DiscordEmbed> GetEmbed() {
        Stats? stats = await GetStats();

        return stats != null ? GetStatsEmbed(stats) : GetErrorEmbed();
    }
    
    public static async Task<Stats?> GetStats() {
        HttpRequestMessage httpRequestMessage = new() {
            Method = HttpMethod.Get,
            RequestUri = new Uri("http://main.txrevive.com/TXServer/StateServer/stats")
        };

        using (HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) }) {
            try {
                HttpResponseMessage responseMessage = await httpClient.SendAsync(httpRequestMessage);
                return JsonConvert.DeserializeObject<Stats>(await responseMessage.Content.ReadAsStringAsync());
            } catch (Exception ex) {
                await Console.Error.WriteLineAsync(ex.ToString());
                return null;
            }
        }
    }

    static DiscordEmbed GetStatsEmbed(Stats stats) {
        DiscordEmbed statsEmbed = new DiscordEmbedBuilder()
                                  .WithTitle("**Revive TX Server Statistics:**")
                                  .WithColor(DiscordColor.SpringGreen)
                                  .WithDescription($"{stats}")
                                  .WithFooter("Dev: C6OI#6060")
                                  .WithThumbnail("https://i.imgur.com/N3jgexY.png")
                                  .WithTimestamp(DateTime.UtcNow);
        
        return statsEmbed!;
    }

    static DiscordEmbed GetErrorEmbed() {
        DiscordEmbed errorEmbed = new DiscordEmbedBuilder()
                                  .WithTitle("**Server isn't responding!**")
                                  .WithColor(DiscordColor.Red)
                                  .WithFooter("Dev: C6OI#6060")
                                  .WithThumbnail("https://i.imgur.com/N3jgexY.png")
                                  .WithTimestamp(DateTime.UtcNow);

        return errorEmbed;
    }
}
