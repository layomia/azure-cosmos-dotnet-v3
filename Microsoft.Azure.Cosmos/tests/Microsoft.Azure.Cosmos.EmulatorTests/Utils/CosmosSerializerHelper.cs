//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.SDK.EmulatorTests
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Placeholder for VST Logger.
    /// </summary>
    internal class CosmosSerializerHelper : CosmosSerializer
    {
        private readonly CosmosSerializer cosmosSerializer = TestCommon.Serializer;
        private readonly Action<dynamic> fromStreamCallback;
        private readonly Action<dynamic> toStreamCallBack;

        public CosmosSerializerHelper(
            JsonSerializerOptions options,
            Action<dynamic> fromStreamCallback,
            Action<dynamic> toStreamCallBack)
        {
            if (options == null)
            {
                this.cosmosSerializer = TestCommon.Serializer;
            }
            else
            {
                this.cosmosSerializer = new CosmosSystemTextJsonSerializer(options);
            }

            this.fromStreamCallback = fromStreamCallback;
            this.toStreamCallBack = toStreamCallBack;
        }

        public override T FromStream<T>(Stream stream)
        {
            T item = this.cosmosSerializer.FromStream<T>(stream);
            this.fromStreamCallback?.Invoke(item);

            return item;
        }

        public override Stream ToStream<T>(T input)
        {
            this.toStreamCallBack?.Invoke(input);
            return this.cosmosSerializer.ToStream<T>(input);
        }

        public sealed class FormatNumbersAsTextConverter : JsonConverterFactory
        {
            public override bool CanConvert(Type type)
            {
                return type == typeof(int) || type == typeof(double);
            }

            public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                JsonConverter converter = (JsonConverter)Activator.CreateInstance(
                    typeof(FormatNumbersAsTextConverter<>).MakeGenericType(new Type[] { typeToConvert }),
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    args: null,
                    culture: null);

                return converter;
            }
        }

        public class FormatNumbersAsTextConverter<T> : JsonConverter<T>
        {
            public override T Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                throw new NotSupportedException();
            }

            public override void Write(
                Utf8JsonWriter writer,
                T value,
                JsonSerializerOptions options)
            {
                if (value.GetType() == typeof(int))
                {
                    int number = (int)(object)value;
                    writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    double number = (double)(object)value;
                    writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                }

            }
        }
    }
}
