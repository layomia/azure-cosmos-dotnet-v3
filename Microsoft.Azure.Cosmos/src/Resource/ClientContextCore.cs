﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Handlers;
    using Microsoft.Azure.Cosmos.Query;
    using Microsoft.Azure.Cosmos.Routing;
    using Microsoft.Azure.Documents;

    internal class ClientContextCore : CosmosClientContext
    {
        internal ClientContextCore(
            CosmosClient client,
            CosmosClientOptions clientOptions,
            CosmosSerializer userJsonSerializer,
            CosmosSerializer defaultJsonSerializer,
            CosmosSerializer sqlQuerySpecSerializer,
            CosmosResponseFactory cosmosResponseFactory,
            RequestInvokerHandler requestHandler,
            DocumentClient documentClient)
        {
            this.Client = client;
            this.ClientOptions = clientOptions;
            this.CosmosSerializer = userJsonSerializer;
            this.PropertiesSerializer = defaultJsonSerializer;
            this.SqlQuerySpecSerializer = sqlQuerySpecSerializer;
            this.ResponseFactory = cosmosResponseFactory;
            this.RequestHandler = requestHandler;
            this.DocumentClient = documentClient;
        }

        /// <summary>
        /// The Cosmos client that is used for the request
        /// </summary>
        internal override CosmosClient Client { get; }

        internal override DocumentClient DocumentClient { get; }

        internal override CosmosSerializer CosmosSerializer { get; }

        internal override CosmosSerializer PropertiesSerializer { get; }

        internal override CosmosSerializer SqlQuerySpecSerializer { get; }

        internal override CosmosResponseFactory ResponseFactory { get; }

        internal override RequestInvokerHandler RequestHandler { get; }

        internal override CosmosClientOptions ClientOptions { get; }

        /// <summary>
        /// Generates the URI link for the resource
        /// </summary>
        /// <param name="parentLink">The parent link URI (/dbs/mydbId) </param>
        /// <param name="uriPathSegment">The URI path segment</param>
        /// <param name="id">The id of the resource</param>
        /// <returns>A resource link in the format of {parentLink}/this.UriPathSegment/this.Name with this.Name being a Uri escaped version</returns>
        internal override Uri CreateLink(
            string parentLink,
            string uriPathSegment,
            string id)
        {
            int parentLinkLength = parentLink?.Length ?? 0;
            string idUriEscaped = Uri.EscapeUriString(id);

            StringBuilder stringBuilder = new StringBuilder(parentLinkLength + 2 + uriPathSegment.Length + idUriEscaped.Length);
            if (parentLinkLength > 0)
            {
                stringBuilder.Append(parentLink);
                stringBuilder.Append("/");
            }

            stringBuilder.Append(uriPathSegment);
            stringBuilder.Append("/");
            stringBuilder.Append(idUriEscaped);
            return new Uri(stringBuilder.ToString(), UriKind.Relative);
        }

        internal override void ValidateResource(string resourceId)
        {
            this.DocumentClient.ValidateResource(resourceId);
        }

        internal override Task<ResponseMessage> ProcessResourceOperationStreamAsync(
            Uri resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            RequestOptions requestOptions,
            ContainerCore cosmosContainerCore,
            PartitionKey? partitionKey,
            string itemId,
            Stream streamPayload,
            Action<RequestMessage> requestEnricher,
            CancellationToken cancellationToken)
        {
            if (this.IsBulkOperationSupported(resourceType, operationType))
            {
                if (!partitionKey.HasValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(partitionKey));
                }

                return this.ProcessResourceOperationAsBulkStreamAsync(
                    resourceUri: resourceUri,
                    resourceType: resourceType,
                    operationType: operationType,
                    requestOptions: requestOptions,
                    cosmosContainerCore: cosmosContainerCore,
                    partitionKey: partitionKey.Value,
                    itemId: itemId,
                    streamPayload: streamPayload,
                    requestEnricher: requestEnricher,
                    cancellationToken: cancellationToken);
            }

            return this.ProcessResourceOperationStreamAsync(
                resourceUri: resourceUri,
                resourceType: resourceType,
                operationType: operationType,
                requestOptions: requestOptions,
                cosmosContainerCore: cosmosContainerCore,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestEnricher: requestEnricher,
                cancellationToken: cancellationToken);
        }

        internal override Task<ResponseMessage> ProcessResourceOperationStreamAsync(
            Uri resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            RequestOptions requestOptions,
            ContainerCore cosmosContainerCore,
            PartitionKey? partitionKey,
            Stream streamPayload,
            Action<RequestMessage> requestEnricher,
            CancellationToken cancellationToken)
        {
            return this.RequestHandler.SendAsync(
                resourceUri: resourceUri,
                resourceType: resourceType,
                operationType: operationType,
                requestOptions: requestOptions,
                cosmosContainerCore: cosmosContainerCore,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestEnricher: requestEnricher,
                cancellationToken: cancellationToken);
        }

        internal override Task<T> ProcessResourceOperationAsync<T>(
            Uri resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            RequestOptions requestOptions,
            ContainerCore cosmosContainerCore,
            PartitionKey? partitionKey,
            Stream streamPayload,
            Action<RequestMessage> requestEnricher,
            Func<ResponseMessage, T> responseCreator,
            CancellationToken cancellationToken)
        {
            return this.RequestHandler.SendAsync<T>(
                resourceUri: resourceUri,
                resourceType: resourceType,
                operationType: operationType,
                requestOptions: requestOptions,
                cosmosContainerCore: cosmosContainerCore,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestEnricher: requestEnricher,
                responseCreator: responseCreator,
                cancellationToken: cancellationToken);
        }

        internal override async Task<ContainerProperties> GetCachedContainerPropertiesAsync(
            string containerUri,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ClientCollectionCache collectionCache = await this.DocumentClient.GetCollectionCacheAsync();
            try
            {
                return await collectionCache.ResolveByNameAsync(
                    HttpConstants.Versions.CurrentVersion,
                    containerUri,
                    cancellationToken);
            }
            catch (DocumentClientException ex)
            {
                throw new CosmosException(ex.ToCosmosResponseMessage(null), ex.Message, ex.Error);
            }
        }

        private async Task<ResponseMessage> ProcessResourceOperationAsBulkStreamAsync(
            Uri resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            RequestOptions requestOptions,
            ContainerCore cosmosContainerCore,
            PartitionKey partitionKey,
            string itemId,
            Stream streamPayload,
            Action<RequestMessage> requestEnricher,
            CancellationToken cancellationToken)
        {
            ItemRequestOptions itemRequestOptions = requestOptions as ItemRequestOptions;
            TransactionalBatchItemRequestOptions batchItemRequestOptions = TransactionalBatchItemRequestOptions.FromItemRequestOptions(itemRequestOptions);
            ItemBatchOperation itemBatchOperation = new ItemBatchOperation(operationType, /* index */ 0, partitionKey, itemId, streamPayload, batchItemRequestOptions);
            TransactionalBatchOperationResult batchOperationResult = await cosmosContainerCore.BatchExecutor.AddAsync(itemBatchOperation, itemRequestOptions, cancellationToken);
            return batchOperationResult.ToResponseMessage();
        }

        private bool IsBulkOperationSupported(
            ResourceType resourceType,
            OperationType operationType)
        {
            if (!this.ClientOptions.AllowBulkExecution)
            {
                return false;
            }

            return resourceType == ResourceType.Document
                && (operationType == OperationType.Create
                || operationType == OperationType.Upsert
                || operationType == OperationType.Read
                || operationType == OperationType.Delete
                || operationType == OperationType.Replace);
        }
    }
}