namespace Sinsam.CommandFramework
{
    public static class PreviewAware
    {
        public static T Data<T>(T real) where T : class
        {
            return real;
        }
    }
}
