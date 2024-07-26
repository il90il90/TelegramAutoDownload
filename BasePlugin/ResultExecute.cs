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
        public string MessageType { get; set; }
        public string ErrorMessage { get; set; }
    }
}
