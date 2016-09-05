using System;

namespace GoogleAppEngine.Datastore.LINQ
{
    public abstract class ProjectionRow
    {
        public abstract object GetValue(string columnName, TypeCode typeCode);
    }
}
