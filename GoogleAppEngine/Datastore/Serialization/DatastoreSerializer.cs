using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Google.Apis.Datastore.v1beta3.Data;
using GoogleAppEngine.Datastore.LINQ;
using GoogleAppEngine.Shared;

namespace GoogleAppEngine.Datastore.Serialization
{
    public class DatastoreSerializer<T> : IDatastoreSerializer<T>
        where T : new()
    {
        private bool IsKeyUsed(CloudAuthenticator authenticator, string key)
        {
            // TODO consider using AllocateIds()
            var result = new Google.Apis.Datastore.v1beta3.DatastoreService(authenticator.GetInitializer()).Projects.Lookup(new LookupRequest
            {
                Keys = new List<Key>
                {
                    new Key
                    {
                        Path = new List<PathElement>
                        {
                            new PathElement
                            {
                                Name = key,
                                Kind = typeof(T).Name
                            }
                        }
                    }
                }
            }, authenticator.GetProjectId()).Execute();

            return result.Found.Any();
        }

        public List<Entity> SerializeAndAutoKey(IEnumerable<T> entities, CloudAuthenticator authenticator, bool verifyThatIdIsUnused)
        {
            var datastoreEntities = new List<Entity>();

            foreach (var entity in entities)
            {
                var idPropInfo = QueryHelper.GetIdProperty<T>();
                string key = null;

                if (idPropInfo != null)
                {
                    var typeCode = idPropInfo.PropertyType.GetTypeCode();
                    switch (typeCode)
                    {
                        case TypeCode.String:
                            {
                                var idField = idPropInfo.GetValue(entity);

                                if (string.IsNullOrWhiteSpace((string)idField))
                                {
                                    string autoId;

                                    do
                                    {
                                        autoId = Guid.NewGuid().ToString();
                                    } while (verifyThatIdIsUnused && IsKeyUsed(authenticator, autoId));

                                    idPropInfo.SetValue(entity, autoId);
                                    key = autoId;
                                }
                                else
                                {
                                    key = (string)idField;
                                }
                                break;
                            }
                        default:
                            throw new NotSupportedException($"Id type `{typeCode}` is not supported. Id type must be a string.");
                    }
                }
                else
                {
                    // Require that an id or key field exists.
                    throw new MissingMemberException($"`{typeof(T).Name}` does not contain an Id property nor any other property with the DatastoreKey attribute.");
                }

                // TODO(neil-119) Right now the id duplicates the key column, need to change behavior, must account for Select(x => x.key) projection

                // Serialize it.
                var datastoreEntity = SerializeEntity(entity);

                // Setup its key.
                if (datastoreEntity == null)
                    throw new NullReferenceException("Serialized entity is null");

                if (datastoreEntity?.Key?.Path == null)
                    datastoreEntity.Key = new Key { Path = new List<PathElement>() };

                datastoreEntity.Key.Path.Add(new PathElement
                {
                    Kind = typeof(T).GetTypeInfo().Name,
                    Name = key
                });

                datastoreEntities.Add(datastoreEntity);
            }

            return datastoreEntities;
        }

        private Value SerializeProperty(Type propType, object val, bool isExcludedFromIndex)
        {
            if (propType == typeof(string))
                return new Value { StringValue = (string)val ?? "", ExcludeFromIndexes = isExcludedFromIndex }; // string cannot be null in datastore
            else if (propType == typeof(int) || propType == typeof(long) || propType == typeof(short))
                return new Value { IntegerValue = Convert.ToInt64(val), ExcludeFromIndexes = isExcludedFromIndex };
            else if (propType == typeof(double))
                return new Value { DoubleValue = (double)val, ExcludeFromIndexes = isExcludedFromIndex };
            else if (propType == typeof(decimal))
                return new Value { StringValue = Convert.ToString((decimal)val, CultureInfo.InvariantCulture), ExcludeFromIndexes = false };
            else if (propType == typeof(bool))
                return new Value { BooleanValue = (bool)val, ExcludeFromIndexes = isExcludedFromIndex };
            else if (propType == typeof(DateTime))
                return new Value { TimestampValue = (DateTime)val, ExcludeFromIndexes = isExcludedFromIndex };
            else if (propType == typeof(byte[]))
                return new Value { BlobValue = val == null ? null : Convert.ToBase64String((byte[])val), ExcludeFromIndexes = isExcludedFromIndex };
            else if (propType.GetTypeInfo().IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(propType);
                if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(uint) || underlyingType == typeof(ushort))
                    return new Value { IntegerValue = Convert.ToInt64(val), ExcludeFromIndexes = isExcludedFromIndex };
                else
                    throw new NotSupportedException("The underlying enumerator type is not supported.");
            }
            else if (propType.GetTypeInfo().IsClass)
                return new Value { EntityValue = val != null ? SerializeEntity(val, propType) : null, ExcludeFromIndexes = isExcludedFromIndex };
            else
                throw new NotSupportedException("Serialization of this object is not supported.");
        }
        

