﻿using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.IO;
using System.Diagnostics;

namespace discordBot
{
    class Program
    {
        private readonly DiscordSocketClient client;
        private readonly CommandService commands;
        private readonly IServiceProvider services;
        private Random rand;
        private readonly string tokenPath;
        private readonly string token;
        public static readonly Stopwatch sw = new Stopwatch();
        // A string that specifies how long the bot has been running based on when it connected
        public static string swElapsed
        {
            get
            {
                string days = (sw.Elapsed.Days < 10) ? "0" + sw.Elapsed.Days : sw.Elapsed.Days.ToString();
                string hours = (sw.Elapsed.Hours < 10) ? "0" + sw.Elapsed.Hours : sw.Elapsed.Hours.ToString();
                string minutes = (sw.Elapsed.Minutes < 10) ? "0" + sw.Elapsed.Minutes : sw.Elapsed.Minutes.ToString();
                string seconds = (sw.Elapsed.Seconds < 10) ? "0" + sw.Elapsed.Seconds : sw.Elapsed.Seconds.ToString();
                return $"{days}:{hours}:{minutes}.{seconds}";
            }
        }

        readonly Task T;

        static void Main(string[] args) => new Program().Start(args).GetAwaiter().GetResult();

        public Program()
        {
            // Sets things needed for Program()
            rand = new Random();
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                //MessageCacheSize = 20,
            });
            commands = new CommandService();
            services = new ServiceCollection().BuildServiceProvider();

            tokenPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.txt");
            // Checks if the token file exists and either creates the file and asks for token, or reads token from file
            if (!File.Exists(tokenPath))
            {
                Console.WriteLine("What is the bot token?");
                Console.Write("Token: ");
                token = Console.ReadLine();
                Console.Clear();
                // Writes the token to the file
                File.WriteAllText(tokenPath, token);
            }
            else
            {
                // Reads the first line from the token file
                token = File.ReadLines(tokenPath).FirstOrDefault();
            }

            // Sets the task, T, to update the "playing" status with uptime every 45 seconds
            T = new Task(async () =>
            {
                sw.Start();
                while (true)
                {
                    await UpdateUptime();
                    await Task.Delay(45000);
                }
            });
        }

        public async Task Start(string[] args)
        {
            // Adds the logger
            client.Log += Logger;

            string arg = string.Empty;
            if (args.Length > 0)
            {
                arg = args[0];
            }
            // If there is no argument, install commands and run like normal
            if (string.IsNullOrEmpty(arg) || arg != "sbRoleLottery")
            {
                await InstallCommands();
            }
            // If "sbRoleLottery" is the first argument, run the lottery instead of normal operation
            else if (arg == "sbRoleLottery")
            {
                client.Ready += RoleLottery;
            }
            
            // Tries to login, if it fails the token is probably incorrect (or discord is down)
            try
            {
                await client.LoginAsync(TokenType.Bot, token);
            }
            catch (Exception) // Restarts the program if the token is incorrect
            {
                Console.Clear();
                Console.WriteLine("That token doesn't work, or Discord may be down, please try again.");
                File.Delete(tokenPath);
                Console.ReadLine();
                new Program().Start(args).GetAwaiter().GetResult();
            }

            await client.StartAsync();
            //await a;
            await Task.Delay(-1);
        }

        public async Task RoleLottery()
        {
            IGuild g = client.GetGuild(259533308512174081) as IGuild; // Spirit Bear Guild
            IGuildChannel c = await g.GetChannelAsync(335460607279235072); // Announcement channel
            IVoiceChannel v = await g.GetVoiceChannelAsync(434092857415041024); // The winner's voice channel
            IRole e = g.EveryoneRole; // The everyone role
            IRole l = g.GetRole(411281455331672064); // The lottery role
            IRole w = g.GetRole(335456437352529921); // The winning role
            // All users in the guild
            var users = await g.GetUsersAsync();

            // All possible participants
            IEnumerable<IGuildUser> participants = users.Where(a => a.RoleIds.Any(b => b == l.Id));
            // Everyone who currently has the winning role
            IEnumerable<IGuildUser> currentWinners = users.Where(a => a.RoleIds.Any(b => b == w.Id));
            
            // Removes any current winner from the participants list
            participants.ToList().RemoveAll(a => currentWinners.Any(b => a == b));

            string msg = "Lottery:\n";
            
            // Adds who the role was removed from to the message
            msg += $"Took away {string.Join(", ", currentWinners.Select(a => a.Username))}\'s {w.Name}\n";

            // Removes the winning role from anyone who currently has it
            foreach (var user in currentWinners)
                await user.RemoveRoleAsync(w, new RequestOptions { AuditLogReason = $"Previous {w.Name}" });

            // Randomly selects the winner
            IGuildUser winner = participants.ElementAt(rand.Next(0, participants.Count()));

            // Gives the winner their role
            await winner.AddRoleAsync(w, new RequestOptions { AuditLogReason = $"The new {w.Name} is in town" });

            // Edits the winner's voice channel name
            await v.ModifyAsync((VoiceChannelProperties p) =>
            {
                p.Name = $"{winner.Username}\'s Executive Suite";
                p.Bitrate = 64000;
            }, new RequestOptions { AuditLogReason = "Reset and rename" });

            // Resets permissions to their 'default' values
            await v.SyncPermissionsAsync(new RequestOptions { AuditLogReason = "Reset permissions" });
            // Edits everyone role permission overwrites
            await v.AddPermissionOverwriteAsync(e,
                new OverwritePermissions(connect: PermValue.Deny, moveMembers: PermValue.Deny),
                new RequestOptions { AuditLogReason = "Reset permissions" });
            // Edits winner role permission overwrites
            await v.AddPermissionOverwriteAsync(w,
                new OverwritePermissions(connect: PermValue.Allow, moveMembers: PermValue.Allow),
                new RequestOptions { AuditLogReason = "Reset permssions" });

            msg += $"Participants: {string.Join(", ",participants.Select(a=>a.Username))}\n";
            msg += $"This week's winner is: {winner.Username}!";

            await (c as ISocketMessageChannel).SendMessageAsync(msg);

            await client.StopAsync();
            Environment.Exit(0);
        }

        public async Task InstallCommands()
        {
            // Hook the MessageReceived Event into our Command Handler
            client.MessageReceived += HandleCommand;

            client.Connected += async () => T.Start();
            
            //client.MessageDeleted += HandleDelete;
            // Discover all of the commands in this assembly and load them.
            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
        }

        public async Task UpdateUptime()
        {
            await client.SetGameAsync($"m!help for {swElapsed}", type: ActivityType.Playing);
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;
            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!(message.HasStringPrefix("m!", ref argPos) || message.HasMentionPrefix(client.CurrentUser, ref argPos))) return;
            // Create a Command Context
            var context = new CommandContext(client, message);
            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await commands.ExecuteAsync(context, argPos, services);
            if (!result.IsSuccess)
                await context.Channel.SendMessageAsync(result.ErrorReason);
            // Update the uptime
            await UpdateUptime();
        }

        public async Task HandleDelete(Cacheable<IMessage, ulong> message, ISocketMessageChannel channel)
        {
            IMessage x = await message.GetOrDownloadAsync();
            await x.Channel.SendMessageAsync($"The deleted message was from {x.Author} and was: {x.Content}");
        }

        private Task Logger(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
