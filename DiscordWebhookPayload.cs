namespace DiscordRssWebhook
{
    public class Author
    {
        public string name { get; set; }
        public string url { get; set; }
        public string icon_url { get; set; }
    }

    public class Embed
    {
        public Author author { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string description { get; set; }
        public int color { get; set; }
        public List<Field> fields { get; set; }
        public Thumbnail thumbnail { get; set; }
        public Image image { get; set; }
        public Footer footer { get; set; }
    }

    public class Field
    {
        public string name { get; set; }
        public string value { get; set; }
        public bool inline { get; set; }
    }

    public class Footer
    {
        public string text { get; set; }
        public string icon_url { get; set; }
    }

    public class Image
    {
        public string url { get; set; }
    }

    public class DiscordWebhookPayload
    {
        public string username { get; set; }
        public string avatar_url { get; set; }
        public string content { get; set; }
        public List<Embed> embeds { get; set; }
    }

    public class Thumbnail
    {
        public string url { get; set; }
    }
}
