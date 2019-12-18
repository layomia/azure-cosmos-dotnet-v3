//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System.IO;
    using System.Text.Json;
    
    /// <summary>
    /// The default Cosmos JSON.NET serializer.
    /// </summary>
    internal sealed class CosmosSystemTextJsonSerializer : CosmosSerializer
    {
        //private static readonly Encoding DefaultEncoding = new UTF8Encoding(false, true);
        private readonly JsonSerializerOptions serializerOptions;

        /// <summary>
        /// Create a serializer that uses the JSON.net serializer
        /// </summary>
        internal CosmosSystemTextJsonSerializer()
        {
            this.serializerOptions = new JsonSerializerOptions();
        }

        /// <summary>
        /// Create a serializer that uses the System.Text.Json serializer
        /// </summary>
        internal CosmosSystemTextJsonSerializer(CosmosSerializationOptions cosmosSerializerOptions)
        {
            this.serializerOptions = new JsonSerializerOptions()
            {
                IgnoreNullValues = cosmosSerializerOptions.IgnoreNullValues,
                WriteIndented = cosmosSerializerOptions.Indented,
            };

            if (cosmosSerializerOptions.PropertyNamingPolicy == CosmosPropertyNamingPolicy.CamelCase)
            {
                this.serializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            }
        }

        /// <summary>
        /// Create a serializer that uses the JSON.net serializer
        /// </summary>
        /// <remarks>
        /// This is internal to reduce exposure of JSON.net types so
        /// it is easier to convert to System.Text.Json
        /// </remarks>
        internal CosmosSystemTextJsonSerializer(JsonSerializerOptions options)
        {
            this.serializerOptions = options;
        }

        /// <summary>
        /// Convert a Stream to the passed in type.
        /// </summary>
        /// <typeparam name="T">The type of object that should be deserialized</typeparam>
        /// <param name="stream">An open stream that is readable that contains JSON</param>
        /// <returns>The object representing the deserialized stream</returns>
        public override T FromStream<T>(Stream stream)
        {
            using (stream)
            {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                return JsonSerializer.DeserializeAsync<T>(stream, this.serializerOptions).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            }
        }

        /// <summary>
        /// Converts an object to a open readable stream
        /// </summary>
        /// <typeparam name="T">The type of object being serialized</typeparam>
        /// <param name="input">The object to be serialized</param>
        /// <returns>An open readable stream containing the JSON of the serialized object</returns>
        public override Stream ToStream<T>(T input)
        {
            MemoryStream streamPayload = new MemoryStream();

            JsonSerializer.SerializeAsync(streamPayload, input, this.serializerOptions).ConfigureAwait(true);
            streamPayload.Position = 0;
            return streamPayload;
        }
    }
}
