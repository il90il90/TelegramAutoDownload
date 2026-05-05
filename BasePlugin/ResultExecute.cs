namespace BasePlugins
{
    public class ResultExecute
    {
        public ResultExecute(string name)
        {
            this.Name = name;
        }
        /// <summary>
        /// Name = Group\Channel\User Name
        /// </summary>
        public string Name { get; set; }
        public bool IsSuccess { get; set; }
        public string FileName { get; set; }
        /// <summary>
        /// Full path to the downloaded file on disk.
        /// </summary>
        public string FilePath { get; set; }
        public string MessageType { get; set; }
        public string ErrorMessage { get; set; }
        /// <summary>
        /// Key used to match this result with an in-progress Telegram notification message.
        /// Plugins that send progress updates should set this to the same key used during OnProgress.
        /// </summary>
        public string NotificationKey { get; set; }
    }
}
