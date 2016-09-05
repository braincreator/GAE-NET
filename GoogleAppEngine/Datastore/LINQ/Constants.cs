using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("GoogleAppEngine.Tests")]

namespace GoogleAppEngine.Datastore.LINQ
{
    internal static class Constants
    {
        internal const string DictionaryKeyValuePairPrefix = "g/kv_";
        internal const string DictionaryKeyDivider = "_g/k_";
        internal const string QueryPartTakeParameterName = "qlim";
        internal const string QueryPartSkipParameterName = "qoffset";
    }
}
