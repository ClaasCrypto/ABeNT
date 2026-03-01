namespace ABeNT.Services
{
    public static class SttServiceFactory
    {
        public static ISttService Create(string provider)
        {
            return provider switch
            {
                "Azure" => new AzureSttService(),
                "Custom" => new CustomSttService(),
                _ => new DeepgramService()
            };
        }
    }
}
