﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Routing;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class BatchAsyncContainerExecutorTests
    {
        private static CosmosSerializer cosmosDefaultJsonSerializer = new CosmosSystemTextJsonSerializer();

        [TestMethod]
        public async Task RetryOnSplit()
        {
            ItemBatchOperation itemBatchOperation = CreateItem("test");

            Mock<CosmosClientContext> mockedContext = new Mock<CosmosClientContext>();
            mockedContext.Setup(c => c.ClientOptions).Returns(new CosmosClientOptions());
            mockedContext
                .SetupSequence(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(this.GenerateSplitResponseAsync(itemBatchOperation))
                .Returns(this.GenerateOkResponseAsync(itemBatchOperation));

            mockedContext.Setup(c => c.CosmosSerializer).Returns(new CosmosSystemTextJsonSerializer());

            Uri link = new Uri($"/dbs/db/colls/colls", UriKind.Relative);
            Mock<ContainerCore> mockContainer = new Mock<ContainerCore>();
            mockContainer.Setup(x => x.LinkUri).Returns(link);
            mockContainer.Setup(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(new PartitionKeyDefinition() { Paths = new Collection<string>() { "/id" } }));

            CollectionRoutingMap routingMap = CollectionRoutingMap.TryCreateCompleteRoutingMap(
                new[]
                    {
                        Tuple.Create(new PartitionKeyRange{ Id = "0", MinInclusive = "", MaxExclusive = "FF"}, (ServiceIdentity)null)
                    },
                string.Empty);
            mockContainer.Setup(x => x.GetRoutingMapAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(routingMap));
            BatchAsyncContainerExecutor executor = new BatchAsyncContainerExecutor(mockContainer.Object, mockedContext.Object, 20, Constants.MaxDirectModeBatchRequestBodySizeInBytes, 1);
            TransactionalBatchOperationResult result = await executor.AddAsync(itemBatchOperation);

            Mock.Get(mockContainer.Object)
                .Verify(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            Mock.Get(mockedContext.Object)
                .Verify(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            Assert.IsNotNull(result.Diagnostics);

            int diagnosticsLines = 0;
            string diagnosticsString = result.Diagnostics.ToString();
            int index = diagnosticsString.IndexOf(Environment.NewLine);
            while(index > -1)
            {
                diagnosticsLines++;
                index = diagnosticsString.IndexOf(Environment.NewLine, index + 1);
            }

            Assert.IsTrue(diagnosticsLines > 1, "Diagnostics might be missing");
        }

        [TestMethod]
        public async Task RetryOnNameStale()
        {
            ItemBatchOperation itemBatchOperation = CreateItem("test");

            Mock<CosmosClientContext> mockedContext = new Mock<CosmosClientContext>();
            mockedContext.Setup(c => c.ClientOptions).Returns(new CosmosClientOptions());
            mockedContext
                .SetupSequence(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(this.GenerateCacheStaleResponseAsync(itemBatchOperation))
                .Returns(this.GenerateOkResponseAsync(itemBatchOperation));

            mockedContext.Setup(c => c.CosmosSerializer).Returns(new CosmosSystemTextJsonSerializer());

            Uri link = new Uri($"/dbs/db/colls/colls", UriKind.Relative);
            Mock<ContainerCore> mockContainer = new Mock<ContainerCore>();
            mockContainer.Setup(x => x.LinkUri).Returns(link);
            mockContainer.Setup(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(new PartitionKeyDefinition() { Paths = new Collection<string>() { "/id" } }));

            CollectionRoutingMap routingMap = CollectionRoutingMap.TryCreateCompleteRoutingMap(
                new[]
                    {
                        Tuple.Create(new PartitionKeyRange{ Id = "0", MinInclusive = "", MaxExclusive = "FF"}, (ServiceIdentity)null)
                    },
                string.Empty);
            mockContainer.Setup(x => x.GetRoutingMapAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(routingMap));
            BatchAsyncContainerExecutor executor = new BatchAsyncContainerExecutor(mockContainer.Object, mockedContext.Object, 20, Constants.MaxDirectModeBatchRequestBodySizeInBytes, 1);
            TransactionalBatchOperationResult result = await executor.AddAsync(itemBatchOperation);

            Mock.Get(mockContainer.Object)
                .Verify(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            Mock.Get(mockedContext.Object)
                .Verify(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            Assert.IsNotNull(result.Diagnostics);

            int diagnosticsLines = 0;
            string diagnosticsString = result.Diagnostics.ToString();
            int index = diagnosticsString.IndexOf(Environment.NewLine);
            while (index > -1)
            {
                diagnosticsLines++;
                index = diagnosticsString.IndexOf(Environment.NewLine, index + 1);
            }

            Assert.IsTrue(diagnosticsLines > 1, "Diagnostics might be missing");
        }

        [TestMethod]
        public async Task RetryOn429()
        {
            ItemBatchOperation itemBatchOperation = CreateItem("test");

            Mock<CosmosClientContext> mockedContext = new Mock<CosmosClientContext>();
            mockedContext.Setup(c => c.ClientOptions).Returns(new CosmosClientOptions());
            mockedContext
                .SetupSequence(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(this.Generate429ResponseAsync(itemBatchOperation))
                .Returns(this.GenerateOkResponseAsync(itemBatchOperation));

            mockedContext.Setup(c => c.CosmosSerializer).Returns(new CosmosSystemTextJsonSerializer());

            Uri link = new Uri($"/dbs/db/colls/colls", UriKind.Relative);
            Mock<ContainerCore> mockContainer = new Mock<ContainerCore>();
            mockContainer.Setup(x => x.LinkUri).Returns(link);
            mockContainer.Setup(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(new PartitionKeyDefinition() { Paths = new Collection<string>() { "/id" } }));

            CollectionRoutingMap routingMap = CollectionRoutingMap.TryCreateCompleteRoutingMap(
                new[]
                    {
                        Tuple.Create(new PartitionKeyRange{ Id = "0", MinInclusive = "", MaxExclusive = "FF"}, (ServiceIdentity)null)
                    },
                string.Empty);
            mockContainer.Setup(x => x.GetRoutingMapAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(routingMap));
            BatchAsyncContainerExecutor executor = new BatchAsyncContainerExecutor(mockContainer.Object, mockedContext.Object, 20, Constants.MaxDirectModeBatchRequestBodySizeInBytes, 1);
            TransactionalBatchOperationResult result = await executor.AddAsync(itemBatchOperation);

            Mock.Get(mockContainer.Object)
                .Verify(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            Mock.Get(mockedContext.Object)
                .Verify(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            Assert.IsNotNull(result.Diagnostics);

            int diagnosticsLines = 0;
            string diagnosticsString = result.Diagnostics.ToString();
            int index = diagnosticsString.IndexOf(Environment.NewLine);
            while (index > -1)
            {
                diagnosticsLines++;
                index = diagnosticsString.IndexOf(Environment.NewLine, index + 1);
            }

            Assert.IsTrue(diagnosticsLines > 1, "Diagnostics might be missing");
        }

        [TestMethod]
        public async Task DoesNotRecalculatePartitionKeyRangeOnNoSplits()
        {
            ItemBatchOperation itemBatchOperation = CreateItem("test");

            Mock<CosmosClientContext> mockedContext = new Mock<CosmosClientContext>();
            mockedContext.Setup(c => c.ClientOptions).Returns(new CosmosClientOptions());
            mockedContext
                .Setup(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(this.GenerateOkResponseAsync(itemBatchOperation));

            mockedContext.Setup(c => c.CosmosSerializer).Returns(new CosmosSystemTextJsonSerializer());

            Uri link = new Uri($"/dbs/db/colls/colls", UriKind.Relative);
            Mock<ContainerCore> mockContainer = new Mock<ContainerCore>();
            mockContainer.Setup(x => x.LinkUri).Returns(link);
            mockContainer.Setup(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(new PartitionKeyDefinition() { Paths = new Collection<string>() { "/id" } }));

            CollectionRoutingMap routingMap = CollectionRoutingMap.TryCreateCompleteRoutingMap(
                new[]
                    {
                        Tuple.Create(new PartitionKeyRange{ Id = "0", MinInclusive = "", MaxExclusive = "FF"}, (ServiceIdentity)null)
                    },
                string.Empty);
            mockContainer.Setup(x => x.GetRoutingMapAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(routingMap));
            BatchAsyncContainerExecutor executor = new BatchAsyncContainerExecutor(mockContainer.Object, mockedContext.Object, 20, Constants.MaxDirectModeBatchRequestBodySizeInBytes, 1);
            TransactionalBatchOperationResult result = await executor.AddAsync(itemBatchOperation);

            Mock.Get(mockContainer.Object)
                .Verify(x => x.GetPartitionKeyDefinitionAsync(It.IsAny<CancellationToken>()), Times.Once);
            Mock.Get(mockedContext.Object)
                .Verify(c => c.ProcessResourceOperationStreamAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<ResourceType>(),
                    It.IsAny<OperationType>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<ContainerCore>(),
                    It.IsAny<Cosmos.PartitionKey?>(),
                    It.IsAny<Stream>(),
                    It.IsAny<Action<RequestMessage>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }

        private async Task<ResponseMessage> GenerateSplitResponseAsync(ItemBatchOperation itemBatchOperation)
        {
            List<TransactionalBatchOperationResult> results = new List<TransactionalBatchOperationResult>();
            ItemBatchOperation[] arrayOperations = new ItemBatchOperation[1];
            results.Add(
                new TransactionalBatchOperationResult(HttpStatusCode.Gone)
                {
                    ETag = itemBatchOperation.Id,
                    SubStatusCode = SubStatusCodes.PartitionKeyRangeGone
                });

            arrayOperations[0] = itemBatchOperation;

            MemoryStream responseContent = await new BatchResponsePayloadWriter(results).GeneratePayloadAsync();

            SinglePartitionKeyServerBatchRequest batchRequest = await SinglePartitionKeyServerBatchRequest.CreateAsync(
                partitionKey: null,
                operations: new ArraySegment<ItemBatchOperation>(arrayOperations),
                serializer: new CosmosSystemTextJsonSerializer(),
            cancellationToken: CancellationToken.None);

            ResponseMessage responseMessage = new ResponseMessage(HttpStatusCode.Gone)
            {
                Content = responseContent,
                Diagnostics = new PointOperationStatistics(
                    activityId: Guid.NewGuid().ToString(),
                    statusCode: HttpStatusCode.Gone,
                    subStatusCode: SubStatusCodes.Unknown,
                    requestCharge: 0,
                    errorMessage: string.Empty,
                    method: HttpMethod.Get,
                    requestUri: new Uri("http://localhost"),
                    requestSessionToken: null,
                    responseSessionToken: null,
                    clientSideRequestStatistics: new CosmosClientSideRequestStatistics())
            };
            responseMessage.Headers.SubStatusCode = SubStatusCodes.PartitionKeyRangeGone;
            return responseMessage;
        }

        private async Task<ResponseMessage> GenerateCacheStaleResponseAsync(ItemBatchOperation itemBatchOperation)
        {
            List<TransactionalBatchOperationResult> results = new List<TransactionalBatchOperationResult>();
            ItemBatchOperation[] arrayOperations = new ItemBatchOperation[1];
            results.Add(
                new TransactionalBatchOperationResult(HttpStatusCode.Gone)
                {
                    ETag = itemBatchOperation.Id,
                    SubStatusCode = SubStatusCodes.NameCacheIsStale
                });

            arrayOperations[0] = itemBatchOperation;

            MemoryStream responseContent = await new BatchResponsePayloadWriter(results).GeneratePayloadAsync();

            SinglePartitionKeyServerBatchRequest batchRequest = await SinglePartitionKeyServerBatchRequest.CreateAsync(
                partitionKey: null,
                operations: new ArraySegment<ItemBatchOperation>(arrayOperations),
                serializer: new CosmosSystemTextJsonSerializer(),
            cancellationToken: CancellationToken.None);

            ResponseMessage responseMessage = new ResponseMessage(HttpStatusCode.Gone)
            {
                Content = responseContent,
                Diagnostics = new PointOperationStatistics(
                    activityId: Guid.NewGuid().ToString(),
                    statusCode: HttpStatusCode.Gone,
                    subStatusCode: SubStatusCodes.Unknown,
                    requestCharge: 0,
                    errorMessage: string.Empty,
                    method: HttpMethod.Get,
                    requestUri: new Uri("http://localhost"),
                    requestSessionToken: null,
                    responseSessionToken: null,
                    clientSideRequestStatistics: new CosmosClientSideRequestStatistics())
            };
            responseMessage.Headers.SubStatusCode = SubStatusCodes.NameCacheIsStale;
            return responseMessage;
        }

        private async Task<ResponseMessage> Generate429ResponseAsync(ItemBatchOperation itemBatchOperation)
        {
            List<TransactionalBatchOperationResult> results = new List<TransactionalBatchOperationResult>();
            ItemBatchOperation[] arrayOperations = new ItemBatchOperation[1];
            results.Add(
                new TransactionalBatchOperationResult((HttpStatusCode) StatusCodes.TooManyRequests)
                {
                    ETag = itemBatchOperation.Id
                });

            arrayOperations[0] = itemBatchOperation;

            MemoryStream responseContent = await new BatchResponsePayloadWriter(results).GeneratePayloadAsync();

            SinglePartitionKeyServerBatchRequest batchRequest = await SinglePartitionKeyServerBatchRequest.CreateAsync(
                partitionKey: null,
                operations: new ArraySegment<ItemBatchOperation>(arrayOperations),
                serializer: new CosmosSystemTextJsonSerializer(),
            cancellationToken: CancellationToken.None);

            ResponseMessage responseMessage = new ResponseMessage((HttpStatusCode)StatusCodes.TooManyRequests)
            {
                Content = responseContent,
                Diagnostics = new PointOperationStatistics(
                    activityId: Guid.NewGuid().ToString(),
                    statusCode: (HttpStatusCode)StatusCodes.TooManyRequests,
                    subStatusCode: SubStatusCodes.Unknown,
                    requestCharge: 0,
                    errorMessage: string.Empty,
                    method: HttpMethod.Get,
                    requestUri: new Uri("http://localhost"),
                    requestSessionToken: null,
                    responseSessionToken: null,
                    clientSideRequestStatistics: new CosmosClientSideRequestStatistics())
            };
            return responseMessage;
        }

        private async Task<ResponseMessage> GenerateOkResponseAsync(ItemBatchOperation itemBatchOperation)
        {
            List<TransactionalBatchOperationResult> results = new List<TransactionalBatchOperationResult>();
            ItemBatchOperation[] arrayOperations = new ItemBatchOperation[1];
            results.Add(
                new TransactionalBatchOperationResult(HttpStatusCode.OK)
                {
                    ETag = itemBatchOperation.Id
                });

            arrayOperations[0] = itemBatchOperation;

            MemoryStream responseContent = await new BatchResponsePayloadWriter(results).GeneratePayloadAsync();

            SinglePartitionKeyServerBatchRequest batchRequest = await SinglePartitionKeyServerBatchRequest.CreateAsync(
                partitionKey: null,
                operations: new ArraySegment<ItemBatchOperation>(arrayOperations),
                serializer: new CosmosSystemTextJsonSerializer(),
            cancellationToken: CancellationToken.None);

            ResponseMessage responseMessage = new ResponseMessage(HttpStatusCode.OK)
            {
                Content = responseContent,
                Diagnostics = new PointOperationStatistics(
                     activityId: Guid.NewGuid().ToString(),
                     statusCode: HttpStatusCode.OK,
                     subStatusCode: SubStatusCodes.Unknown,
                     requestCharge: 0,
                     errorMessage: string.Empty,
                     method: HttpMethod.Get,
                     requestUri: new Uri("http://localhost"),
                     requestSessionToken: null,
                     responseSessionToken: null,
                     clientSideRequestStatistics: new CosmosClientSideRequestStatistics())
            };
            return responseMessage;
        }

        private static ItemBatchOperation CreateItem(string id)
        {
            MyDocument myDocument = new MyDocument() { id = id, Status = id };
            return new ItemBatchOperation(OperationType.Create, 0, new Cosmos.PartitionKey(id), id, cosmosDefaultJsonSerializer.ToStream(myDocument));
        }

        private class MyDocument
        {
            public string id { get; set; }

            public string Status { get; set; }

            public bool Updated { get; set; }
        }
    }
}
