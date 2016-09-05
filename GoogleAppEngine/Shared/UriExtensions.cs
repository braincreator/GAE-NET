using System;
using System.IO;

namespace GoogleAppEngine.Shared
{
    public static class UriExtensions
    {
        public static bool IsAbsoluteUri(string url)
        {
            Uri result;
            return Uri.TryCreate(url, UriKind.Absolute, out result);
        }

        public static string GetAbsoluteUri(string path)
        {
            return IsAbsoluteUri(path) ? path :
                new Uri(Path.Combine(System.IO.Directory.GetCurrentDirectory(), path)).LocalPath;

        }
    }
}