        private bool IsPropertyIndexed(Type parentType, string propertyName)
        {
            var propInfo = parentType.GetProperty(propertyName);
            if (propInfo == null)
                return false;
            var attributes = propInfo.GetCustomAttributes(typeof (DatastoreNotIndexedAttribute), false);
            return !attributes.Any();
        }
        private bool IsPropertyExcludedFromIndex(Type parentType, string propertyName)
        {
            var propInfo = parentType.GetProperty(propertyName);
            if (propInfo == null)
                return false;
            var attributes = propInfo.GetCustomAttributes(typeof(DatastoreNotIndexedAttribute), false);
            return attributes.Any();
        }

        private Value SerializeProperty(Type parentType, Type propType, object val)
        {
            var isExcludedFromIndex = IsPropertyExcludedFromIndex(parentType, propType.Name);
            return SerializeProperty(propType, val, isExcludedFromIndex);
        }


        //// Not sure why Google is using two different (but schematically equivalent) classes
        //private Property ConvertValueToProperty(Value prop)
        //{
        //    return new Property
        //    {
        //        ListValue = prop.ListValue,
        //        StringValue = prop.StringValue,
        //        IntegerValue = prop.IntegerValue,
        //        DoubleValue = prop.DoubleValue,
        //        BooleanValue = prop.BooleanValue,
        //        BlobKeyValue = prop.BlobKeyValue,
        //        BlobValue = prop.BlobValue,
        //        DateTimeValue = prop.DateTimeValue,
        //        DateTimeValueRaw = prop.DateTimeValueRaw,
        //        ETag = prop.ETag,
        //        EntityValue = prop.EntityValue,
        //        Indexed = prop.Indexed,
        //        KeyValue = prop.KeyValue,
        //        Meaning = prop.Meaning
        //    };
        //}

        //private Value ConvertPropertyToValue(Property prop)
        //{
        //    return new Value
        //    {
        //        ArrayValue = prop.ListValue,
        //        StringValue = prop.StringValue,
        //        IntegerValue = prop.IntegerValue,
        //        DoubleValue = prop.DoubleValue,
        //        BooleanValue = prop.BooleanValue,
        //        BlobValue = prop.BlobValue,
        //        TimestampValue = prop.DateTimeValue,
        //        ETag = prop.ETag,
        //        EntityValue = prop.EntityValue,
        //        ExcludeFromIndexes = prop.Indexed,
        //        KeyValue = prop.KeyValue,
        //        Meaning = prop.Meaning
        //    };
        //}

        //private Property SerializeProperty(Type parentType, Type propType, IList list)
        //{
        //    var isIndexed = IsPropertyIndexed(parentType, propType.Name);

        //    var values = new List<Value>();
        //    var enumerator = list.GetEnumerator();
        //    var genericParamType = TypeSystem.GetElementType(propType);

        //    while (enumerator.MoveNext())
        //    {
        //        values.Add(ConvertPropertyToValue(SerializeProperty(genericParamType, enumerator.Current, isIndexed)));
        //    }

        //    return new Property { ListValue = values, Indexed  = isIndexed };
        //}

        private Value SerializeFromPropertyInfo(Type parentType, PropertyInfo prop, object entity)
        {
            var val = prop.GetValue(entity, null);
            var isExcludedFromIndex = IsPropertyExcludedFromIndex(parentType, prop.Name);
            return SerializeProperty(prop.PropertyType, val, isExcludedFromIndex);
        }

        public Entity SerializeEntity(T entity)
        {
            return SerializeEntity(entity, typeof(T));
        }

        public Entity SerializeEntity(object entity, Type entityType)
        {
            var datastoreEntity = new Entity();

            // Setup properties
            datastoreEntity.Properties = new Dictionary<string, Value>();

            var properties = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttributes(typeof(DatastoreNotSavedAttribute), false).Any())
                    continue;

                if (prop.Name.StartsWith(Constants.DictionaryKeyValuePairPrefix))
                    throw new InvalidOperationException($"Property `{prop.Name}` cannot begin with `{Constants.DictionaryKeyValuePairPrefix}`");

                if (prop.Name.Contains(Constants.DictionaryKeyDivider))
                    throw new InvalidOperationException($"Property `{prop.Name}` cannot contain `{Constants.DictionaryKeyDivider}`");

