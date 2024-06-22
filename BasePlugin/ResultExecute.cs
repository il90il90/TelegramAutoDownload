namespace BasePlugins
{
    public class ResultExecute
    {
        public ResultExecute()
        {

        }
        public ResultExecute(bool isSuccess)
        {
            this.IsSuccess = isSuccess;
        }
        public bool IsSuccess { get; set; }
        public string FileName { get; set; }
    }
}
