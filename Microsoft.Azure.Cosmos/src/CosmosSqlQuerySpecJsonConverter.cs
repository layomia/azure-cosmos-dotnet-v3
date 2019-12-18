//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Microsoft.Azure.Cosmos.Query.Core;

    /// <summary>
    /// A custom serializer converter for SQL query spec
    /// </summary>
    internal sealed class CosmosSqlQuerySpecJsonConverter : JsonConverter<SqlParameter>
    {
        private readonly CosmosSerializer UserSerializer;

        internal CosmosSqlQuerySpecJsonConverter(CosmosSerializer userSerializer)
        {
            this.UserSerializer = userSerializer ?? throw new ArgumentNullException(nameof(userSerializer));
        }

        public override SqlParameter Read(ref Utf8JsonReader reader, Type objectType, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, SqlParameter value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            JsonSerializer.Serialize(writer, value.Name, options);
            writer.WritePropertyName("value");

            // Use the user serializer for the parameter values so custom conversions are correctly handled
            using (Stream str = this.UserSerializer.ToStream(value.Value))
            {
                using (StreamReader streamReader = new StreamReader(str))
                {
                    string parameterValue = streamReader.ReadToEnd();
                    writer.WriteStringValue(parameterValue);
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Only create a custom SQL query spec serializer if there is a customer serializer else
        /// use the default properties serializer
        /// </summary>
        internal static CosmosSerializer CreateSqlQuerySpecSerializer(
            CosmosSerializer cosmosSerializer,
            CosmosSerializer propertiesSerializer)
        {
            // If both serializers are the same no need for the custom converter
            if (object.ReferenceEquals(cosmosSerializer, propertiesSerializer))
            {
                return propertiesSerializer;
            }

            JsonSerializerOptions options = new JsonSerializerOptions { };
            options.Converters.Add(new CosmosSqlQuerySpecJsonConverter(cosmosSerializer));

            return new CosmosJsonSerializerWrapper(new CosmosSystemTextJsonSerializer(options));
        }
    }
}