                if (prop.PropertyType.GetTypeInfo().IsGenericType)
                {
                    var gtypedef = prop.PropertyType.GetGenericTypeDefinition();

                    if (gtypedef == typeof(Dictionary<,>))
                    {
                        var dict = (IDictionary)prop.GetValue(entity, null);

                        if (dict != null && dict.Count > 0)
                        {
                            var dictEnumerator = dict.GetEnumerator();

                            while (dictEnumerator.MoveNext())
                            {
                                datastoreEntity.Properties.Add($"{Constants.DictionaryKeyValuePairPrefix}{prop.Name}{Constants.DictionaryKeyDivider}{dictEnumerator.Key}",
                                    SerializeProperty(entityType, dictEnumerator.Value.GetType(), dictEnumerator.Value));
                            }
                        }
                    }
                    else if (gtypedef == typeof(List<>))
                    {
                        var list = (IList)prop.GetValue(entity, null);
                        
                        if (list != null && list.Count > 0)
                            datastoreEntity.Properties.Add(prop.Name, SerializeProperty(entityType, prop.PropertyType, list));
                    }
                }
                else
                {
                    if (prop.GetValue(entity) != null)
                        datastoreEntity.Properties.Add(prop.Name, SerializeFromPropertyInfo(entityType, prop, entity));
                }
            }

            return datastoreEntity;
        }

        private void DeserializeEntity(Entity entity, Type entityType, object t)
        {
            foreach (var kv in entity.Properties)
            {
                PropertyInfo property;

                // Is the key indicating a flattened dictionary?
                if (kv.Key.StartsWith(Constants.DictionaryKeyValuePairPrefix))
                {
                    var dictKeys = kv.Key.Replace(Constants.DictionaryKeyValuePairPrefix, string.Empty)
                        .Split(new[] { Constants.DictionaryKeyDivider }, 2, StringSplitOptions.None);

                    if (dictKeys.Length != 2 || dictKeys.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException($"`{kv.Key}` is an invalid dictionary key");

                    // Get the property as defined by the embedded key
                    // TODO we can make this faster by caching
                    property = entityType.GetProperties().FirstOrDefault(x => x.Name == dictKeys[0]);

                    if (property != null && property.PropertyType.GetTypeInfo().IsGenericType
                        && property.PropertyType.GetTypeInfo().GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        var dict = (IDictionary)property.GetValue(t, null);
                        var ga = property.PropertyType.GetGenericArguments();
                        dict.Add(dictKeys[1], Convert.ChangeType(GetValueFromProperty(kv,
                            ga[1]), ga[1]));
                    }
                    continue;
                }

                // Get property by key
                property = entityType.GetProperties().FirstOrDefault(x => x.Name == kv.Key);

                if (property != null)
                {
                    if (property.PropertyType.GetTypeInfo().IsGenericType)
                    {
                        if (property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            var genericParamType = TypeSystem.GetElementType(property.PropertyType);
                            var list = (IList)property.GetValue(t, null);

                            foreach (var val in kv.Value.ArrayValue.Values)
                                list.Add(Convert.ChangeType(GetValueFromProperty(kv.Key, val,
                                    genericParamType), genericParamType));
                        }
                    }
                    else
                    {
                        // Set its property
                        property.SetValue(t, GetValueFromProperty(kv, property.PropertyType));
                    }
                }
            }
        }

        // TODO throw if property DNE? How should we handle it?
        public T DeserializeEntity(Entity entity)
        {
            var t = new T();
            DeserializeEntity(entity, typeof(T), t);
            return t;
        }

        private object GetValueFromProperty(KeyValuePair<string, Value> kv, Type type)
        {
            return GetValueFromProperty(kv.Key, kv.Value, type);
        }

        private object GetValueFromProperty(string key, Value propValue, Type propType)
        {
            if (propType == typeof(string))
                return propValue.StringValue;
            else if (propType == typeof(short))
                return propValue.IntegerValue != null ? Convert.ToInt16(propValue.IntegerValue) : default(short);
            else if (propType == typeof(int))
                return propValue.IntegerValue != null ? Convert.ToInt32(propValue.IntegerValue) : default(int);
            else if (propType == typeof(long))
                return propValue.IntegerValue ?? default(long);
            else if (propType == typeof(double))
                return propValue.DoubleValue ?? default(double);
            else if (propType == typeof(decimal))
                return string.IsNullOrWhiteSpace(propValue.StringValue) ? default(decimal) : Convert.ToDecimal(propValue.StringValue);
            else if (propType == typeof(bool))
                return propValue.BooleanValue ?? default(bool);
            else if (propType == typeof(DateTime))
                return propValue.TimestampValue ?? default(DateTime);
            else if (propType == typeof(byte[]))
                return string.IsNullOrWhiteSpace(propValue.BlobValue) ? null : Convert.FromBase64String(propValue.BlobValue);
            else if (propType.GetTypeInfo().IsEnum)
                return Enum.ToObject(propType, propValue.IntegerValue ?? default(long));
            else if (propType.GetTypeInfo().IsClass)
            {
                if (propValue.EntityValue != null)
                {
                    var o = propType.GetConstructor(new Type[] { })?.Invoke(new object[] { });
                    if (o != null)
                        DeserializeEntity(propValue.EntityValue, propType, o);
                    return o;
                }
                return null;
            }
            else
                throw new NotSupportedException($"Deserialization of `{key}` is not supported.");
        }
    }
}
