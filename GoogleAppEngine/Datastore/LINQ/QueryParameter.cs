using System;

namespace GoogleAppEngine.Datastore.LINQ
{
    public class Parameter
    {
        public TypeCode TypeCode { get; set; }
        public object Value { get; set; }
        public string ParameterName { get; set; }
    }
}
