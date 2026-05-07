namespace TelegramClient.Models
{
    /// <summary>
    /// A single member of a Telegram group, supergroup, or channel.
    /// Used for both CSV export and vCard generation.
    /// </summary>
    public class MemberEntry
    {
        public long   UserId    { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName  { get; set; } = string.Empty;
        public string Username  { get; set; } = string.Empty;

        /// <summary>
        /// Phone number — only populated when the user has shared it with you.
        /// Most members will have an empty string here due to privacy settings.
        /// </summary>
        public string Phone     { get; set; } = string.Empty;

        public bool   IsBot     { get; set; }
        public bool   IsAdmin   { get; set; }

        /// <summary>Display name built from first + last name (or username as fallback).</summary>
        public string DisplayName =>
            string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
                ? (string.IsNullOrWhiteSpace(Username) ? UserId.ToString() : $"@{Username}")
                : $"{FirstName} {LastName}".Trim();
    }
}
