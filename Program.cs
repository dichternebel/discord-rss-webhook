using System.Configuration;
using System.Net.Http.Json;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using LiteDB;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using static DiscordRssWebhook.Native;

namespace DiscordRssWebhook
{
    public class Program
    {
        private static readonly string? webHookUrl = ConfigurationManager.AppSettings["Webhook"];
        private static readonly string? feedUrl = ConfigurationManager.AppSettings["Feed"];
        private static double updateInteval = 10.0;
        private static List<string> categories = new();
        private static string? userName = ConfigurationManager.AppSettings["UserName"];
        private static string? avatarUrl = ConfigurationManager.AppSettings["Avatar"];
        private static string? contentMessage = ConfigurationManager.AppSettings["Content"];
        private static string baseDir = AppContext.BaseDirectory;
        private static readonly HttpClient _httpClient = new();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ConsoleEventDelegate handler;
        private static System.Timers.Timer feedTimer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static async Task<int> Main()
        {
            // Initialize path
            //var baseDir = AppContext.BaseDirectory;

            // Initialize serilog logger
            Log.Logger = new LoggerConfiguration()
                 .WriteTo.Console(Serilog.Events.LogEventLevel.Debug, theme: AnsiConsoleTheme.Literate)
                 .WriteTo.File(Path.Combine(baseDir, "logs", $"log-.txt"), Serilog.Events.LogEventLevel.Information, rollingInterval: RollingInterval.Day)
                 .MinimumLevel.Information()
                 .Enrich.FromLogContext()
                 .CreateLogger();

            if (string.IsNullOrEmpty(webHookUrl))
            {
                Log.Logger.Fatal("Please provide a webHook URL in config! Aborting...");
                return 1;
            }
            if (string.IsNullOrEmpty(feedUrl))
            {
                Log.Logger.Fatal("Please provide a feed URL in config! Aborting...");
                return 1;
            }
            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["UpdateInterval"]))
            {
                // set interval between 1m and 30 days
                if (double.TryParse(ConfigurationManager.AppSettings["UpdateInterval"], out double parseNumber) && parseNumber >= 1.0 && parseNumber <= 43200.0)
                {
                    updateInteval = parseNumber;
                }
            }

            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["Categories"]))
            {
                categories = ConfigurationManager.AppSettings["Categories"].Split(new char[] { ',' }).Select(category => category.Trim()).ToList();
            }

            // Name this thing
            Console.Title = $"Discord RSS Webhook crawling {feedUrl}";

            try
            {
                SetQuickEditMode(false);

                handler = new ConsoleEventDelegate(ConsoleEventCallback);
                SetConsoleCtrlHandler(handler, true);

                // Initialize timer
                feedTimer = new System.Timers.Timer(updateInteval * 60 * 1000); // every x minutes
                feedTimer.Elapsed += FeedTimer_Elapsed;
                feedTimer.Start();
                // Run it immediately
                FeedTimer_Elapsed(null, null);

                // Block this task until the program is closed.
                await Task.Delay(-1);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Logger.Fatal(ex.Message);
                return 1;
            }
        }

        private static bool ConsoleEventCallback(int eventType)
        {
            if (eventType == 2)
            {
                feedTimer.Elapsed -= FeedTimer_Elapsed;
                feedTimer.Stop();
                Log.Logger.Warning("Timer stopped!");
            }
            return false;
        }

        private static async void FeedTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var rawfeed = await FeedReader.ReadAsync(feedUrl);
            var feed = (Rss20Feed)rawfeed.SpecificFeed;
            var feedItems = (feed.Items.Select(item => (Rss20FeedItem)item)).ToList();
            var filteredItems = new List<Rss20FeedItem>();

            if (!categories.Any())
            {
                filteredItems = feedItems;
            }
            else
            {
                filteredItems.AddRange(feedItems.Where(item => item.Categories.Intersect(categories).Any()));
            }

            if (!filteredItems.Any()) return;

            if (string.IsNullOrEmpty(avatarUrl))
            {
                avatarUrl = feed.Image.Url;
            }
            if (string.IsNullOrEmpty(userName))
            {
                userName = "The Feed Poster";
            }
            if (string.IsNullOrEmpty(contentMessage))
            {
                contentMessage = $"New posts from feed '{feed.Title} - {feed.Description}' arrived!";
            }

            var currentWebHook = new DiscordWebhookPayload
            {
                username = userName,
                avatar_url = avatarUrl,
                content = contentMessage,
                embeds = new List<Embed>()
            };

            // ToDo: Discord limit messages with embeds to 6000 characters. Exceeding this will result in a bad request...

            using (var db = new LiteDatabase(Path.Combine(baseDir, "FeedPosts.db")))
            {
                // Get feed post collection
                var col = db.GetCollection<FeedPost>("feedposts");
                col.EnsureIndex(x => x.PostGuid, true);
                var transientFeedPosts = new List<FeedPost>();

                foreach (var item in filteredItems)
                {
                    // check if post is already sent
                    if (col.Exists(x  => x.PostGuid == item.Guid)) { continue; }

                    var currentEmbed = new Embed
                    {
                        color = 15258703,
                        author = new Author
                        {
                            name = item.DC.Creator
                        },
                        title = item.Title,
                        url = item.Link,
                        description = ":mega: " + item.Description.Replace("[&#8230;]", "..."),
                        footer = new Footer
                        {
                            text = item.PublishingDateString
                        }
                    };

                    Log.Logger.Information($"Adding '{item.Title} - {item.Link}' to webhook message...");
                    currentWebHook.embeds.Add(currentEmbed);

                    var transientFeedPost = new FeedPost { PostGuid = item.Guid, PostedOn = DateTime.UtcNow };
                    transientFeedPosts.Add(transientFeedPost);
                }

                if (currentWebHook.embeds.Any())
                {
                    Log.Logger.Information($"Posting {currentWebHook.embeds.Count} post(s) from feed '{feed.Title} - {feed.Description}' to Discord...");
                    var result = await _httpClient.PostAsJsonAsync(webHookUrl, currentWebHook);

                    if (result == null)
                    {
                        Log.Logger.Error("Aw snap! No result received from HTTP post...");
                        return;
                    }
                    if (result.IsSuccessStatusCode)
                    {
                        col.Insert(transientFeedPosts);
                        Log.Logger.Information("Done!");
                        return;
                    }

                    Log.Logger.Error($"{result}");
                }
            }
        }
    }
}