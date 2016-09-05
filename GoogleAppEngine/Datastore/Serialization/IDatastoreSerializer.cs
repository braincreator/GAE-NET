using System;
using System.Collections.Generic;
using Google.Apis.Datastore.v1beta3.Data;

namespace GoogleAppEngine.Datastore.Serialization
{
    public interface IDatastoreSerializer<T>
    {
        List<Entity> SerializeAndAutoKey(IEnumerable<T> entities, CloudAuthenticator authenticator, bool verifyThatIdIsUnused);
        T DeserializeEntity(Entity entity);
        Entity SerializeEntity(T entity);
        Entity SerializeEntity(object entity, Type entityType);
    }
}
